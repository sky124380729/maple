using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Maple.Input;

namespace Maple.InputBroker;

public sealed class BrokerSafetyGate : IBrokerSafetyGate
{
    private readonly IBrokerClock clock;
    private ArmTargetPayload target;

    public BrokerSafetyGate(IBrokerClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
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

    private static BrokerSafetyResult ValidateIdentity(ArmTargetPayload candidate)
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
            using Process process = Process.GetProcessById(candidate.Pid);
            if (process.StartTime.ToUniversalTime().Ticks != candidate.StartedAtUtcTicks)
                return BrokerSafetyResult.Reject("TARGET_IDENTITY_CHANGED");
            string actualPath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(actualPath) ||
                !string.Equals(
                    Path.GetFullPath(actualPath),
                    Path.GetFullPath(candidate.ExecutablePath),
                    StringComparison.OrdinalIgnoreCase))
                return BrokerSafetyResult.Reject("TARGET_IDENTITY_CHANGED");
        }
        catch
        {
            return BrokerSafetyResult.Reject("TARGET_IDENTITY_CHANGED");
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
