using Maple.Contracts;
using Maple.Core;
using Maple.Runtime;
using Xunit;

namespace Maple.Runtime.Tests.Runtime;

public sealed class ProductionOrchestratorTests
{
    [Fact]
    public async Task StationaryRhythmHoldsOnceThenMovesInOppositeDirections()
    {
        var source = new QueueObservationSource(
            Observation(frameId: 1, capturedAt: 1_000, selfX: 0.645, monsterX: 0.70),
            Observation(frameId: 2, capturedAt: 2_000, selfX: 0.645, monsterX: 0.70),
            Observation(frameId: 3, capturedAt: 2_060, selfX: 0.64, monsterX: 0.70),
            Observation(frameId: 4, capturedAt: 2_110, selfX: 0.64, monsterX: 0.70),
            Observation(frameId: 5, capturedAt: 2_170, selfX: 0.645, monsterX: 0.70));
        var executor = new RecordingActionExecutor();
        var random = new ScriptedRandomSource(97, 1_000, 0, 60, 50, 60, 99);
        var orchestrator = CreateOrchestrator(source, executor, stationaryRhythmEnabled: true, random);

        OrchestratorRunResult result = await orchestrator.RunUntilPausedAsync(1, CancellationToken.None);

        Assert.Equal(PauseReason.WatchdogTimeout, result.PauseReason);
        Assert.Equal(
            ["Attack:SingleAttack.down", "Attack:SingleAttack.up", "MoveLeft.down", "MoveLeft.up", "MoveRight.down", "MoveRight.up", "releaseAll"],
            executor.Events);
        Assert.Equal([1_000, 60, 60], executor.Actions.Select(action => action.HoldMs));
    }

    [Fact]
    public async Task StationaryRhythmPublishesRightFirstCountdownAndRestsWithNoActiveKey()
    {
        var source = new QueueObservationSource(
            Observation(frameId: 1, capturedAt: 1_000, selfX: 0.645, monsterX: 0.70),
            Observation(frameId: 2, capturedAt: 2_000, selfX: 0.645, monsterX: 0.70),
            Observation(frameId: 3, capturedAt: 2_060, selfX: 0.65, monsterX: 0.70),
            Observation(frameId: 4, capturedAt: 2_110, selfX: 0.65, monsterX: 0.70),
            Observation(frameId: 5, capturedAt: 2_170, selfX: 0.645, monsterX: 0.70),
            Observation(frameId: 6, capturedAt: 4_170, selfX: 0.645, monsterX: 0.70));
        var executor = new RecordingActionExecutor();
        var sink = new RecordingRhythmSink(executor);
        var random = new ScriptedRandomSource(97, 1_000, 1, 60, 50, 60, 0, 2_000);
        var orchestrator = CreateOrchestrator(source, executor, stationaryRhythmEnabled: true, random, sink);

        await orchestrator.RunUntilPausedAsync(1, CancellationToken.None);

        Assert.Equal(
            ["Attack:SingleAttack.down", "Attack:SingleAttack.up", "MoveRight.down", "MoveRight.up", "MoveLeft.down", "MoveLeft.up", "releaseAll"],
            executor.Events);
        Assert.Contains(sink.Updates, update => update.Snapshot.Phase == CombatRhythmPhase.AttackHolding && update.Snapshot.SampledDurationMs == 1_000);
        Assert.Contains(sink.Updates, update => update.Snapshot.Phase == CombatRhythmPhase.MoveRight);
        Assert.Contains(sink.Updates, update => update.Snapshot.Phase == CombatRhythmPhase.MovementGap);
        Assert.Contains(sink.Updates, update => update.Snapshot.Phase == CombatRhythmPhase.MoveLeft);
        Assert.Contains(sink.Updates, update => update.Snapshot.Phase == CombatRhythmPhase.Resting && !update.HadActiveKey);
    }

