using System.Runtime.InteropServices;
using Maple.Input;

namespace Maple.InputBroker;

public sealed class WindowsKeybdEventSender : IBrokerKeySender
{
    public const uint KeyEventFExtendedKey = BrokerKeyFlags.ExtendedKey;
    public const uint KeyEventFKeyUp = BrokerKeyFlags.KeyUp;

    public void Send(BrokerKeyEncoding encoding, bool isKeyUp)
    {
        uint flags = encoding.Extended ? BrokerKeyFlags.ExtendedKey : 0;
        if (isKeyUp) flags |= BrokerKeyFlags.KeyUp;
        NativeKeybdEvent(checked((byte)encoding.VirtualKey), checked((byte)encoding.ScanCode), flags, 0);
    }

    [DllImport("user32.dll", EntryPoint = "keybd_event", SetLastError = true)]
    private static extern void NativeKeybdEvent(
        byte virtualKey,
        byte scanCode,
        uint flags,
        nuint extraInfo);
}
