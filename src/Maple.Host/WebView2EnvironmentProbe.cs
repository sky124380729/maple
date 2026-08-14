namespace Maple.Host;

public enum WebView2EnvironmentState
{
    Missing,
    InvalidVersion,
    VersionTooLow,
    Ready,
}

public sealed record WebView2EnvironmentStatus(
    WebView2EnvironmentState State,
    string Code,
    string? InstalledVersion,
    string MinimumVersion)
{
    public bool IsReady => State == WebView2EnvironmentState.Ready;
}

public sealed class WebView2EnvironmentProbe
{
    // This is the oldest Evergreen line accepted by this application, not the installed SDK version.
    public const string MinimumRuntimeVersion = "109.0.1518.78";

    private readonly Func<string?> getAvailableVersion;
    private readonly string minimumVersion;

    public WebView2EnvironmentProbe(
        Func<string?> getAvailableVersion,
        string minimumVersion = MinimumRuntimeVersion)
    {
        this.getAvailableVersion = getAvailableVersion ?? throw new ArgumentNullException(nameof(getAvailableVersion));
        this.minimumVersion = ParseVersion(minimumVersion)?.ToString()
            ?? throw new ArgumentException("Invalid WebView2 minimum version", nameof(minimumVersion));
    }

    public WebView2EnvironmentStatus Probe()
    {
        string? installedVersion;
        try { installedVersion = getAvailableVersion(); }
        catch
        {
            return new(WebView2EnvironmentState.Missing, "WEBVIEW2_RUNTIME_MISSING", null, minimumVersion);
        }

        Version? installed = ParseVersion(installedVersion);
        if (installed is null)
        {
            return new(WebView2EnvironmentState.InvalidVersion, "WEBVIEW2_VERSION_INVALID", installedVersion, minimumVersion);
        }

        Version minimum = Version.Parse(minimumVersion);
        return installed < minimum
            ? new(WebView2EnvironmentState.VersionTooLow, "WEBVIEW2_VERSION_TOO_LOW", installedVersion, minimumVersion)
            : new(WebView2EnvironmentState.Ready, "WEBVIEW2_READY", installedVersion, minimumVersion);
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string numeric = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        string[] components = numeric.Split('.');
        if (components.Length != 4 || components.Any(component => !int.TryParse(component, out int number) || number < 0)) return null;
        return Version.TryParse(numeric, out Version? version) ? version : null;
    }
}
