namespace Maple.InputProbe;

public sealed class ProbeEvidence
{
    public string SessionId { get; set; } = "";
    public string ActionId { get; set; } = "";
    public long TargetHwnd { get; set; }
    public int TargetPid { get; set; }
    public string TargetClass { get; set; } = "";
    public string TargetTitle { get; set; } = "";
    public int ClientWidth { get; set; }
    public int ClientHeight { get; set; }
    public uint Dpi { get; set; }
    public int TargetIntegrity { get; set; }
    public int ProbeIntegrity { get; set; }
    public long ForegroundBefore { get; set; }
    public long ForegroundAfter { get; set; }
    public bool ForegroundConfirmed { get; set; }
    public bool IsMinimized { get; set; }
    public int HoldMs { get; set; }
    public string InputMode { get; set; } = "";
    public ushort Vk { get; set; }
    public uint ScanCode { get; set; }
    public uint FlagsDown { get; set; }
    public uint FlagsUp { get; set; }
    public bool InputAttempted { get; set; }
    public string ScreenshotBefore { get; set; } = "";
    public string ScreenshotAfter { get; set; } = "";
    public string Classification { get; set; } = "UNKNOWN";
    public string Reason { get; set; } = "";
    public bool AllKeysReleased { get; set; }
}
