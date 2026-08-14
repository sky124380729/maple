using Maple.Contracts;
using Maple.Input;
using Xunit;

namespace Maple.Host.Tests;

public sealed class HostSafetyCoordinatorTests
{
    [Fact]
    public void UnsafeCommandOrContentResetPausesAndReleasesAll()
    {
        var input = new RecordingInputAdapter();
        var safety = new HostSafetyCoordinator(input, () => 120);

        safety.PauseAndRelease();

        Assert.Equal(SessionState.Paused, safety.State);
        Assert.Equal(PauseReason.SafetyViolation, safety.PauseReason);
        Assert.Equal([120], input.ReleaseTimes);
    }

    [Fact]
    public void RuntimeFailurePausesAndReleasesAll()
    {
        var input = new RecordingInputAdapter();
        var safety = new HostSafetyCoordinator(input, () => 240);

        safety.PauseAndRelease();

        Assert.Equal(SessionState.Paused, safety.State);
        Assert.Equal([240], input.ReleaseTimes);
    }

    [Fact]
    public void EmergencyStopTransitionsStateAndReleasesAll()
    {
        var input = new RecordingInputAdapter();
        var safety = new HostSafetyCoordinator(input, () => 360);

        safety.EmergencyStop();

        Assert.Equal(SessionState.EmergencyStop, safety.State);
        Assert.Equal([360], input.ReleaseTimes);
    }

    [Fact]
    public void WindowShutdownReleasesOnlyOnce()
    {
        var input = new RecordingInputAdapter();
        var safety = new HostSafetyCoordinator(input, () => 480);

        safety.ReleaseForShutdown();
        safety.ReleaseForShutdown();

        Assert.Equal([480], input.ReleaseTimes);
    }

    private sealed class RecordingInputAdapter : IInputAdapter
    {
        public List<long> ReleaseTimes { get; } = [];

        public InputResult KeyDown(AbstractAction action, string key, long nowMonoMs) => Result(action.ActionId, nowMonoMs);
        public InputResult KeyUp(AbstractAction action, string key, long nowMonoMs) => Result(action.ActionId, nowMonoMs);
        public InputResult Press(AbstractAction action, string key, long nowMonoMs) => Result(action.ActionId, nowMonoMs);
        public bool Heartbeat(long nowMonoMs) => true;
        public InputAdapterStatus GetStatus() => new();

        public InputResult ReleaseAll(long nowMonoMs)
        {
            ReleaseTimes.Add(nowMonoMs);
            return Result("release-all", nowMonoMs);
        }

        private static InputResult Result(string actionId, long nowMonoMs) => new()
        {
            SchemaVersion = ContractConstants.SchemaVersion,
            ActionId = actionId,
            Status = InputStatus.Completed,
            StartedAtMonoMs = nowMonoMs,
            EndedAtMonoMs = nowMonoMs,
            ReleasedKeys = [],
        };
    }
}
