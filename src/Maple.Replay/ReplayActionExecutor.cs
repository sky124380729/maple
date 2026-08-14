using Maple.Contracts;
using Maple.Runtime;

namespace Maple.Replay;

public sealed record ReplayActionEvent(string Label);

public sealed class ReplayActionExecutor : IActionExecutor
{
    private readonly object sync = new();
    private readonly HashSet<string> activeActions = new(StringComparer.Ordinal);
    private readonly List<ReplayActionEvent> events = [];

    public IReadOnlyList<ReplayActionEvent> Events
    {
        get { lock (sync) return events.ToArray(); }
    }

    public bool HasActiveActions
    {
        get { lock (sync) return activeActions.Count != 0; }
    }

    public ValueTask KeyDownAsync(AbstractAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(action);
        lock (sync)
        {
            if (!activeActions.Add(action.ActionId)) throw new InvalidOperationException("Replay 动作已经处于按下状态");
            events.Add(new ReplayActionEvent(ActionLabel(action) + ".down"));
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask KeyUpAsync(AbstractAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(action);
        lock (sync)
        {
            if (!activeActions.Remove(action.ActionId)) throw new InvalidOperationException("Replay 动作没有对应的按下事件");
            events.Add(new ReplayActionEvent(ActionLabel(action) + ".up"));
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAllAsync(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            activeActions.Clear();
            events.Add(new ReplayActionEvent("releaseAll"));
        }
        return ValueTask.CompletedTask;
    }

    private static void Validate(AbstractAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ContractValidationResult validation = ContractValidation.ValidateAction(action);
        if (!validation.IsValid) throw new ArgumentException("Replay 动作契约无效：" + validation.Error, nameof(action));
    }

    private static string ActionLabel(AbstractAction action) => action.ProfileId.HasValue
        ? $"{action.Type}:{action.ProfileId.Value}"
        : action.Type.ToString();
}
