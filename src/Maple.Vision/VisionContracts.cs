using Maple.Capture;
using Maple.Contracts;

namespace Maple.Vision;

public sealed class DynamicVisionResult
{
    public long FrameId { get; init; }
    public SelfObservation? Self { get; init; }
    public List<PlayerObservation> Players { get; init; } = [];
    public List<MonsterObservation> Monsters { get; init; } = [];
    public string ModelVersion { get; init; } = "unknown";
    public bool CanDriveActions { get; init; }
    public string Diagnostic { get; init; } = string.Empty;
}

public sealed class FixedUiVisionResult
{
    public long FrameId { get; init; }
    public LootObservation Loot { get; init; } = new();
    public MapObservation Map { get; init; } = new();
    public List<ResourceObservation> HpCandidates { get; init; } = [];
    public List<ResourceObservation> MpCandidates { get; init; } = [];
}

public interface IDynamicVisionProvider
{
    ValueTask<DynamicVisionResult> ObserveDynamicAsync(CapturedFrame frame, CancellationToken cancellationToken);
}

public interface IFixedUiVisionProvider
{
    ValueTask<FixedUiVisionResult> ObserveFixedUiAsync(CapturedFrame frame, CancellationToken cancellationToken);
}
