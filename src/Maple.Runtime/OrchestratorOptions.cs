namespace Maple.Runtime;

public sealed class OrchestratorOptions
{
    public int MaximumFeedbackFramesPerAction { get; init; } = 16;

    internal void Validate()
    {
        if (MaximumFeedbackFramesPerAction is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumFeedbackFramesPerAction));
        }
    }
}
