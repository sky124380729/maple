using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Maple.Contracts;
using Maple.Host;
using Maple.Input;
using Xunit;

namespace Maple.Host.Tests;

public sealed class BrokerActionExecutorTests
{
    [Theory]
    [InlineData(ActionType.MoveLeft, null, BrokerActionKind.MoveLeft, null)]
    [InlineData(ActionType.Jump, null, BrokerActionKind.Jump, "Alt")]
    [InlineData(ActionType.Attack, ActionProfileId.SingleAttack, BrokerActionKind.SingleAttack, "Ctrl")]
    [InlineData(ActionType.Attack, ActionProfileId.AreaAttack, BrokerActionKind.AreaAttack, "Ctrl")]
    [InlineData(ActionType.UsePotion, ActionProfileId.HpPotion, BrokerActionKind.HpPotion, "Delete")]
    [InlineData(ActionType.UsePotion, ActionProfileId.MpPotion, BrokerActionKind.MpPotion, "End")]
    public async Task ExecutorMapsOnlySupportedAbstractActions(
        ActionType type,
        ActionProfileId? profile,
        BrokerActionKind expected,
        string? expectedLogicalKey)
    {
        var adapter = new RecordingAdapter();
        var executor = new BrokerActionExecutor(adapter);
        var action = new AbstractAction
        {
            ActionId = "action-1",
            Type = type,
            ProfileId = profile,
            IssuedAtMonoMs = 100,
            HoldMs = 120,
            MaxDurationMs = 300
        };

        await executor.KeyDownAsync(action, CancellationToken.None);

        Assert.Equal(expected, BrokerActionMapping.ToBrokerAction(action));
        Assert.Equal(expectedLogicalKey, Assert.Single(adapter.Keys));
    }

    [Fact]
    public async Task ExecutorUsesTheActiveNativeKeyProfile()
    {
        var adapter = new RecordingAdapter();
        CombatConfiguration configuration = CombatConfiguration.Default with { SingleAttackKey = "X" };
        var executor = new BrokerActionExecutor(adapter, () => configuration);
        var action = new AbstractAction
        {
            ActionId = "configured-attack",
            Type = ActionType.Attack,
            ProfileId = ActionProfileId.SingleAttack,
            IssuedAtMonoMs = 100,
            HoldMs = 120,
            MaxDurationMs = 300,
        };

        await executor.KeyDownAsync(action, CancellationToken.None);

        Assert.Equal("X", Assert.Single(adapter.Keys));
    }

    private sealed class RecordingAdapter : IInputAdapter
    {
        public List<string?> Keys { get; } = new();
        public InputResult KeyDown(AbstractAction action, string key, long nowMonoMs)
        {
            Keys.Add(key);
            return Result(action, InputStatus.Accepted);
        }
        public InputResult KeyUp(AbstractAction action, string key, long nowMonoMs) => Result(action, InputStatus.Completed);
        public InputResult Press(AbstractAction action, string key, long nowMonoMs) => Result(action, InputStatus.Completed);
        public InputResult ReleaseAll(long nowMonoMs) => Result(new AbstractAction { ActionId = "release-all" }, InputStatus.Completed);
        public bool Heartbeat(long nowMonoMs) => true;
        public InputAdapterStatus GetStatus() => new();
        private static InputResult Result(AbstractAction action, InputStatus status) => new()
        {
            ActionId = action.ActionId,
            Status = status,
            ReleasedKeys = new List<string>()
        };
    }
}
