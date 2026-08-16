using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    public double? DisplayConfidenceThreshold { get; init; }
    public double NmsThreshold { get; init; }
    public string[] Classes { get; init; } = [];
    public Dictionary<string, DetectionRole> ClassRoles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public OnnxOutputLayout OutputLayout { get; init; }
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
    public static ModelManifestValidation Load(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath)) return Invalid("MODEL_MANIFEST_MISSING");
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            ModelManifest? manifest = JsonSerializer.Deserialize<ModelManifest>(File.ReadAllText(manifestPath), options);
            if (manifest is null || manifest.SchemaVersion != 2 || string.IsNullOrWhiteSpace(manifest.ModelId) || manifest.Runtime != "onnx") return Invalid("MODEL_MANIFEST_INVALID");
            if (manifest.InputWidth <= 0 || manifest.InputHeight <= 0 || manifest.ConfidenceThreshold is < 0 or > 1
                || manifest.DisplayConfidenceThreshold is < 0 or > 1
                || manifest.NmsThreshold is < 0 or > 1) return Invalid("MODEL_MANIFEST_INVALID");
            if (manifest.OutputLayout == OnnxOutputLayout.Unsupported || manifest.Classes is null || manifest.Classes.Length == 0 || manifest.Classes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Classes.Length) return Invalid("MODEL_CLASSES_INVALID");
            if (manifest.ClassRoles is null || manifest.ClassRoles.Keys.Any(key => !manifest.Classes.Contains(key, StringComparer.OrdinalIgnoreCase))
                || !manifest.ClassRoles.Values.Contains(DetectionRole.CharacterCandidate)
                || !manifest.ClassRoles.Values.Contains(DetectionRole.Monster)) return Invalid("MODEL_CLASSES_INVALID");
            if (string.IsNullOrWhiteSpace(manifest.ModelFile)) return Invalid("MODEL_FILE_MISSING");
            string manifestDirectory = Path.GetFullPath(Path.GetDirectoryName(manifestPath)!);
            string modelPath = Path.IsPathRooted(manifest.ModelFile)
                ? Path.GetFullPath(manifest.ModelFile)
                : Path.GetFullPath(Path.Combine(manifestDirectory, manifest.ModelFile));
            if (!Path.IsPathRooted(manifest.ModelFile))
            {
                string root = manifestDirectory + Path.DirectorySeparatorChar;
                if (!modelPath.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    return Invalid("MODEL_FILE_MISSING");
            }
            if (!File.Exists(modelPath)) return Invalid("MODEL_FILE_MISSING");
            if (manifest.Sha256.Length != 64) return Invalid("MODEL_HASH_INVALID");
            byte[] expected = Convert.FromHexString(manifest.Sha256);
            byte[] actual;
            using (FileStream stream = File.OpenRead(modelPath)) actual = SHA256.HashData(stream);
            if (!CryptographicOperations.FixedTimeEquals(actual, expected)) return Invalid("MODEL_HASH_MISMATCH");
            return new ModelManifestValidation { IsValid = true, Diagnostic = "OK", Manifest = manifest, ModelPath = modelPath };
        }
        catch (JsonException) { return Invalid("MODEL_MANIFEST_INVALID"); }
        catch (FormatException) { return Invalid("MODEL_HASH_INVALID"); }
        catch (IOException) { return Invalid("MODEL_READ_FAILED"); }
    }

    private static ModelManifestValidation Invalid(string diagnostic) => new() { IsValid = false, Diagnostic = diagnostic };
}
