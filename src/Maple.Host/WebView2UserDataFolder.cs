namespace Maple.Host;

public static class WebView2UserDataFolder
{
    public static string ResolveDefault()
    {
        string preferred = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Maple", "WebView2");
        string fallback = Path.Combine(Path.GetTempPath(), "Maple", "WebView2");
        return Resolve(preferred, fallback, path => Directory.CreateDirectory(path));
    }

    public static string Resolve(string preferred, string fallback, Action<string> ensureDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferred);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);
        ArgumentNullException.ThrowIfNull(ensureDirectory);
        string preferredPath = Path.GetFullPath(preferred);
        try
        {
            ensureDirectory(preferredPath);
            return preferredPath;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            string fallbackPath = Path.GetFullPath(fallback);
            ensureDirectory(fallbackPath);
            return fallbackPath;
        }
    }
}
