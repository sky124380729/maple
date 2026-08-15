using System.Globalization;
using System.Runtime.InteropServices;
using Maple.Contracts;
using Maple.Input;

namespace Maple.Host;

public delegate void CountdownChangedEventHandler(object? sender, int secondsRemaining);

public sealed record ForegroundResumeResult(bool Success, string Code);

public interface IInputBrokerSession
{
    Task EnsureStartedAsync(CancellationToken cancellationToken);
    void ArmTarget(ArmTargetPayload target);
    InputAdapterStatus GetStatus();
    InputResult ReleaseAll(long nowMonoMs);
}

public interface IForegroundWindowController
{
    bool TryActivate(nint hwnd);
    nint GetForegroundWindow();
}

public interface IForegroundSessionDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class ForegroundSessionController : IDisposable
{
    private readonly ITargetWindowLocator locator;
    private readonly IForegroundWindowController foreground;
    private readonly IInputBrokerSession broker;
    private readonly HostSafetyCoordinator safety;
    private readonly IForegroundSessionDelay delay;
    private readonly SemaphoreSlim transitionLock = new(1, 1);
    private readonly Func<long> clock;
    private nint armedHwnd;
    private bool disposed;
    private string? disabledCode;

    public ForegroundSessionController(
        ITargetWindowLocator locator,
        IForegroundWindowController foreground,
        IInputBrokerSession broker,
        HostSafetyCoordinator safety,
        IForegroundSessionDelay delay,
        Func<long>? clock = null)
    {
        this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
        this.foreground = foreground ?? throw new ArgumentNullException(nameof(foreground));
        this.broker = broker ?? throw new ArgumentNullException(nameof(broker));
        this.safety = safety ?? throw new ArgumentNullException(nameof(safety));
        this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
        this.clock = clock ?? (() => Environment.TickCount64);
    }

    public event CountdownChangedEventHandler? CountdownChanged;

    public bool IsArmed { get; private set; }

    public async Task<ForegroundResumeResult> ResumeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (disabledCode is not null) return new(false, disabledCode);
            if (!safety.BeginArming()) return new(false, "SESSION_CANNOT_ARM");
            TargetWindowDiscoveryResult discovery = locator.Locate();
            WindowIdentity? target = discovery.Target;
            if (target is null) return Fail(discovery.DiagnosticCode, PauseReason.TargetLost);
            if (!TryParseHwnd(target.Hwnd, out nint hwnd) || string.IsNullOrWhiteSpace(target.ProcessPath))
                return Fail("TARGET_IDENTITY_INCOMPLETE", PauseReason.TargetLost);
            try { await broker.EnsureStartedAsync(cancellationToken).ConfigureAwait(false); }
            catch (InputUnavailableException exception)
            {
                return Fail(exception.Code, PauseReason.InputUnavailable);
            }

            for (int remaining = 3; remaining >= 1; remaining--)
            {
                CountdownChanged?.Invoke(this, remaining);
                await delay.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }

            if (!foreground.TryActivate(hwnd))
                return Fail("TARGET_ACTIVATION_FAILED", PauseReason.WindowNotForeground);

