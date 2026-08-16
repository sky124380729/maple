using Maple.Contracts;
using Maple.Core;
using Maple.Runtime;
using Xunit;

namespace Maple.Host.Tests;

public sealed class SamePlatformCombatTrialTests
{
    [Fact]
    public void NormalizeKeepsOnlyNearestMonsterOnSelfFootBand()
    {
        RuntimeObservationContext source = Context(
            self: [0.40, 0.50, 0.08, 0.18],
            monsters:
            [
                Monster("near", [0.58, 0.54, 0.08, 0.14]),
                Monster("far", [0.78, 0.54, 0.08, 0.14]),
                Monster("upper", [0.48, 0.22, 0.08, 0.14]),
            ]);

        RuntimeObservationContext normalized = LocalCombatTrialObservationSource.Normalize(source, 1000);

        MonsterObservation target = Assert.Single(normalized.Snapshot.Monsters);
        Assert.Equal("near", target.TargetId);
        Assert.True(normalized.Platform.SamePlatform);
        Assert.Equal(MapArchiveState.Validated, normalized.Snapshot.Map.State);
        Assert.True(normalized.Platform.DistanceToBoundaryPx > 24);
    }

    [Fact]
    public void NormalizeDoesNotTreatViewportEdgeAsAMapBoundary()
    {
        RuntimeObservationContext source = Context(
            self: [0.005, 0.50, 0.08, 0.18],
            monsters: [Monster("target", [0.25, 0.54, 0.08, 0.14])]);

        RuntimeObservationContext normalized = LocalCombatTrialObservationSource.Normalize(source, 1000);

        Assert.True(normalized.Platform.DistanceToBoundaryPx > 24);
    }

    [Fact]
    public async Task TrialObservationSkipsTransientFrameWithoutSamePlatformMonster()
    {
        RuntimeObservationContext missingTarget = Context(
            self: [0.40, 0.50, 0.08, 0.18],
            monsters: []);
        RuntimeObservationContext recovered = Context(
            self: [0.40, 0.50, 0.08, 0.18],
            monsters: [Monster("target", [0.58, 0.54, 0.08, 0.14])]);
        var source = new LocalCombatTrialObservationSource(new SequenceObservationSource(missingTarget, recovered));

        RuntimeObservationContext result = await source.ReadNextAsync(CancellationToken.None);

        Assert.Equal(MapArchiveState.Validated, result.Snapshot.Map.State);
        Assert.Single(result.Snapshot.Monsters);
        Assert.True(result.Platform.SamePlatform);
    }

    [Fact]
    public async Task TrialObservationCarriesLastMovementDirectionIntoFacingFeedback()
    {
        RuntimeObservationContext frame = Context(
            self: [0.40, 0.50, 0.08, 0.18],
            monsters: [Monster("target", [0.34, 0.54, 0.08, 0.14])]);
        var source = new LocalCombatTrialObservationSource(new SequenceObservationSource(frame, frame));

        RuntimeObservationContext beforeTurn = await source.ReadNextAsync(CancellationToken.None);
        source.ObserveAction(new AbstractAction { Type = ActionType.MoveLeft });
        RuntimeObservationContext afterTurn = await source.ReadNextAsync(CancellationToken.None);

        Assert.Equal(FacingDirection.Unknown, beforeTurn.Platform.Facing);
        Assert.Equal(FacingDirection.Left, afterTurn.Platform.Facing);
    }

    [Fact]
    public void EvidenceRequiresAttackAdvancedFeedbackAndReleasedKeys()
    {
        var recorder = new CombatTrialEvidenceRecorder(10);
        recorder.Record(new AbstractAction { Type = ActionType.MoveRight });
        recorder.Record(new AbstractAction { Type = ActionType.Attack });

        CombatTrialEvidenceReport report = recorder.Complete(new CombatTrialCompletion(
            PauseReason.WatchdogTimeout, 2, 18, "TRIAL_TIME_LIMIT_REACHED", true));

        Assert.True(report.Success);
        Assert.Equal("COMBAT_CLOSED_LOOP_CONFIRMED", report.Code);
        Assert.Equal(1, report.MovementActions);
        Assert.Equal(1, report.AttackActions);
        Assert.True(report.FeedbackFrameAdvanced);
        Assert.True(report.AllKeysReleased);
    }

    [Theory]
    [InlineData(0, 18, true)]
    [InlineData(1, 10, true)]
    [InlineData(1, 18, false)]
    public void EvidenceRejectsIncompleteClosedLoop(int attackCount, long lastFrameId, bool released)
    {
        var recorder = new CombatTrialEvidenceRecorder(10);
        for (int index = 0; index < attackCount; index++)
            recorder.Record(new AbstractAction { Type = ActionType.Attack });

        CombatTrialEvidenceReport report = recorder.Complete(new CombatTrialCompletion(
            PauseReason.WatchdogTimeout, attackCount, lastFrameId, "TRIAL_TIME_LIMIT_REACHED", released));

        Assert.False(report.Success);
    }

