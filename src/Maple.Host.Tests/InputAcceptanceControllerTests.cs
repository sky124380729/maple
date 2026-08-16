using Maple.Contracts;
using Maple.Runtime;
using Xunit;

namespace Maple.Host.Tests;

public sealed class InputAcceptanceControllerTests
{
    [Fact]
    public async Task SuccessfulTestRunsOneBoundedAbstractActionAndAlwaysReleases()
    {
        var session = new RecordingSession(new ForegroundResumeResult(true, "INPUT_SESSION_READY"));
        var executor = new RecordingExecutor();
        var controller = new InputAcceptanceController(session, executor, new ImmediateDelay(), () => 1000);

        InputResult result = await controller.RunAsync(InputAcceptanceKind.Jump, 90, CancellationToken.None);

        Assert.Equal(InputStatus.Completed, result.Status);
        Assert.Equal(new[] { "down:Jump", "up:Jump", "release" }, executor.Trace);
        Assert.Equal(PauseReason.OperatorRequested, session.Pauses.Single());
    }

    [Fact]
    public async Task FailedForegroundArmingSendsNoActionAndStillReleases()
    {
        var session = new RecordingSession(new ForegroundResumeResult(false, "TARGET_FOREGROUND_TIMEOUT"));
        var executor = new RecordingExecutor();
        var controller = new InputAcceptanceController(session, executor, new ImmediateDelay(), () => 1000);

        InputResult result = await controller.RunAsync(InputAcceptanceKind.MoveLeft, 120, CancellationToken.None);

        Assert.Equal(InputStatus.Failed, result.Status);
        Assert.Equal("TARGET_FOREGROUND_TIMEOUT", result.Message);
        Assert.Equal(new[] { "release" }, executor.Trace);
    }

    [Theory]
    [InlineData(InputAcceptanceKind.Attack, ActionType.Attack, ActionProfileId.SingleAttack)]
    [InlineData(InputAcceptanceKind.HpPotion, ActionType.UsePotion, ActionProfileId.HpPotion)]
    [InlineData(InputAcceptanceKind.MpPotion, ActionType.UsePotion, ActionProfileId.MpPotion)]
    public async Task ProfileActionsRemainTyped(
        InputAcceptanceKind kind,
        ActionType expectedType,
        ActionProfileId expectedProfile)
    {
        var executor = new RecordingExecutor();
        var controller = new InputAcceptanceController(
            new RecordingSession(new ForegroundResumeResult(true, "INPUT_SESSION_READY")),
            executor,
            new ImmediateDelay(),
            () => 1000);

        await controller.RunAsync(kind, 80, CancellationToken.None);

        Assert.Equal(expectedType, executor.Actions.Single().Type);
        Assert.Equal(expectedProfile, executor.Actions.Single().ProfileId);
    }

    private sealed class RecordingSession(ForegroundResumeResult resume) : IAutomaticCombatInputSession
    {
        public bool IsArmed { get; private set; }
        public List<PauseReason> Pauses { get; } = [];
        public Task<ForegroundResumeResult> ResumeAsync(CancellationToken cancellationToken)
        {
            IsArmed = resume.Success;
            return Task.FromResult(resume);
        }
        public void Pause(PauseReason reason) { IsArmed = false; Pauses.Add(reason); }
        public void EmergencyStop() { IsArmed = false; }
    }

    private sealed class ImmediateDelay : IInputAcceptanceDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingExecutor : IActionExecutor
    {
        public List<string> Trace { get; } = [];
        public List<AbstractAction> Actions { get; } = [];
        public ValueTask KeyDownAsync(AbstractAction action, CancellationToken cancellationToken)
        {
            Trace.Add("down:" + action.Type);
            Actions.Add(action);
            return ValueTask.CompletedTask;
        }
        public ValueTask KeyUpAsync(AbstractAction action, CancellationToken cancellationToken)
        {
            Trace.Add("up:" + action.Type);
            return ValueTask.CompletedTask;
        }
        public ValueTask ReleaseAllAsync(CancellationToken cancellationToken)
        {
            Trace.Add("release");
            return ValueTask.CompletedTask;
        }
    }
}
