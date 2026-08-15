using Maple.Contracts;
using Maple.Host;
using Maple.Input;
using Xunit;

namespace Maple.Host.Tests;

public sealed class ForegroundSessionControllerTests
{
    [Fact]
    public async Task ResumeCountsDownActivatesThenArmsConfirmedGame()
    {
        WindowIdentity target = Target(isForeground: false);
        var locator = new SequenceLocator(target, target with { IsForeground = true });
        var activation = new RecordingActivation { ForegroundHwnd = ParseHwnd(target.Hwnd) };
        var broker = new RecordingBrokerSession();
        var safety = new HostSafetyCoordinator(broker, () => 500);
        var delay = new RecordingDelay();
        var controller = new ForegroundSessionController(locator, activation, broker, safety, delay);
        var countdown = new List<int>();
        controller.CountdownChanged += (_, value) => countdown.Add(value);

        ForegroundResumeResult result = await controller.ResumeAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal([3, 2, 1], countdown);
        Assert.Equal(ParseHwnd(target.Hwnd), activation.ActivatedHwnd);
        Assert.Equal(1, broker.StartCalls);
        Assert.NotNull(broker.ArmedTarget);
        Assert.Equal(target.Pid, broker.ArmedTarget!.Pid);
        Assert.Equal(target.ProcessPath, broker.ArmedTarget.ExecutablePath);
        Assert.True(controller.IsArmed);
        Assert.Equal(SessionState.Observing, safety.State);
    }

    [Fact]
    public async Task ActivationFailureNeverArmsBroker()
    {
        WindowIdentity target = Target(isForeground: false);
        var locator = new SequenceLocator(target);
        var activation = new RecordingActivation { ActivationResult = false };
        var broker = new RecordingBrokerSession();
        var safety = new HostSafetyCoordinator(broker, () => 600);
        var controller = new ForegroundSessionController(
            locator,
            activation,
            broker,
            safety,
            new RecordingDelay());

        ForegroundResumeResult result = await controller.ResumeAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("TARGET_ACTIVATION_FAILED", result.Code);
        Assert.Null(broker.ArmedTarget);
        Assert.False(controller.IsArmed);
        Assert.Equal(SessionState.Paused, safety.State);
        Assert.Equal(PauseReason.WindowNotForeground, safety.PauseReason);
    }

    [Fact]
    public async Task MinimizedTargetIsRestoredBeforeItIsArmed()
    {
        WindowIdentity minimized = Target(isForeground: false) with { IsMinimized = true };
        WindowIdentity restored = minimized with { IsMinimized = false, IsForeground = true };
        var locator = new SequenceLocator(minimized, restored);
        var activation = new RecordingActivation { ForegroundHwnd = ParseHwnd(minimized.Hwnd) };
        var broker = new RecordingBrokerSession();
        var safety = new HostSafetyCoordinator(broker, () => 650);
        var controller = new ForegroundSessionController(
            locator,
            activation,
            broker,
            safety,
            new RecordingDelay());

        ForegroundResumeResult result = await controller.ResumeAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ParseHwnd(minimized.Hwnd), activation.ActivatedHwnd);
        Assert.NotNull(broker.ArmedTarget);
    }

    [Fact]
    public async Task ForegroundLossReleasesAndPausesOnlyOnce()
    {
        WindowIdentity target = Target(isForeground: true);
        var locator = new SequenceLocator(target, target);
        var activation = new RecordingActivation { ForegroundHwnd = ParseHwnd(target.Hwnd) };
        var broker = new RecordingBrokerSession();
        var safety = new HostSafetyCoordinator(broker, () => 700);
        var controller = new ForegroundSessionController(
            locator,
            activation,
            broker,
            safety,
            new RecordingDelay());
        Assert.True((await controller.ResumeAsync(CancellationToken.None)).Success);

        await controller.OnForegroundChangedAsync((nint)999, CancellationToken.None);
        await controller.OnForegroundChangedAsync((nint)998, CancellationToken.None);

        Assert.Equal(1, broker.ReleaseAllCalls);
        Assert.False(controller.IsArmed);
        Assert.Equal(PauseReason.WindowNotForeground, safety.PauseReason);
    }

    private static WindowIdentity Target(bool isForeground) => new(
        Hwnd: "0x0000000000001234",
        Pid: 321,
        Title: WindowsTargetWindowLocator.TargetTitle,
        ClassName: WindowsTargetWindowLocator.TargetClassName,
        IsForeground: isForeground,
        IsMinimized: false,
        ClientLeft: 10,
        ClientTop: 20,
        ClientWidth: 1280,
        ClientHeight: 720,
        Dpi: 96,
        ProcessStartedAtUtc: new DateTimeOffset(2026, 8, 15, 1, 2, 3, TimeSpan.Zero),
        ProcessPathSha256: "abc",
        ProcessVersion: "1.0",
        ProcessPath: "C:\\Games\\MapleStory\\Maplestory_Classic.exe");

    private static nint ParseHwnd(string value) => (nint)Convert.ToInt64(value[2..], 16);

    private sealed class SequenceLocator(params WindowIdentity[] targets) : ITargetWindowLocator
    {
        private readonly Queue<WindowIdentity> remaining = new(targets);
        private WindowIdentity? last;

        public TargetWindowDiscoveryResult Locate()
        {
            if (remaining.Count > 0) last = remaining.Dequeue();
            return last is null
                ? new(TargetWindowDiscoveryStatus.NotFound, "TARGET_NOT_FOUND", [])
                : new(TargetWindowDiscoveryStatus.Found, "TARGET_BOUND", [last]);
        }
    }

    private sealed class RecordingActivation : IForegroundWindowController
    {
        public bool ActivationResult { get; init; } = true;
        public nint ForegroundHwnd { get; init; }
        public nint ActivatedHwnd { get; private set; }

        public bool TryActivate(nint hwnd)
        {
            ActivatedHwnd = hwnd;
            return ActivationResult;
        }

        public nint GetForegroundWindow() => ForegroundHwnd;
    }

    private sealed class RecordingDelay : IForegroundSessionDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBrokerSession : IInputBrokerSession, IInputAdapter
    {
        public int StartCalls { get; private set; }
        public int ReleaseAllCalls { get; private set; }
        public ArmTargetPayload? ArmedTarget { get; private set; }

        public Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.CompletedTask;
        }

        public void ArmTarget(ArmTargetPayload target) => ArmedTarget = target;

        public InputAdapterStatus GetStatus() => new()
        {
            AdapterName = "test-broker",
            Code = "INPUT_BROKER_READY",
            IsHealthy = true,
            InjectionEnabled = ArmedTarget is not null,
            ActiveKeys = []
        };

        public InputResult ReleaseAll(long nowMonoMs)
        {
            ReleaseAllCalls++;
            return Result("release-all", nowMonoMs);
        }

        public InputResult KeyDown(AbstractAction action, string key, long nowMonoMs) => Result(action.ActionId, nowMonoMs);
        public InputResult KeyUp(AbstractAction action, string key, long nowMonoMs) => Result(action.ActionId, nowMonoMs);
        public InputResult Press(AbstractAction action, string key, long nowMonoMs) => Result(action.ActionId, nowMonoMs);
        public bool Heartbeat(long nowMonoMs) => true;

        private static InputResult Result(string actionId, long nowMonoMs) => new()
        {
            SchemaVersion = ContractConstants.SchemaVersion,
            ActionId = actionId,
            Status = InputStatus.Completed,
            StartedAtMonoMs = nowMonoMs,
            EndedAtMonoMs = nowMonoMs,
            ReleasedKeys = []
        };
    }
}
