using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maple.Host;

public sealed record WindowsRuntimeDiagnosticReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string OsDescription,
    string OsArchitecture,
    WebView2EnvironmentStatus WebView2,
    TargetWindowDiscoveryResult Target,
    string CaptureFallback,
    string InputAdapter,
    string InputStatus,
    string WgcStatus,
    string ModelStatus,
    string HidStatus);

public static class WindowsRuntimeDiagnostics
{
    public static WindowsRuntimeDiagnosticReport Create(
        ITargetWindowLocator targetLocator,
        WebView2EnvironmentStatus webView2Status)
    {
        ArgumentNullException.ThrowIfNull(targetLocator);
        ArgumentNullException.ThrowIfNull(webView2Status);
        return new WindowsRuntimeDiagnosticReport(
            1,
            DateTimeOffset.UtcNow,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            webView2Status,
            targetLocator.Locate(),
            "BitBlt",
            "NullInputAdapter",
            "INPUT_INJECTION=DISABLED",
            "WINDOWS_PENDING",
            "MODEL_PENDING",
            "HID_CONTRACT_UNVERIFIED");
    }

    public static void Write(string path, WindowsRuntimeDiagnosticReport report)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Diagnostic output path is required", nameof(path));
        ArgumentNullException.ThrowIfNull(report);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporaryPath = fullPath + ".tmp";
        try
        {
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() },
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            });
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
