using Maple.Contracts;
using Maple.Core;
using Maple.Runtime;
using Xunit;

namespace Maple.Host.Tests;

public sealed class AutomaticCombatControllerTests
{
    [Fact]
    public async Task ArmRejectsMissingObservationBeforeStartingInputSession()
    {
        using var observations = new LiveObservationSource((snapshot, _) => Context(snapshot));
        var input = new RecordingInputSession();
        var executor = new RecordingExecutor();
        using var controller = new AutomaticCombatController(input, observations, executor, accepted => CreateOrchestrator(observations, executor, accepted), () => true, () => 1000);

        AutomaticCombatArmResult result = await controller.ArmAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("OBSERVATION_MISSING", result.Code);
        Assert.Equal(0, input.ResumeCalls);
        Assert.Equal(["releaseAll"], executor.Events);
    }

    [Fact]
    public async Task ValidSamePlatformTargetMovesThenAttacksAndReleases()
    {
        using var observations = new LiveObservationSource((snapshot, _) => Context(snapshot));
        var input = new RecordingInputSession();
        var executor = new RecordingExecutor();
        using var controller = new AutomaticCombatController(input, observations, executor, accepted => CreateOrchestrator(observations, executor, accepted), () => true, () => 1000);
        List<SessionState> states = [];
        controller.StatusChanged += (_, status) => states.Add(status.State);
        observations.Publish(Snapshot(10, 1000, 0.20, 0.70), true);

        AutomaticCombatArmResult armed = await controller.ArmAsync(CancellationToken.None);
        Assert.True(armed.Success);
        await executor.MoveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        observations.Publish(Snapshot(11, 1080, 0.645, 0.70), true);
        await executor.AttackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        observations.Publish(Snapshot(12, 1160, 0.645, null), true);
        AutomaticCombatRunResult finished = await controller.WaitForCompletionAsync(CancellationToken.None);

        Assert.Equal(PauseReason.TargetLost, finished.PauseReason);
        Assert.Equal(
            ["MoveRight.down", "MoveRight.up", "Attack:SingleAttack.down", "Attack:SingleAttack.up", "releaseAll"],
            executor.Events);
        Assert.Equal(PauseReason.TargetLost, input.LastPauseReason);
        Assert.Contains(SessionState.Navigating, states);
        Assert.Contains(SessionState.Attacking, states);
    }

    [Theory]
    [InlineData(false, true, true, true, true, "MODEL_NOT_READY")]
    [InlineData(true, false, true, true, true, "SELF_AMBIGUOUS")]
    [InlineData(true, true, false, true, true, "MAP_NOT_VALIDATED")]
    [InlineData(true, true, true, false, true, "HEALTH_UNREADABLE")]
    [InlineData(true, true, true, true, false, "INPUT_BROKER_NOT_READY")]
    public void ReadinessFailsClosed(bool modelReady, bool canDrive, bool mapValidated, bool healthReadable, bool inputReady, string expectedCode)
    {
        RuntimeObservationContext context = Context(Snapshot(1, 1000, 0.2, 0.7)) with
        {
            HpHealthy = healthReadable,
            MpHealthy = healthReadable,
            InputAdapterHealthy = inputReady,
        };
        if (!mapValidated) context.Snapshot.Map.State = MapArchiveState.Candidate;

        AutomaticCombatReadiness readiness = AutomaticCombatController.EvaluateReadiness(context, canDrive, modelReady, 1000);

        Assert.False(readiness.Ready);
        Assert.Equal(expectedCode, readiness.Code);
    }

