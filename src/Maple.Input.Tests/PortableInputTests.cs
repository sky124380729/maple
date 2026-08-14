using Maple.Contracts;
using Maple.Input;
using Xunit;

namespace Maple.Input.Tests;

public sealed class PortableInputTests
{
    [Fact]
    public void ActiveKeyRegistryIsIdempotentAndReleaseAllClearsEveryKey()
    {
        var registry = new ActiveKeyRegistry();
        Assert.True(registry.KeyDown("Right"));
        Assert.False(registry.KeyDown("Right"));
        registry.KeyDown("Alt");
        Assert.Equal(["Alt", "Right"], registry.ActiveKeys);
        Assert.Equal(["Alt", "Right"], registry.ReleaseAll());
        Assert.Empty(registry.ActiveKeys);
    }

    [Fact]
    public void NullAdapterNeverClaimsInputInjection()
    {
        var adapter = new NullInputAdapter();
        var action = new AbstractAction { ActionId = "null-1", Type = ActionType.MoveRight, HoldMs = 100, MaxDurationMs = 300 };

        Assert.Equal(InputStatus.Rejected, adapter.KeyDown(action, "Right", 10).Status);
        Assert.False(adapter.GetStatus().InjectionEnabled);
        Assert.Equal("INPUT_INJECTION=DISABLED", adapter.GetStatus().Code);
        Assert.Empty(adapter.GetStatus().ActiveKeys);
    }

    [Fact]
    public void ReplayAdapterRecordsDownUpAndReleasesOrphanedKeys()
    {
        var adapter = new ReplayInputAdapter();
        var action = new AbstractAction { ActionId = "replay-1", Type = ActionType.MoveLeft, HoldMs = 100, MaxDurationMs = 300 };

        Assert.Equal(InputStatus.Accepted, adapter.KeyDown(action, "Left", 10).Status);
        Assert.Equal(InputStatus.Completed, adapter.ReleaseAll(20).Status);
        Assert.Equal(2, adapter.Events.Count);
        Assert.Equal("KeyUp", adapter.Events[1].Phase);
    }
}
