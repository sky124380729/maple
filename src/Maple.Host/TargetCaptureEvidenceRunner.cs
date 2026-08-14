using System.Diagnostics;
using Maple.Capture;
using Maple.Contracts;
using Maple.Input;

namespace Maple.Host;

public sealed record TargetCaptureEvidenceReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    bool Success,
    string Code,
    int RequestedFrames,
    int CapturedFrames,
    string? Hwnd,
    int? Pid,
    int? ClientWidth,
    int? ClientHeight,
    int? Dpi,
    string? ProcessPathSha256,
    IReadOnlyDictionary<string, int> CaptureBackends,
    double EffectiveFps,
    double P50CaptureDurationMs,
    double P95CaptureDurationMs,
    string InputStatus);

public sealed class TargetCaptureEvidenceRunner
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);
    private readonly ITargetWindowLocator locator;
    private readonly ICaptureBackend backend;
    private readonly Func<TimeSpan, CancellationToken, ValueTask> delay;

    public TargetCaptureEvidenceRunner(
        ITargetWindowLocator locator,
        ICaptureBackend backend,
        Func<TimeSpan, CancellationToken, ValueTask>? delay = null)
    {
        this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.delay = delay ?? ((duration, cancellationToken) => new ValueTask(Task.Delay(duration, cancellationToken)));
    }

    public async Task<TargetCaptureEvidenceReport> RunAsync(int requestedFrames, CancellationToken cancellationToken)
    {
        if (requestedFrames is < 1 or > 600) throw new ArgumentOutOfRangeException(nameof(requestedFrames));
        TargetWindowDiscoveryResult discovery = locator.Locate();
        WindowIdentity? target = discovery.Target;
        var sink = new CaptureMetricsSink();
        var input = new NullInputAdapter();
        using var coordinator = new CaptureCoordinator(locator, backend, sink, new HostSafetyCoordinator(input));
        long started = Stopwatch.GetTimestamp();
        string code = discovery.DiagnosticCode;

        for (int index = 0; index < requestedFrames; index++)
        {
            CaptureTickResult result = await coordinator.CaptureOnceAsync(cancellationToken).ConfigureAwait(false);
            code = result.Code;
            if (!result.Success) break;
            if (index + 1 < requestedFrames) await delay(FrameInterval, cancellationToken).ConfigureAwait(false);
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        bool success = sink.Metadata.Count == requestedFrames;
        return CreateReport(
            success,
            success ? "CLIENT_CAPTURE_PASS" : code,
            requestedFrames,
            target,
            sink.Metadata,
            elapsed);
    }

    private static TargetCaptureEvidenceReport CreateReport(
        bool success,
        string code,
        int requestedFrames,
        WindowIdentity? target,
        IReadOnlyList<CaptureFrameMetadata> metadata,
        TimeSpan elapsed)
    {
        double[] durations = metadata.Select(frame => frame.CaptureDurationMs).Order().ToArray();
        var backends = metadata
            .GroupBy(frame => frame.CaptureBackend.ToString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        double seconds = Math.Max(elapsed.TotalSeconds, 0.000001);
        return new TargetCaptureEvidenceReport(
            ContractConstants.SchemaVersion,
            DateTimeOffset.UtcNow,
            success,
            code,
            requestedFrames,
            metadata.Count,
            target?.Hwnd,
            target?.Pid,
            target?.ClientWidth,
            target?.ClientHeight,
            target?.Dpi,
            target?.ProcessPathSha256,
            backends,
            metadata.Count / seconds,
            Percentile(durations, 0.50),
            Percentile(durations, 0.95),
            "INPUT_INJECTION=DISABLED");
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 0;
        int index = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private sealed class CaptureMetricsSink : ICaptureFrameSink
    {
        public List<CaptureFrameMetadata> Metadata { get; } = [];

        public void Publish(CapturedFrame frame)
        {
            Metadata.Add(frame.Metadata);
            frame.Dispose();
        }
    }
}
