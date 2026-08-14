#nullable enable

using System.Text.RegularExpressions;

namespace Maple.Cloud;

public static partial class BailianSecretRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        string redacted = AuthorizationPattern().Replace(value, "Authorization: Bearer [REDACTED]");
        redacted = NamedCredentialPattern().Replace(redacted, "$1=[REDACTED]");
        return KeyShapePattern().Replace(redacted, "[REDACTED]");
    }

    [GeneratedRegex(@"Authorization\s*:\s*Bearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationPattern();

    [GeneratedRegex(@"(api[_-]?key|DASHSCOPE_API_KEY)\s*[:=]\s*[^;\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedCredentialPattern();

    [GeneratedRegex(@"sk-[A-Za-z0-9_-]{16,}", RegexOptions.CultureInvariant)]
    private static partial Regex KeyShapePattern();
}
