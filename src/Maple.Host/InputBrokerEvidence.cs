using System.Text.Json;

namespace Maple.Host;

public sealed record InputBrokerEvidenceRecord(
    string ActionId,
    DateTimeOffset ObservedAtUtc,
    long TargetHwnd,
    int TargetPid,
    bool ForegroundConfirmed,
    int HostIntegrity,
    int BrokerIntegrity,
    int TargetIntegrity,
    int Vk,
    int ScanCode,
    int FlagsDown,
    int FlagsUp,
    string ScreenshotBefore,
    string ScreenshotAfter,
    string Classification,
    bool AllKeysReleased);

public sealed class InputBrokerEvidenceWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly object sync = new();
    private readonly string rootPrefix;

    public InputBrokerEvidenceWriter(string evidenceRoot)
    {
        if (string.IsNullOrWhiteSpace(evidenceRoot))
            throw new ArgumentException("Evidence root is required", nameof(evidenceRoot));
        Root = Path.GetFullPath(evidenceRoot);
        Directory.CreateDirectory(Root);
        rootPrefix = Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        JsonlPath = Path.Combine(Root, "broker-evidence.jsonl");
        if (File.Exists(JsonlPath) && new FileInfo(JsonlPath).Length > 0)
            throw new InvalidDataException("Evidence session already contains records");
    }

    public string Root { get; }
    public string JsonlPath { get; }

    public void Append(InputBrokerEvidenceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRelativePath(record.ScreenshotBefore);
        ValidateRelativePath(record.ScreenshotAfter);
        string line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        lock (sync) File.AppendAllText(JsonlPath, line);
    }

    private void ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Evidence screenshot path must be relative");
        string resolved = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!resolved.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Evidence screenshot path escapes its session");
    }
}
