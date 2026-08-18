namespace Maple.Runtime;

public sealed class OrchestratorOptions
{
    public int MaximumFeedbackFramesPerAction { get; init; } = 16;
    public bool StationaryRhythmEnabled { get; init; } = true;
    public int RhythmUpdateIntervalMs { get; init; } = 250;

    internal void Validate()
    {
        if (MaximumFeedbackFramesPerAction is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumFeedbackFramesPerAction));
        }
        if (RhythmUpdateIntervalMs is < 50 or > 5_000)
        {
            throw new ArgumentOutOfRangeException(nameof(RhythmUpdateIntervalMs));
        }
    }
}
