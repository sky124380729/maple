using Maple.Contracts;
using Maple.Core;
using Maple.Runtime;

namespace Maple.Host;

public sealed record CombatTrialCompletion(
    PauseReason PauseReason,
    int ExecutedActions,
    long LastFrameId,
    string Code,
    bool AllKeysReleased);

public sealed class LocalCombatTrialObservationSource(IObservationSource source) : IObservationSource
{
    private readonly IObservationSource source = source ?? throw new ArgumentNullException(nameof(source));
    private RuntimeObservationContext? pending;
    private FacingDirection facing = FacingDirection.Unknown;

    public async ValueTask<RuntimeObservationContext> ReadNextAsync(CancellationToken cancellationToken)
    {
        if (pending is not null)
        {
            RuntimeObservationContext result = pending;
            pending = null;
            return result;
        }
        while (true)
        {
            RuntimeObservationContext next = await source.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            RuntimeObservationContext normalized = Normalize(next, Environment.TickCount64, facing);
            if (HasLocalStructure(normalized)) return normalized;
        }
    }

    public void Seed(RuntimeObservationContext context) => pending = context;

    public void ObserveAction(AbstractAction action)
    {
        facing = action.Type switch
        {
            ActionType.MoveLeft => FacingDirection.Left,
            ActionType.MoveRight => FacingDirection.Right,
            _ => facing,
        };
    }

    public static RuntimeObservationContext Normalize(
        RuntimeObservationContext source,
        long nowMonoMs,
        FacingDirection facing = FacingDirection.Unknown)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObservationSnapshot input = source.Snapshot;
        SelfObservation? self = input.Self;
        int width = input.Target?.ClientWidth ?? 0;
        int height = input.Target?.ClientHeight ?? 0;
        MonsterObservation? target = self is null || width <= 0 || height <= 0
            ? null
            : input.Monsters?
                .Where(monster => monster is not null
                    && monster.FreshUntilMonoMs >= nowMonoMs
                    && ValidBox(monster.Box)
                    && Math.Abs(FootY(monster.Box) - FootY(self.Box)) * height <= 70)
                .OrderBy(monster => Math.Abs(CenterX(monster.Box) - CenterX(self.Box)))
                .FirstOrDefault();

        bool localReady = self is not null && self.FreshUntilMonoMs >= nowMonoMs && target is not null;
        long freshUntil = Math.Max(input.CapturedAtMonoMs, Math.Min(input.CapturedAtMonoMs + 500, nowMonoMs + 250));
        var snapshot = new ObservationSnapshot
        {
            SchemaVersion = input.SchemaVersion,
            FrameId = input.FrameId,
            CapturedAtMonoMs = input.CapturedAtMonoMs,
            Target = input.Target,
            Self = self,
            Players = input.Players ?? [],
            Monsters = target is null ? [] : [target],
            Loot = input.Loot,
            Hp = input.Hp,
            Mp = input.Mp,
            Map = new MapObservation
            {
                MapId = "local-same-platform-trial",
                State = localReady ? MapArchiveState.Validated : MapArchiveState.Candidate,
                Confidence = localReady ? 1 : 0,
                FreshUntilMonoMs = freshUntil,
            },
            State = input.State,
        };
        return source with
        {
            Snapshot = snapshot,
            Platform = new PlatformContext
            {
                CurrentPlatformId = localReady ? "local-platform" : null,
                TargetPlatformId = localReady ? "local-platform" : null,
                SamePlatform = localReady,
                CameraStable = localReady,
                // The viewport edge moves with the camera and is not a map collision boundary.
                DistanceToBoundaryPx = Math.Max(width, height),
                Facing = facing,
            },
            HpHealthy = true,
            MpHealthy = true,
        };
    }

    private static double CenterX(double[] box) => box[0] + box[2] / 2;
    private static double FootY(double[] box) => box[1] + box[3];
    private static bool ValidBox(double[]? box) => box is { Length: 4 }
        && box.All(double.IsFinite)
        && box[0] >= 0 && box[1] >= 0 && box[2] > 0 && box[3] > 0
        && box[0] + box[2] <= 1 && box[1] + box[3] <= 1;

    private static bool HasLocalStructure(RuntimeObservationContext context) =>
        context.Snapshot.Map?.State == MapArchiveState.Validated
        && context.Snapshot.Self is not null
        && context.Snapshot.Monsters.Count == 1
        && context.Platform.SamePlatform;
}

