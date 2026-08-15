using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Maple.InputBroker;

public static class BrokerClientIdentity
{
    public static int GetClientProcessId(SafePipeHandle pipeHandle)
    {
        if (pipeHandle == null || pipeHandle.IsInvalid)
            throw new ArgumentException("PIPE_HANDLE_INVALID", nameof(pipeHandle));
        if (!GetNamedPipeClientProcessId(pipeHandle, out uint clientPid))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CLIENT_PID_UNAVAILABLE");
        if (clientPid > int.MaxValue) throw new InvalidOperationException("CLIENT_PID_OUT_OF_RANGE");
        return (int)clientPid;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);
}
