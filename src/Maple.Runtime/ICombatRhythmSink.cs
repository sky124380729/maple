using Maple.Contracts;

namespace Maple.Runtime;

public interface ICombatRhythmSink
{
    ValueTask PublishAsync(CombatRhythmSnapshot snapshot, CancellationToken cancellationToken);
}

public sealed class NullCombatRhythmSink : ICombatRhythmSink
{
    public static NullCombatRhythmSink Instance { get; } = new();

    private NullCombatRhythmSink()
    {
    }

    public ValueTask PublishAsync(CombatRhythmSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
