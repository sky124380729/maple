using System;
using System.Runtime.InteropServices;
using Maple.Input;

namespace Maple.InputProbe;

internal sealed class WindowsKeybdEventSender : IKeyboardEventSender
{
    public void Send(ushort virtualKey, uint scanCode, uint flags)
    {
        NativeMethods.keybd_event((byte)virtualKey, (byte)scanCode, flags, UIntPtr.Zero);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    }
}

