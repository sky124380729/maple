using Maple.Contracts;
using Maple.Runtime;

namespace Maple.Host;

public interface IAutomaticCombatInputSession
{
    bool IsArmed { get; }
    Task<ForegroundResumeResult> ResumeAsync(CancellationToken cancellationToken);
    void Pause(PauseReason reason);
    void EmergencyStop();
}

public sealed record AutomaticCombatReadiness(bool Ready, PauseReason PauseReason, string Code)
{
    public static AutomaticCombatReadiness Allow() => new(true, PauseReason.None, "READY");
    public static AutomaticCombatReadiness Block(PauseReason reason, string code) => new(false, reason, code);
}

public sealed record AutomaticCombatArmResult(bool Success, string Code, PauseReason PauseReason);
public sealed record AutomaticCombatRunResult(PauseReason PauseReason, int ExecutedActions, long LastFrameId, string Code);
public sealed record AutomaticCombatStatus(SessionState State, PauseReason PauseReason, string Code);

public sealed class AutomaticCombatController : IDisposable
{
    private readonly IAutomaticCombatInputSession inputSession;
    private readonly LiveObservationSource observations;
    private readonly IActionExecutor executor;
    private readonly Func<Action<AbstractAction>, ProductionOrchestrator> orchestratorFactory;
    private readonly Func<bool> modelReady;
    private readonly Func<long> clock;
    private readonly int maximumActions;
    private readonly SemaphoreSlim transition = new(1, 1);
    private readonly object sync = new();
    private CancellationTokenSource? runCancellation;
    private Task<AutomaticCombatRunResult>? runTask;
    private bool disposed;

    public AutomaticCombatController(
        IAutomaticCombatInputSession inputSession,
        LiveObservationSource observations,
        IActionExecutor executor,
        Func<Action<AbstractAction>, ProductionOrchestrator> orchestratorFactory,
        Func<bool> modelReady,
        Func<long>? clock = null,
        int maximumActions = 10_000)
    {
        this.inputSession = inputSession ?? throw new ArgumentNullException(nameof(inputSession));
        this.observations = observations ?? throw new ArgumentNullException(nameof(observations));
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.orchestratorFactory = orchestratorFactory ?? throw new ArgumentNullException(nameof(orchestratorFactory));
        this.modelReady = modelReady ?? throw new ArgumentNullException(nameof(modelReady));
        this.clock = clock ?? (() => Environment.TickCount64);
        this.maximumActions = maximumActions is >= 1 and <= 1_000_000 ? maximumActions : throw new ArgumentOutOfRangeException(nameof(maximumActions));
    }

    public event EventHandler<AutomaticCombatStatus>? StatusChanged;

    public bool IsRunning
    {
        get { lock (sync) return runTask is { IsCompleted: false }; }
    }