public sealed class SamePlatformCombatTrialController : IDisposable
{
    private readonly IAutomaticCombatInputSession inputSession;
    private readonly LiveObservationSource observations;
    private readonly IActionExecutor executor;
    private readonly Func<CombatConfiguration> configuration;
    private readonly object sync = new();
    private CancellationTokenSource? runCancellation;
    private Task? runTask;
    private bool disposed;

    public SamePlatformCombatTrialController(
        IAutomaticCombatInputSession inputSession,
        LiveObservationSource observations,
        IActionExecutor executor,
        Func<CombatConfiguration> configuration)
    {
        this.inputSession = inputSession ?? throw new ArgumentNullException(nameof(inputSession));
        this.observations = observations ?? throw new ArgumentNullException(nameof(observations));
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public event EventHandler<AutomaticCombatStatus>? StatusChanged;
    public event Action<AbstractAction>? ActionAccepted;
    public event Action<CombatTrialCompletion>? Completed;
    public bool IsRunning { get { lock (sync) return runTask is { IsCompleted: false }; } }

    public async Task<AutomaticCombatArmResult> StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsRunning) return new(true, "TRIAL_ALREADY_RUNNING", PauseReason.None);

        Publish(SessionState.Arming, PauseReason.None, "TRIAL_ARMING");
        ForegroundResumeResult resumed = await inputSession.ResumeAsync(cancellationToken).ConfigureAwait(false);
        if (!resumed.Success) return await RejectAsync(PauseReason.InputUnavailable, resumed.Code).ConfigureAwait(false);

