using System.Globalization;
using System.Runtime.InteropServices;
using Maple.Contracts;
using Maple.Input;

namespace Maple.Host;

public delegate void CountdownChangedEventHandler(object? sender, int secondsRemaining);

public sealed record ForegroundResumeResult(bool Success, string Code);
public sealed record InputSessionStatus(
    string Status,
    string Integrity,
    IReadOnlyList<string> ActiveKeys,
    bool LastReleaseSucceeded,
    string? ErrorCode,
    SessionState SessionState,
    PauseReason PauseReason);

public interface IInputBrokerSession
{
    Task EnsureStartedAsync(CancellationToken cancellationToken);
    void ArmTarget(ArmTargetPayload target);
    InputAdapterStatus GetStatus();
    InputResult ReleaseAll(long nowMonoMs);
    bool Heartbeat(long nowMonoMs);
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

public sealed class ForegroundSessionController : IAutomaticCombatInputSession, IDisposable
{
    private readonly ITargetWindowLocator locator;
    private readonly IForegroundWindowController foreground;
    private readonly IInputBrokerSession broker;
    private readonly HostSafetyCoordinator safety;
    private readonly IForegroundSessionDelay delay;
    private readonly SemaphoreSlim transitionLock = new(1, 1);
    private readonly Func<long> clock;
    private readonly object stateSync = new();
    private nint armedHwnd;
    private volatile bool isArmed;
    private bool isArming;
    private CancellationTokenSource? resumeCancellation;
    private long lastHealthCheckMonoMs;
    private bool disposed;
    private string? disabledCode;
    private bool lastReleaseSucceeded = true;

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
        CurrentStatus = CreateStatus("disconnected", null);
    }

    public event CountdownChangedEventHandler? CountdownChanged;
    public event EventHandler<InputSessionStatus>? StatusChanged;

    public bool IsArmed => isArmed;
    public bool IsArming { get { lock (stateSync) return isArming; } }
    public InputSessionStatus CurrentStatus { get; private set; }

    public async Task<ForegroundResumeResult> ResumeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (stateSync)
        {
            if (isArming) return new(false, "SESSION_ALREADY_ARMING");
            if (isArmed) return new(true, "INPUT_SESSION_READY");
        }
        await transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? linkedCancellation = null;
        try
        {
            lock (stateSync)
            {
                if (isArmed) return new(true, "INPUT_SESSION_READY");
                linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                resumeCancellation = linkedCancellation;
                isArming = true;
            }
            CancellationToken transitionToken = linkedCancellation.Token;
            if (disabledCode is not null) return new(false, disabledCode);
            if (!safety.BeginArming())
            {
                PublishStatus("faulted", "SESSION_CANNOT_ARM");
                return new(false, "SESSION_CANNOT_ARM");
            }
            PublishStatus("starting", null);
            TargetWindowDiscoveryResult discovery = locator.Locate();
            WindowIdentity? target = discovery.Target;
            if (target is null) return Fail(discovery.DiagnosticCode, PauseReason.TargetLost);
            if (!TryParseHwnd(target.Hwnd, out nint hwnd) || string.IsNullOrWhiteSpace(target.ProcessPath))
                return Fail("TARGET_IDENTITY_INCOMPLETE", PauseReason.TargetLost);
            try { await broker.EnsureStartedAsync(transitionToken).ConfigureAwait(false); }
            catch (InputUnavailableException exception)
            {
                return Fail(exception.Code, PauseReason.InputUnavailable);
            }

            for (int remaining = 3; remaining >= 1; remaining--)
            {
                CountdownChanged?.Invoke(this, remaining);
                await delay.DelayAsync(TimeSpan.FromSeconds(1), transitionToken).ConfigureAwait(false);
            }

            bool activationRequested = foreground.TryActivate(hwnd);

            bool confirmed = false;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (foreground.GetForegroundWindow() == hwnd)
                {
                    confirmed = true;
                    break;
                }
                await delay.DelayAsync(TimeSpan.FromMilliseconds(50), transitionToken).ConfigureAwait(false);
            }
            if (!confirmed)
                return Fail(
                    activationRequested ? "TARGET_FOREGROUND_TIMEOUT" : "TARGET_ACTIVATION_FAILED",
                    PauseReason.WindowNotForeground);

