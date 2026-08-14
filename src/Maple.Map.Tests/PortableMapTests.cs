using Maple.Contracts;
using Maple.Map;
using Xunit;

namespace Maple.Map.Tests;

public sealed class PortableMapTests
{
    [Fact]
    public void CandidateMapCannotProduceActionsUntilValidated()
    {
        MapWorld world = ValidCandidate();
        var validator = new TopologyValidator(new TopologyValidationOptions { SupportedSchemaVersion = 1, MinimumCoverage = 0.85, MaximumCalibrationErrorPx = 5 });

        TopologyValidationReport report = validator.Validate(world);
        Assert.True(report.IsValid);
        Assert.False(world.CanProduceActions);
        world.ApplyValidation(report);
        Assert.Equal(MapArchiveState.Validated, world.State);
        Assert.True(world.CanProduceActions);
    }

    [Fact]
    public void LowCoverageAndUnknownLadderEndpointsBlockValidation()
    {
        MapWorld world = ValidCandidate();
        world.Coverage = 0.4;
        world.Ladders.Add(new LadderNode { LadderId = "bad", FromPlatformId = "missing", ToPlatformId = "p-2" });
        TopologyValidationReport report = new TopologyValidator(new TopologyValidationOptions { SupportedSchemaVersion = 1, MinimumCoverage = 0.85, MaximumCalibrationErrorPx = 5 }).Validate(world);
        Assert.False(report.IsValid);
        Assert.Contains("地图覆盖率不足", report.Errors);
        Assert.Contains("梯子端点未连接到有效平台", report.Errors);
    }

    private static MapWorld ValidCandidate()
    {
        var world = new MapWorld("forest-east", 1) { Coverage = 0.92, CalibrationErrorPx = 2 };
        world.SourceFrames.Add(new MapSourceFrame { FrameId = 1, CapturedAtMonoMs = 1000, ImageReference = "frame-1" });
        world.CameraTransforms.Add(new CameraTransform { FrameId = 1, Confidence = 0.97 });
        world.Platforms.Add(new PlatformNode { PlatformId = "p-1", X1 = 0, X2 = 300, SafeMarginPx = 24 });
        world.Platforms.Add(new PlatformNode { PlatformId = "p-2", X1 = 180, X2 = 420, SafeMarginPx = 24 });
        world.Ladders.Add(new LadderNode { LadderId = "l-1", FromPlatformId = "p-1", ToPlatformId = "p-2" });
        world.Edges.Add(new TopologyEdge { EdgeId = "e-1", FromPlatformId = "p-1", ToPlatformId = "p-2", Type = TopologyEdgeType.Climb, MaximumDistancePx = 150 });
        return world;
    }
}
