using Maple.Capture;
using Maple.Contracts;
using Maple.Vision;

namespace Maple.Host;

public interface IVisionPipelineProcessor
{
    ValueTask<VisionPipelineResult> ProcessAsync(CapturedFrame frame, TargetBinding target, long nowMonoMs, CancellationToken cancellationToken);
}

public sealed class VisionPipelineProcessor(VisionPipeline pipeline) : IVisionPipelineProcessor
{
    private readonly VisionPipeline pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    public ValueTask<VisionPipelineResult> ProcessAsync(CapturedFrame frame, TargetBinding target, long nowMonoMs, CancellationToken cancellationToken) =>
        pipeline.ProcessAsync(frame, target, nowMonoMs, cancellationToken);
}

public sealed record VisionRuntimePublication(
    VisionPipelineResult Result,
    CaptureFrameMetadata FrameMetadata,
    TargetBinding Target,
    double DetectorLatencyMs,
    double QueueAgeMs,
    long DroppedFrames,
    FrameCameraTransform? CameraTransform = null);

public interface IVisionRuntimePublisher
{
    void Publish(VisionRuntimePublication publication);
    void PublishFault(string code, long droppedFrames);
}

public sealed class VisionRuntimeService
{
    private readonly LatestVisionFrameQueue frames;
    private readonly IVisionPipelineProcessor pipeline;
    private readonly Func<TargetBinding?> targetProvider;
    private readonly HostSafetyCoordinator safety;
    private readonly IVisionRuntimePublisher publisher;
    private readonly Func<long> clock;
    private readonly CameraTransformTracker? cameraTracker;
    private int running;
    private bool faulted;

    public VisionRuntimeService(
        LatestVisionFrameQueue frames,
        IVisionPipelineProcessor pipeline,
        Func<TargetBinding?> targetProvider,
        HostSafetyCoordinator safety,
        IVisionRuntimePublisher publisher,
        Func<long>? clock = null,
        CameraTransformTracker? cameraTracker = null)
    {
        this.frames = frames ?? throw new ArgumentNullException(nameof(frames));
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        this.targetProvider = targetProvider ?? throw new ArgumentNullException(nameof(targetProvider));
        this.safety = safety ?? throw new ArgumentNullException(nameof(safety));
        this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        this.clock = clock ?? (() => Environment.TickCount64);
        this.cameraTracker = cameraTracker;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("VISION_WORKER_ALREADY_RUNNING");
        try
        {
            while (await frames.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                using CapturedFrame frame = frames.TakeLatest(cancellationToken);
                TargetBinding? target = targetProvider();
                if (target is null)
                {
                    PublishFault("VISION_TARGET_UNAVAILABLE");
                    continue;
                }

                long started = clock();
                try
                {
                    FrameCameraTransform? cameraTransform = cameraTracker?.Track(frame);
                    VisionPipelineResult result = await pipeline.ProcessAsync(frame, target, started, cancellationToken).ConfigureAwait(false);
                    long completed = clock();
                    publisher.Publish(new VisionRuntimePublication(
                        result, frame.Metadata, target, Math.Max(0, completed - started),
                        Math.Max(0, started - frame.Metadata.CapturedAtMonoMs), frames.DroppedFrames, cameraTransform));
                    faulted = false;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    PublishFault("VISION_INFERENCE_FAILED:" + exception.GetType().Name);
                }
            }
        }
        finally { Interlocked.Exchange(ref running, 0); }
    }

    private void PublishFault(string code)
    {
        publisher.PublishFault(code, frames.DroppedFrames);
        if (faulted) return;
        faulted = true;
        safety.PauseAndRelease(PauseReason.SafetyViolation);
    }
}
