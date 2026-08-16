using Maple.Cloud;
using Maple.Contracts;
using Maple.Vision;
using Maple.Map;
using Xunit;

namespace Maple.Host.Tests;

public sealed class ActiveMapRuntimeTests
{
    [Fact]
    public void CandidateCannotProduceActionsUntilExplicitConfirmation()
    {
        var runtime = new ActiveMapRuntime();
        ActiveMapStatus candidate = runtime.PrepareCandidate("forest-east", Annotation(), Transform);

        Assert.Equal(MapArchiveState.Candidate, candidate.State);
        Assert.False(candidate.CanProduceActions);
        Assert.False(runtime.TryGetValidated("forest-east", out _));

        ActiveMapStatus validated = runtime.ConfirmCandidate("forest-east");

        Assert.Equal(MapArchiveState.Validated, validated.State);
        Assert.True(validated.CanProduceActions);
        Assert.True(runtime.TryGetValidated("forest-east", out var world));
        Assert.NotNull(world);
    }

    [Fact]
    public void MissingCameraTransformKeepsCandidateLocked()
    {
        var runtime = new ActiveMapRuntime();
        ActiveMapStatus candidate = runtime.PrepareCandidate("forest-east", Annotation(), _ => null);

        ActiveMapStatus result = runtime.ConfirmCandidate("forest-east");

        Assert.Equal(MapArchiveState.Candidate, result.State);
        Assert.False(result.CanProduceActions);
        Assert.Contains(result.Errors, error => error.Contains("未解析", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatedMapIsScopedToItsVisualMapIdentity()
    {
        var runtime = new ActiveMapRuntime();
        runtime.PrepareCandidate("forest-east", Annotation(), Transform);
        runtime.ConfirmCandidate("forest-east");

        Assert.False(runtime.TryGetValidated("other-map", out _));
    }

    [Fact]
    public void StoredArchiveRequiresCurrentSessionRelocalizationBeforeActions()
    {
        string directory = Path.Combine(Path.GetTempPath(), "maple-active-map-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new MapArchiveRepository(directory);
            var firstSession = new ActiveMapRuntime(archives: repository);
            firstSession.PrepareCandidate("forest-east", Annotation(), Transform);
            firstSession.ConfirmCandidate("forest-east");

            var restarted = new ActiveMapRuntime(archives: repository);

            Assert.NotNull(restarted.LoadStoredForRelocalization("forest-east"));
            Assert.False(restarted.TryGetValidated("forest-east", out _));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static FrameCameraTransform Transform(long frameId) => new(frameId, frameId == 1 ? 0 : 100, 0, 0.9, true, "OK");

    private static InitialMapAnnotation Annotation() => new()
    {
        SchemaVersion = 2,
        CoordinateSystem = "mapworld-px",
        SourceFrameIds = [1, 2],
        Confidence = 0.95,
        Coverage = 0.92,
        CalibrationErrorPx = 2,
        Platforms =
        [
            new MapAnnotationPlatform { PlatformId = "p1", X1 = 0, X2 = 300, Y = 200, Confidence = 0.95 },
            new MapAnnotationPlatform { PlatformId = "p2", X1 = 320, X2 = 620, Y = 100, Confidence = 0.95 },
        ],
        Ladders = [new MapAnnotationLadder { LadderId = "l1", FromPlatformId = "p1", ToPlatformId = "p2", X = 350, Confidence = 0.95 }],
        Boundaries =
        [
            new MapAnnotationBoundary { BoundaryId = "b1", PlatformId = "p1", X = 0, Kind = "left" },
            new MapAnnotationBoundary { BoundaryId = "b2", PlatformId = "p1", X = 300, Kind = "right" },
        ],
        Connections = [new MapAnnotationConnection { ConnectionId = "c1", FromPlatformId = "p1", ToPlatformId = "p2", Type = "climb" }],
    };
}