    [Fact]
    public async Task StartArmsInputBeforeWaitingForFirstForegroundObservation()
    {
        var input = new FakeInputSession();
        var executor = new FakeExecutor();
        using var observations = new LiveObservationSource((_, _) => throw new InvalidOperationException("not used"));
        using var controller = new SamePlatformCombatTrialController(input, observations, executor, () => CombatConfiguration.Default);

        AutomaticCombatArmResult result = await controller.StartAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, input.ResumeCalls);
        Assert.True(controller.IsRunning);
        await controller.PauseAsync();
        Assert.True(executor.ReleaseCalls > 0);
    }

    [Fact]
    public async Task ContinuousCombatDoesNotStopAfterFormerTrialLimit()
    {
        var input = new FakeInputSession();
        var executor = new FakeExecutor();
        using var observations = new LiveObservationSource((_, _) => throw new InvalidOperationException("not used"));
        using var controller = new SamePlatformCombatTrialController(input, observations, executor, () => CombatConfiguration.Default);

        AutomaticCombatArmResult result = await controller.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(15_300));

        Assert.True(result.Success);
        Assert.True(controller.IsRunning);
        await controller.PauseAsync();
        Assert.True(executor.ReleaseCalls > 0);
    }

    [Fact]
    public async Task StableIdentityWarmupSkipsObservationQueuedBeforeBrokerWasArmed()
    {
        RuntimeObservationContext ready = LocalCombatTrialObservationSource.Normalize(Context(
            self: [0.40, 0.50, 0.08, 0.18],
            monsters: [Monster("target", [0.58, 0.54, 0.08, 0.14])]), 1000);
        RuntimeObservationContext stale = ready with { InputAdapterHealthy = false };
        var source = new SequenceObservationSource(stale, ready, ready, ready);

        RuntimeObservationContext stable = await SamePlatformCombatTrialController.AwaitStableIdentityAsync(
            source,
            CancellationToken.None,
            maximumObservations: 4);

        Assert.True(stable.InputAdapterHealthy);
        Assert.Equal(ready.Snapshot.FrameId, stable.Snapshot.FrameId);
    }

    private static RuntimeObservationContext Context(double[] self, List<MonsterObservation> monsters)
    {
        long fresh = Environment.TickCount64 + 10_000;
        return new RuntimeObservationContext(new ObservationSnapshot
        {
            SchemaVersion = 2,
            FrameId = 10,
            CapturedAtMonoMs = 1000,
            Target = new TargetBinding { SchemaVersion = 2, Hwnd = "0x1", Pid = 1, ClientWidth = 1280, ClientHeight = 720, Dpi = 96 },
            Self = new SelfObservation { Box = self, Confidence = 0.35, FreshUntilMonoMs = fresh },
            Players = [],
            Monsters = monsters,
            Loot = new LootObservation { FreshUntilMonoMs = fresh },
            Hp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.8, Confidence = 0.8, FreshUntilMonoMs = fresh },
            Mp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.8, Confidence = 0.8, FreshUntilMonoMs = fresh },
            Map = new MapObservation { MapId = "unknown", State = MapArchiveState.Candidate, Confidence = 0, FreshUntilMonoMs = fresh },
            State = SessionState.Observing,
        }, new PlatformContext(), true, true, true, true, true, true, false);
    }

    private static MonsterObservation Monster(string id, double[] box) => new()
    {
        TargetId = id,
        Class = "mob",
        Box = box,
        Confidence = 0.2,
        FreshUntilMonoMs = Environment.TickCount64 + 10_000,
    };

    private sealed class FakeInputSession : IAutomaticCombatInputSession
    {
        public bool IsArmed { get; private set; }
        public int ResumeCalls { get; private set; }
        public Task<ForegroundResumeResult> ResumeAsync(CancellationToken cancellationToken)
        {
            ResumeCalls++;
            IsArmed = true;
            return Task.FromResult(new ForegroundResumeResult(true, "READY"));
        }
        public void Pause(PauseReason reason) => IsArmed = false;
        public void EmergencyStop() => IsArmed = false;
    }

    private sealed class FakeExecutor : IActionExecutor
    {
        public int ReleaseCalls { get; private set; }
        public ValueTask KeyDownAsync(AbstractAction action, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask KeyUpAsync(AbstractAction action, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ReleaseAllAsync(CancellationToken cancellationToken)
        {
            ReleaseCalls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SequenceObservationSource(params RuntimeObservationContext[] observations) : IObservationSource
    {
        private readonly Queue<RuntimeObservationContext> remaining = new(observations);

        public ValueTask<RuntimeObservationContext> ReadNextAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(remaining.Dequeue());
        }
    }
}
