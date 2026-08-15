using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Maple.InputProbe;

internal sealed class TargetWindowInfo
{
    public IntPtr Hwnd { get; init; }
    public int ProcessId { get; init; }
    public string Title { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string ProcessPath { get; init; } = "";
    public DateTimeOffset? ProcessStartTime { get; init; }
    public int ClientX { get; init; }
    public int ClientY { get; init; }
    public int ClientWidth { get; init; }
    public int ClientHeight { get; init; }
    public uint Dpi { get; init; }
    public bool IsVisible { get; init; }
    public bool IsMinimized { get; init; }
    public IntPtr ForegroundHwnd { get; init; }
    public int ForegroundProcessId { get; init; }
    public int TargetIntegrity { get; init; }
    public int ProbeIntegrity { get; init; }
}

internal sealed class TargetWindowInspector
{
    internal const string ExpectedTitle = "冒险岛怀旧服";
    internal const string ExpectedClass = "UnityWndClass";

    public IReadOnlyList<TargetWindowInfo> FindTargets()
    {
        var matches = new List<TargetWindowInfo>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            string title = GetWindowText(hwnd);
            string className = GetClassName(hwnd);
            if (title.Contains(ExpectedTitle, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(className, ExpectedClass, StringComparison.Ordinal))
            {
                matches.Add(Inspect(hwnd));
            }

            return true;
        }, IntPtr.Zero);
        return matches;
    }

    public TargetWindowInfo Inspect(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            throw new InvalidOperationException("TARGET_WINDOW_INVALID");
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint rawPid);
        int processId = checked((int)rawPid);
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundPid);

        if (!NativeMethods.GetClientRect(hwnd, out NativeMethods.Rect rect))
        {
            throw new InvalidOperationException("TARGET_CLIENT_RECT_UNAVAILABLE");
        }

        var origin = new NativeMethods.Point();
        if (!NativeMethods.ClientToScreen(hwnd, ref origin))
        {
            throw new InvalidOperationException("TARGET_CLIENT_ORIGIN_UNAVAILABLE");
        }

        string processPath = "";
        DateTimeOffset? processStartTime = null;
        try
        {
            using Process process = Process.GetProcessById(processId);
            processPath = process.MainModule?.FileName ?? "";
            processStartTime = process.StartTime;
        }
        catch
        {
            // Identity remains useful even when process metadata access is denied.
        }

        return new TargetWindowInfo
        {
            Hwnd = hwnd,
            ProcessId = processId,
            Title = GetWindowText(hwnd),
            ClassName = GetClassName(hwnd),
            ProcessPath = processPath,
            ProcessStartTime = processStartTime,
            ClientX = origin.X,
            ClientY = origin.Y,
            ClientWidth = Math.Max(0, rect.Right - rect.Left),
            ClientHeight = Math.Max(0, rect.Bottom - rect.Top),
            Dpi = NativeMethods.GetDpiForWindow(hwnd),
            IsVisible = NativeMethods.IsWindowVisible(hwnd),
            IsMinimized = NativeMethods.IsIconic(hwnd),
            ForegroundHwnd = foreground,
            ForegroundProcessId = checked((int)foregroundPid),
            TargetIntegrity = GetProcessIntegrity(processId),
            ProbeIntegrity = GetProcessIntegrity(Environment.ProcessId)
        };
    }

    public bool IsForeground(IntPtr hwnd)
    {
        return hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd) && NativeMethods.GetForegroundWindow() == hwnd;
    }

    public bool TryBringToForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || NativeMethods.IsIconic(hwnd))
        {
            return false;
        }

        NativeMethods.SetForegroundWindow(hwnd);
        return IsForeground(hwnd);
    }

    private static int GetProcessIntegrity(int processId)
    {
        IntPtr process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return -1;
        }

        try
        {
            if (!NativeMethods.OpenProcessToken(process, NativeMethods.TokenQuery, out IntPtr token))
            {
                return -1;
            }

            try
            {
                NativeMethods.GetTokenInformation(token, NativeMethods.TokenIntegrityLevel, IntPtr.Zero, 0, out uint length);
                if (length == 0)
                {
                    return -1;
                }

                IntPtr buffer = Marshal.AllocHGlobal(checked((int)length));
                try
                {
                    if (!NativeMethods.GetTokenInformation(token, NativeMethods.TokenIntegrityLevel, buffer, length, out _))
                    {
                        return -1;
                    }

                    var label = Marshal.PtrToStructure<NativeMethods.TokenMandatoryLabel>(buffer);
                    byte count = Marshal.ReadByte(NativeMethods.GetSidSubAuthorityCount(label.Label.Sid));
                    return Marshal.ReadInt32(NativeMethods.GetSidSubAuthority(label.Label.Sid, (uint)(count - 1)));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static string GetWindowText(IntPtr hwnd)
    {
        int length = NativeMethods.GetWindowTextLength(hwnd);
        var buffer = new StringBuilder(Math.Max(1, length + 1));
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static class NativeMethods
    {
        internal const uint ProcessQueryLimitedInformation = 0x1000;
        internal const uint TokenQuery = 0x0008;
        internal const int TokenIntegrityLevel = 25;

        internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point { internal int X; internal int Y; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SidAndAttributes { internal IntPtr Sid; internal uint Attributes; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TokenMandatoryLabel { internal SidAndAttributes Label; }

        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll")] internal static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, uint length, out uint returnLength);
        [DllImport("advapi32.dll")] internal static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")] internal static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);
    }
}

