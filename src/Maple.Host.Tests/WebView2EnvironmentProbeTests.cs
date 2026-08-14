using Xunit;

namespace Maple.Host.Tests;

public sealed class WebView2EnvironmentProbeTests
{
    [Fact]
    public void MissingRuntimeIsReportedExplicitly()
    {
        var probe = new WebView2EnvironmentProbe(() => throw new InvalidOperationException("missing"));

        WebView2EnvironmentStatus status = probe.Probe();

        Assert.Equal(WebView2EnvironmentState.Missing, status.State);
        Assert.Equal("WEBVIEW2_RUNTIME_MISSING", status.Code);
        Assert.Null(status.InstalledVersion);
    }

    [Fact]
    public void InvalidVersionIsRejected()
    {
        var probe = new WebView2EnvironmentProbe(() => "not-a-version");

        WebView2EnvironmentStatus status = probe.Probe();

        Assert.Equal(WebView2EnvironmentState.InvalidVersion, status.State);
        Assert.Equal("WEBVIEW2_VERSION_INVALID", status.Code);
    }

    [Fact]
    public void RuntimeBelowCompatibilityFloorIsRejected()
    {
        var probe = new WebView2EnvironmentProbe(() => "108.0.1462.76");

        WebView2EnvironmentStatus status = probe.Probe();

        Assert.Equal(WebView2EnvironmentState.VersionTooLow, status.State);
        Assert.Equal("WEBVIEW2_VERSION_TOO_LOW", status.Code);
        Assert.Equal("108.0.1462.76", status.InstalledVersion);
    }

    [Theory]
    [InlineData("109.0.1518.78")]
    [InlineData("151.0.4129.78")]
    [InlineData("151.0.4129.78 dev")]
    public void CompatibleRuntimeIsReady(string installedVersion)
    {
        var probe = new WebView2EnvironmentProbe(() => installedVersion);

        WebView2EnvironmentStatus status = probe.Probe();

        Assert.Equal(WebView2EnvironmentState.Ready, status.State);
        Assert.Equal("WEBVIEW2_READY", status.Code);
        Assert.Equal(installedVersion, status.InstalledVersion);
    }
}
