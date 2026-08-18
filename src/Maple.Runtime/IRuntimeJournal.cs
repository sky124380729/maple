using Maple.Contracts;

namespace Maple.Runtime;

public sealed record RuntimeJournalEntry(
    int SchemaVersion,
    string Type,
    long TimestampMonoMs,
    long FrameId,
    string? ActionId = null,
    string? ActionType = null,
    string? ProfileId = null,
    int? ComputedHoldMs = null,
    string? PauseReason = null,
    long? RhythmCycleId = null,
    string? RhythmPhase = null,
    int? PlannedDurationMs = null,
    int? RemainingDurationMs = null,
    string? EarlyReleaseReason = null);

public interface IRuntimeJournal
{
    ValueTask WriteAsync(RuntimeJournalEntry entry, CancellationToken cancellationToken);
}

public sealed class NullRuntimeJournal : IRuntimeJournal
{
    public static NullRuntimeJournal Instance { get; } = new();

    private NullRuntimeJournal()
    {
    }

    public ValueTask WriteAsync(RuntimeJournalEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
