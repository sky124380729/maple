using System.Security.Cryptography;
using Maple.Vision;
using Xunit;

namespace Maple.Runtime.Tests.Vision;

public sealed class OnnxModelInspectorTests
{
    [Fact]
    public void ParsesUltralyticsNamesAndClassifiesBothYoloLayouts()
    {
        string[] classes = OnnxModelInspector.ParseUltralyticsNames("{0: 'character', 1: 'environment', 2: 'mob'}");

        Assert.Equal(["character", "environment", "mob"], classes);
        Assert.Equal(OnnxOutputLayout.YoloChannelsFirst, OnnxModelInspector.ClassifyOutput([1, 7, 2100], classes.Length));
        Assert.Equal(OnnxOutputLayout.YoloChannelsLast, OnnxModelInspector.ClassifyOutput([1, 2100, 7], classes.Length));
        Assert.Equal(OnnxOutputLayout.Unsupported, OnnxModelInspector.ClassifyOutput([1, 5, 2100], classes.Length));
    }

    [Fact]
    public void ManifestRequiresCharacterAndMonsterRolesAndRejectsHashMismatch()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"maple-model-v2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string modelPath = Path.Combine(directory, "detector.onnx");
            File.WriteAllBytes(modelPath, [1, 2, 3, 4]);
            string checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(modelPath))).ToLowerInvariant();
            string manifestPath = Path.Combine(directory, "manifest.json");
            File.WriteAllText(manifestPath, Manifest(checksum, includeMonster: true));

            Assert.True(ModelManifestLoader.Load(manifestPath).IsValid);

            File.WriteAllText(manifestPath, Manifest(checksum, includeMonster: false));
            Assert.Equal("MODEL_CLASSES_INVALID", ModelManifestLoader.Load(manifestPath).Diagnostic);

            File.WriteAllText(manifestPath, Manifest(new string('0', 64), includeMonster: true));
            Assert.Equal("MODEL_HASH_MISMATCH", ModelManifestLoader.Load(manifestPath).Diagnostic);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Manifest(string checksum, bool includeMonster) => $$"""
        {
          "schemaVersion": 2,
          "modelId": "maple-dynamic-v2",
          "version": "1.0.0",
          "modelFile": "detector.onnx",
          "sha256": "{{checksum}}",
          "runtime": "onnx",
          "inputWidth": 320,
          "inputHeight": 320,
          "confidenceThreshold": 0.6,
          "nmsThreshold": 0.45,
          "classes": ["character", "mob"],
          "classRoles": {
            "character": "characterCandidate"{{(includeMonster ? ",\n    \"mob\": \"monster\"" : string.Empty)}}
          },
          "outputLayout": "yoloChannelsFirst"
        }
        """;
}
