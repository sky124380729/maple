using Maple.Contracts;
using Maple.Core;
using Maple.Runtime;

namespace Maple.Host;

public sealed record StationaryAttackTiming(
    int AttackMinMs,
    int AttackMaxMs,
    int MoveMinMs,
    int MoveMaxMs,
    int BetweenMoveMinMs,
    int BetweenMoveMaxMs)
{
    public static StationaryAttackTiming Default { get; } = new(25_000, 35_000, 120, 260, 100, 420);
}

public sealed class StationaryAttackController : IDisposable
{
    private const int LeaseRefreshMinMs = 900;
    private const int LeaseRefreshMaxMs = 1_300;
    private readonly IAutomaticCombatInputSession inputSession;
    private readonly IActionExecutor executor;
    private readonly StationaryAttackTiming timing;
    private readonly Func<int, int, int> nextInt;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly object sync = new();
    private CancellationTokenSource? cancellation;
    private bool disposed;

    public StationaryAttackController(
        IAutomaticCombatInputSession inputSession,
        IActionExecutor executor,
        StationaryAttackTiming? timing = null,
        Func<int, int, int>? nextInt = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.inputSession = inputSession ?? throw new ArgumentNullException(nameof(inputSession));
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.timing = timing ?? StationaryAttackTiming.Default;
        this.nextInt = nextInt ?? ((minimum, maximum) => Random.Shared.Next(minimum, maximum + 1));
        this.delay = delay ?? Task.Delay;
        if (this.timing.AttackMinMs < 1 || this.timing.AttackMaxMs < this.timing.AttackMinMs)
            throw new ArgumentOutOfRangeException(nameof(timing));
    }

    public event EventHandler<AutomaticCombatStatus>? StatusChanged;
    public Task? Completion { get; private set; }
    public bool IsRunning { get { lock (sync) return Completion is { IsCompleted: false }; } }

    public async Task<AutomaticCombatArmResult> StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsRunning) return new(true, "STATIONARY_ATTACK_ALREADY_RUNNING", PauseReason.None);

        Publish(SessionState.Arming, PauseReason.None, "STATIONARY_ATTACK_ARMING");
        ForegroundResumeResult resumed = await inputSession.ResumeAsync(cancellationToken).ConfigureAwait(false);
        if (!resumed.Success)
        {
            inputSession.Pause(PauseReason.InputUnavailable);
            return new(false, resumed.Code, PauseReason.InputUnavailable);
        }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task run = RunAsync(linked.Token);
        lock (sync)
        {
            cancellation?.Dispose();
            cancellation = linked;
            Completion = run;
        }
        Publish(SessionState.Observing, PauseReason.None, "STATIONARY_ATTACK_RUNNING");
        return new(true, "STATIONARY_ATTACK_RUNNING", PauseReason.None);
    }

    public async Task StopAsync(PauseReason reason = PauseReason.OperatorRequested)
    {
        Task? run;
        lock (sync) { cancellation?.Cancel(); run = Completion; }
        if (run is not null)
        {
            try { await run.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        await executor.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
        inputSession.Pause(reason);
        Publish(SessionState.Paused, reason, "STATIONARY_ATTACK_STOPPED");
    }

    public async Task EmergencyStopAsync()
    {
        lock (sync) cancellation?.Cancel();
        inputSession.EmergencyStop();
        await executor.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
        Publish(SessionState.EmergencyStop, PauseReason.SafetyViolation, "STATIONARY_ATTACK_EMERGENCY_STOP");
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await HoldAsync(ActionType.Attack, ActionProfileId.SingleAttack, nextInt(timing.AttackMinMs, timing.AttackMaxMs), cancellationToken).ConfigureAwait(false);
                await DelayAsync(timing.BetweenMoveMinMs, timing.BetweenMoveMaxMs, cancellationToken).ConfigureAwait(false);

                bool leftFirst = nextInt(0, 1) == 0;
                ActionType first = leftFirst ? ActionType.MoveLeft : ActionType.MoveRight;
                ActionType second = leftFirst ? ActionType.MoveRight : ActionType.MoveLeft;
                await HoldAsync(first, null, nextInt(timing.MoveMinMs, timing.MoveMaxMs), cancellationToken).ConfigureAwait(false);
                await DelayAsync(timing.BetweenMoveMinMs, timing.BetweenMoveMaxMs, cancellationToken).ConfigureAwait(false);
                await HoldAsync(second, null, nextInt(timing.MoveMinMs, timing.MoveMaxMs), cancellationToken).ConfigureAwait(false);
                await DelayAsync(timing.BetweenMoveMinMs, timing.BetweenMoveMaxMs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            inputSession.Pause(PauseReason.OperatorRequested);
            Publish(SessionState.Paused, PauseReason.OperatorRequested, "STATIONARY_ATTACK_STOPPED");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            inputSession.Pause(PauseReason.SafetyViolation);
            Publish(SessionState.Paused, PauseReason.SafetyViolation, "STATIONARY_ATTACK_FAULT:" + exception.GetType().Name);
        }
        finally
        {
            await executor.ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task HoldAsync(ActionType type, ActionProfileId? profile, int holdMs, CancellationToken cancellationToken)
    {
        bool down = false;
        try
        {
            await executor.KeyDownAsync(CreateAction(type, profile), cancellationToken).ConfigureAwait(false);
            down = true;
            int remainingMs = holdMs;
            while (remainingMs > 0)
            {
                int sliceMs = Math.Min(remainingMs, nextInt(LeaseRefreshMinMs, LeaseRefreshMaxMs));
                await delay(TimeSpan.FromMilliseconds(sliceMs), cancellationToken).ConfigureAwait(false);
                remainingMs -= sliceMs;
                if (remainingMs > 0)
                    await executor.KeyDownAsync(CreateAction(type, profile), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (down) await executor.KeyUpAsync(CreateAction(type, profile), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static AbstractAction CreateAction(ActionType type, ActionProfileId? profile)
    {
        long now = Environment.TickCount64;
        return new AbstractAction
        {
            ActionId = "stationary-" + Guid.NewGuid().ToString("N"),
            Type = type,
            ProfileId = profile,
            IssuedAtMonoMs = now,
            HoldMs = 0,
            MaxDurationMs = ContractConstants.MaxActionDurationMs,
        };
    }

    private Task DelayAsync(int minimum, int maximum, CancellationToken cancellationToken) =>
        delay(TimeSpan.FromMilliseconds(nextInt(minimum, maximum)), cancellationToken);

    private void Publish(SessionState state, PauseReason reason, string code) =>
        StatusChanged?.Invoke(this, new AutomaticCombatStatus(state, reason, code));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation?.Cancel();
        try { Completion?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        executor.ReleaseAllAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        cancellation?.Dispose();
    }
}
