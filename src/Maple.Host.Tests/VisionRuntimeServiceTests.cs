using Maple.Capture;
using Maple.Contracts;
using Maple.Input;
using Maple.Vision;
using Xunit;

namespace Maple.Host.Tests;

public sealed class VisionRuntimeServiceTests
{
    [Fact]
    public async Task WorkerPublishesLatestResultOffTheCaptureThread()
    {
        using var queue = new LatestVisionFrameQueue(1);
        var processor = new RecordingProcessor();
        var publisher = new RecordingPublisher();
        var input = new RecordingInputAdapter();
        var service = new VisionRuntimeService(queue, processor, Target, new HostSafetyCoordinator(input, () => 500), publisher, () => 2000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Task worker = service.RunAsync(cancellation.Token);
        using CapturedFrame frame = LatestVisionFrameQueueTests.Frame(9, 90);

        queue.Observe(frame);
        VisionRuntimePublication publication = await publisher.Published.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker);

        Assert.Equal(9, publication.Result.Observation!.FrameId);
        Assert.Equal(9, processor.ThreadFrameId);
        Assert.Equal(0, input.ReleaseCalls);
    }

    [Fact]
    public async Task PublicationCarriesTheSameFrameCameraTrackingResult()
    {
        using var queue = new LatestVisionFrameQueue(1);
        var publisher = new RecordingPublisher();
        var tracker = new CameraTransformTracker();
        var service = new VisionRuntimeService(queue, new RecordingProcessor(), Target, new HostSafetyCoordinator(new RecordingInputAdapter(), () => 500), publisher, () => 2_000, tracker);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Task worker = service.RunAsync(cancellation.Token);
        using CapturedFrame frame = LatestVisionFrameQueueTests.Frame(12, 90);

        queue.Observe(frame);
        VisionRuntimePublication publication = await publisher.Published.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker);

        Assert.NotNull(publication.CameraTransform);
        Assert.Equal(12, publication.CameraTransform.FrameId);
        Assert.True(publication.CameraTransform.Ready);
        Assert.Equal("CAMERA_ORIGIN", publication.CameraTransform.Diagnostic);
    }

    [Fact]
    public async Task RepeatedInferenceFaultPausesAndReleasesOnlyOncePerTransition()
    {
        using var queue = new LatestVisionFrameQueue(1);
        var processor = new ThrowingProcessor();
        var publisher = new RecordingPublisher();
        var input = new RecordingInputAdapter();
        var service = new VisionRuntimeService(queue, processor, Target, new HostSafetyCoordinator(input, () => 500), publisher, () => 2000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Task worker = service.RunAsync(cancellation.Token);

        using (CapturedFrame first = LatestVisionFrameQueueTests.Frame(10, 1)) queue.Observe(first);
        await publisher.WaitForFaultsAsync(1, cancellation.Token);
        using (CapturedFrame second = LatestVisionFrameQueueTests.Frame(11, 1)) queue.Observe(second);
        await publisher.WaitForFaultsAsync(2, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker);

        Assert.Equal(1, input.ReleaseCalls);
        Assert.Equal(2, publisher.FaultCount);
        Assert.Equal("VISION_INFERENCE_FAILED:InvalidOperationException", publisher.LastFault);
    }

    private static TargetBinding? Target() => new() { SchemaVersion = 2, Hwnd = "0x1", Pid = 7, ClientWidth = 2, ClientHeight = 2, Dpi = 96 };

    private sealed class RecordingProcessor : IVisionPipelineProcessor
    {
        public long ThreadFrameId { get; private set; }
        public ValueTask<VisionPipelineResult> ProcessAsync(CapturedFrame frame, TargetBinding target, long nowMonoMs, CancellationToken cancellationToken)
        {
            ThreadFrameId = frame.Metadata.FrameId;
            return ValueTask.FromResult(new VisionPipelineResult
            {
                Status = VisionPipelineStatus.Ready,
                Diagnostic = "OK",
                Observation = new ObservationSnapshot { SchemaVersion = 2, FrameId = frame.Metadata.FrameId, CapturedAtMonoMs = frame.Metadata.CapturedAtMonoMs, Target = target },
            });
        }
    }

    private sealed class ThrowingProcessor : IVisionPipelineProcessor
    {
        public ValueTask<VisionPipelineResult> ProcessAsync(CapturedFrame frame, TargetBinding target, long nowMonoMs, CancellationToken cancellationToken) => throw new InvalidOperationException("bad model");
    }

    private sealed class RecordingPublisher : IVisionRuntimePublisher
    {
        private readonly object sync = new();
        public TaskCompletionSource<VisionRuntimePublication> Published { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FaultCount { get; private set; }
        public string? LastFault { get; private set; }
        public void Publish(VisionRuntimePublication publication) => Published.TrySetResult(publication);
        public void PublishFault(string code, long droppedFrames)
        {
            lock (sync) { LastFault = code; FaultCount++; Monitor.PulseAll(sync); }
        }
        public Task WaitForFaultsAsync(int count, CancellationToken cancellationToken) => Task.Run(() =>
        {
            lock (sync) while (FaultCount < count) { cancellationToken.ThrowIfCancellationRequested(); Monitor.Wait(sync, 25); }
        }, cancellationToken);
    }

    private sealed class RecordingInputAdapter : IInputAdapter
    {
        public int ReleaseCalls { get; private set; }
        public InputResult ReleaseAll(long nowMonoMs) { ReleaseCalls++; return Result(nowMonoMs); }
        public InputResult KeyDown(AbstractAction action, string key, long nowMonoMs) => Result(nowMonoMs);
        public InputResult KeyUp(AbstractAction action, string key, long nowMonoMs) => Result(nowMonoMs);
        public InputResult Press(AbstractAction action, string key, long nowMonoMs) => Result(nowMonoMs);
        public bool Heartbeat(long nowMonoMs) => true;
        public InputAdapterStatus GetStatus() => new();
        private static InputResult Result(long time) => new() { SchemaVersion = 2, ActionId = "test", Status = InputStatus.Completed, StartedAtMonoMs = time, EndedAtMonoMs = time, ReleasedKeys = [] };
    }
}
