using Xunit;

namespace Maple.Host.Tests;

public sealed class WindowsRuntimeDiagnosticsTests
{
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