    private static ProductionOrchestrator CreateOrchestrator(IObservationSource source, IActionExecutor executor, Action<AbstractAction> accepted)
    {
        var settings = new ActionPolicySettings
        {
            ClientWidthPx = 1280,
            AttackRangePx = 80,
            SelfConfidenceThreshold = 0.9,
            TargetConfidenceThreshold = 0.8,
            ObservedSpeedPxPerSecond = 320,
            MinMoveHoldMs = 60,
            MaxMoveHoldMs = 400,
            AttackHoldMs = 80,
            HpPotionThreshold = 0.35,
            MpPotionThreshold = 0.3,
            PickupEnabled = false,
            MaxAttackNoFeedbackAttempts = 2,
        };
        return new ProductionOrchestrator(source, executor, new SafetyGate(0.9), new ActionPolicy(new MovementDurationEstimator()), settings, new OrchestratorOptions(), actionAccepted: accepted);
    }

    private static RuntimeObservationContext Context(ObservationSnapshot snapshot) => new(
        snapshot,
        new PlatformContext { CurrentPlatformId = "p1", TargetPlatformId = "p1", SamePlatform = true, CameraStable = true, DistanceToBoundaryPx = 300, Facing = FacingDirection.Right },
        true, true, true, true, true, true, false);

    private static ObservationSnapshot Snapshot(long frameId, long capturedAt, double selfX, double? monsterX)
    {
        long fresh = capturedAt + 250;
        return new ObservationSnapshot
        {
            SchemaVersion = 2,
            FrameId = frameId,
            CapturedAtMonoMs = capturedAt,
            Target = new TargetBinding { SchemaVersion = 2, Hwnd = "0x1", Pid = 1, ClientWidth = 1280, ClientHeight = 720, Dpi = 96 },
            Self = new SelfObservation { Box = [selfX, 0.5, 0.08, 0.18], Confidence = 0.98, FreshUntilMonoMs = fresh },
            Players = [],
            Monsters = monsterX.HasValue ? [new MonsterObservation { TargetId = "snail-1", Class = "snail", Box = [monsterX.Value, 0.5, 0.08, 0.18], Confidence = 0.96, FreshUntilMonoMs = fresh }] : [],
            Loot = new LootObservation { FreshUntilMonoMs = fresh },
            Hp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.9, Confidence = 0.99, FreshUntilMonoMs = fresh },
            Mp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.8, Confidence = 0.99, FreshUntilMonoMs = fresh },
            Map = new MapObservation { MapId = "forest-east", State = MapArchiveState.Validated, Confidence = 0.99, FreshUntilMonoMs = fresh },
            State = SessionState.Observing,
        };
    }

    private sealed class RecordingInputSession : IAutomaticCombatInputSession
    {
        public int ResumeCalls { get; private set; }
        public PauseReason LastPauseReason { get; private set; }
        public bool IsArmed { get; private set; }
        public Task<ForegroundResumeResult> ResumeAsync(CancellationToken cancellationToken) { ResumeCalls++; IsArmed = true; return Task.FromResult(new ForegroundResumeResult(true, "INPUT_SESSION_READY")); }
        public void Pause(PauseReason reason) { LastPauseReason = reason; IsArmed = false; }
        public void EmergencyStop() => IsArmed = false;
    }

    private sealed class RecordingExecutor : IActionExecutor
    {
        public List<string> Events { get; } = [];
        public TaskCompletionSource MoveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AttackStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask KeyDownAsync(AbstractAction action, CancellationToken cancellationToken)
        {
            Events.Add(Label(action) + ".down");
            if (action.Type is ActionType.MoveLeft or ActionType.MoveRight) MoveStarted.TrySetResult();
            if (action.Type == ActionType.Attack) AttackStarted.TrySetResult();
            return ValueTask.CompletedTask;
        }
        public ValueTask KeyUpAsync(AbstractAction action, CancellationToken cancellationToken) { Events.Add(Label(action) + ".up"); return ValueTask.CompletedTask; }
        public ValueTask ReleaseAllAsync(CancellationToken cancellationToken) { Events.Add("releaseAll"); return ValueTask.CompletedTask; }
        private static string Label(AbstractAction action) => action.ProfileId.HasValue ? $"{action.Type}:{action.ProfileId}" : action.Type.ToString();
    }
}
