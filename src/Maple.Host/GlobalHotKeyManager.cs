using System.Runtime.InteropServices;

namespace Maple.Host;

public enum GlobalHotKeyId
{
    PauseResume = 1,
    EmergencyStop = 2
}

public sealed record HotKeyRegistrationResult(
    bool Success,
    string Code,
    string PauseResume = "F9",
    string EmergencyStop = "F12");

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
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

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
    public string PauseResumeLabel { get; private set; } = "F9";
    public string EmergencyStopLabel { get; private set; } = "F12";

    public HotKeyRegistrationResult Register(nint hwnd)
    {
        if (hwnd == nint.Zero) return new(false, "HOTKEY_WINDOW_INVALID");
        if (registered) return new(true, "HOTKEYS_READY", PauseResumeLabel, EmergencyStopLabel);

        if (TryRegisterPair(hwnd, ModNoRepeat))
        {
            CompleteRegistration(hwnd, "F9", "F12");
            return new(true, "HOTKEYS_READY", PauseResumeLabel, EmergencyStopLabel);
        }

        uint fallbackModifiers = ModNoRepeat | ModControl | ModShift;
        if (TryRegisterPair(hwnd, fallbackModifiers))
        {
            CompleteRegistration(hwnd, "Ctrl+Shift+F9", "Ctrl+Shift+F12");
            return new(true, "HOTKEYS_FALLBACK_READY", PauseResumeLabel, EmergencyStopLabel);
        }

        return new(false, "HOTKEY_REGISTRATION_FAILED");
    }

    private bool TryRegisterPair(nint hwnd, uint modifiers)
    {
        if (!registrar.Register(hwnd, (int)GlobalHotKeyId.PauseResume, modifiers, VirtualKeyF9)) return false;
        if (registrar.Register(hwnd, (int)GlobalHotKeyId.EmergencyStop, modifiers, VirtualKeyF12)) return true;
        registrar.Unregister(hwnd, (int)GlobalHotKeyId.PauseResume);
        return false;
    }

    private void CompleteRegistration(nint hwnd, string pauseResumeLabel, string emergencyStopLabel)
    {
        windowHandle = hwnd;
        PauseResumeLabel = pauseResumeLabel;
        EmergencyStopLabel = emergencyStopLabel;
        registered = true;
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
