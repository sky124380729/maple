using System.Runtime.InteropServices;

namespace Maple.Host;

public static class WindowsProcessIntegrity
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    public static int ReadRid(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0) return -1;
        nint process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == nint.Zero) return -1;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out nint token)) return -1;
            try
            {
                _ = GetTokenInformation(token, TokenIntegrityLevel, nint.Zero, 0, out uint length);
                if (length == 0) return -1;
                nint buffer = Marshal.AllocHGlobal(checked((int)length));
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out _)) return -1;
                    TokenMandatoryLabel label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
                    nint countPointer = GetSidSubAuthorityCount(label.Label.Sid);
                    if (countPointer == nint.Zero) return -1;
                    byte count = Marshal.ReadByte(countPointer);
                    if (count == 0) return -1;
                    nint ridPointer = GetSidSubAuthority(label.Label.Sid, (uint)(count - 1));
                    return ridPointer == nint.Zero ? -1 : Marshal.ReadInt32(ridPointer);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { _ = CloseHandle(token); }
        }
        finally { _ = CloseHandle(process); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes { public nint Sid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel { public SidAndAttributes Label; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint token, int informationClass, nint information, uint length, out uint returnLength);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthority(nint sid, uint subAuthority);
}
