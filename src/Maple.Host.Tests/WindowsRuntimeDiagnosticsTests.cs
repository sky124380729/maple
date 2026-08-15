using Xunit;
using System.Text.Json;

namespace Maple.Host.Tests;

public sealed class WindowsRuntimeDiagnosticsTests
{
    [Fact]
    public void DiagnosticJsonDoesNotExposeTargetExecutablePath()
    {
        var target = new WindowIdentity(
            "0x1", 7, WindowsTargetWindowLocator.TargetTitle, WindowsTargetWindowLocator.TargetClassName,
            true, false, 0, 0, 1280, 720, 96, DateTimeOffset.UtcNow, "hash", "1.0",
            "C:\\Private\\Maplestory_Classic.exe");
        var report = WindowsRuntimeDiagnostics.Create(
            new FixedLocator(new TargetWindowDiscoveryResult(
                TargetWindowDiscoveryStatus.Found, "TARGET_BOUND", [target])),
            new WebView2EnvironmentStatus(
                WebView2EnvironmentState.Ready,
                "WEBVIEW2_READY",
                "151.0.4129.78",
                WebView2EnvironmentProbe.MinimumRuntimeVersion));

        string json = JsonSerializer.Serialize(report);

        Assert.DoesNotContain("C:\\Private", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("processPath\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportKeepsUnverifiedNativeCapabilitiesPending()
    {
        var locator = new FixedLocator(new TargetWindowDiscoveryResult(
            TargetWindowDiscoveryStatus.NotFound,
            "TARGET_NOT_FOUND",
            []));
        var webView = new WebView2EnvironmentStatus(
            WebView2EnvironmentState.Ready,
            "WEBVIEW2_READY",
            "151.0.4129.78",
            WebView2EnvironmentProbe.MinimumRuntimeVersion);

        WindowsRuntimeDiagnosticReport report = WindowsRuntimeDiagnostics.Create(locator, webView);

        Assert.Equal("NullInputAdapter", report.InputAdapter);
        Assert.Equal("INPUT_INJECTION=DISABLED", report.InputStatus);
        Assert.Equal("WINDOWS_PENDING", report.WgcStatus);
        Assert.Equal("MODEL_PENDING", report.ModelStatus);
        Assert.Equal("HID_CONTRACT_UNVERIFIED", report.HidStatus);
        Assert.Equal("TARGET_NOT_FOUND", report.Target.DiagnosticCode);
    }

    private sealed class FixedLocator(TargetWindowDiscoveryResult result) : ITargetWindowLocator
    {
        public TargetWindowDiscoveryResult Locate() => result;
    }
}
