using System.Runtime.InteropServices;

namespace Maple.Host;

public enum GlobalHotKeyId
{
    PauseResume = 1,
    EmergencyStop = 2
}

public sealed record HotKeyRegistrationResult(bool Success, string Code);

public interface IGlobalHotKeyRegistrar
{
    bool Register(nint hwnd, int id, uint modifiers, uint virtualKey);
    bool Unregister(nint hwnd, int id);
}

public sealed class GlobalHotKeyManager : IDisposable
{
    public const int WmHotKey = 0x0312;
    public const uint VirtualKeyF9 = 0x78;
    public const uint VirtualKeyF12 = 0x7B;
    private const uint ModNoRepeat = 0x4000;

    private readonly IGlobalHotKeyRegistrar registrar;
    private readonly Action pauseResume;
    private readonly Action emergencyStop;
    private nint windowHandle;
    private bool registered;

    public GlobalHotKeyManager(
        IGlobalHotKeyRegistrar registrar,
        Action pauseResume,
        Action emergencyStop)
    {
        this.registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
        this.pauseResume = pauseResume ?? throw new ArgumentNullException(nameof(pauseResume));
        this.emergencyStop = emergencyStop ?? throw new ArgumentNullException(nameof(emergencyStop));
    }

    public bool IsRegistered => registered;

    public HotKeyRegistrationResult Register(nint hwnd)
    {
        if (hwnd == nint.Zero) return new(false, "HOTKEY_WINDOW_INVALID");
        if (registered) return new(true, "HOTKEYS_READY");

        if (!registrar.Register(hwnd, (int)GlobalHotKeyId.PauseResume, ModNoRepeat, VirtualKeyF9))
            return new(false, "HOTKEY_REGISTRATION_FAILED");
        if (!registrar.Register(hwnd, (int)GlobalHotKeyId.EmergencyStop, ModNoRepeat, VirtualKeyF12))
        {
            registrar.Unregister(hwnd, (int)GlobalHotKeyId.PauseResume);
            return new(false, "HOTKEY_REGISTRATION_FAILED");
        }

        windowHandle = hwnd;
        registered = true;
        return new(true, "HOTKEYS_READY");
    }

    public bool Dispatch(int message, nint wParam)
    {
        if (!registered || message != WmHotKey) return false;
        switch ((GlobalHotKeyId)wParam.ToInt32())
        {
            case GlobalHotKeyId.PauseResume:
                pauseResume();
                return true;
            case GlobalHotKeyId.EmergencyStop:
                emergencyStop();
                return true;
            default:
                return false;
        }
    }

    public void Dispose()
    {
        if (!registered) return;
        registered = false;
        registrar.Unregister(windowHandle, (int)GlobalHotKeyId.PauseResume);
        registrar.Unregister(windowHandle, (int)GlobalHotKeyId.EmergencyStop);
        windowHandle = nint.Zero;
    }
}

public sealed class WindowsGlobalHotKeyRegistrar : IGlobalHotKeyRegistrar
{
    public bool Register(nint hwnd, int id, uint modifiers, uint virtualKey) =>
        RegisterHotKey(hwnd, id, modifiers, virtualKey);

    public bool Unregister(nint hwnd, int id) => UnregisterHotKey(hwnd, id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}