    [Fact]
    public async Task StationaryRhythmReportsTargetLossAndSkipsMovement()
    {
        var source = new QueueObservationSource(
            Observation(frameId: 1, capturedAt: 1_000, selfX: 0.645, monsterX: 0.70),
            Observation(frameId: 2, capturedAt: 1_500, selfX: 0.645, monsterX: null));
        var executor = new RecordingActionExecutor();
        var sink = new RecordingRhythmSink(executor);
        var random = new ScriptedRandomSource(97, 1_000);
        var orchestrator = CreateOrchestrator(source, executor, stationaryRhythmEnabled: true, random, sink);

        OrchestratorRunResult result = await orchestrator.RunUntilPausedAsync(2, CancellationToken.None);

        Assert.Equal(PauseReason.TargetLost, result.PauseReason);
        Assert.Equal(["Attack:SingleAttack.down", "Attack:SingleAttack.up", "releaseAll"], executor.Events);
        Assert.Contains(sink.Updates, update => update.Snapshot.EarlyReleaseReason == nameof(PauseReason.TargetLost));
    }

    [Fact]
    public async Task StationaryRhythmWritesSampledPhasesToTheJournal()
    {
        var source = new QueueObservationSource(
            Observation(frameId: 1, capturedAt: 1_000, selfX: 0.645, monsterX: 0.70),
            Observation(frameId: 2, capturedAt: 2_000, selfX: 0.645, monsterX: 0.70),
            Observation(frameId: 3, capturedAt: 2_060, selfX: 0.64, monsterX: 0.70),
            Observation(frameId: 4, capturedAt: 2_110, selfX: 0.64, monsterX: 0.70),
            Observation(frameId: 5, capturedAt: 2_170, selfX: 0.645, monsterX: 0.70));
        var executor = new RecordingActionExecutor();
        var journal = new RecordingJournal();
        var random = new ScriptedRandomSource(97, 1_000, 0, 60, 50, 60, 99);
        var orchestrator = CreateOrchestrator(source, executor, stationaryRhythmEnabled: true, random, journal: journal);

        await orchestrator.RunUntilPausedAsync(1, CancellationToken.None);

        Assert.Contains(journal.Entries, entry => entry.Type == "combat.rhythm.updated"
            && entry.RhythmPhase == nameof(CombatRhythmPhase.AttackHolding)
            && entry.PlannedDurationMs == 1_000);
        Assert.Contains(journal.Entries, entry => entry.RhythmPhase == nameof(CombatRhythmPhase.MoveLeft));
        Assert.Contains(journal.Entries, entry => entry.RhythmPhase == nameof(CombatRhythmPhase.MoveRight));
    }