        var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task started = RunAsync(run.Token);
        lock (sync)
        {
            runCancellation?.Dispose();
            runCancellation = run;
            runTask = started;
        }
        Publish(SessionState.Observing, PauseReason.None, "SAME_PLATFORM_TRIAL_RUNNING");
        return new(true, "SAME_PLATFORM_TRIAL_RUNNING", PauseReason.None);
    }

    public async Task PauseAsync(PauseReason reason = PauseReason.OperatorRequested)
    {
        CancellationTokenSource? cancellation;
        Task? current;
        lock (sync) { cancellation = runCancellation; current = runTask; }
        cancellation?.Cancel();
        if (current is not null)
        {
            try { await current.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        await executor.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
        inputSession.Pause(reason);
        Publish(SessionState.Paused, reason, "TRIAL_PAUSED");
    }

    public async Task EmergencyStopAsync()
    {
        CancellationTokenSource? cancellation;
        lock (sync) cancellation = runCancellation;
        cancellation?.Cancel();
        inputSession.EmergencyStop();
        await executor.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
        Publish(SessionState.EmergencyStop, PauseReason.SafetyViolation, "EMERGENCY_STOP");
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var trialSource = new LocalCombatTrialObservationSource(observations);
        CombatTrialCompletion completion;
        try
        {
            RuntimeObservationContext stable = await AwaitStableIdentityAsync(trialSource, cancellationToken).ConfigureAwait(false);
            trialSource.Seed(stable);
            CombatConfiguration active = configuration();
            var settings = new ActionPolicySettings
            {
                ClientWidthPx = Math.Max(1, stable.Snapshot.Target.ClientWidth),
                AttackRangePx = active.PreferredDistancePx,
                SelfConfidenceThreshold = 0,
                TargetConfidenceThreshold = 0,
                ObservedSpeedPxPerSecond = 320,
                MinMoveHoldMs = 80,
                MaxMoveHoldMs = 250,
                AttackHoldMs = 80,
                AttackMode = AttackSelectionMode.Single,
                AreaTargetCount = int.MaxValue,
                AttackProfileSwitchCooldownMs = 0,
                HpPotionThreshold = -1,
                MpPotionThreshold = -1,
                PickupEnabled = false,
                MaxAttackNoFeedbackAttempts = 2,
            };
            var orchestrator = new ProductionOrchestrator(
                trialSource,
                executor,
                new SafetyGate(0),
                new ActionPolicy(new MovementDurationEstimator()),
                settings,
                new OrchestratorOptions { MaximumFeedbackFramesPerAction = 16 },
                actionAccepted: action =>
                {
                    trialSource.ObserveAction(action);
                    OnActionAccepted(action);
                });
            OrchestratorRunResult result = await orchestrator.RunUntilPausedAsync(cancellationToken).ConfigureAwait(false);
            inputSession.Pause(result.PauseReason);
            Publish(SessionState.Paused, result.PauseReason, "TRIAL_STOPPED");
            completion = new(result.PauseReason, result.ExecutedActions, result.LastFrameId, "TRIAL_STOPPED", false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            inputSession.Pause(PauseReason.WatchdogTimeout);
            Publish(SessionState.Paused, PauseReason.WatchdogTimeout, "COMBAT_RUN_CANCELLED");
            completion = new(PauseReason.WatchdogTimeout, 0, observations.Latest?.Snapshot.FrameId ?? -1, "COMBAT_RUN_CANCELLED", false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            inputSession.Pause(PauseReason.SafetyViolation);
            string code = "TRIAL_FAULT:" + exception.GetType().Name;
            Publish(SessionState.Paused, PauseReason.SafetyViolation, code);
            completion = new(PauseReason.SafetyViolation, 0, observations.Latest?.Snapshot.FrameId ?? -1, code, false);
        }
        finally
        {
            await executor.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
        Completed?.Invoke(completion with { AllKeysReleased = true });
    }

    internal static async Task<RuntimeObservationContext> AwaitStableIdentityAsync(
        IObservationSource source,
        CancellationToken cancellationToken,
        int maximumObservations = 30)
    {
        if (maximumObservations < 3) throw new ArgumentOutOfRangeException(nameof(maximumObservations));
        var centers = new List<double>(3);
        RuntimeObservationContext? latest = null;
        for (int index = 0; index < maximumObservations; index++)
        {
            latest = await source.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            if (!Ready(latest))
            {
                centers.Clear();
                continue;
            }
            centers.Add(CenterX(latest.Snapshot.Self.Box));
            if (centers.Count < 3) continue;
            if (centers.Zip(centers.Skip(1), (left, right) => Math.Abs(left - right)).All(delta => delta <= 0.12))
                return latest;
            centers.RemoveAt(0);
        }
        throw new InvalidOperationException(latest is null ? "LOCAL_TRIAL_NO_OBSERVATION" : "LOCAL_TRIAL_NOT_READY");
    }

    private static bool Ready(RuntimeObservationContext context) => context.TargetBound
        && context.IsForeground
        && context.FrameFresh
        && context.InputAdapterHealthy
        && !context.EmergencyStop
        && context.Snapshot.Self is not null
        && context.Snapshot.Self.Confidence > 0
        && context.Snapshot.Monsters.Count == 1
        && context.Platform.SamePlatform
        && context.Platform.DistanceToBoundaryPx > 24;

    private async Task<AutomaticCombatArmResult> RejectAsync(PauseReason reason, string code)
    {
        inputSession.Pause(reason);
        await executor.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
        Publish(SessionState.Paused, reason, code);
        return new(false, code, reason);
    }

    private void OnActionAccepted(AbstractAction action)
    {
        ActionAccepted?.Invoke(action);
        Publish(
            action.Type == ActionType.Attack ? SessionState.Attacking : SessionState.Navigating,
            PauseReason.None,
            action.Type.ToString());
    }

    private void Publish(SessionState state, PauseReason reason, string code) =>
        StatusChanged?.Invoke(this, new AutomaticCombatStatus(state, reason, code));

    private static double CenterX(double[] box) => box[0] + box[2] / 2;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        runCancellation?.Cancel();
        try { runTask?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        executor.ReleaseAllAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        runCancellation?.Dispose();
    }
}
