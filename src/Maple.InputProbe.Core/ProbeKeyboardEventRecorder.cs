using System;
using System.Collections.Generic;
using Maple.Input;

namespace Maple.InputProbe;

public readonly record struct ProbeKeyboardEvent(ushort VirtualKey, uint ScanCode, uint Flags);

public sealed class ProbeKeyboardEventRecorder : IKeyboardEventSender
{
    private readonly IKeyboardEventSender inner;
    private readonly object sync = new();
    private readonly List<ProbeKeyboardEvent> events = new();

    public ProbeKeyboardEventRecorder(IKeyboardEventSender inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public int Mark()
    {
        lock (sync) return events.Count;
    }

    public IReadOnlyList<ProbeKeyboardEvent> GetEventsSince(int marker)
    {
        lock (sync)
        {
            if (marker < 0 || marker > events.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(marker));
            }

            return events.GetRange(marker, events.Count - marker);
        }
    }

    public void Send(ushort virtualKey, uint scanCode, uint flags)
    {
        inner.Send(virtualKey, scanCode, flags);
        lock (sync) events.Add(new ProbeKeyboardEvent(virtualKey, scanCode, flags));
    }
}
