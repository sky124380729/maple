using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Maple.InputBroker;

public sealed record BrokerProcessIdentity(long StartedAtUtcTicks, string ExecutablePath);

public interface IBrokerProcessIdentityReader
{
    BrokerProcessIdentity Read(int processId);
}

public sealed class WindowsBrokerProcessIdentityReader : IBrokerProcessIdentityReader
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public BrokerProcessIdentity Read(int processId)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        nint process = OpenProcess(ProcessQueryLimitedInformation, false, unchecked((uint)processId));
        if (process == nint.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!GetProcessTimes(process, out FileTime created, out _, out _, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var path = new StringBuilder(32768);
            uint pathLength = unchecked((uint)path.Capacity);
            if (!QueryFullProcessImageName(process, 0, path, ref pathLength))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            long fileTime = unchecked(((long)created.High << 32) | created.Low);
            long startedAtUtcTicks = DateTime.FromFileTimeUtc(fileTime).Ticks;
            return new BrokerProcessIdentity(startedAtUtcTicks, Path.GetFullPath(path.ToString()));
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime
    {
        public readonly uint Low;
        public readonly uint High;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(nint process, out FileTime creation, out FileTime exit, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
