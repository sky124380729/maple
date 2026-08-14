using Maple.Capture;
using Maple.Contracts;

namespace Maple.Vision;

public sealed class ObservationFusionOptions
{
    public double ResourceConflictTolerance { get; init; } = 0.08;
}

public sealed class ObservationFusionInput
{
    public long NowMonoMs { get; init; }
    public required CapturedFrame Frame { get; init; }
    public required TargetBinding Target { get; init; }
    public required DynamicVisionResult Dynamic { get; init; }
    public required FixedUiVisionResult FixedUi { get; init; }
}

public sealed class ObservationFusionResult
{
    public ObservationSnapshot? Observation { get; init; }
    public bool HealthReadable { get; init; }
    public PauseReason PauseReason { get; init; }
    public required string Message { get; init; }
}

public sealed class ObservationFusion(ObservationFusionOptions options)
{
    private readonly ObservationFusionOptions options = options ?? throw new ArgumentNullException(nameof(options));

    public ObservationFusionResult Fuse(ObservationFusionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Dynamic.FrameId != input.Frame.Metadata.FrameId || input.FixedUi.FrameId != input.Frame.Metadata.FrameId)
            return Failure(PauseReason.StaleFrame, "视觉结果与采集帧不一致");
        if (input.Dynamic.Self is null || input.Dynamic.Self.FreshUntilMonoMs < input.NowMonoMs)
            return Failure(PauseReason.CalibrationRequired, "Self 观察丢失或已过期");

        bool hpReadable = TrySelectResource(input.FixedUi.HpCandidates, input.NowMonoMs, out ResourceObservation? hp);
        bool mpReadable = TrySelectResource(input.FixedUi.MpCandidates, input.NowMonoMs, out ResourceObservation? mp);
        var observation = new ObservationSnapshot
        {
            SchemaVersion = ContractConstants.SchemaVersion,
            FrameId = input.Frame.Metadata.FrameId,
            CapturedAtMonoMs = input.Frame.Metadata.CapturedAtMonoMs,
            Target = input.Target,
            Self = input.Dynamic.Self,
            Players = Fresh(input.Dynamic.Players, input.NowMonoMs, item => item.FreshUntilMonoMs),
            Monsters = Fresh(input.Dynamic.Monsters, input.NowMonoMs, item => item.FreshUntilMonoMs),
            Loot = FreshOrUnavailable(input.FixedUi.Loot, input.NowMonoMs),
            Hp = hp ?? UnavailableResource(input.NowMonoMs),
            Mp = mp ?? UnavailableResource(input.NowMonoMs),
            Map = FreshOrUnavailable(input.FixedUi.Map, input.NowMonoMs),
            State = SessionState.Observing,
        };
        bool healthReadable = hpReadable && mpReadable;
        return new ObservationFusionResult
        {
            Observation = observation,
            HealthReadable = healthReadable,
            PauseReason = healthReadable ? PauseReason.None : PauseReason.HealthUnknown,
            Message = healthReadable ? "视觉观察融合完成" : "HP/MP 观察冲突或过期",
        };
    }

    private ObservationFusionResult Failure(PauseReason reason, string message) => new() { HealthReadable = false, PauseReason = reason, Message = message };

    private bool TrySelectResource(IReadOnlyList<ResourceObservation>? candidates, long nowMonoMs, out ResourceObservation? selected)
    {
        selected = null;
        if (candidates is null) return false;
        List<ResourceObservation> fresh = candidates.Where(candidate => candidate is not null && candidate.FreshUntilMonoMs >= nowMonoMs && candidate.Confidence > 0).OrderByDescending(candidate => candidate.Confidence).ToList();
        if (fresh.Count == 0) return false;
        selected = fresh[0];
        foreach (ResourceObservation candidate in fresh.Skip(1))
        {
            if (candidate.Mode != selected.Mode || Math.Abs(candidate.Value - selected.Value) > options.ResourceConflictTolerance) { selected = null; return false; }
        }
        return true;
    }

    private static List<T> Fresh<T>(IEnumerable<T>? items, long nowMonoMs, Func<T, long> freshness) where T : class => items?.Where(item => item is not null && freshness(item) >= nowMonoMs).ToList() ?? [];

    private static LootObservation FreshOrUnavailable(LootObservation? value, long nowMonoMs) => value is not null && value.FreshUntilMonoMs >= nowMonoMs ? value : new LootObservation { Visible = false, Confidence = 0, FreshUntilMonoMs = nowMonoMs };

    private static MapObservation FreshOrUnavailable(MapObservation? value, long nowMonoMs) => value is not null && value.FreshUntilMonoMs >= nowMonoMs ? value : new MapObservation { MapId = "unknown", State = MapArchiveState.Candidate, Confidence = 0, FreshUntilMonoMs = nowMonoMs };

    private static ResourceObservation UnavailableResource(long nowMonoMs) => new() { Mode = ResourceMode.Percent, Value = 0, Confidence = 0, FreshUntilMonoMs = nowMonoMs };
}
