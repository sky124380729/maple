using Maple.Contracts;
using Maple.Runtime;

namespace Maple.Host;

public enum InputAcceptanceKind
{
    MoveLeft,
    MoveRight,
    ClimbUp,
    ClimbDown,
    Jump,
    Attack,
    Pickup,
    HpPotion,
    MpPotion,
}

public interface IInputAcceptanceDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public interface IInputAcceptanceController
{
    Task<InputResult> RunAsync(InputAcceptanceKind kind, int holdMs, CancellationToken cancellationToken);
}

public sealed class SystemInputAcceptanceDelay : IInputAcceptanceDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public sealed class InputAcceptanceController : IInputAcceptanceController
{
    private readonly IAutomaticCombatInputSession session;
    private readonly IActionExecutor executor;
    private readonly IInputAcceptanceDelay delay;
    private readonly Func<long> clock;
    private readonly SemaphoreSlim gate = new(1, 1);

    public InputAcceptanceController(
        IAutomaticCombatInputSession session,
        IActionExecutor executor,
        IInputAcceptanceDelay delay,
        Func<long>? clock = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
        this.clock = clock ?? (() => Environment.TickCount64);
    }

    public async Task<InputResult> RunAsync(
        InputAcceptanceKind kind,
        int holdMs,
        CancellationToken cancellationToken)
    {
        if (holdMs is < 50 or > 600) throw new ArgumentOutOfRangeException(nameof(holdMs));
        string actionId = $"input-test-{Guid.NewGuid():N}";
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return Result(actionId, InputStatus.Rejected, null, clock(), "INPUT_TEST_ALREADY_RUNNING");

        long? startedAt = null;
        InputStatus status = InputStatus.Failed;
        string message = "INPUT_TEST_FAILED";
        try
        {
            if (session.IsArmed)
            {
                status = InputStatus.Rejected;
                message = "INPUT_TEST_REQUIRES_PAUSED_SESSION";
            }
            else
            {
                ForegroundResumeResult resumed = await session.ResumeAsync(cancellationToken).ConfigureAwait(false);
                if (!resumed.Success)
                {
                    message = resumed.Code;
                }
                else
                {
                    AbstractAction action = CreateAction(actionId, kind, holdMs, clock());
                    startedAt = clock();
                    await executor.KeyDownAsync(action, cancellationToken).ConfigureAwait(false);
                    await delay.DelayAsync(TimeSpan.FromMilliseconds(holdMs), cancellationToken).ConfigureAwait(false);
                    await executor.KeyUpAsync(action, cancellationToken).ConfigureAwait(false);
                    status = InputStatus.Completed;
                    message = "INPUT_TEST_COMPLETED";
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = InputStatus.Cancelled;
            message = "INPUT_TEST_CANCELLED";
        }
        catch (InputUnavailableException exception)
        {
            status = InputStatus.Failed;
            message = exception.Code;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            status = InputStatus.Failed;
            message = "INPUT_TEST_FAILED";
        }
        finally
        {
            try
            {
                await executor.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception) { status = InputStatus.Failed; message = "INPUT_TEST_RELEASE_FAILED"; }
            finally
            {
                session.Pause(PauseReason.OperatorRequested);
                gate.Release();
            }
        }

        return Result(actionId, status, startedAt, clock(), message);
    }

    private static AbstractAction CreateAction(string actionId, InputAcceptanceKind kind, int holdMs, long nowMonoMs)
    {
        (ActionType type, ActionProfileId? profile) = kind switch
        {
            InputAcceptanceKind.MoveLeft => (ActionType.MoveLeft, (ActionProfileId?)null),
            InputAcceptanceKind.MoveRight => (ActionType.MoveRight, (ActionProfileId?)null),
            InputAcceptanceKind.ClimbUp => (ActionType.ClimbUp, (ActionProfileId?)null),
            InputAcceptanceKind.ClimbDown => (ActionType.ClimbDown, (ActionProfileId?)null),
            InputAcceptanceKind.Jump => (ActionType.Jump, (ActionProfileId?)null),
            InputAcceptanceKind.Attack => (ActionType.Attack, ActionProfileId.SingleAttack),
            InputAcceptanceKind.Pickup => (ActionType.Pickup, (ActionProfileId?)null),
            InputAcceptanceKind.HpPotion => (ActionType.UsePotion, ActionProfileId.HpPotion),
            InputAcceptanceKind.MpPotion => (ActionType.UsePotion, ActionProfileId.MpPotion),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return new AbstractAction
        {
            ActionId = actionId,
            Type = type,
            ProfileId = profile,
            IssuedAtMonoMs = nowMonoMs,
            HoldMs = holdMs,
            MaxDurationMs = 600,
        };
    }

    private static InputResult Result(
        string actionId,
        InputStatus status,
        long? startedAt,
        long endedAt,
        string message) => new()
        {
            SchemaVersion = ContractConstants.SchemaVersion,
            ActionId = actionId,
            Status = status,
            StartedAtMonoMs = startedAt,
            EndedAtMonoMs = endedAt,
            ReleasedKeys = [],
            Message = message,
        };
}
