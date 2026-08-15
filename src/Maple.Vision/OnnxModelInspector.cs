using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;

namespace Maple.Vision;

public sealed record OnnxTensorInfo(string Name, string ElementType, int[] Dimensions);

public sealed class OnnxModelInspectionReport
{
    public int SchemaVersion { get; init; } = 1;
    public string ModelPath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string Producer { get; init; } = string.Empty;
    public string License { get; init; } = string.Empty;
    public IReadOnlyList<OnnxTensorInfo> Inputs { get; init; } = [];
    public IReadOnlyList<OnnxTensorInfo> Outputs { get; init; } = [];
    public IReadOnlyList<string> Classes { get; init; } = [];
    public IReadOnlyDictionary<string, DetectionRole> ClassRoles { get; init; } = new Dictionary<string, DetectionRole>();
    public OnnxOutputLayout OutputLayout { get; init; }
    public IReadOnlyList<string> AvailableProviders { get; init; } = [];
    public bool ModelReady { get; init; }
    public bool CanDriveActions { get; init; }
    public string Diagnostic { get; init; } = string.Empty;
}

public static partial class OnnxModelInspector
{
    public static OnnxModelInspectionReport Inspect(string modelPath)
    {
        string fullPath = Path.GetFullPath(modelPath ?? throw new ArgumentNullException(nameof(modelPath)));
        if (!File.Exists(fullPath)) throw new FileNotFoundException("ONNX 模型不存在", fullPath);
        using var session = new InferenceSession(fullPath);
        Dictionary<string, string> metadata = session.ModelMetadata.CustomMetadataMap;
        string[] classes = metadata.TryGetValue("names", out string? names) ? ParseUltralyticsNames(names) : [];
        OnnxTensorInfo[] inputs = session.InputMetadata.Select(item => Tensor(item.Key, item.Value)).ToArray();
        OnnxTensorInfo[] outputs = session.OutputMetadata.Select(item => Tensor(item.Key, item.Value)).ToArray();
        OnnxOutputLayout layout = outputs.Length == 1 ? ClassifyOutput(outputs[0].Dimensions, classes.Length) : OnnxOutputLayout.Unsupported;
        Dictionary<string, DetectionRole> roles = DefaultRoles(classes);
        bool validRoles = roles.Values.Contains(DetectionRole.CharacterCandidate) && roles.Values.Contains(DetectionRole.Monster);
        bool ready = inputs.Length == 1 && outputs.Length == 1 && layout != OnnxOutputLayout.Unsupported && validRoles;
        string diagnostic = !validRoles ? "MODEL_CLASSES_INVALID" : layout == OnnxOutputLayout.Unsupported ? "MODEL_OUTPUT_UNSUPPORTED" : ready ? "SELF_NOT_RESOLVED" : "MODEL_METADATA_INVALID";
        return new OnnxModelInspectionReport
        {
            ModelPath = fullPath,
            Sha256 = ComputeSha256(fullPath),
            Producer = session.ModelMetadata.ProducerName ?? string.Empty,
            License = metadata.TryGetValue("license", out string? license) ? license : string.Empty,
            Inputs = inputs,
            Outputs = outputs,
            Classes = classes,
            ClassRoles = roles,
            OutputLayout = layout,
            AvailableProviders = OrtEnv.Instance().GetAvailableProviders(),
            ModelReady = ready,
            CanDriveActions = false,
            Diagnostic = diagnostic,
        };
    }

    public static void Write(string outputPath, OnnxModelInspectionReport report)
    {
        string fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, options));
    }

    public static string[] ParseUltralyticsNames(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        return NamesPattern().Matches(value).Select(match => match.Groups[1].Value).ToArray();
    }

    public static OnnxOutputLayout ClassifyOutput(IReadOnlyList<int> dimensions, int classCount)
    {
        if (dimensions.Count == 2 && dimensions[1] == 6) return OnnxOutputLayout.FixedNmsNx6;
        if (dimensions.Count == 3 && dimensions[0] == 1)
        {
            int channels = 4 + classCount;
            if (classCount > 0 && dimensions[1] == channels) return OnnxOutputLayout.YoloChannelsFirst;
            if (classCount > 0 && dimensions[2] == channels) return OnnxOutputLayout.YoloChannelsLast;
            if (dimensions[2] == 6) return OnnxOutputLayout.FixedNmsNx6;
        }
        return OnnxOutputLayout.Unsupported;
    }

    public static Dictionary<string, DetectionRole> DefaultRoles(IEnumerable<string> classes)
    {
        var result = new Dictionary<string, DetectionRole>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in classes)
        {
            result[name] = name.Equals("character", StringComparison.OrdinalIgnoreCase)
                ? DetectionRole.CharacterCandidate
                : name.Equals("mob", StringComparison.OrdinalIgnoreCase) || name.Equals("monster", StringComparison.OrdinalIgnoreCase)
                    ? DetectionRole.Monster
                    : DetectionRole.Ignore;
        }
        return result;
    }

    private static OnnxTensorInfo Tensor(string name, NodeMetadata metadata) => new(name, metadata.ElementType.Name, metadata.Dimensions);

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    [GeneratedRegex("""\d+\s*:\s*['"]([^'"]+)['"]""", RegexOptions.CultureInvariant)]
    private static partial Regex NamesPattern();
}
