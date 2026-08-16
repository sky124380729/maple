using Maple.Contracts;

namespace Maple.Host;

public sealed record CombatTrialEvidenceReport(
    int SchemaVersion,
    bool Success,
    string Code,
    long StartFrameId,
    long LastFrameId,
    int ExecutedActions,
    int MovementActions,
    int AttackActions,
    bool FeedbackFrameAdvanced,
    bool AllKeysReleased,
    string PauseReason,
    IReadOnlyList<string> Actions,
    DateTimeOffset CompletedAtUtc);

public sealed class CombatTrialEvidenceRecorder(long startFrameId)
{
    private readonly List<string> actions = [];

    public void Record(AbstractAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        actions.Add(action.Type.ToString());
    }

    public CombatTrialEvidenceReport Complete(CombatTrialCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        int movements = actions.Count(value => value is nameof(ActionType.MoveLeft) or nameof(ActionType.MoveRight));
        int attacks = actions.Count(value => value == nameof(ActionType.Attack));
        bool feedbackAdvanced = completion.LastFrameId > startFrameId;
        bool success = attacks > 0
            && completion.ExecutedActions > 0
            && feedbackAdvanced
            && completion.AllKeysReleased
            && !completion.Code.StartsWith("TRIAL_FAULT", StringComparison.Ordinal);
        return new CombatTrialEvidenceReport(
            1,
            success,
            success ? "COMBAT_CLOSED_LOOP_CONFIRMED" : completion.Code,
            startFrameId,
            completion.LastFrameId,
            completion.ExecutedActions,
            movements,
            attacks,
            feedbackAdvanced,
            completion.AllKeysReleased,
            completion.PauseReason.ToString(),
            actions.ToArray(),
            DateTimeOffset.UtcNow);
    }
}
