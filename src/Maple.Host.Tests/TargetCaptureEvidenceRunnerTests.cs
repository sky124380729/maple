using System.Buffers;
using Maple.Capture;
using Maple.Contracts;
using Xunit;

namespace Maple.Host.Tests;

public sealed class TargetCaptureEvidenceRunnerTests
{
    [Fact]
    public async Task CapturesStableForegroundFramesAndReportsMetrics()
    {
        WindowIdentity target = ValidTarget();
        var backend = new RecordingBackend();
        var runner = new TargetCaptureEvidenceRunner(
            new SequenceLocator(Found(target), Found(target), Found(target), Found(target)),
            backend,
            (_, _) => ValueTask.CompletedTask);

        TargetCaptureEvidenceReport report = await runner.RunAsync(3, CancellationToken.None);

        Assert.True(report.Success);
        Assert.Equal("CLIENT_CAPTURE_PASS", report.Code);
        Assert.Equal(3, report.RequestedFrames);
        Assert.Equal(3, report.CapturedFrames);
        Assert.Equal(target.Hwnd, report.Hwnd);
        Assert.Equal(target.Pid, report.Pid);
        Assert.Equal(3, report.CaptureBackends["Wgc"]);
        Assert.True(report.EffectiveFps > 0);
        Assert.True(report.P95CaptureDurationMs >= report.P50CaptureDurationMs);
        Assert.Equal("INPUT_INJECTION=DISABLED", report.InputStatus);
        Assert.True(backend.Disposed);
    }

    [Fact]
    public async Task MinimizedTargetFailsBeforeCapture()
    {
        WindowIdentity target = ValidTarget() with { IsMinimized = true, ClientWidth = 0, ClientHeight = 0 };
        var backend = new RecordingBackend();
        var runner = new TargetCaptureEvidenceRunner(
            new SequenceLocator(Found(target), Found(target)),
            backend,
            (_, _) => ValueTask.CompletedTask);

        TargetCaptureEvidenceReport report = await runner.RunAsync(3, CancellationToken.None);

        Assert.False(report.Success);
        Assert.Equal("TARGET_MINIMIZED", report.Code);
        Assert.Equal(0, report.CapturedFrames);
        Assert.Equal(0, backend.Calls);
        Assert.True(backend.Disposed);
    }

    [Fact]
    public async Task FocusLossDuringCaptureStopsImmediately()
    {
        WindowIdentity target = ValidTarget();
        var backend = new RecordingBackend();
        var runner = new TargetCaptureEvidenceRunner(
            new SequenceLocator(Found(target), Found(target), Found(target with { IsForeground = false })),
            backend,
            (_, _) => ValueTask.CompletedTask);

        TargetCaptureEvidenceReport report = await runner.RunAsync(3, CancellationToken.None);

        Assert.False(report.Success);
        Assert.Equal("TARGET_NOT_FOREGROUND", report.Code);
        Assert.Equal(1, report.CapturedFrames);
        Assert.Equal(1, backend.Calls);
        Assert.True(backend.Disposed);
    }

    private static TargetWindowDiscoveryResult Found(WindowIdentity target) =>
        new(TargetWindowDiscoveryStatus.Found, "TARGET_BOUND", [target]);

    private static WindowIdentity ValidTarget() => new(
        "0x0000000000012345",
        4768,
        "冒险岛怀旧服",
        "UnityWndClass",
        true,
        false,
        10,
        20,
        1280,
        720,
        96,
        DateTimeOffset.UtcNow,
        new string('a', 64),
        "1.0.0");

    private sealed class SequenceLocator(params TargetWindowDiscoveryResult[] sequence) : ITargetWindowLocator
    {
        private int index;
        public TargetWindowDiscoveryResult Locate()
        {
            int current = Math.Min(Interlocked.Increment(ref index) - 1, sequence.Length - 1);
            return sequence[current];
        }
    }

    private sealed class RecordingBackend : ICaptureBackend, IDisposable
    {
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }
        public CaptureBackend Backend => CaptureBackend.Wgc;

        public ValueTask<CaptureResult> CaptureAsync(CaptureTarget target, long frameId, long nowMonoMs, CancellationToken cancellationToken)
        {
            Calls++;
            const int width = 16;
            const int height = 16;
            const int stride = width * 4;
            IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(stride * height);
            owner.Memory.Span[..(stride * height)].Fill(32);
            var metadata = new CaptureFrameMetadata
            {
                SchemaVersion = ContractConstants.SchemaVersion,
                FrameId = frameId,
                CapturedAtMonoMs = nowMonoMs,
                ClientWidth = target.ClientWidth,
                ClientHeight = target.ClientHeight,
                Dpi = target.Dpi,
                CaptureBackend = CaptureBackend.Wgc,
                CaptureDurationMs = Calls,
                DroppedReason = DroppedFrameReason.None,
            };
            var frame = new CapturedFrame(metadata, width, height, stride, CapturedPixelFormat.Bgra32, owner, stride * height);
            return ValueTask.FromResult(new CaptureResult { Success = true, Reason = "OK", Metadata = metadata, Frame = frame });
        }

        public void Dispose() => Disposed = true;
    }
}
