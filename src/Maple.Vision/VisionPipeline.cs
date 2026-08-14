using Maple.Capture;
using Maple.Contracts;

namespace Maple.Vision;

public enum VisionPipelineStatus { Ready, DynamicTimedOut, StaleResult, ObservationUnavailable }

public sealed class VisionPipelineOptions
{
    public TimeSpan DynamicTimeout { get; init; } = TimeSpan.FromMilliseconds(100);
    public double CriticalHealthPercent { get; init; } = 0.2;
}

public sealed class VisionPipelineResult
{
    public VisionPipelineStatus Status { get; init; }
    public FixedUiVisionResult? FixedUi { get; init; }
    public DynamicVisionResult? Dynamic { get; init; }
    public ObservationSnapshot? Observation { get; init; }
    public bool HealthCritical { get; init; }
    public required string Diagnostic { get; init; }
}

public sealed class VisionPipeline(IFixedUiVisionProvider fixedProvider, IDynamicVisionProvider dynamicProvider, ObservationFusion fusion, VisionPipelineOptions options)
{
    private readonly IFixedUiVisionProvider fixedProvider = fixedProvider ?? throw new ArgumentNullException(nameof(fixedProvider));
    private readonly IDynamicVisionProvider dynamicProvider = dynamicProvider ?? throw new ArgumentNullException(nameof(dynamicProvider));
    private readonly ObservationFusion fusion = fusion ?? throw new ArgumentNullException(nameof(fusion));
    private readonly VisionPipelineOptions options = options ?? throw new ArgumentNullException(nameof(options));

    public async ValueTask<VisionPipelineResult> ProcessAsync(CapturedFrame frame, TargetBinding target, long nowMonoMs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(target);
        Task<FixedUiVisionResult> fixedTask = fixedProvider.ObserveFixedUiAsync(frame, cancellationToken).AsTask();
        using var dynamicCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<DynamicVisionResult> dynamicTask = dynamicProvider.ObserveDynamicAsync(frame, dynamicCancellation.Token).AsTask();
        FixedUiVisionResult fixedUi = await fixedTask.ConfigureAwait(false);
        bool healthCritical = HasCriticalHealth(fixedUi.HpCandidates);
        Task completed = await Task.WhenAny(dynamicTask, Task.Delay(options.DynamicTimeout, cancellationToken)).ConfigureAwait(false);
        if (completed != dynamicTask)
        {
            dynamicCancellation.Cancel();
            await ObserveCancellationAsync(dynamicTask).ConfigureAwait(false);
            return new VisionPipelineResult { Status = VisionPipelineStatus.DynamicTimedOut, FixedUi = fixedUi, HealthCritical = healthCritical, Diagnostic = "DYNAMIC_DETECTION_TIMEOUT" };
        }
        DynamicVisionResult dynamic = await dynamicTask.ConfigureAwait(false);
        if (fixedUi.FrameId != frame.Metadata.FrameId || dynamic.FrameId != frame.Metadata.FrameId)
            return new VisionPipelineResult { Status = VisionPipelineStatus.StaleResult, FixedUi = fixedUi, Dynamic = dynamic, HealthCritical = healthCritical, Diagnostic = "VISION_FRAME_ID_MISMATCH" };
        ObservationFusionResult fused = fusion.Fuse(new ObservationFusionInput { NowMonoMs = nowMonoMs, Frame = frame, Target = target, Dynamic = dynamic, FixedUi = fixedUi });
        return new VisionPipelineResult
        {
            Status = fused.Observation is null ? VisionPipelineStatus.ObservationUnavailable : VisionPipelineStatus.Ready,
            FixedUi = fixedUi, Dynamic = dynamic, Observation = fused.Observation, HealthCritical = healthCritical, Diagnostic = fused.Message,
        };
    }

    private bool HasCriticalHealth(IEnumerable<ResourceObservation> candidates) => candidates.Any(candidate => candidate.Mode == ResourceMode.Percent && candidate.Confidence > 0 && candidate.Value <= options.CriticalHealthPercent);

    private static async Task ObserveCancellationAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
}
