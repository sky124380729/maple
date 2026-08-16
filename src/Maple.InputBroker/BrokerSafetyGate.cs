using System;
using System.IO;
using System.Runtime.InteropServices;
using Maple.Input;

namespace Maple.InputBroker;

public sealed class BrokerSafetyGate : IBrokerSafetyGate
{
    private readonly IBrokerClock clock;
    private readonly IBrokerProcessIdentityReader identityReader;
    private ArmTargetPayload target;

    public BrokerSafetyGate(IBrokerClock clock, IBrokerProcessIdentityReader identityReader = null)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.identityReader = identityReader ?? new WindowsBrokerProcessIdentityReader();
    }

    public BrokerSafetyResult Arm(ArmTargetPayload requestedTarget)
    {
        BrokerSafetyResult identity = ValidateIdentity(requestedTarget);
        if (!identity.Allowed) return identity;
        if (NativeMethods.GetForegroundWindow().ToInt64() != requestedTarget.Hwnd)
            return BrokerSafetyResult.Reject("TARGET_NOT_FOREGROUND");
        if (NativeMethods.IsIconic(new IntPtr(requestedTarget.Hwnd)))
            return BrokerSafetyResult.Reject("TARGET_MINIMIZED");
        target = requestedTarget;
        return BrokerSafetyResult.Allow();
    }

    public BrokerSafetyResult Evaluate(BrokerActionPayload action)
    {
        if (target == null) return BrokerSafetyResult.Reject("TARGET_NOT_ARMED");
        BrokerSafetyResult identity = ValidateIdentity(target);
        if (!identity.Allowed) return identity;
        if (NativeMethods.GetForegroundWindow().ToInt64() != target.Hwnd)
            return BrokerSafetyResult.Reject("TARGET_NOT_FOREGROUND");
        if (NativeMethods.IsIconic(new IntPtr(target.Hwnd)))
            return BrokerSafetyResult.Reject("TARGET_MINIMIZED");
        if (action.FrameFreshUntilMonoMs < clock.NowMonoMs)
            return BrokerSafetyResult.Reject("FRAME_STALE");
        return BrokerSafetyResult.Allow();
    }

    private BrokerSafetyResult ValidateIdentity(ArmTargetPayload candidate)
    {
        if (candidate == null || candidate.Hwnd == 0 || candidate.Pid <= 0 ||
            candidate.StartedAtUtcTicks <= 0 || string.IsNullOrWhiteSpace(candidate.ExecutablePath))
            return BrokerSafetyResult.Reject("TARGET_IDENTITY_INVALID");

        var hwnd = new IntPtr(candidate.Hwnd);
        if (!NativeMethods.IsWindow(hwnd)) return BrokerSafetyResult.Reject("TARGET_IDENTITY_CHANGED");
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint windowPid);
        if (windowPid != candidate.Pid) return BrokerSafetyResult.Reject("TARGET_IDENTITY_CHANGED");

        try
        {
            BrokerProcessIdentity actual = identityReader.Read(candidate.Pid);
            if (actual.StartedAtUtcTicks != candidate.StartedAtUtcTicks)
                return BrokerSafetyResult.Reject("TARGET_START_TIME_CHANGED");
            if (string.IsNullOrWhiteSpace(actual.ExecutablePath) ||
                !string.Equals(
                    Path.GetFullPath(actual.ExecutablePath),
                    Path.GetFullPath(candidate.ExecutablePath),
                    StringComparison.OrdinalIgnoreCase))
                return BrokerSafetyResult.Reject("TARGET_PATH_CHANGED");
        }
        catch
        {
            return BrokerSafetyResult.Reject("TARGET_PROCESS_QUERY_FAILED");
        }

        return BrokerSafetyResult.Allow();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    }
}
