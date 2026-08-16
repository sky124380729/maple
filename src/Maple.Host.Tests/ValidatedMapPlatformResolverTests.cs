using Maple.Contracts;
using Maple.Map;
using Xunit;

namespace Maple.Host.Tests;

public sealed class ValidatedMapPlatformResolverTests
{
    [Fact]
    public void ResolveMapsSelfAndNearestMonsterToSameValidatedPlatform()
    {
        MapWorld world = ValidatedWorld();
        ObservationSnapshot snapshot = Snapshot(MapArchiveState.Validated);

        PlatformResolution result = new ValidatedMapPlatformResolver(36).Resolve(
            snapshot,
            world,
            new CameraTransform { FrameId = snapshot.FrameId, OffsetX = 0, OffsetY = 0, Confidence = 0.98 });

        Assert.True(result.Resolved);
        Assert.True(result.Context.SamePlatform);
        Assert.Equal("p1", result.Context.CurrentPlatformId);
        Assert.Equal("p1", result.Context.TargetPlatformId);
    }

    [Fact]
    public void ResolveRejectsCandidateMap()
    {
        MapWorld world = ValidatedWorld();
        ObservationSnapshot snapshot = Snapshot(MapArchiveState.Candidate);

        PlatformResolution result = new ValidatedMapPlatformResolver().Resolve(
            snapshot,
            world,
            new CameraTransform { FrameId = snapshot.FrameId, Confidence = 0.98 });

        Assert.False(result.Resolved);
        Assert.Equal("MAP_NOT_VALIDATED", result.Code);
    }

    private static MapWorld ValidatedWorld()
    {
        var world = new MapWorld("forest-east", 2) { Coverage = 1, CalibrationErrorPx = 1 };
        world.SourceFrames.Add(new MapSourceFrame { FrameId = 7, CapturedAtMonoMs = 1000, ImageReference = "frame.png" });
        world.CameraTransforms.Add(new CameraTransform { FrameId = 7, Confidence = 0.98 });
        world.Platforms.Add(new PlatformNode { PlatformId = "p1", X1 = 0, X2 = 1280, Y = 490, SafeMarginPx = 20 });
        world.ApplyValidation(new TopologyValidator(new TopologyValidationOptions
        {
            SupportedSchemaVersion = 2,
            MinimumCoverage = 0.9,
            MaximumCalibrationErrorPx = 4,
            MinimumPlatformLengthPx = 32,
        }).Validate(world));
        return world;
    }

    private static ObservationSnapshot Snapshot(MapArchiveState mapState) => new()
    {
        SchemaVersion = 2,
        FrameId = 7,
        CapturedAtMonoMs = 1000,
        Target = new TargetBinding { SchemaVersion = 2, Hwnd = "0x1", Pid = 1, ClientWidth = 1280, ClientHeight = 720, Dpi = 96 },
        Self = new SelfObservation { Box = [0.2, 0.5, 0.08, 0.18], Confidence = 0.98, FreshUntilMonoMs = 1200 },
        Players = [],
        Monsters = [new MonsterObservation { TargetId = "snail-1", Class = "snail", Box = [0.6, 0.56, 0.08, 0.10], Confidence = 0.96, FreshUntilMonoMs = 1200 }],
        Loot = new LootObservation { FreshUntilMonoMs = 1200 },
        Hp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.9, Confidence = 0.99, FreshUntilMonoMs = 1200 },
        Mp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.8, Confidence = 0.99, FreshUntilMonoMs = 1200 },
        Map = new MapObservation { MapId = "forest-east", State = mapState, Confidence = 0.99, FreshUntilMonoMs = 1200 },
        State = SessionState.Observing,
    };
}
