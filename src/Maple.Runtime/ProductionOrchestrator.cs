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
    private readonly StationaryAttackRhythmSampler rhythmSampler;
    private readonly ICombatRhythmSink rhythmSink;
    private RuntimeObservationContext? pendingObservation;
    private long rhythmCycleSequence;
    private long lastRhythmPublishedAtMonoMs = long.MinValue;

    public ProductionOrchestrator(
        IObservationSource observationSource,
        IActionExecutor actionExecutor,
        SafetyGate safetyGate,
        ActionPolicy actionPolicy,
        ActionPolicySettings policySettings,
        OrchestratorOptions options,
        IRuntimeJournal? journal = null,
        StationaryAttackRhythmSampler? rhythmSampler = null,
        ICombatRhythmSink? rhythmSink = null)
    {
        this.observationSource = observationSource ?? throw new ArgumentNullException(nameof(observationSource));
        this.actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        this.safetyGate = safetyGate ?? throw new ArgumentNullException(nameof(safetyGate));
        this.actionPolicy = actionPolicy ?? throw new ArgumentNullException(nameof(actionPolicy));
        this.policySettings = policySettings ?? throw new ArgumentNullException(nameof(policySettings));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.journal = journal ?? NullRuntimeJournal.Instance;
        this.rhythmSampler = rhythmSampler ?? new StationaryAttackRhythmSampler(new SystemRandomSource(), new StationaryAttackRhythmOptions());
        this.rhythmSink = rhythmSink ?? NullCombatRhythmSink.Instance;
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
                if (options.StationaryRhythmEnabled && decision.Action.Type == ActionType.Attack)
                {
                    RuntimeObservationContext cycleFeedback = await ExecuteStationaryRhythmCycleAsync(decision.Action, cancellationToken).ConfigureAwait(false);
                    pendingObservation = cycleFeedback;
                    lastFrameId = cycleFeedback.Snapshot.FrameId;
                    executedActions++;
                    continue;
                }
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

    private async ValueTask<RuntimeObservationContext> ExecuteStationaryRhythmCycleAsync(
        AbstractAction attackDecision,
        CancellationToken cancellationToken)
    {
        long cycleId = ++rhythmCycleSequence;
        int attackHoldMs = rhythmSampler.SampleAttackHoldMs();
        AbstractAction attack = CreateRhythmAction(
            $"rhythm-{cycleId}-attack",
            ActionType.Attack,
            attackDecision.ProfileId,
            attackDecision.IssuedAtMonoMs,
            attackHoldMs,
            ContractConstants.MaxAttackDurationMs);
        await PublishRhythmAsync(cycleId, CombatRhythmPhase.AttackHolding, attackHoldMs, attackHoldMs, attack.IssuedAtMonoMs, null, true, cancellationToken).ConfigureAwait(false);
        TimedActionResult attackResult = await ExecuteRhythmActionAsync(
            cycleId,
            CombatRhythmPhase.AttackHolding,
            attack,
            allowPolicyAttack: true,
            cancellationToken).ConfigureAwait(false);
        if (!attackResult.Completed) return attackResult.Latest;

        HorizontalDirection firstDirection = rhythmSampler.SampleFirstDirection();
        ActionType firstType = firstDirection == HorizontalDirection.Left ? ActionType.MoveLeft : ActionType.MoveRight;
        ActionType secondType = firstDirection == HorizontalDirection.Left ? ActionType.MoveRight : ActionType.MoveLeft;

        int firstHoldMs = rhythmSampler.SampleMovementHoldMs();
        AbstractAction firstMove = CreateRhythmAction(
            $"rhythm-{cycleId}-move-1",
            firstType,
            null,
            attackResult.Latest.Snapshot.CapturedAtMonoMs,
            firstHoldMs,
            ContractConstants.MaxActionDurationMs);
        await PublishRhythmAsync(cycleId, PhaseFor(firstType), firstHoldMs, firstHoldMs, firstMove.IssuedAtMonoMs, null, true, cancellationToken).ConfigureAwait(false);
        TimedActionResult firstMoveResult = await ExecuteRhythmActionAsync(
            cycleId,
            PhaseFor(firstType),
            firstMove,
            allowPolicyAttack: false,
            cancellationToken).ConfigureAwait(false);
        if (!firstMoveResult.Completed) return firstMoveResult.Latest;

        int gapMs = rhythmSampler.SampleMovementGapMs();
        await PublishRhythmAsync(cycleId, CombatRhythmPhase.MovementGap, gapMs, gapMs, firstMoveResult.Latest.Snapshot.CapturedAtMonoMs, null, true, cancellationToken).ConfigureAwait(false);
        TimedWaitResult gapResult = await WaitWithFeedbackAsync(
            cycleId,
            CombatRhythmPhase.MovementGap,
            firstMoveResult.Latest,
            gapMs,
            cancellationToken).ConfigureAwait(false);
        if (!gapResult.Completed) return gapResult.Latest;

        int secondHoldMs = rhythmSampler.SampleMovementHoldMs();
        AbstractAction secondMove = CreateRhythmAction(
            $"rhythm-{cycleId}-move-2",
            secondType,
            null,
            gapResult.Latest.Snapshot.CapturedAtMonoMs,
            secondHoldMs,
            ContractConstants.MaxActionDurationMs);
        await PublishRhythmAsync(cycleId, PhaseFor(secondType), secondHoldMs, secondHoldMs, secondMove.IssuedAtMonoMs, null, true, cancellationToken).ConfigureAwait(false);
        TimedActionResult secondMoveResult = await ExecuteRhythmActionAsync(
            cycleId,
            PhaseFor(secondType),
            secondMove,
            allowPolicyAttack: false,
            cancellationToken).ConfigureAwait(false);
        if (!secondMoveResult.Completed || !rhythmSampler.ShouldRest()) return secondMoveResult.Latest;

        int restMs = rhythmSampler.SampleRestMs();
        await PublishRhythmAsync(cycleId, CombatRhythmPhase.Resting, restMs, restMs, secondMoveResult.Latest.Snapshot.CapturedAtMonoMs, null, true, cancellationToken).ConfigureAwait(false);
        TimedWaitResult restResult = await WaitWithFeedbackAsync(
            cycleId,
            CombatRhythmPhase.Resting,
            secondMoveResult.Latest,
            restMs,
            cancellationToken).ConfigureAwait(false);
        return restResult.Latest;
    }

    private async ValueTask<TimedActionResult> ExecuteRhythmActionAsync(
        long cycleId,
        CombatRhythmPhase phase,
        AbstractAction action,
        bool allowPolicyAttack,
        CancellationToken cancellationToken)
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

            while (true)
            {
                latest = await observationSource.ReadNextAsync(cancellationToken).ConfigureAwait(false);
                await WriteJournalAsync("action.feedback", latest, action, null, cancellationToken).ConfigureAwait(false);
                SafetyGateDecision safety = EvaluateSafety(latest);
                string? interruptionReason = GetRhythmInterruptionReason(latest, safety, action, allowPolicyAttack);
                long elapsedMs = Math.Max(0, latest.Snapshot.CapturedAtMonoMs - action.IssuedAtMonoMs);
                int remainingMs = (int)Math.Max(0, action.HoldMs - elapsedMs);
                if (interruptionReason is not null)
                {
                    await PublishRhythmAsync(cycleId, phase, action.HoldMs, remainingMs, latest.Snapshot.CapturedAtMonoMs, interruptionReason, true, cancellationToken).ConfigureAwait(false);
                    return new TimedActionResult(latest, false);
                }

                await PublishRhythmAsync(cycleId, phase, action.HoldMs, remainingMs, latest.Snapshot.CapturedAtMonoMs, null, remainingMs == 0, cancellationToken).ConfigureAwait(false);
                if (elapsedMs >= action.HoldMs)
                {
                    return new TimedActionResult(latest, true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            long updatedAtMonoMs = latest?.Snapshot.CapturedAtMonoMs ?? action.IssuedAtMonoMs;
            int remainingMs = (int)Math.Max(0, action.HoldMs - (updatedAtMonoMs - action.IssuedAtMonoMs));
            await PublishRhythmAsync(
                cycleId,
                phase,
                action.HoldMs,
                remainingMs,
                updatedAtMonoMs,
                "Cancelled",
                true,
                CancellationToken.None).ConfigureAwait(false);
            throw;
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

    private async ValueTask<TimedWaitResult> WaitWithFeedbackAsync(
        long cycleId,
        CombatRhythmPhase phase,
        RuntimeObservationContext initial,
        int durationMs,
        CancellationToken cancellationToken)
    {
        long startedAtMonoMs = initial.Snapshot.CapturedAtMonoMs;
        RuntimeObservationContext latest = initial;
        while (latest.Snapshot.CapturedAtMonoMs - startedAtMonoMs < durationMs)
        {
            latest = await observationSource.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            SafetyGateDecision safety = EvaluateSafety(latest);
            string? interruptionReason = GetRhythmInterruptionReason(latest, safety, null, allowPolicyAttack: false);
            int remainingMs = (int)Math.Max(0, durationMs - (latest.Snapshot.CapturedAtMonoMs - startedAtMonoMs));
            if (interruptionReason is not null)
            {
                await PublishRhythmAsync(cycleId, phase, durationMs, remainingMs, latest.Snapshot.CapturedAtMonoMs, interruptionReason, true, cancellationToken).ConfigureAwait(false);
                return new TimedWaitResult(latest, false);
            }
            await PublishRhythmAsync(cycleId, phase, durationMs, remainingMs, latest.Snapshot.CapturedAtMonoMs, null, remainingMs == 0, cancellationToken).ConfigureAwait(false);
        }
        return new TimedWaitResult(latest, true);
    }

    private string? GetRhythmInterruptionReason(
        RuntimeObservationContext observation,
        SafetyGateDecision safety,
        AbstractAction? currentAction,
        bool allowPolicyAttack)
    {
        if (!safety.CanAct) return safety.Reason.ToString();
        ActionDecision next = Decide(observation, safety);
        if (next.Action.Type == ActionType.Pause) return next.PauseReason.ToString();
        if (next.Action.Type == ActionType.Replan) return PauseReason.CalibrationRequired.ToString();
        if (next.Action.Type == ActionType.UsePotion) return next.Action.ProfileId?.ToString() ?? ActionType.UsePotion.ToString();
        if (currentAction?.Type == ActionType.Attack)
        {
            return !allowPolicyAttack || next.Action.Type != ActionType.Attack || next.Action.ProfileId != currentAction.ProfileId
                ? "AttackPolicyChanged"
                : null;
        }
        return observation.Platform.DistanceToBoundaryPx <= 24 && !observation.Platform.CanJump
            ? "PlatformBoundary"
            : null;
    }

    private async ValueTask PublishRhythmAsync(
        long cycleId,
        CombatRhythmPhase phase,
        int sampledDurationMs,
        int remainingMs,
        long updatedAtMonoMs,
        string? earlyReleaseReason,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!force && lastRhythmPublishedAtMonoMs != long.MinValue
            && updatedAtMonoMs - lastRhythmPublishedAtMonoMs < options.RhythmUpdateIntervalMs)
        {
            return;
        }
        lastRhythmPublishedAtMonoMs = updatedAtMonoMs;
        var snapshot = new CombatRhythmSnapshot
        {
            SchemaVersion = ContractConstants.SchemaVersion,
            CycleId = cycleId,
            Phase = phase,
            SampledDurationMs = sampledDurationMs,
            RemainingMs = remainingMs,
            UpdatedAtMonoMs = updatedAtMonoMs,
            EarlyReleaseReason = earlyReleaseReason
        };
        await journal.WriteAsync(new RuntimeJournalEntry(
            ContractConstants.SchemaVersion,
            "combat.rhythm.updated",
            updatedAtMonoMs,
            -1,
            RhythmCycleId: cycleId,
            RhythmPhase: phase.ToString(),
            PlannedDurationMs: sampledDurationMs,
            RemainingDurationMs: remainingMs,
            EarlyReleaseReason: earlyReleaseReason), cancellationToken).ConfigureAwait(false);
        await rhythmSink.PublishAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private static CombatRhythmPhase PhaseFor(ActionType movementType)
    {
        return movementType == ActionType.MoveLeft ? CombatRhythmPhase.MoveLeft : CombatRhythmPhase.MoveRight;
    }

    private static AbstractAction CreateRhythmAction(
        string actionId,
        ActionType type,
        ActionProfileId? profileId,
        long issuedAtMonoMs,
        int holdMs,
        int maximumDurationMs)
    {
        return new AbstractAction
        {
            ActionId = actionId,
            Type = type,
            ProfileId = profileId,
            IssuedAtMonoMs = issuedAtMonoMs,
            HoldMs = holdMs,
            MaxDurationMs = maximumDurationMs
        };
    }

    private sealed record TimedActionResult(RuntimeObservationContext Latest, bool Completed);
    private sealed record TimedWaitResult(RuntimeObservationContext Latest, bool Completed);

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