    [Fact]
    public async Task StationaryRhythmReportsCancellationAndReleasesTheHeldAttack()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new QueueObservationSource(Observation(frameId: 1, capturedAt: 1_000, selfX: 0.645, monsterX: 0.70));
        var executor = new RecordingActionExecutor { OnKeyDown = cancellation.Cancel };
        var sink = new RecordingRhythmSink(executor);
        var random = new ScriptedRandomSource(97, 1_000);
        var orchestrator = CreateOrchestrator(source, executor, stationaryRhythmEnabled: true, random, sink);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.RunUntilPausedAsync(1, cancellation.Token));

        Assert.Equal(["Attack:SingleAttack.down", "Attack:SingleAttack.up", "releaseAll"], executor.Events);
        Assert.Contains(sink.Updates, update => update.Snapshot.EarlyReleaseReason == "Cancelled");
    }

    [Fact]
    public async Task FeedbackClosesMovementBeforeItsLimitAndThenAttacks()
    {
        var source = new QueueObservationSource(
            Observation(frameId: 1, capturedAt: 1_000, selfX: 0.20, monsterX: 0.70),
            Observation(frameId: 2, capturedAt: 1_080, selfX: 0.645, monsterX: 0.70),
            Observation(frameId: 3, capturedAt: 1_160, selfX: 0.645, monsterX: null));
        var executor = new RecordingActionExecutor();
        var orchestrator = CreateOrchestrator(source, executor);

        OrchestratorRunResult result = await orchestrator.RunUntilPausedAsync(8, CancellationToken.None);

        Assert.Equal(PauseReason.TargetLost, result.PauseReason);
        Assert.Equal(2, result.ExecutedActions);
        Assert.Equal(
            ["MoveRight.down", "MoveRight.up", "Attack:SingleAttack.down", "Attack:SingleAttack.up", "releaseAll"],
            executor.Events);
        Assert.Equal(3, source.ReadCount);
    }

    [Fact]
    public async Task PlayersNeverBecomeAttackTargets()
    {
        var source = new QueueObservationSource(Observation(frameId: 1, capturedAt: 1_000, selfX: 0.20, monsterX: null, includePlayer: true));
        var executor = new RecordingActionExecutor();
        var orchestrator = CreateOrchestrator(source, executor);

        OrchestratorRunResult result = await orchestrator.RunUntilPausedAsync(2, CancellationToken.None);

        Assert.Equal(PauseReason.TargetLost, result.PauseReason);
        Assert.Equal(["releaseAll"], executor.Events);
    }

    [Fact]
    public async Task SafetyFailurePreventsInputAndReleasesEverything()
    {
        RuntimeObservationContext stale = Observation(frameId: 1, capturedAt: 1_000, selfX: 0.20, monsterX: 0.70) with
        {
            FrameFresh = false
        };
        var source = new QueueObservationSource(stale);
        var executor = new RecordingActionExecutor();
        var orchestrator = CreateOrchestrator(source, executor);

        OrchestratorRunResult result = await orchestrator.RunUntilPausedAsync(2, CancellationToken.None);

        Assert.Equal(PauseReason.StaleFrame, result.PauseReason);
        Assert.Equal(["releaseAll"], executor.Events);
    }

    [Fact]
    public async Task ExecutorExceptionStillReleasesEveryKey()
    {
        var source = new QueueObservationSource(Observation(frameId: 1, capturedAt: 1_000, selfX: 0.20, monsterX: 0.70));
        var executor = new RecordingActionExecutor { ThrowOnKeyDown = true };
        var orchestrator = CreateOrchestrator(source, executor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.RunUntilPausedAsync(2, CancellationToken.None));

        Assert.Equal(["MoveRight.down", "releaseAll"], executor.Events);
    }

    [Fact]
    public async Task CancellationReleasesTheCurrentActionAndAllKeys()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new QueueObservationSource(Observation(frameId: 1, capturedAt: 1_000, selfX: 0.20, monsterX: 0.70));
        var executor = new RecordingActionExecutor { OnKeyDown = cancellation.Cancel };
        var orchestrator = CreateOrchestrator(source, executor);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.RunUntilPausedAsync(2, cancellation.Token));

        Assert.Equal(["MoveRight.down", "MoveRight.up", "releaseAll"], executor.Events);
    }

    private static ProductionOrchestrator CreateOrchestrator(
        IObservationSource source,
        IActionExecutor executor,
        bool stationaryRhythmEnabled = false,
        IRandomSource? random = null,
        ICombatRhythmSink? rhythmSink = null,
        IRuntimeJournal? journal = null)
    {
        var settings = new ActionPolicySettings
        {
            ClientWidthPx = 1280,
            AttackRangePx = 80,
            SelfConfidenceThreshold = 0.90,
            TargetConfidenceThreshold = 0.80,
            ObservedSpeedPxPerSecond = 320,
            MinMoveHoldMs = 60,
            MaxMoveHoldMs = 400,
            AttackHoldMs = 80,
            HpPotionThreshold = 0.35,
            MpPotionThreshold = 0.30,
            PickupEnabled = false,
            MaxAttackNoFeedbackAttempts = 2
        };
        return new ProductionOrchestrator(
            source,
            executor,
            new SafetyGate(settings.SelfConfidenceThreshold),
            new ActionPolicy(new MovementDurationEstimator()),
            settings,
            new OrchestratorOptions { MaximumFeedbackFramesPerAction = 16, StationaryRhythmEnabled = stationaryRhythmEnabled },
            journal,
            rhythmSampler: random is null ? null : new StationaryAttackRhythmSampler(random, new StationaryAttackRhythmOptions()),
            rhythmSink: rhythmSink);
    }

    private static RuntimeObservationContext Observation(long frameId, long capturedAt, double selfX, double? monsterX, bool includePlayer = true)
    {
        long freshUntil = capturedAt + 250;
        var target = new TargetBinding { SchemaVersion = 2, Hwnd = "0x1", Pid = 1, ClientWidth = 1280, ClientHeight = 720, Dpi = 96 };
        var snapshot = new ObservationSnapshot
        {
            SchemaVersion = 2,
            FrameId = frameId,
            CapturedAtMonoMs = capturedAt,
            Target = target,
            Self = new SelfObservation { Box = [selfX, 0.50, 0.08, 0.18], Confidence = 0.98, FreshUntilMonoMs = freshUntil },
            Players = includePlayer
                ? [new PlayerObservation { Box = [0.22, 0.50, 0.08, 0.18], Confidence = 0.99, FreshUntilMonoMs = freshUntil, TrackId = "player-1" }]
                : [],
            Monsters = monsterX.HasValue
                ? [new MonsterObservation { Class = "snail", Box = [monsterX.Value, 0.50, 0.08, 0.18], Confidence = 0.96, FreshUntilMonoMs = freshUntil, TargetId = "monster-1" }]
                : [],
            Loot = new LootObservation { Visible = false, Confidence = 0, FreshUntilMonoMs = freshUntil },
            Hp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.90, Confidence = 0.99, FreshUntilMonoMs = freshUntil },
            Mp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.80, Confidence = 0.99, FreshUntilMonoMs = freshUntil },
            Map = new MapObservation { MapId = "forest-east", State = MapArchiveState.Validated, Confidence = 0.99, FreshUntilMonoMs = freshUntil },
            State = SessionState.Observing
        };
        return new RuntimeObservationContext(
            snapshot,
            new PlatformContext
            {
                CurrentPlatformId = "p1",
                TargetPlatformId = "p1",
                SamePlatform = true,
                CanJump = true,
                DistanceToBoundaryPx = 500,
                CameraStable = true,
                Facing = FacingDirection.Right
            },
            TargetBound: true,
            IsForeground: true,
            FrameFresh: true,
            HpHealthy: true,
            MpHealthy: true,
            InputAdapterHealthy: true,
            EmergencyStop: false);
    }

    private sealed class QueueObservationSource(params RuntimeObservationContext[] observations) : IObservationSource
    {
        private readonly Queue<RuntimeObservationContext> queue = new(observations);
        public int ReadCount { get; private set; }

        public ValueTask<RuntimeObservationContext> ReadNextAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (queue.Count == 0) throw new InvalidOperationException("Replay observation queue exhausted");
            return ValueTask.FromResult(queue.Dequeue());
        }
    }

    private sealed class RecordingActionExecutor : IActionExecutor
    {
        private readonly HashSet<string> activeActionIds = [];
        public List<string> Events { get; } = [];
        public List<AbstractAction> Actions { get; } = [];
        public bool HasActiveActions => activeActionIds.Count != 0;
        public bool ThrowOnKeyDown { get; init; }
        public Action? OnKeyDown { get; init; }

        public ValueTask KeyDownAsync(AbstractAction action, CancellationToken cancellationToken)
        {
            Actions.Add(action);
            activeActionIds.Add(action.ActionId);
            Events.Add(Label(action) + ".down");
            if (ThrowOnKeyDown) throw new InvalidOperationException("injected input failure");
            OnKeyDown?.Invoke();
            return ValueTask.CompletedTask;
        }

        public ValueTask KeyUpAsync(AbstractAction action, CancellationToken cancellationToken)
        {
            activeActionIds.Remove(action.ActionId);
            Events.Add(Label(action) + ".up");
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAllAsync(CancellationToken cancellationToken)
        {
            activeActionIds.Clear();
            Events.Add("releaseAll");
            return ValueTask.CompletedTask;
        }

        private static string Label(AbstractAction action) => action.ProfileId.HasValue
            ? $"{action.Type}:{action.ProfileId.Value}"
            : action.Type.ToString();
    }

    private sealed class ScriptedRandomSource(params int[] values) : IRandomSource
    {
        private readonly Queue<int> values = new(values);

        public int Next(int minInclusive, int maxExclusive)
        {
            if (values.Count == 0) throw new InvalidOperationException("No scripted random value remains");
            int value = values.Dequeue();
            if (value < minInclusive || value >= maxExclusive)
            {
                throw new InvalidOperationException($"Scripted value {value} is outside [{minInclusive}, {maxExclusive})");
            }
            return value;
        }
    }

    private sealed class RecordingRhythmSink(RecordingActionExecutor executor) : ICombatRhythmSink
    {
        public List<(CombatRhythmSnapshot Snapshot, bool HadActiveKey)> Updates { get; } = [];

        public ValueTask PublishAsync(CombatRhythmSnapshot snapshot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Updates.Add((snapshot, executor.HasActiveActions));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingJournal : IRuntimeJournal
    {
        public List<RuntimeJournalEntry> Entries { get; } = [];

        public ValueTask WriteAsync(RuntimeJournalEntry entry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entries.Add(entry);
            return ValueTask.CompletedTask;
        }
    }
}
