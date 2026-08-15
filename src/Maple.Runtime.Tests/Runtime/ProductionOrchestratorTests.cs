using Maple.Contracts;
using Maple.Core;
using Maple.Runtime;
using Xunit;

namespace Maple.Runtime.Tests.Runtime;

public sealed class ProductionOrchestratorTests
{
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

    [Fact]
    public async Task TimingDecisionIsJournaledWithItsSessionSeed()
    {
        var source = new QueueObservationSource(
            Observation(frameId: 1, capturedAt: 1_000, selfX: 0.20, monsterX: 0.70),
            Observation(frameId: 2, capturedAt: 1_080, selfX: 0.645, monsterX: 0.70));
        var executor = new RecordingActionExecutor();
        var journal = new RecordingJournal();
        var orchestrator = CreateOrchestrator(
            source,
            executor,
            journal,
            new ActionTimingRandomizer(42, 0.08));

        await orchestrator.RunUntilPausedAsync(1, CancellationToken.None);

        RuntimeJournalEntry entry = Assert.Single(
            journal.Entries,
            item => item.Type == "action.decided");
        Assert.Equal(42, entry.TimingSeed);
        Assert.NotNull(entry.BaselineHoldMs);
        Assert.Equal(entry.ComputedHoldMs, entry.FinalHoldMs);
        Assert.Equal(entry.FinalHoldMs - entry.BaselineHoldMs, entry.VariationMs);
    }

    private static ProductionOrchestrator CreateOrchestrator(
        IObservationSource source,
        IActionExecutor executor,
        IRuntimeJournal? journal = null,
        ActionTimingRandomizer? timingRandomizer = null)
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
            new OrchestratorOptions { MaximumFeedbackFramesPerAction = 16 },
            journal,
            timingRandomizer);
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
        public List<string> Events { get; } = [];
        public bool ThrowOnKeyDown { get; init; }
        public Action? OnKeyDown { get; init; }

        public ValueTask KeyDownAsync(AbstractAction action, CancellationToken cancellationToken)
        {
            Events.Add(Label(action) + ".down");
            if (ThrowOnKeyDown) throw new InvalidOperationException("injected input failure");
            OnKeyDown?.Invoke();
            return ValueTask.CompletedTask;
        }

        public ValueTask KeyUpAsync(AbstractAction action, CancellationToken cancellationToken)
        {
            Events.Add(Label(action) + ".up");
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAllAsync(CancellationToken cancellationToken)
        {
            Events.Add("releaseAll");
            return ValueTask.CompletedTask;
        }

        private static string Label(AbstractAction action) => action.ProfileId.HasValue
            ? $"{action.Type}:{action.ProfileId.Value}"
            : action.Type.ToString();
    }

    private sealed class RecordingJournal : IRuntimeJournal
    {
        public List<RuntimeJournalEntry> Entries { get; } = [];

        public ValueTask WriteAsync(RuntimeJournalEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return ValueTask.CompletedTask;
        }
    }
}
