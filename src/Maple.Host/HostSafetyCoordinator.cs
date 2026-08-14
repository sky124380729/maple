using Maple.Contracts;
using Maple.Core;
using Maple.Input;

namespace Maple.Host;

public sealed class HostSafetyCoordinator
{
    private readonly object sync = new();
    private readonly IInputAdapter inputAdapter;
    private readonly Func<long> clock;
    private readonly SessionStateMachine session = new(SessionState.Stopped);
    private bool shutdownReleased;

    public HostSafetyCoordinator(IInputAdapter inputAdapter, Func<long>? clock = null)
    {
        this.inputAdapter = inputAdapter ?? throw new ArgumentNullException(nameof(inputAdapter));
        this.clock = clock ?? (() => Environment.TickCount64);
    }

    public SessionState State
    {
        get { lock (sync) return session.State; }
    }

    public PauseReason PauseReason
    {
        get { lock (sync) return session.PauseReason; }
    }

    public void PauseAndRelease()
    {
        lock (sync)
        {
            session.Pause(PauseReason.SafetyViolation);
            inputAdapter.ReleaseAll(clock());
        }
    }

    public void EmergencyStop()
    {
        lock (sync)
        {
            session.EmergencyStop();
            inputAdapter.ReleaseAll(clock());
        }
    }

    public void ReleaseForShutdown()
    {
        lock (sync)
        {
            if (shutdownReleased) return;
            shutdownReleased = true;
            inputAdapter.ReleaseAll(clock());
        }
    }
}
