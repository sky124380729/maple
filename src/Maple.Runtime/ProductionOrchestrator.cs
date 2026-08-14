using Maple.Contracts;
using Maple.Core;

namespace Maple.Runtime;

public sealed record OrchestratorRunResult(PauseReason PauseReason, int ExecutedActions, long LastFrameId);

public sealed class ProductionOrchestrator
{
    private readonly IObservationSource observationSource;
    private readonly IActionExecutor actionExecutor;
    private readonly SafetyGate safetyGate;
    private readonly ActionPolicy actionPolicy;
    private readonly ActionPolicySettings policySettings;
    private readonly OrchestratorOptions options;
    private readonly IRuntimeJournal journal;
    private RuntimeObservationContext? pendingObservation;

    public ProductionOrchestrator(
        IObservationSource observationSource,
        IActionExecutor actionExecutor,
        SafetyGate safetyGate,
        ActionPolicy actionPolicy,
        ActionPolicySettings policySettings,
        OrchestratorOptions options,
        IRuntimeJournal? journal = null)
    {
        this.observationSource = observationSource ?? throw new ArgumentNullException(nameof(observationSource));
        this.actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        this.safetyGate = safetyGate ?? throw new ArgumentNullException(nameof(safetyGate));
        this.actionPolicy = actionPolicy ?? throw new ArgumentNullException(nameof(actionPolicy));
        this.policySettings = policySettings ?? throw new ArgumentNullException(nameof(policySettings));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.journal = journal ?? NullRuntimeJournal.Instance;
        options.Validate();
    }

