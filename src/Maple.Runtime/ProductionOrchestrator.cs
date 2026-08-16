using Maple.Contracts;
using Maple.Core;
using System.Security.Cryptography;

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
    private readonly ActionTimingRandomizer timingRandomizer;
    private readonly Action<AbstractAction>? actionAccepted;
    private RuntimeObservationContext? pendingObservation;

    public ProductionOrchestrator(
        IObservationSource observationSource,
        IActionExecutor actionExecutor,
        SafetyGate safetyGate,
        ActionPolicy actionPolicy,
        ActionPolicySettings policySettings,
        OrchestratorOptions options,
        IRuntimeJournal? journal = null,
        ActionTimingRandomizer? timingRandomizer = null,
        Action<AbstractAction>? actionAccepted = null)
    {
        this.observationSource = observationSource ?? throw new ArgumentNullException(nameof(observationSource));
        this.actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        this.safetyGate = safetyGate ?? throw new ArgumentNullException(nameof(safetyGate));
        this.actionPolicy = actionPolicy ?? throw new ArgumentNullException(nameof(actionPolicy));
        this.policySettings = policySettings ?? throw new ArgumentNullException(nameof(policySettings));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.journal = journal ?? NullRuntimeJournal.Instance;
        this.timingRandomizer = timingRandomizer ?? new ActionTimingRandomizer(
            RandomNumberGenerator.GetInt32(int.MaxValue),
            maximumFraction: 0.08);
        this.actionAccepted = actionAccepted;
        options.Validate();
    }

    public async Task<OrchestratorRunResult> RunUntilPausedAsync(int maximumActions, CancellationToken cancellationToken)
    {
        if (maximumActions < 1) throw new ArgumentOutOfRangeException(nameof(maximumActions));
        return await RunUntilPausedCoreAsync(maximumActions, cancellationToken).ConfigureAwait(false);
    }

    public Task<OrchestratorRunResult> RunUntilPausedAsync(CancellationToken cancellationToken) =>
        RunUntilPausedCoreAsync(null, cancellationToken);

    private async Task<OrchestratorRunResult> RunUntilPausedCoreAsync(int? maximumActions, CancellationToken cancellationToken)
    {
        int executedActions = 0;
        long lastFrameId = -1;
        try
        {
            while (!maximumActions.HasValue || executedActions < maximumActions.Value)
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

                (AbstractAction action, ActionTimingDecision timing) = ApplyTiming(decision.Action);
                await WriteJournalAsync(
                    "action.decided",
                    observation,
                    action,
                    null,
                    cancellationToken,
                    timing).ConfigureAwait(false);
                RuntimeObservationContext feedback = await ExecuteWithFeedbackAsync(action, cancellationToken).ConfigureAwait(false);
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
            actionAccepted?.Invoke(action);
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
        CancellationToken cancellationToken,
        ActionTimingDecision? timing = null)
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
            pauseReason?.ToString(),
            timing?.Seed,
            timing?.BaselineHoldMs,
            timing?.VariationMs,
            timing?.FinalHoldMs), cancellationToken);
    }

    private (AbstractAction Action, ActionTimingDecision Timing) ApplyTiming(AbstractAction action)
    {
        int minimum;
        int maximum = action.MaxDurationMs;
        bool randomize = true;
        switch (action.Type)
        {
            case ActionType.MoveLeft:
            case ActionType.MoveRight:
                minimum = policySettings.MinMoveHoldMs;
                maximum = Math.Min(maximum, policySettings.MaxMoveHoldMs);
                break;
            case ActionType.Jump:
            case ActionType.ClimbUp:
            case ActionType.ClimbDown:
                minimum = 60;
                break;
            case ActionType.Attack:
                minimum = 20;
                break;
            case ActionType.Pickup:
                minimum = 40;
                break;
            case ActionType.UsePotion:
                minimum = Math.Clamp(action.HoldMs, 0, maximum);
                randomize = false;
                break;
            default:
                minimum = 0;
                randomize = false;
                break;
        }

        minimum = Math.Min(minimum, maximum);
        ActionTimingDecision timing = randomize
            ? timingRandomizer.ApplyWithTrace(action.HoldMs, minimum, maximum)
            : new ActionTimingDecision(
                timingRandomizer.Seed,
                action.HoldMs,
                0,
                Math.Clamp(action.HoldMs, minimum, maximum));
        var adjusted = new AbstractAction
        {
            ActionId = action.ActionId,
            Type = action.Type,
            ProfileId = action.ProfileId,
            IssuedAtMonoMs = action.IssuedAtMonoMs,
            HoldMs = timing.FinalHoldMs,
            MaxDurationMs = action.MaxDurationMs
        };
        return (adjusted, timing);
    }
}
