using System.Diagnostics;
using Maple.Capture;
using Maple.Contracts;
using Maple.Input;
using Maple.Vision;

namespace Maple.Host;

public sealed record LiveVisionDiagnosticReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    bool Success,
    string CaptureCode,
    int CapturedFrames,
    string? Hwnd,
    int? Pid,
    int? ClientWidth,
    int? ClientHeight,
    string CaptureBackend,
    double ElapsedMs,
    string ModelId,
    string Provider,
    string PipelineStatus,
    string Diagnostic,
    bool CanDriveActions,
    FixedUiVisionResult? FixedUi,
    DynamicVisionResult? Dynamic,
    ObservationSnapshot? Observation,
    string InputStatus);

public static class LiveVisionDiagnostics
{
    public static async Task<int> RunAsync(string outputPath, CancellationToken cancellationToken)
    {
        OcrRuntimeSelection ocr = OcrRuntime.TryCreate();
        VisionRuntimeBootstrapResult bootstrap = VisionRuntimeBootstrap.Load(
            ocrEngine: ocr.Engine,
            resourceOcrEngine: ocr.ResourceEngine,
            dynamicTimeout: TimeSpan.FromSeconds(5));
        if (!bootstrap.Ready || bootstrap.Pipeline is null)
        {
            Write(outputPath, false, bootstrap.Diagnostic, 0, null, null, bootstrap, null, 0);
            return 2;
        }

        IWindowFrameSource? wgc = null;
        try { wgc = new WindowsWgcFrameSource(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        using var backend = new WindowsGraphicsCaptureBackend(wgc, new WindowsBitBltFrameSource());
        var collector = new FrameCollector();
        using var coordinator = new CaptureCoordinator(
            new WindowsTargetWindowLocator(new Win32WindowSystem()),
            backend,
            collector,
            new HostSafetyCoordinator(new NullInputAdapter()));

        var stopwatch = Stopwatch.StartNew();
        VisionPipelineResult? vision = null;
        CaptureFrameMetadata? lastMetadata = null;
        string captureCode = "NOT_STARTED";
        int capturedFrames = 0;
        try
        {
            for (int index = 0; index < 5; index++)
            {
                CaptureTickResult capture = await coordinator.CaptureOnceAsync(cancellationToken).ConfigureAwait(false);
                captureCode = capture.Code;
                if (!capture.Success || !collector.TryTake(out CapturedFrame? frame) || frame is null)
                {
                    Write(outputPath, false, captureCode, capturedFrames, coordinator.ActiveTargetBinding,
                        lastMetadata, bootstrap, vision, stopwatch.Elapsed.TotalMilliseconds);
                    return 2;
                }

                using (frame)
                {
                    lastMetadata = frame.Metadata;
                    TargetBinding target = coordinator.ActiveTargetBinding
                        ?? throw new InvalidOperationException("TARGET_BINDING_MISSING");
                    vision = await bootstrap.Pipeline
                        .ProcessAsync(frame, target, frame.Metadata.CapturedAtMonoMs, cancellationToken)
                        .ConfigureAwait(false);
                    capturedFrames++;
                }
                if (index < 4) await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { collector.Dispose(); }

        stopwatch.Stop();
        bool success = vision?.Status == VisionPipelineStatus.Ready && vision.Observation is not null;
        Write(outputPath, success, captureCode, capturedFrames, coordinator.ActiveTargetBinding,
            lastMetadata, bootstrap, vision, stopwatch.Elapsed.TotalMilliseconds);
        return success ? 0 : 2;
    }

    private static void Write(
        string outputPath,
        bool success,
        string captureCode,
        int capturedFrames,
        TargetBinding? target,
        CaptureFrameMetadata? metadata,
        VisionRuntimeBootstrapResult bootstrap,
        VisionPipelineResult? vision,
        double elapsedMs)
    {
        WindowsRuntimeDiagnostics.WriteJson(outputPath, new LiveVisionDiagnosticReport(
            1,
            DateTimeOffset.UtcNow,
            success,
            captureCode,
            capturedFrames,
            target?.Hwnd,
            target?.Pid,
            target?.ClientWidth,
            target?.ClientHeight,
            metadata?.CaptureBackend.ToString() ?? "none",
            elapsedMs,
            bootstrap.ModelId,
            RuntimeTelemetryCollector.ProviderLabel(bootstrap.Provider),
            vision?.Status.ToString() ?? "NOT_RUN",
            vision?.Diagnostic ?? bootstrap.Diagnostic,
            vision?.Dynamic?.CanDriveActions == true && vision.Observation is not null,
            vision?.FixedUi,
            vision?.Dynamic,
            vision?.Observation,
            "INPUT_INJECTION=DISABLED"));
    }

    private sealed class FrameCollector : ICaptureFrameSink, IDisposable
    {
        private CapturedFrame? frame;

        public void Publish(CapturedFrame value)
        {
            CapturedFrame? replaced = Interlocked.Exchange(ref frame, value);
            replaced?.Dispose();
        }

        public bool TryTake(out CapturedFrame? value)
        {
            value = Interlocked.Exchange(ref frame, null);
            return value is not null;
        }

        public void Dispose() => Interlocked.Exchange(ref frame, null)?.Dispose();
    }
}