    public async Task<AutomaticCombatArmResult> ArmAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning) return new(true, "ALREADY_RUNNING", PauseReason.None);
            AutomaticCombatReadiness readiness = EvaluateReadiness(
                observations.Latest,
                observations.LatestCanDriveActions,
                modelReady(),
                clock(),
                requireInputReady: false);
            if (!readiness.Ready) return await RejectArmAsync(readiness).ConfigureAwait(false);

            Publish(SessionState.Arming, PauseReason.None, "ARMING");
            ForegroundResumeResult input = await inputSession.ResumeAsync(cancellationToken).ConfigureAwait(false);
            if (!input.Success)
                return await RejectArmAsync(AutomaticCombatReadiness.Block(PauseReason.InputUnavailable, input.Code)).ConfigureAwait(false);

            observations.RefreshLatest();
            readiness = CurrentReadiness();
            if (!readiness.Ready) return await RejectArmAsync(readiness).ConfigureAwait(false);

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ProductionOrchestrator orchestrator = orchestratorFactory(OnActionAccepted);
            Task<AutomaticCombatRunResult> started = RunCoreAsync(orchestrator, cancellation.Token);
            lock (sync)
            {
                runCancellation?.Dispose();
                runCancellation = cancellation;
                runTask = started;
            }
            Publish(SessionState.Observing, PauseReason.None, "RUNNING");
            return new(true, "AUTOMATIC_COMBAT_RUNNING", PauseReason.None);
        }
        finally { transition.Release(); }
    }

    public async Task<AutomaticCombatRunResult> WaitForCompletionAsync(CancellationToken cancellationToken)
    {
        Task<AutomaticCombatRunResult>? current;
        lock (sync) current = runTask;
        if (current is null) return new(PauseReason.OperatorRequested, 0, -1, "NOT_RUNNING");
        return await current.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PauseAsync(PauseReason reason = PauseReason.OperatorRequested)
    {
        CancellationTokenSource? cancellation;
        Task<AutomaticCombatRunResult>? current;
        lock (sync) { cancellation = runCancellation; current = runTask; }
        cancellation?.Cancel();
        inputSession.Pause(reason);
        if (current is not null)
        {
            try { await current.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        else await executor.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
        Publish(SessionState.Paused, reason, "PAUSED");
    }

    public Task<AutomaticCombatArmResult> ToggleAsync(CancellationToken cancellationToken) =>
        IsRunning
            ? PauseAndReturnAsync()
            : ArmAsync(cancellationToken);

    public async Task EmergencyStopAsync()
    {
        CancellationTokenSource? cancellation;
        lock (sync) cancellation = runCancellation;
        cancellation?.Cancel();
        inputSession.EmergencyStop();
        await executor.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
        Publish(SessionState.EmergencyStop, PauseReason.SafetyViolation, "EMERGENCY_STOP");
    }

    public AutomaticCombatReadiness CurrentReadiness() => EvaluateReadiness(
        observations.Latest,
        observations.LatestCanDriveActions,
        modelReady(),
        clock());

    public static AutomaticCombatReadiness EvaluateReadiness(
        RuntimeObservationContext? observation,
        bool canDriveActions,
        bool modelReady,
        long nowMonoMs,
        bool requireInputReady = true)
    {
        if (!modelReady) return AutomaticCombatReadiness.Block(PauseReason.CalibrationRequired, "MODEL_NOT_READY");
        if (observation is null) return AutomaticCombatReadiness.Block(PauseReason.StaleFrame, "OBSERVATION_MISSING");
        if (!canDriveActions || observation.Snapshot.Self is null || observation.Snapshot.Self.Confidence <= 0)
            return AutomaticCombatReadiness.Block(PauseReason.CalibrationRequired, "SELF_AMBIGUOUS");
        if (!observation.FrameFresh
            || observation.Snapshot.Self.FreshUntilMonoMs < nowMonoMs
            || observation.Snapshot.CapturedAtMonoMs > nowMonoMs + 1000)
            return AutomaticCombatReadiness.Block(PauseReason.StaleFrame, "OBSERVATION_STALE");
        if (!observation.TargetBound) return AutomaticCombatReadiness.Block(PauseReason.TargetLost, "TARGET_NOT_BOUND");
        if (!observation.IsForeground) return AutomaticCombatReadiness.Block(PauseReason.WindowNotForeground, "TARGET_NOT_FOREGROUND");
        if (observation.Snapshot.Map?.State != MapArchiveState.Validated)
            return AutomaticCombatReadiness.Block(PauseReason.MapNotValidated, "MAP_NOT_VALIDATED");
        if (string.IsNullOrWhiteSpace(observation.Platform.CurrentPlatformId)
            || string.IsNullOrWhiteSpace(observation.Platform.TargetPlatformId)
            || !observation.Platform.SamePlatform)
            return AutomaticCombatReadiness.Block(PauseReason.CalibrationRequired, "PLATFORM_UNRESOLVED");
        if (!observation.HpHealthy || !observation.MpHealthy
            || !FreshResource(observation.Snapshot.Hp, nowMonoMs)
            || !FreshResource(observation.Snapshot.Mp, nowMonoMs))
            return AutomaticCombatReadiness.Block(PauseReason.HealthUnknown, "HEALTH_UNREADABLE");
        if (requireInputReady && !observation.InputAdapterHealthy) return AutomaticCombatReadiness.Block(PauseReason.InputUnavailable, "INPUT_BROKER_NOT_READY");
        if (observation.EmergencyStop) return AutomaticCombatReadiness.Block(PauseReason.SafetyViolation, "EMERGENCY_STOP_ACTIVE");
        if (!observation.Snapshot.Monsters.Any(monster => monster is not null && monster.Confidence > 0 && monster.FreshUntilMonoMs >= nowMonoMs))
            return AutomaticCombatReadiness.Block(PauseReason.TargetLost, "MONSTER_TARGET_MISSING");
        return AutomaticCombatReadiness.Allow();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CancellationTokenSource? cancellation;
        Task<AutomaticCombatRunResult>? current;
        lock (sync) { cancellation = runCancellation; current = runTask; }
        cancellation?.Cancel();
        if (current is not null)
        {
            try { current.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
        }
        else executor.ReleaseAllAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        observations.Dispose();
        cancellation?.Dispose();
        transition.Dispose();
    }

    private async Task<AutomaticCombatRunResult> RunCoreAsync(ProductionOrchestrator orchestrator, CancellationToken cancellationToken)
    {
        AutomaticCombatRunResult result;
        try
        {
            OrchestratorRunResult completed = await orchestrator.RunUntilPausedAsync(maximumActions, cancellationToken).ConfigureAwait(false);
            result = new(completed.PauseReason, completed.ExecutedActions, completed.LastFrameId, "ORCHESTRATOR_PAUSED");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = new(PauseReason.OperatorRequested, 0, observations.Latest?.Snapshot.FrameId ?? -1, "RUN_CANCELLED");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await executor.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
            result = new(PauseReason.SafetyViolation, 0, observations.Latest?.Snapshot.FrameId ?? -1, "ORCHESTRATOR_FAULT:" + exception.GetType().Name);
        }
        inputSession.Pause(result.PauseReason);
        Publish(SessionState.Paused, result.PauseReason, result.Code);
        return result;
    }

    private async Task<AutomaticCombatArmResult> RejectArmAsync(AutomaticCombatReadiness readiness)
    {
        inputSession.Pause(readiness.PauseReason);
        await executor.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
        Publish(SessionState.Paused, readiness.PauseReason, readiness.Code);
        return new(false, readiness.Code, readiness.PauseReason);
    }

    private async Task<AutomaticCombatArmResult> PauseAndReturnAsync()
    {
        await PauseAsync(PauseReason.OperatorRequested).ConfigureAwait(false);
        return new(false, "PAUSED", PauseReason.OperatorRequested);
    }

    private void Publish(SessionState state, PauseReason reason, string code) =>
        StatusChanged?.Invoke(this, new AutomaticCombatStatus(state, reason, code));

    private void OnActionAccepted(AbstractAction action)
    {
        SessionState state = action.Type switch
        {
            ActionType.MoveLeft or ActionType.MoveRight or ActionType.Jump or ActionType.ClimbUp or ActionType.ClimbDown => SessionState.Navigating,
            ActionType.Attack => SessionState.Attacking,
            ActionType.Pickup => SessionState.Looting,
            ActionType.UsePotion => SessionState.UsingPotion,
            _ => SessionState.Observing,
        };
        Publish(state, PauseReason.None, action.Type.ToString());
    }

    private static bool FreshResource(ResourceObservation? resource, long nowMonoMs) =>
        resource is not null && resource.Confidence > 0 && resource.FreshUntilMonoMs >= nowMonoMs;
}
