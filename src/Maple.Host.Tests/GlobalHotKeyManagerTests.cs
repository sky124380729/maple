using Maple.Host;
using Xunit;

namespace Maple.Host.Tests;

public sealed class GlobalHotKeyManagerTests
{
    [Fact]
    public void RegistersF9AndF12AndDispatchesWithoutWebView()
    {
        var registrar = new RecordingRegistrar();
        int pauseResumeCalls = 0;
        int emergencyStopCalls = 0;
        using var hotkeys = new GlobalHotKeyManager(
            registrar,
            () => pauseResumeCalls++,
            () => emergencyStopCalls++);

        HotKeyRegistrationResult result = hotkeys.Register((nint)42);
        bool pauseHandled = hotkeys.Dispatch(GlobalHotKeyManager.WmHotKey, (nint)GlobalHotKeyId.PauseResume);
        bool stopHandled = hotkeys.Dispatch(GlobalHotKeyManager.WmHotKey, (nint)GlobalHotKeyId.EmergencyStop);

        Assert.True(result.Success);
        Assert.Equal(
            [(42, (int)GlobalHotKeyId.PauseResume, GlobalHotKeyManager.VirtualKeyF9),
             (42, (int)GlobalHotKeyId.EmergencyStop, GlobalHotKeyManager.VirtualKeyF12)],
            registrar.Registered);
        Assert.True(pauseHandled);
        Assert.True(stopHandled);
        Assert.Equal(1, pauseResumeCalls);
        Assert.Equal(1, emergencyStopCalls);
    }

    [Fact]
    public void RegistrationFailureRollsBackAndPreventsDispatch()
    {
        var registrar = new RecordingRegistrar { FailAllEmergencyStopRegistrations = true };
        int calls = 0;
        using var hotkeys = new GlobalHotKeyManager(registrar, () => calls++, () => calls++);

        HotKeyRegistrationResult result = hotkeys.Register((nint)77);

        Assert.False(result.Success);
        Assert.Equal("HOTKEY_REGISTRATION_FAILED", result.Code);
        Assert.Contains((77, (int)GlobalHotKeyId.PauseResume), registrar.Unregistered);
        Assert.False(hotkeys.Dispatch(GlobalHotKeyManager.WmHotKey, (nint)GlobalHotKeyId.PauseResume));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void FallsBackToModifiedHotKeysWhenBareF12IsAlreadyRegistered()
    {
        var registrar = new RecordingRegistrar { FailBareEmergencyStop = true };
        using var hotkeys = new GlobalHotKeyManager(registrar, () => { }, () => { });

        HotKeyRegistrationResult result = hotkeys.Register((nint)88);

        Assert.True(result.Success);
        Assert.Equal("HOTKEYS_FALLBACK_READY", result.Code);
        Assert.Equal("Ctrl+Shift+F9", result.PauseResume);
        Assert.Equal("Ctrl+Shift+F12", result.EmergencyStop);
        Assert.Contains((88, (int)GlobalHotKeyId.PauseResume), registrar.Unregistered);
        Assert.Contains(registrar.Attempts, item => item.Id == (int)GlobalHotKeyId.PauseResume && item.Modifiers == 0x4006);
        Assert.Contains(registrar.Attempts, item => item.Id == (int)GlobalHotKeyId.EmergencyStop && item.Modifiers == 0x4006);
    }

    private sealed class RecordingRegistrar : IGlobalHotKeyRegistrar
    {
        public bool FailBareEmergencyStop { get; init; }
        public bool FailAllEmergencyStopRegistrations { get; init; }
        public List<(long Hwnd, int Id, uint VirtualKey)> Registered { get; } = [];
        public List<(long Hwnd, int Id, uint Modifiers, uint VirtualKey)> Attempts { get; } = [];
        public List<(long Hwnd, int Id)> Unregistered { get; } = [];

        public bool Register(nint hwnd, int id, uint modifiers, uint virtualKey)
        {
            Attempts.Add((hwnd.ToInt64(), id, modifiers, virtualKey));
            if (id == (int)GlobalHotKeyId.EmergencyStop
                && (FailAllEmergencyStopRegistrations || (FailBareEmergencyStop && modifiers == 0x4000))) return false;
            Registered.Add((hwnd.ToInt64(), id, virtualKey));
            return true;
        }

        public bool Unregister(nint hwnd, int id)
        {
            Unregistered.Add((hwnd.ToInt64(), id));
            return true;
        }
    }
}