            bool confirmed = false;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (foreground.GetForegroundWindow() == hwnd)
                {
                    confirmed = true;
                    break;
                }
                await delay.DelayAsync(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
            if (!confirmed) return Fail("TARGET_FOREGROUND_TIMEOUT", PauseReason.WindowNotForeground);

            WindowIdentity? confirmedTarget = locator.Locate().Target;
            if (confirmedTarget is null
                || !SameIdentity(target, confirmedTarget)
                || !confirmedTarget.IsForeground
                || confirmedTarget.IsMinimized)
                return Fail("TARGET_IDENTITY_CHANGED", PauseReason.TargetLost);

            try
            {
                broker.ArmTarget(new ArmTargetPayload(
                    hwnd.ToInt64(),
                    target.Pid,
                    target.ProcessStartedAtUtc.UtcDateTime.Ticks,
                    target.ProcessPath));
            }
            catch (InputUnavailableException exception)
            {
                return Fail(exception.Code, PauseReason.InputUnavailable);
            }

            InputAdapterStatus status = broker.GetStatus();
            if (!status.IsHealthy || !status.InjectionEnabled)
                return Fail(status.Code ?? "INPUT_BROKER_NOT_READY", PauseReason.InputUnavailable);

            armedHwnd = hwnd;
            IsArmed = true;
            if (!safety.MarkObserving()) return Fail("SESSION_OBSERVING_REJECTED", PauseReason.SafetyViolation);
            return new(true, "INPUT_SESSION_READY");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Fail("RESUME_CANCELLED", PauseReason.OperatorRequested);
        }
        finally { transitionLock.Release(); }
    }

    public Task ToggleAsync(CancellationToken cancellationToken)
    {
        if (IsArmed)
        {
            Pause(PauseReason.OperatorRequested);
            return Task.CompletedTask;
        }
        return ResumeAsync(cancellationToken);
    }

    public Task OnForegroundChangedAsync(nint foregroundHwnd, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsArmed && foregroundHwnd != armedHwnd)
        {
            if (safety.State == SessionState.Paused)
            {
                IsArmed = false;
                armedHwnd = nint.Zero;
            }
            else Pause(PauseReason.WindowNotForeground);
        }
        return Task.CompletedTask;
    }

    public void Pause(PauseReason reason = PauseReason.OperatorRequested) => PauseCore(reason);

    public void Disable(string code)
    {
        disabledCode = string.IsNullOrWhiteSpace(code) ? "INPUT_UNAVAILABLE" : code;
        PauseCore(PauseReason.InputUnavailable);
    }

    public void EmergencyStop()
    {
        IsArmed = false;
        armedHwnd = nint.Zero;
        safety.EmergencyStop();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        IsArmed = false;
        armedHwnd = nint.Zero;
        safety.ReleaseForShutdown();
        transitionLock.Dispose();
    }

    private ForegroundResumeResult Fail(string code, PauseReason reason)
    {
        PauseCore(reason);
        return new(false, string.IsNullOrWhiteSpace(code) ? "INPUT_SESSION_FAILED" : code);
    }

    private void PauseCore(PauseReason reason)
    {
        bool wasArmed = IsArmed;
        IsArmed = false;
        armedHwnd = nint.Zero;
        if (wasArmed || safety.State != SessionState.Paused)
            safety.PauseAndRelease(reason);
    }

    private static bool SameIdentity(WindowIdentity expected, WindowIdentity actual) =>
        string.Equals(expected.Hwnd, actual.Hwnd, StringComparison.OrdinalIgnoreCase)
        && expected.Pid == actual.Pid
        && expected.ProcessStartedAtUtc == actual.ProcessStartedAtUtc
        && string.Equals(expected.ProcessPathSha256, actual.ProcessPathSha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(expected.ProcessPath, actual.ProcessPath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(expected.ClassName, actual.ClassName, StringComparison.Ordinal);

    private static bool TryParseHwnd(string value, out nint hwnd)
    {
        hwnd = nint.Zero;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (!long.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long parsed) || parsed == 0)
            return false;
        hwnd = (nint)parsed;
        return true;
    }
}

public sealed class WindowsForegroundWindowController : IForegroundWindowController
{
    private const int SwRestore = 9;

    public bool TryActivate(nint hwnd)
    {
        if (hwnd == nint.Zero) return false;
        if (NativeGetForegroundWindow() == hwnd && !IsIconic(hwnd)) return true;
        if (IsIconic(hwnd)) _ = ShowWindowAsync(hwnd, SwRestore);
        return SetForegroundWindow(hwnd);
    }

    public nint GetForegroundWindow() => NativeGetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint NativeGetForegroundWindow();
}

public sealed class SystemForegroundSessionDelay : IForegroundSessionDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
