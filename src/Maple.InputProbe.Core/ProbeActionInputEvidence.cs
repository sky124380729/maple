using System;
using System.Collections.Generic;
using Maple.Input;

namespace Maple.InputProbe;

public sealed class ProbeActionInputEvidence
{
    public string InputMode { get; init; } = "";
    public ushort VirtualKey { get; init; }
    public uint ScanCode { get; init; }
    public uint FlagsDown { get; init; }
    public uint FlagsUp { get; init; }

    public static ProbeActionInputEvidence FromEmittedEvents(
        KeybdEventMode inputMode,
        IReadOnlyList<ProbeKeyboardEvent> events)
    {
        if (events == null) throw new ArgumentNullException(nameof(events));

        ProbeKeyboardEvent? down = null;
        ProbeKeyboardEvent? up = null;
        foreach (ProbeKeyboardEvent keyboardEvent in events)
        {
            if (down == null && (keyboardEvent.Flags & KeybdEventInputAdapter.KeyEventFKeyUp) == 0)
            {
                down = keyboardEvent;
                continue;
            }

            if (up == null &&
                (keyboardEvent.Flags & KeybdEventInputAdapter.KeyEventFKeyUp) != 0 &&
                (down == null || keyboardEvent.VirtualKey == down.Value.VirtualKey))
            {
                up = keyboardEvent;
            }
        }

        ProbeKeyboardEvent? identity = down ?? up;
        return new ProbeActionInputEvidence
        {
            InputMode = inputMode.ToString(),
            VirtualKey = identity?.VirtualKey ?? 0,
            ScanCode = identity?.ScanCode ?? 0,
            FlagsDown = down?.Flags ?? 0,
            FlagsUp = up?.Flags ?? 0
        };
    }
}
