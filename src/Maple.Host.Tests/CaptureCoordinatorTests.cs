using System.Buffers;
using Maple.Capture;
using Maple.Contracts;
using Maple.Host;
using Maple.Input;
using Xunit;

namespace Maple.Host.Tests;

public sealed class CaptureCoordinatorTests
{
    [Fact]
    public async Task MissingTargetPausesAndDoesNotCapture()
    {
        var input = new RecordingInputAdapter();
        var backend = new RecordingCaptureBackend();
        var coordinator = CreateCoordinator(
            new TargetWindowDiscoveryResult(TargetWindowDiscoveryStatus.NotFound, "TARGET_NOT_FOUND", []),
            backend,
            new RecordingFrameSink(),
            input);

        CaptureTickResult result = await coordinator.CaptureOnceAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("TARGET_NOT_FOUND", result.Code);
        Assert.Equal(0, backend.Calls);
        Assert.Equal(1, input.ReleaseCalls);
    }

    [Theory]
    [InlineData(true, true, "TARGET_MINIMIZED")]
    [InlineData(false, false, "TARGET_NOT_FOREGROUND")]
    public async Task UnsafeWindowStatePausesBeforeCapture(bool minimized, bool foreground, string expectedCode)
    {
        var input = new RecordingInputAdapter();
        var backend = new RecordingCaptureBackend();
        WindowIdentity target = ValidTarget() with { IsMinimized = minimized, IsForeground = foreground };
        var coordinator = CreateCoordinator(Found(target), backend, new RecordingFrameSink(), input);

        CaptureTickResult result = await coordinator.CaptureOnceAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(0, backend.Calls);
        Assert.Equal(1, input.ReleaseCalls);
    }

    [Fact]
    public async Task SuccessfulFrameTransfersOwnershipToPreviewSink()
    {
        var input = new RecordingInputAdapter();
        var sink = new RecordingFrameSink();
        var backend = new RecordingCaptureBackend { Next = SuccessFrame(80) };
        var coordinator = CreateCoordinator(Found(ValidTarget()), backend, sink, input);

        CaptureTickResult result = await coordinator.CaptureOnceAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("OK", result.Code);
        Assert.Equal(1, backend.Calls);
        Assert.Equal(0, input.ReleaseCalls);
        Assert.NotNull(sink.Frame);
        sink.Dispose();
    }

    [Fact]
    public async Task BlackFrameIsDisposedAndFailsClosed()
    {
        var input = new RecordingInputAdapter();
        var sink = new RecordingFrameSink();
        CaptureResult black = SuccessFrame(0);
        var backend = new RecordingCaptureBackend { Next = black };
        var coordinator = CreateCoordinator(Found(ValidTarget()), backend, sink, input);

        CaptureTickResult result = await coordinator.CaptureOnceAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CAPTURE_BLACK_FRAME", result.Code);
        Assert.Null(sink.Frame);
        Assert.Equal(1, input.ReleaseCalls);
        Assert.Throws<ObjectDisposedException>(() => _ = black.Frame!.Pixels);
    }

    [Fact]
    public async Task RepeatedIdenticalPauseOnlyReleasesInputOnce()
    {
        var input = new RecordingInputAdapter();
        var backend = new RecordingCaptureBackend();
        var coordinator = CreateCoordinator(
            new TargetWindowDiscoveryResult(TargetWindowDiscoveryStatus.NotFound, "TARGET_NOT_FOUND", []),
            backend,
            new RecordingFrameSink(),
            input);

        CaptureTickResult first = await coordinator.CaptureOnceAsync(CancellationToken.None);
        CaptureTickResult second = await coordinator.CaptureOnceAsync(CancellationToken.None);

        Assert.False(first.Success);
        Assert.False(second.Success);
        Assert.Equal(1, input.ReleaseCalls);
    }

    private static CaptureCoordinator CreateCoordinator(
        TargetWindowDiscoveryResult discovery,
        ICaptureBackend backend,
        ICaptureFrameSink sink,
        RecordingInputAdapter input)
    {
        return new CaptureCoordinator(
            new FixedTargetLocator(discovery),
            backend,
            sink,
            new HostSafetyCoordinator(input, () => 500),
            () => 1000);
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

    private static CaptureResult SuccessFrame(byte value)
    {
        const int width = 16;
        const int height = 16;
        const int stride = width * 4;
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(stride * height);
        owner.Memory.Span[..(stride * height)].Fill(value);
        var metadata = new CaptureFrameMetadata
        {
            SchemaVersion = ContractConstants.SchemaVersion,
            FrameId = 7,
            CapturedAtMonoMs = 1000,
            ClientWidth = width,
            ClientHeight = height,
            Dpi = 96,
            CaptureBackend = CaptureBackend.BitBlt,
            DroppedReason = DroppedFrameReason.None,
        };
        var frame = new CapturedFrame(metadata, width, height, stride, CapturedPixelFormat.Bgra32, owner, stride * height);
        return new CaptureResult { Success = true, Frame = frame, Metadata = metadata, Reason = "OK" };
    }

    private sealed class FixedTargetLocator(TargetWindowDiscoveryResult result) : ITargetWindowLocator
    {
        public TargetWindowDiscoveryResult Locate() => result;
    }

    private sealed class RecordingCaptureBackend : ICaptureBackend
    {
        public int Calls { get; private set; }
        public CaptureResult? Next { get; init; }
        public CaptureBackend Backend => CaptureBackend.BitBlt;

        public ValueTask<CaptureResult> CaptureAsync(CaptureTarget target, long frameId, long nowMonoMs, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(Next ?? new CaptureResult
            {
                Success = false,
                Reason = "CAPTURE_FRAME_UNAVAILABLE",
                Metadata = new CaptureFrameMetadata
                {
                    SchemaVersion = ContractConstants.SchemaVersion,
                    FrameId = frameId,
                    CapturedAtMonoMs = nowMonoMs,
                    ClientWidth = target.ClientWidth,
                    ClientHeight = target.ClientHeight,
                    Dpi = target.Dpi,
                    CaptureBackend = CaptureBackend.BitBlt,
                    DroppedReason = DroppedFrameReason.Invalid,
                },
            });
        }
    }

    private sealed class RecordingFrameSink : ICaptureFrameSink, IDisposable
    {
        public CapturedFrame? Frame { get; private set; }
        public void Publish(CapturedFrame frame) => Frame = frame;
        public void Dispose() { Frame?.Dispose(); Frame = null; }
    }

    private sealed class RecordingInputAdapter : IInputAdapter
    {
        public int ReleaseCalls { get; private set; }
        public InputResult KeyDown(AbstractAction action, string key, long nowMonoMs) => Result(action.ActionId, nowMonoMs);
        public InputResult KeyUp(AbstractAction action, string key, long nowMonoMs) => Result(action.ActionId, nowMonoMs);
        public InputResult Press(AbstractAction action, string key, long nowMonoMs) => Result(action.ActionId, nowMonoMs);
        public bool Heartbeat(long nowMonoMs) => true;
        public InputAdapterStatus GetStatus() => new();
        public InputResult ReleaseAll(long nowMonoMs) { ReleaseCalls++; return Result("release-all", nowMonoMs); }

        private static InputResult Result(string actionId, long nowMonoMs) => new()
        {
            SchemaVersion = ContractConstants.SchemaVersion,
            ActionId = actionId,
            Status = InputStatus.Completed,
            StartedAtMonoMs = nowMonoMs,
            EndedAtMonoMs = nowMonoMs,
            ReleasedKeys = [],
        };
    }
}
