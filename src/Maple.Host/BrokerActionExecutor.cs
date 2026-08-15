using System;
using System.Threading;
using System.Threading.Tasks;
using Maple.Contracts;
using Maple.Input;
using Maple.Runtime;

namespace Maple.Host;

public static class BrokerActionMapping
{
    public static BrokerActionKind ToBrokerAction(AbstractAction action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        return action.Type switch
        {
            ActionType.MoveLeft => BrokerActionKind.MoveLeft,
            ActionType.MoveRight => BrokerActionKind.MoveRight,
            ActionType.Jump => BrokerActionKind.Jump,
            ActionType.ClimbUp => BrokerActionKind.ClimbUp,
            ActionType.ClimbDown => BrokerActionKind.ClimbDown,
            ActionType.Pickup => BrokerActionKind.Pickup,
            ActionType.Attack when action.ProfileId == ActionProfileId.SingleAttack => BrokerActionKind.SingleAttack,
            ActionType.Attack when action.ProfileId == ActionProfileId.AreaAttack => BrokerActionKind.AreaAttack,
            ActionType.UsePotion when action.ProfileId == ActionProfileId.HpPotion => BrokerActionKind.HpPotion,
            ActionType.UsePotion when action.ProfileId == ActionProfileId.MpPotion => BrokerActionKind.MpPotion,
            _ => throw new InputUnavailableException("ABSTRACT_ACTION_NOT_INPUT_CAPABLE")
        };
    }

    public static string? DefaultLogicalKey(AbstractAction action) => ToBrokerAction(action) switch
    {
        BrokerActionKind.Jump => "Alt",
        BrokerActionKind.SingleAttack => "J",
        BrokerActionKind.AreaAttack => "A",
        BrokerActionKind.Pickup => "Z",
        BrokerActionKind.HpPotion => "1",
        BrokerActionKind.MpPotion => "2",
        _ => null
    };
}

public sealed class BrokerActionExecutor : IActionExecutor
{
    private readonly IInputAdapter inputAdapter;

    public BrokerActionExecutor(IInputAdapter inputAdapter) =>
        this.inputAdapter = inputAdapter ?? throw new ArgumentNullException(nameof(inputAdapter));

    public ValueTask KeyDownAsync(AbstractAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InputResult result = inputAdapter.KeyDown(
            action,
            BrokerActionMapping.DefaultLogicalKey(action),
            Environment.TickCount64);
        Ensure(result, InputStatus.Accepted);
        return ValueTask.CompletedTask;
    }

    public ValueTask KeyUpAsync(AbstractAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InputResult result = inputAdapter.KeyUp(
            action,
            BrokerActionMapping.DefaultLogicalKey(action),
            Environment.TickCount64);
        Ensure(result, InputStatus.Completed);
        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAllAsync(CancellationToken cancellationToken)
    {
        InputResult result = inputAdapter.ReleaseAll(Environment.TickCount64);
        Ensure(result, InputStatus.Completed);
        return ValueTask.CompletedTask;
    }

    private static void Ensure(InputResult result, InputStatus expected)
    {
        if (result == null || result.Status != expected)
            throw new InputUnavailableException(result?.Message ?? "INPUT_BROKER_RESULT_MISSING");
    }
}
