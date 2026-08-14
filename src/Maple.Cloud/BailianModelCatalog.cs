#nullable enable

namespace Maple.Cloud;

public static class BailianModelCatalog
{
    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        "qwen3-vl-plus",
        "qwen3-vl-flash",
        "qwen-vl-max"
    };

    public const string DefaultModelId = "qwen3-vl-plus";
    public static IReadOnlyCollection<string> ModelIds => Supported;

    public static bool IsSupported(string? modelId) => modelId is not null && Supported.Contains(modelId);
}
