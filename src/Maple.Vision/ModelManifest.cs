using System.Security.Cryptography;
using System.Text.Json;

namespace Maple.Vision;

public sealed class ModelManifest
{
    public int SchemaVersion { get; init; }
    public string ModelId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string ModelFile { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string Runtime { get; init; } = string.Empty;
    public int InputWidth { get; init; }
    public int InputHeight { get; init; }
    public double ConfidenceThreshold { get; init; }
    public double NmsThreshold { get; init; }
    public string[] Classes { get; init; } = [];
}

public sealed class ModelManifestValidation
{
    public bool IsValid { get; init; }
    public string Diagnostic { get; init; } = string.Empty;
    public ModelManifest? Manifest { get; init; }
    public string? ModelPath { get; init; }
}

public static class ModelManifestLoader
{
    private static readonly string[] RequiredClasses = ["self", "player", "monster"];

    public static ModelManifestValidation Load(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath)) return Invalid("MODEL_MANIFEST_MISSING");
        try
        {
            ModelManifest? manifest = JsonSerializer.Deserialize<ModelManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.ModelId) || manifest.Runtime != "onnx") return Invalid("MODEL_MANIFEST_INVALID");
            if (manifest.InputWidth <= 0 || manifest.InputHeight <= 0 || manifest.ConfidenceThreshold is < 0 or > 1 || manifest.NmsThreshold is < 0 or > 1) return Invalid("MODEL_MANIFEST_INVALID");
            if (!RequiredClasses.SequenceEqual(manifest.Classes ?? [], StringComparer.Ordinal)) return Invalid("MODEL_CLASSES_INVALID");
            string modelPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.ModelFile));
            string root = Path.GetFullPath(Path.GetDirectoryName(manifestPath)! + Path.DirectorySeparatorChar);
            if (!modelPath.StartsWith(root, StringComparison.Ordinal) || !File.Exists(modelPath)) return Invalid("MODEL_FILE_MISSING");
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(modelPath))).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(manifest.Sha256))) return Invalid("MODEL_HASH_MISMATCH");
            return new ModelManifestValidation { IsValid = true, Diagnostic = "OK", Manifest = manifest, ModelPath = modelPath };
        }
        catch (JsonException) { return Invalid("MODEL_MANIFEST_INVALID"); }
        catch (FormatException) { return Invalid("MODEL_HASH_INVALID"); }
        catch (IOException) { return Invalid("MODEL_READ_FAILED"); }
    }

    private static ModelManifestValidation Invalid(string diagnostic) => new() { IsValid = false, Diagnostic = diagnostic };
}
