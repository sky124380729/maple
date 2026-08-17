using Maple.Contracts;
using Maple.Core;
using Maple.Runtime;
using Xunit;

namespace Maple.Host.Tests;

public sealed class StationaryAttackControllerTests
{
    [Fact]
    public async Task OneCycleHoldsAttackThenMovesBothDirectionsAndReleasesEveryKey()
    {
        using var cancellation = new CancellationTokenSource();
        var input = new FakeInputSession();
        var executor = new RecordingExecutor(() => cancellation.Cancel());
        var delays = new List<int>();
        using var controller = new StationaryAttackController(
            input,
            executor,
            new StationaryAttackTiming(25_000, 35_000, 120, 260, 100, 420),
            (minimum, maximum) => (minimum + maximum) / 2,
            (duration, token) => { delays.Add((int)duration.TotalMilliseconds); return Task.CompletedTask; });

        Assert.True((await controller.StartAsync(cancellation.Token)).Success);
        await controller.Completion!;

        Assert.Equal(
            ["Attack.down", "Attack.up", "MoveLeft.down", "MoveLeft.up", "MoveRight.down", "MoveRight.up", "releaseAll"],
            executor.Events);
        Assert.True(delays.Sum() >= 30_000);
        Assert.True(executor.AttackKeyDownCalls > 1);
        Assert.True(executor.ReleaseCalls > 0);
    }

    private sealed class FakeInputSession : IAutomaticCombatInputSession
    {
        public bool IsArmed { get; private set; }
        public Task<ForegroundResumeResult> ResumeAsync(CancellationToken cancellationToken)
        {
            IsArmed = true;
            return Task.FromResult(new ForegroundResumeResult(true, "READY"));
        }
        public void Pause(PauseReason reason) => IsArmed = false;
        public void EmergencyStop() => IsArmed = false;
    }

    private sealed class RecordingExecutor(Action completedCycle) : IActionExecutor
    {
        private readonly HashSet<ActionType> active = [];
        public List<string> Events { get; } = [];
        public int ReleaseCalls { get; private set; }
        public int AttackKeyDownCalls { get; private set; }
        public ValueTask KeyDownAsync(AbstractAction action, CancellationToken cancellationToken)
        {
            if (action.Type == ActionType.Attack) AttackKeyDownCalls++;
            if (active.Add(action.Type)) Events.Add(action.Type + ".down");
            return ValueTask.CompletedTask;
        }
        public ValueTask KeyUpAsync(AbstractAction action, CancellationToken cancellationToken)
        {
            active.Remove(action.Type);
            Events.Add(action.Type + ".up");
            if (action.Type == ActionType.MoveRight) completedCycle();
            return ValueTask.CompletedTask;
        }
        public ValueTask ReleaseAllAsync(CancellationToken cancellationToken)
        {
            ReleaseCalls++;
            active.Clear();
            Events.Add("releaseAll");
            return ValueTask.CompletedTask;
        }
    }
}
