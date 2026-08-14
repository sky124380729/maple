using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Maple.Host;

public interface IWindowSystem
{
    IReadOnlyList<WindowCandidate> EnumerateTopLevelWindows();
}

public interface ITargetWindowLocator
{
    TargetWindowDiscoveryResult Locate();
}

public sealed class WindowsTargetWindowLocator : ITargetWindowLocator
{
    public const string TargetTitle = "冒险岛怀旧服";
    public const string TargetClassName = "UnityWndClass";

    private readonly IWindowSystem windowSystem;

    public WindowsTargetWindowLocator(IWindowSystem windowSystem)
    {
        this.windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
    }

    public TargetWindowDiscoveryResult Locate()
    {
        List<WindowIdentity> candidates = windowSystem
            .EnumerateTopLevelWindows()
            .Where(IsEligible)
            .Select(CreateIdentity)
            .OrderBy(candidate => candidate.Pid)
            .ToList();

        return candidates.Count switch
        {
            0 => new(TargetWindowDiscoveryStatus.NotFound, "TARGET_NOT_FOUND", candidates),
            1 => new(TargetWindowDiscoveryStatus.Found, "TARGET_BOUND", candidates),
            _ => new(TargetWindowDiscoveryStatus.SelectionRequired, "TARGET_SELECTION_REQUIRED", candidates),
        };
    }

    private static bool IsEligible(WindowCandidate candidate)
    {
        return candidate.IsVisible
            && string.Equals(candidate.Title, TargetTitle, StringComparison.Ordinal)
            && string.Equals(candidate.ClassName, TargetClassName, StringComparison.Ordinal)
            && (candidate.IsMinimized || (candidate.ClientWidth >= 640 && candidate.ClientHeight >= 360))
            && candidate.Pid > 0
            && candidate.Hwnd != nint.Zero
            && candidate.ProcessStartedAtUtc != default
            && !string.IsNullOrWhiteSpace(candidate.ProcessPath);
    }

    private static WindowIdentity CreateIdentity(WindowCandidate candidate)
    {
        ulong hwnd = unchecked((ulong)candidate.Hwnd.ToInt64());
        string normalizedPath = candidate.ProcessPath.Trim().Replace('\\', '/').ToUpperInvariant();
        string pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath))).ToLowerInvariant();
        return new WindowIdentity(
            $"0x{hwnd:X16}",
            candidate.Pid,
            candidate.Title,
            candidate.ClassName,
            candidate.IsForeground,
            candidate.IsMinimized,
            candidate.ClientLeft,
            candidate.ClientTop,
            candidate.ClientWidth,
            candidate.ClientHeight,
            Math.Max(96, candidate.Dpi),
            candidate.ProcessStartedAtUtc,
            pathHash,
            candidate.ProcessVersion ?? string.Empty);
    }
}

public sealed class Win32WindowSystem : IWindowSystem
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public IReadOnlyList<WindowCandidate> EnumerateTopLevelWindows()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var windows = new List<WindowCandidate>();
        nint foreground = GetForegroundWindow();
        EnumWindows((hwnd, parameter) =>
        {
            uint pidValue;
            GetWindowThreadProcessId(hwnd, out pidValue);
            int pid = unchecked((int)pidValue);
            NativeRect rect = default;
            NativePoint origin = default;
            bool hasClientSize = GetClientRect(hwnd, out rect);
            bool hasClientOrigin = ClientToScreen(hwnd, ref origin);
            string path = TryGetProcessPath(pid);
            DateTimeOffset startedAt = TryGetProcessStart(pid);
            string version = TryGetFileVersion(path);
            windows.Add(new WindowCandidate(
                hwnd,
                pid,
                ReadWindowText(hwnd),
                ReadClassName(hwnd),
                IsWindowVisible(hwnd),
                IsIconic(hwnd),
                hwnd == foreground,
                hasClientOrigin ? origin.X : 0,
                hasClientOrigin ? origin.Y : 0,
                hasClientSize ? rect.Right - rect.Left : 0,
                hasClientSize ? rect.Bottom - rect.Top : 0,
                TryGetDpi(hwnd),
                startedAt,
                path,
                version));
            return true;
        }, nint.Zero);
        return windows;
    }

    private static string ReadWindowText(nint hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0) return string.Empty;
        var value = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, value, value.Capacity);
        return value.ToString();
    }

    private static string ReadClassName(nint hwnd)
    {
        var value = new StringBuilder(256);
        return GetClassName(hwnd, value, value.Capacity) > 0 ? value.ToString() : string.Empty;
    }

    private static int TryGetDpi(nint hwnd)
    {
        try { return unchecked((int)GetDpiForWindow(hwnd)); }
        catch (EntryPointNotFoundException) { return 96; }
    }

    private static DateTimeOffset TryGetProcessStart(int pid)
    {
        try { return new DateTimeOffset(Process.GetProcessById(pid).StartTime.ToUniversalTime()); }
        catch { return default; }
    }

    private static string TryGetProcessPath(int pid)
    {
        if (pid <= 0) return string.Empty;
        nint process = OpenProcess(ProcessQueryLimitedInformation, false, unchecked((uint)pid));
        if (process == nint.Zero) return string.Empty;
        try
        {
            var value = new StringBuilder(32768);
            uint length = unchecked((uint)value.Capacity);
            return QueryFullProcessImageName(process, 0, value, ref length) ? value.ToString() : string.Empty;
        }
        finally { _ = CloseHandle(process); }
    }

    private static string TryGetFileVersion(string path)
    {
        try { return string.IsNullOrWhiteSpace(path) ? string.Empty : FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty; }
        catch { return string.Empty; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    private delegate bool EnumWindowsCallback(nint hwnd, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder value, int maximum);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder value, int maximum);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