            WindowIdentity? confirmedTarget = locator.Locate().Target;
            if (confirmedTarget is null)
                return Fail("TARGET_LOST_AFTER_ACTIVATION", PauseReason.TargetLost);
            string? identityMismatch = GetIdentityMismatchCode(target, confirmedTarget);
            if (identityMismatch is not null)
                return Fail(identityMismatch, PauseReason.TargetLost);
            if (confirmedTarget.IsMinimized)
                return Fail("TARGET_MINIMIZED_AFTER_ACTIVATION", PauseReason.TargetLost);
            if (!confirmedTarget.IsForeground)
                return Fail("TARGET_FOREGROUND_RACE", PauseReason.WindowNotForeground);

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
            isArmed = true;
            if (!safety.MarkObserving()) return Fail("SESSION_OBSERVING_REJECTED", PauseReason.SafetyViolation);
            PublishStatus("ready", null);
            return new(true, "INPUT_SESSION_READY");
        }
        catch (OperationCanceledException) when (linkedCancellation?.IsCancellationRequested == true)
        {
            return Fail("RESUME_CANCELLED", PauseReason.OperatorRequested);
        }
        finally
        {
            lock (stateSync)
            {
                if (ReferenceEquals(resumeCancellation, linkedCancellation)) resumeCancellation = null;
                isArming = false;
            }
            linkedCancellation?.Dispose();
            transitionLock.Release();
        }
    }

    public Task ToggleAsync(CancellationToken cancellationToken)
    {
        bool cancelArming;
        lock (stateSync)
        {
            cancelArming = isArming;
            if (cancelArming) resumeCancellation?.Cancel();
        }
        if (cancelArming || IsArmed)
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
                isArmed = false;
                armedHwnd = nint.Zero;
                PublishStatus("paused", null);
            }
            else Pause(PauseReason.WindowNotForeground);
        }
        return Task.CompletedTask;
    }

    public void Pause(PauseReason reason = PauseReason.OperatorRequested) => PauseCore(reason);

    public void Disable(string code)
    {
        disabledCode = string.IsNullOrWhiteSpace(code) ? "INPUT_UNAVAILABLE" : code;
        PauseCore(PauseReason.InputUnavailable, publish: false);
        PublishStatus("faulted", disabledCode);
    }

    public void EmergencyStop()
    {
        CancelResume();
        isArmed = false;
        armedHwnd = nint.Zero;
        safety.EmergencyStop();
        UpdateReleaseResult();
        PublishStatus("paused", null);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CancelResume();
        isArmed = false;
        armedHwnd = nint.Zero;
        safety.ReleaseForShutdown();
        transitionLock.Dispose();
    }

    private ForegroundResumeResult Fail(string code, PauseReason reason)
    {
        string resolvedCode = string.IsNullOrWhiteSpace(code) ? "INPUT_SESSION_FAILED" : code;
        PauseCore(reason, publish: false);
        PublishStatus(IsBrokerFault(resolvedCode) ? "faulted" : "paused", resolvedCode);
        return new(false, resolvedCode);
    }

    private void PauseCore(PauseReason reason, bool publish = true)
    {
        bool wasArmed = IsArmed;
        isArmed = false;
        armedHwnd = nint.Zero;
        if (wasArmed || safety.State != SessionState.Paused)
            safety.PauseAndRelease(reason);
        UpdateReleaseResult();
        if (publish) PublishStatus("paused", null);
    }

    private void UpdateReleaseResult()
    {
        InputAdapterStatus adapterStatus = broker.GetStatus();
        lastReleaseSucceeded = !string.Equals(adapterStatus.Code, "BROKER_RELEASE_ALL_FAILED", StringComparison.Ordinal)
            && (adapterStatus.ActiveKeys?.Count ?? 0) == 0;
    }

    private void PublishStatus(string status, string? errorCode)
    {
        InputSessionStatus next = CreateStatus(status, errorCode);
        if (SameStatus(CurrentStatus, next)) return;
        CurrentStatus = next;
        StatusChanged?.Invoke(this, CurrentStatus);
    }

    private InputSessionStatus CreateStatus(string status, string? errorCode)
    {
        InputAdapterStatus adapterStatus = broker.GetStatus();
        return new InputSessionStatus(
            status,
            "unknown",
            adapterStatus.ActiveKeys?.ToArray() ?? [],
            lastReleaseSucceeded,
            errorCode,
            safety.State,
            safety.PauseReason);
    }

    public void RefreshStatus()
    {
        if (disposed || IsArming) return;
        if (safety.State == SessionState.Paused && IsArmed)
        {
            isArmed = false;
            armedHwnd = nint.Zero;
            UpdateReleaseResult();
            PublishStatus("paused", null);
            return;
        }
        if (!IsArmed) return;

        long nowMonoMs = clock();
        if (nowMonoMs - lastHealthCheckMonoMs >= 500)
        {
            lastHealthCheckMonoMs = nowMonoMs;
            bool heartbeatHealthy;
            try { heartbeatHealthy = broker.Heartbeat(nowMonoMs); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { heartbeatHealthy = false; }
            if (!heartbeatHealthy)
            {
                isArmed = false;
                armedHwnd = nint.Zero;
                safety.PauseAndRelease(PauseReason.InputUnavailable);
                UpdateReleaseResult();
                PublishStatus("faulted", "BROKER_HEARTBEAT_FAILED");
                return;
            }
        }

        InputAdapterStatus adapterStatus = broker.GetStatus();
        if (!adapterStatus.IsHealthy || !adapterStatus.InjectionEnabled)
        {
            isArmed = false;
            armedHwnd = nint.Zero;
            safety.PauseAndRelease(PauseReason.InputUnavailable);
            UpdateReleaseResult();
            PublishStatus("faulted", adapterStatus.Code ?? "INPUT_BROKER_NOT_READY");
            return;
        }
        PublishStatus("ready", null);
    }

    private void CancelResume()
    {
        lock (stateSync) resumeCancellation?.Cancel();
    }

    private static bool SameStatus(InputSessionStatus left, InputSessionStatus right) =>
        left.Status == right.Status
        && left.Integrity == right.Integrity
        && left.LastReleaseSucceeded == right.LastReleaseSucceeded
        && left.ErrorCode == right.ErrorCode
        && left.SessionState == right.SessionState
        && left.PauseReason == right.PauseReason
        && left.ActiveKeys.SequenceEqual(right.ActiveKeys, StringComparer.Ordinal);

    private static bool IsBrokerFault(string code) =>
        code.StartsWith("INPUT_", StringComparison.Ordinal)
        || code.StartsWith("BROKER_", StringComparison.Ordinal)
        || code.StartsWith("HOTKEY_", StringComparison.Ordinal);

    private static string? GetIdentityMismatchCode(WindowIdentity expected, WindowIdentity actual)
    {
        if (!string.Equals(expected.Hwnd, actual.Hwnd, StringComparison.OrdinalIgnoreCase)) return "TARGET_HWND_CHANGED";
        if (expected.Pid != actual.Pid) return "TARGET_PID_CHANGED";
        if (expected.ProcessStartedAtUtc != actual.ProcessStartedAtUtc) return "TARGET_START_TIME_CHANGED";
        if (!string.Equals(expected.ProcessPathSha256, actual.ProcessPathSha256, StringComparison.OrdinalIgnoreCase)) return "TARGET_PATH_HASH_CHANGED";
        if (!string.Equals(expected.ProcessPath, actual.ProcessPath, StringComparison.OrdinalIgnoreCase)) return "TARGET_PATH_CHANGED";
        if (!string.Equals(expected.ClassName, actual.ClassName, StringComparison.Ordinal)) return "TARGET_CLASS_CHANGED";
        return null;
    }

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