    public async Task<OrchestratorRunResult> RunUntilPausedAsync(int maximumActions, CancellationToken cancellationToken)
    {
        if (maximumActions < 1) throw new ArgumentOutOfRangeException(nameof(maximumActions));

        int executedActions = 0;
        long lastFrameId = -1;
        try
        {
            while (executedActions < maximumActions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RuntimeObservationContext observation = await TakeObservationAsync(cancellationToken).ConfigureAwait(false);
                lastFrameId = observation.Snapshot.FrameId;
                await WriteJournalAsync("observation.received", observation, null, null, cancellationToken).ConfigureAwait(false);

                SafetyGateDecision safety = EvaluateSafety(observation);
                if (!safety.CanAct)
                {
                    await WriteJournalAsync("session.paused", observation, null, safety.Reason, cancellationToken).ConfigureAwait(false);
                    return new OrchestratorRunResult(safety.Reason, executedActions, lastFrameId);
                }

                ActionDecision decision = Decide(observation, safety);
                if (decision.Action.Type == ActionType.Pause)
                {
                    await WriteJournalAsync("session.paused", observation, decision.Action, decision.PauseReason, cancellationToken).ConfigureAwait(false);
                    return new OrchestratorRunResult(decision.PauseReason, executedActions, lastFrameId);
                }
                if (decision.Action.Type == ActionType.Replan)
                {
                    await WriteJournalAsync("session.paused", observation, decision.Action, PauseReason.CalibrationRequired, cancellationToken).ConfigureAwait(false);
                    return new OrchestratorRunResult(PauseReason.CalibrationRequired, executedActions, lastFrameId);
                }

                await WriteJournalAsync("action.decided", observation, decision.Action, null, cancellationToken).ConfigureAwait(false);
                RuntimeObservationContext feedback = await ExecuteWithFeedbackAsync(decision.Action, cancellationToken).ConfigureAwait(false);
                pendingObservation = feedback;
                lastFrameId = feedback.Snapshot.FrameId;
                executedActions++;
            }

            return new OrchestratorRunResult(PauseReason.WatchdogTimeout, executedActions, lastFrameId);
        }
        finally
        {
            await actionExecutor.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
            await journal.WriteAsync(new RuntimeJournalEntry(ContractConstants.SchemaVersion, "input.releaseAll", 0, lastFrameId), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async ValueTask<RuntimeObservationContext> ExecuteWithFeedbackAsync(AbstractAction action, CancellationToken cancellationToken)
    {
        bool keyIsDown = false;
        RuntimeObservationContext? latest = null;
        try
        {
            await actionExecutor.KeyDownAsync(action, cancellationToken).ConfigureAwait(false);
            keyIsDown = true;
            await journal.WriteAsync(new RuntimeJournalEntry(
                ContractConstants.SchemaVersion,
                "input.keyDown",
                action.IssuedAtMonoMs,
                -1,
                action.ActionId,
                action.Type.ToString(),
                action.ProfileId?.ToString(),
                action.HoldMs), cancellationToken).ConfigureAwait(false);

            for (int frame = 0; frame < options.MaximumFeedbackFramesPerAction; frame++)
            {
                latest = await observationSource.ReadNextAsync(cancellationToken).ConfigureAwait(false);
                await WriteJournalAsync("action.feedback", latest, action, null, cancellationToken).ConfigureAwait(false);
                SafetyGateDecision safety = EvaluateSafety(latest);
                if (!safety.CanAct || ShouldRelease(action, latest, safety))
                {
                    break;
                }
            }

            if (latest is null)
            {
                throw new InvalidOperationException("动作执行期间没有获得反馈画面");
            }
            return latest;
        }
        finally
        {
            if (keyIsDown)
            {
                await actionExecutor.KeyUpAsync(action, CancellationToken.None).ConfigureAwait(false);
                await journal.WriteAsync(new RuntimeJournalEntry(
                    ContractConstants.SchemaVersion,
                    "input.keyUp",
                    latest?.Snapshot.CapturedAtMonoMs ?? action.IssuedAtMonoMs,
                    latest?.Snapshot.FrameId ?? -1,
                    action.ActionId,
                    action.Type.ToString(),
                    action.ProfileId?.ToString(),
                    action.HoldMs), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private bool ShouldRelease(AbstractAction currentAction, RuntimeObservationContext feedback, SafetyGateDecision safety)
    {
        long elapsedMs = Math.Max(0, feedback.Snapshot.CapturedAtMonoMs - currentAction.IssuedAtMonoMs);
        if (elapsedMs >= currentAction.HoldMs) return true;

        ActionDecision next = Decide(feedback, safety);
        return next.Action.Type != currentAction.Type || next.Action.ProfileId != currentAction.ProfileId;
    }

    private ActionDecision Decide(RuntimeObservationContext observation, SafetyGateDecision safety)
    {
        return actionPolicy.Decide(new ActionPolicyContext
        {
            Observation = observation.Snapshot,
            Safety = safety,
            Platform = observation.Platform,
            Settings = policySettings,
            NowMonoMs = observation.Snapshot.CapturedAtMonoMs
        });
    }

    private SafetyGateDecision EvaluateSafety(RuntimeObservationContext observation)
    {
        return safetyGate.Evaluate(new SafetyGateContext
        {
            TargetBound = observation.TargetBound,
            IsForeground = observation.IsForeground,
            FrameFresh = observation.FrameFresh,
            SelfConfidence = observation.Snapshot.Self?.Confidence ?? 0,
            MapValidated = observation.Snapshot.Map?.State == MapArchiveState.Validated,
            HpHealthy = observation.HpHealthy,
            MpHealthy = observation.MpHealthy,
            InputAdapterHealthy = observation.InputAdapterHealthy,
            EmergencyStop = observation.EmergencyStop
        });
    }

    private async ValueTask<RuntimeObservationContext> TakeObservationAsync(CancellationToken cancellationToken)
    {
        if (pendingObservation is not null)
        {
            RuntimeObservationContext result = pendingObservation;
            pendingObservation = null;
            return result;
        }
        return await observationSource.ReadNextAsync(cancellationToken).ConfigureAwait(false);
    }

    private ValueTask WriteJournalAsync(
        string type,
        RuntimeObservationContext observation,
        AbstractAction? action,
        PauseReason? pauseReason,
        CancellationToken cancellationToken)
    {
        return journal.WriteAsync(new RuntimeJournalEntry(
            ContractConstants.SchemaVersion,
            type,
            observation.Snapshot.CapturedAtMonoMs,
            observation.Snapshot.FrameId,
            action?.ActionId,
            action?.Type.ToString(),
            action?.ProfileId?.ToString(),
            action?.HoldMs,
            pauseReason?.ToString()), cancellationToken);
    }
}
