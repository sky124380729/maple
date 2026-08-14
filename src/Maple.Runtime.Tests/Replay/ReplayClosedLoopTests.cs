using Maple.Contracts;
using Maple.Core;
using Maple.Replay;
using Maple.Runtime;
using Xunit;

namespace Maple.Runtime.Tests.Replay;

public sealed class ReplayClosedLoopTests
{
    [Fact]
    public async Task JsonlReplayRunsThroughTheProductionOrchestrator()
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "runtime-closed-loop.jsonl");
        await using FileStream stream = File.OpenRead(fixture);
        var source = new JsonlReplayObservationSource(stream);
        var executor = new ReplayActionExecutor();
        var journalText = new StringWriter();
        var journal = new JsonlReplayJournal(journalText);
        ActionPolicySettings settings = Settings();
        var orchestrator = new ProductionOrchestrator(
            source,
            executor,
            new SafetyGate(settings.SelfConfidenceThreshold),
            new ActionPolicy(new MovementDurationEstimator()),
            settings,
            new OrchestratorOptions(),
            journal);

        OrchestratorRunResult result = await orchestrator.RunUntilPausedAsync(8, CancellationToken.None);

        Assert.Equal(PauseReason.TargetLost, result.PauseReason);
        Assert.Equal(2, result.ExecutedActions);
        Assert.Equal(
            ["MoveRight.down", "MoveRight.up", "Attack:SingleAttack.down", "Attack:SingleAttack.up", "releaseAll"],
            executor.Events.Select(item => item.Label));
        Assert.False(executor.HasActiveActions);
        string[] journalEvents = journalText.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(journalEvents, line => line.Contains("\"type\":\"action.decided\"", StringComparison.Ordinal));
        Assert.Contains(journalEvents, line => line.Contains("\"type\":\"action.feedback\"", StringComparison.Ordinal));
        Assert.Contains(journalEvents, line => line.Contains("\"type\":\"input.releaseAll\"", StringComparison.Ordinal));
    }

    [Fact]
    public void JsonlReplayRejectsVersionOneBeforeExecution()
    {
        const string line = "{\"schemaVersion\":1,\"type\":\"runtime.observation\",\"payload\":{}}";

        Assert.Throws<InvalidDataException>(() => new JsonlReplayObservationSource(new StringReader(line)));
    }

    private static ActionPolicySettings Settings() => new()
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
}
