using System;
using System.Collections.Generic;
using Maple.Contracts;
using Maple.Input;
using Xunit;

namespace Maple.Input.Tests;

public sealed class KeybdEventInputAdapterTests
{
    [Fact]
    public void KeyDownAndKeyUpUseVkAndExplicitReleaseFlag()
    {
        var sender = new RecordingSender();
        var adapter = Create(sender);
        var action = Move("right-1", ActionType.MoveRight);

        Assert.Equal(InputStatus.Accepted, adapter.KeyDown(action, "Right", 10).Status);
        Assert.Equal(InputStatus.Completed, adapter.KeyUp(action, "Right", 20).Status);

        Assert.Collection(sender.Events,
            item => Assert.Equal((VirtualKeyMap.Right, 0u, 0u), item),
            item => Assert.Equal((VirtualKeyMap.Right, 0u, KeybdEventInputAdapter.KeyEventFKeyUp), item));
        Assert.Empty(adapter.GetStatus().ActiveKeys);
    }

    [Theory]
    [InlineData("left", VirtualKeyMap.Left)]
    [InlineData("RIGHT", VirtualKeyMap.Right)]
    [InlineData("up", VirtualKeyMap.Up)]
    [InlineData("down", VirtualKeyMap.Down)]
    [InlineData("alt", VirtualKeyMap.Alt)]
    [InlineData("ctrl", VirtualKeyMap.Ctrl)]
    [InlineData("space", VirtualKeyMap.Space)]
    [InlineData("z", VirtualKeyMap.Z)]
    [InlineData("x", VirtualKeyMap.X)]
    [InlineData("c", VirtualKeyMap.C)]
    [InlineData("a", VirtualKeyMap.A)]
    [InlineData("d", VirtualKeyMap.D)]
    [InlineData("j", VirtualKeyMap.J)]
    [InlineData("k", VirtualKeyMap.K)]
    public void MapsSupportedKeysCaseInsensitively(string key, ushort expected)
    {
        Assert.True(VirtualKeyMap.TryGet(key, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UnknownKeyIsRejectedWithoutSending()
    {
        var sender = new RecordingSender();
        var adapter = Create(sender);

        var result = adapter.KeyDown(Move("unknown", ActionType.MoveLeft), "F13", 10);

        Assert.Equal(InputStatus.Rejected, result.Status);
        Assert.Empty(sender.Events);
        Assert.Empty(adapter.GetStatus().ActiveKeys);
    }

    [Fact]
    public void OppositeHorizontalDirectionIsReleasedBeforeNewDirection()
    {
        var sender = new RecordingSender();
        var adapter = Create(sender);

        adapter.KeyDown(Move("left-1", ActionType.MoveLeft), "Left", 10);
        adapter.KeyDown(Move("right-1", ActionType.MoveRight), "Right", 20);

        Assert.Equal(new[]
        {
            (VirtualKeyMap.Left, 0u, 0u),
            (VirtualKeyMap.Left, 0u, KeybdEventInputAdapter.KeyEventFKeyUp),
            (VirtualKeyMap.Right, 0u, 0u)
        }, sender.Events);
        Assert.Equal(new[] { "Right" }, adapter.GetStatus().ActiveKeys);
    }

    [Fact]
    public void DirectionMutualExclusionIsCaseInsensitive()
    {
        var sender = new RecordingSender();
        var adapter = Create(sender);

        adapter.KeyDown(Move("left-1", ActionType.MoveLeft), "left", 10);
        adapter.KeyDown(Move("right-1", ActionType.MoveRight), "RIGHT", 20);

        Assert.Equal(KeybdEventInputAdapter.KeyEventFKeyUp, sender.Events[1].Flags);
        Assert.Equal(new[] { "RIGHT" }, adapter.GetStatus().ActiveKeys);
    }

    [Fact]
    public void RejectedSafetyGateReleasesExistingKeysAndDoesNotPressNewKey()
    {
        var sender = new RecordingSender();
        var gate = new SwitchGate { Allowed = true };
        var adapter = new KeybdEventInputAdapter(sender, gate);
        adapter.KeyDown(Move("left-1", ActionType.MoveLeft), "Left", 10);
        gate.Allowed = false;

        var result = adapter.KeyDown(Move("right-1", ActionType.MoveRight), "Right", 20);

        Assert.Equal(InputStatus.Rejected, result.Status);
        Assert.Equal(2, sender.Events.Count);
        Assert.Equal((VirtualKeyMap.Left, 0u, KeybdEventInputAdapter.KeyEventFKeyUp), sender.Events[1]);
        Assert.Empty(adapter.GetStatus().ActiveKeys);
        Assert.False(adapter.GetStatus().InjectionEnabled);
    }

    [Fact]
    public void ReleaseAllAttemptsEveryKeyAndClearsRegistryAfterFailure()
    {
        var sender = new RecordingSender();
        var adapter = Create(sender);
        adapter.KeyDown(Move("left-1", ActionType.MoveLeft), "Left", 10);
        adapter.KeyDown(Move("up-1", ActionType.ClimbUp), "Up", 20);
        sender.ThrowOnVirtualKey = VirtualKeyMap.Left;

        var result = adapter.ReleaseAll(30);

        Assert.Equal(InputStatus.Failed, result.Status);
        Assert.Empty(adapter.GetStatus().ActiveKeys);
        Assert.Contains(sender.Events, item => item.VirtualKey == VirtualKeyMap.Up && item.Flags == KeybdEventInputAdapter.KeyEventFKeyUp);
    }

    [Fact]
    public void PressProducesPairedEventsWithoutSleeping()
    {
        var sender = new RecordingSender();
        var adapter = Create(sender);
        var action = new AbstractAction { ActionId = "jump-1", Type = ActionType.Jump, HoldMs = 80, MaxDurationMs = 300 };

        var result = adapter.Press(action, "Alt", 100);

        Assert.Equal(InputStatus.Completed, result.Status);
        Assert.Equal(2, sender.Events.Count);
        Assert.Empty(adapter.GetStatus().ActiveKeys);
    }

    private static KeybdEventInputAdapter Create(RecordingSender sender)
        => new(sender, new SwitchGate { Allowed = true });

    private static AbstractAction Move(string id, ActionType type)
        => new() { ActionId = id, Type = type, HoldMs = 100, MaxDurationMs = 300 };

    private sealed class SwitchGate : IInputSafetyGate
    {
        public bool Allowed { get; set; }
        public bool CanSend(string reason) => Allowed;
    }

    private sealed class RecordingSender : IKeyboardEventSender
    {
        public List<(ushort VirtualKey, uint ScanCode, uint Flags)> Events { get; } = new();
        public ushort? ThrowOnVirtualKey { get; set; }

        public void Send(ushort virtualKey, uint scanCode, uint flags)
        {
            if (ThrowOnVirtualKey == virtualKey && flags == KeybdEventInputAdapter.KeyEventFKeyUp)
            {
                ThrowOnVirtualKey = null;
                throw new InvalidOperationException("simulated sender failure");
            }

            Events.Add((virtualKey, scanCode, flags));
        }
    }
}
