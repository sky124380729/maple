using Maple.Contracts;
using Xunit;

namespace Maple.Map.Tests;

public sealed class MapArchiveRepositoryTests
{
    [Fact]
    public void PersistsOnlyValidatedMapsAndRejectsCandidateFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "maple-archives-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new MapArchiveRepository(directory);
            MapWorld candidate = World();
            Assert.Throws<InvalidOperationException>(() => repository.SaveValidated(candidate));
            candidate.ApplyValidation(new TopologyValidator(new TopologyValidationOptions { SupportedSchemaVersion = 2, MinimumCoverage = 0.85, MaximumCalibrationErrorPx = 5 }).Validate(candidate));
            repository.SaveValidated(candidate);

            MapWorld? loaded = repository.LoadValidated(candidate.MapId);
            Assert.NotNull(loaded);
            Assert.True(loaded!.CanProduceActions);
            Assert.Equal(candidate.Platforms.Count, loaded.Platforms.Count);
            Assert.Null(repository.LoadValidated("missing-map"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static MapWorld World()
    {
        var world = new MapWorld("forest-east", 2) { Coverage = 0.92, CalibrationErrorPx = 2 };
        world.SourceFrames.Add(new MapSourceFrame { FrameId = 1, ImageReference = "scan://1" });
        world.CameraTransforms.Add(new CameraTransform { FrameId = 1, Confidence = 0.9 });
        world.Platforms.Add(new PlatformNode { PlatformId = "p1", X1 = 0, X2 = 300, Y = 200, SafeMarginPx = 8 });
        return world;
    }
}
