using System.Collections.Generic;
using System.Text.Json;
using Maple.Contracts;
using Maple.Input;
using Maple.InputProbe;
using Xunit;

namespace Maple.Input.Tests;

public sealed class ProbeRunnerEvidenceTests
{
    [Theory]
    [InlineData("Left", ActionType.MoveLeft, VirtualKeyMap.Left, VirtualKeyMap.LeftScanCode)]
    [InlineData("Up", ActionType.ClimbUp, VirtualKeyMap.Up, VirtualKeyMap.UpScanCode)]
    [InlineData("Right", ActionType.MoveRight, VirtualKeyMap.Right, VirtualKeyMap.RightScanCode)]
    [InlineData("Down", ActionType.ClimbDown, VirtualKeyMap.Down, VirtualKeyMap.DownScanCode)]
    public void SerializesActualExtendedArrowEvents(
        string key,
        ActionType actionType,
        ushort expectedVirtualKey,
        uint expectedScanCode)
    {
        var sink = new FakeSender();
        var recorder = new ProbeKeyboardEventRecorder(sink);
        var adapter = new KeybdEventInputAdapter(
            recorder,
            new AllowedGate(),
            KeybdEventMode.ExtendedScanCode);
        int marker = recorder.Mark();
        var action = new AbstractAction
        {
            ActionId = "probe-" + key.ToLowerInvariant(),
            Type = actionType,
            HoldMs = 10,
            MaxDurationMs = 100
        };

        adapter.KeyDown(action, key, 10);
        adapter.KeyUp(action, key, 20);

        ProbeActionInputEvidence input = ProbeActionInputEvidence.FromEmittedEvents(
            KeybdEventMode.ExtendedScanCode,
            recorder.GetEventsSince(marker));
        string json = ProbeEvidenceJson.Serialize(new ProbeEvidence
        {
            ActionId = action.ActionId,
            InputMode = input.InputMode,
            Vk = input.VirtualKey,
            ScanCode = input.ScanCode,
            FlagsDown = input.FlagsDown,
            FlagsUp = input.FlagsUp,
            InputAttempted = true,
            AllKeysReleased = adapter.GetStatus().ActiveKeys.Count == 0
        });

        Assert.Equal(new[]
        {
            (expectedVirtualKey, expectedScanCode, KeybdEventInputAdapter.KeyEventFExtendedKey),
            (expectedVirtualKey, expectedScanCode,
                KeybdEventInputAdapter.KeyEventFExtendedKey | KeybdEventInputAdapter.KeyEventFKeyUp)
        }, sink.Events);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("ExtendedScanCode", root.GetProperty("inputMode").GetString());
        Assert.Equal(expectedVirtualKey, root.GetProperty("vk").GetUInt16());
        Assert.Equal(expectedScanCode, root.GetProperty("scanCode").GetUInt32());
        Assert.Equal(1u, root.GetProperty("flagsDown").GetUInt32());
        Assert.Equal(3u, root.GetProperty("flagsUp").GetUInt32());
        Assert.True(root.GetProperty("inputAttempted").GetBoolean());
        Assert.True(root.GetProperty("allKeysReleased").GetBoolean());
    }

    [Fact]
    public void EvidenceUsesRecordedArgumentsInsteadOfExpectedArrowValues()
    {
        var recorder = new ProbeKeyboardEventRecorder(new FakeSender());
        int marker = recorder.Mark();
        recorder.Send(0x7F, 0x5E, 0x11);
        recorder.Send(0x7F, 0x5E, 0x13);

        ProbeActionInputEvidence input = ProbeActionInputEvidence.FromEmittedEvents(
            KeybdEventMode.ExtendedScanCode,
            recorder.GetEventsSince(marker));

        Assert.Equal((ushort)0x7F, input.VirtualKey);
        Assert.Equal(0x5Eu, input.ScanCode);
        Assert.Equal(0x11u, input.FlagsDown);
        Assert.Equal(0x13u, input.FlagsUp);
    }

    private sealed class AllowedGate : IInputSafetyGate
    {
        public bool CanSend(string reason) => true;
    }

    private sealed class FakeSender : IKeyboardEventSender
    {
        public List<(ushort VirtualKey, uint ScanCode, uint Flags)> Events { get; } = new();

        public void Send(ushort virtualKey, uint scanCode, uint flags)
        {
            Events.Add((virtualKey, scanCode, flags));
        }
    }
}
