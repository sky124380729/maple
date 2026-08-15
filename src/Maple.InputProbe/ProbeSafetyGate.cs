using System;
using Maple.Input;

namespace Maple.InputProbe;

internal sealed class ProbeSafetyGate : IInputSafetyGate
{
    private readonly TargetWindowInspector inspector;
    private readonly IntPtr targetHwnd;

    public ProbeSafetyGate(TargetWindowInspector inspector, IntPtr targetHwnd)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        this.targetHwnd = targetHwnd;
    }

    public string LastReason { get; private set; } = "NOT_CHECKED";

    public bool CanSend(string reason)
    {
        try
        {
            TargetWindowInfo target = inspector.Inspect(targetHwnd);
            if (!target.IsVisible) return Reject("TARGET_NOT_VISIBLE");
            if (target.IsMinimized) return Reject("TARGET_MINIMIZED");
            if (target.ClientWidth <= 0 || target.ClientHeight <= 0) return Reject("TARGET_CLIENT_EMPTY");
            if (target.TargetIntegrity < 0 || target.ProbeIntegrity < 0) return Reject("INTEGRITY_UNKNOWN");
            if (target.TargetIntegrity > target.ProbeIntegrity) return Reject("INTEGRITY_MISMATCH");
            if (target.ForegroundHwnd != targetHwnd) return Reject("TARGET_NOT_FOREGROUND");

            LastReason = "ARMED:" + reason;
            return true;
        }
        catch (Exception exception)
        {
            return Reject("TARGET_INSPECTION_FAILED:" + exception.GetType().Name);
        }
    }

    private bool Reject(string reason)
    {
        LastReason = reason;
        return false;
    }
}

