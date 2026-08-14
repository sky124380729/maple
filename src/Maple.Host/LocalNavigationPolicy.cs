namespace Maple.Host;

public static class LocalNavigationPolicy
{
    public static bool IsAllowed(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "maple.local", StringComparison.OrdinalIgnoreCase);
    }
}
