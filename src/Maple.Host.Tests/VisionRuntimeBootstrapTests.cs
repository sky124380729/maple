using Maple.Host;
using Xunit;

namespace Maple.Host.Tests;

public sealed class VisionRuntimeBootstrapTests
{
    [Fact]
    public void Explicit_manifest_path_has_highest_priority()
    {
        string result = VisionRuntimeBootstrap.ResolveManifestPath(
            "explicit.json",
            "environment.json",
            Path.GetTempPath());

        Assert.Equal("explicit.json", result);
    }

    [Fact]
    public void Application_local_manifest_is_used_when_present()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"maple-model-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string local = Path.Combine(directory, "model-manifest.json");
            File.WriteAllText(local, "{}");

            string result = VisionRuntimeBootstrap.ResolveManifestPath(null, null, directory);

            Assert.Equal(local, result);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Environment_manifest_precedes_application_local_manifest()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"maple-model-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "model-manifest.json"), "{}");

            string result = VisionRuntimeBootstrap.ResolveManifestPath(null, "environment.json", directory);

            Assert.Equal("environment.json", result);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
