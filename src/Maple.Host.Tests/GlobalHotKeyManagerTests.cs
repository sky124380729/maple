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
        var registrar = new RecordingRegistrar { FailId = (int)GlobalHotKeyId.EmergencyStop };
        int calls = 0;
        using var hotkeys = new GlobalHotKeyManager(registrar, () => calls++, () => calls++);

        HotKeyRegistrationResult result = hotkeys.Register((nint)77);

        Assert.False(result.Success);
        Assert.Equal("HOTKEY_REGISTRATION_FAILED", result.Code);
        Assert.Contains((77, (int)GlobalHotKeyId.PauseResume), registrar.Unregistered);
        Assert.False(hotkeys.Dispatch(GlobalHotKeyManager.WmHotKey, (nint)GlobalHotKeyId.PauseResume));
        Assert.Equal(0, calls);
    }

    private sealed class RecordingRegistrar : IGlobalHotKeyRegistrar
    {
        public int? FailId { get; init; }
        public List<(long Hwnd, int Id, uint VirtualKey)> Registered { get; } = [];
        public List<(long Hwnd, int Id)> Unregistered { get; } = [];

        public bool Register(nint hwnd, int id, uint modifiers, uint virtualKey)
        {
            if (id == FailId) return false;
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
