using Maple.Cloud;
using Maple.Contracts;
using Maple.Map;
using Maple.Vision;

namespace Maple.Host;

public sealed record ActiveMapStatus(
    string MapId,
    MapArchiveState State,
    double Coverage,
    double CalibrationErrorPx,
    int PlatformCount,
    int LadderCount,
    IReadOnlyList<string> Errors,
    bool CanProduceActions);

public sealed class ActiveMapRuntime
{
    private readonly object sync = new();
    private readonly TopologyValidator validator;
    private readonly IMapArchiveRepository? archives;
    private MapWorld? candidate;
    private MapWorld? active;
    private TopologyValidationReport? candidateValidation;

    public ActiveMapRuntime(TopologyValidator? validator = null, IMapArchiveRepository? archives = null)
    {
        this.validator = validator ?? new TopologyValidator(new TopologyValidationOptions
        {
            SupportedSchemaVersion = ContractConstants.SchemaVersion,
            MinimumCoverage = 0.85,
            MaximumCalibrationErrorPx = 5,
            MinimumPlatformLengthPx = 24,
        });
        this.archives = archives;
    }

    public ActiveMapStatus? CurrentStatus
    {
        get { lock (sync) return Status(active ?? candidate, active is null ? candidateValidation : active.ValidationReport); }
    }

    public ActiveMapStatus PrepareCandidate(string mapId, InitialMapAnnotation annotation, Func<long, FrameCameraTransform?> transformProvider)
    {
        if (string.IsNullOrWhiteSpace(mapId)) throw new ArgumentException("MAP_ID_REQUIRED", nameof(mapId));
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentNullException.ThrowIfNull(transformProvider);
        BailianSchemaValidationResult schema = BailianSchemaValidation.Validate(annotation);
        if (!schema.IsValid) throw new ArgumentException("MAP_ANNOTATION_INVALID:" + schema.Error, nameof(annotation));

        var world = new MapWorld(mapId.Trim(), ContractConstants.SchemaVersion)
        {
            Coverage = annotation.Coverage,
            CalibrationErrorPx = annotation.CalibrationErrorPx,
        };
        if (!string.Equals(annotation.CoordinateSystem, "mapworld-px", StringComparison.Ordinal))
            world.UnresolvedStructures.Add("COORDINATE_SYSTEM_REQUIRES_VISUAL_CONVERSION");
        if (annotation.Confidence < 0.75) world.UnresolvedStructures.Add("ANNOTATION_CONFIDENCE_LOW");
        if (annotation.SourceFrameIds.Count < 2) world.UnresolvedStructures.Add("MAP_SCAN_REQUIRES_MULTIPLE_FRAMES");

        foreach (long frameId in annotation.SourceFrameIds.Distinct())
        {
            world.SourceFrames.Add(new MapSourceFrame { FrameId = frameId, CapturedAtMonoMs = 0, ImageReference = "scan://" + frameId });
            FrameCameraTransform? tracked = transformProvider(frameId);
            if (tracked is not { Ready: true } || tracked.Confidence < 0.55)
            {
                world.UnresolvedStructures.Add("CAMERA_TRANSFORM_MISSING:" + frameId);
                continue;
            }
            world.CameraTransforms.Add(new CameraTransform { FrameId = frameId, OffsetX = tracked.OffsetX, OffsetY = tracked.OffsetY, Confidence = tracked.Confidence });
        }
        foreach (MapAnnotationPlatform platform in annotation.Platforms)
        {
            world.Platforms.Add(new PlatformNode { PlatformId = platform.PlatformId, X1 = platform.X1, X2 = platform.X2, Y = platform.Y, SafeMarginPx = 8 });
            if (platform.Confidence < 0.70) world.UnresolvedStructures.Add("PLATFORM_CONFIDENCE_LOW:" + platform.PlatformId);
        }
        foreach (MapAnnotationLadder ladder in annotation.Ladders)
        {
            world.Ladders.Add(new LadderNode { LadderId = ladder.LadderId, FromPlatformId = ladder.FromPlatformId, ToPlatformId = ladder.ToPlatformId, X = ladder.X });
            if (ladder.Confidence < 0.70) world.UnresolvedStructures.Add("LADDER_CONFIDENCE_LOW:" + ladder.LadderId);
        }
        foreach (MapAnnotationBoundary boundary in annotation.Boundaries)
        {
            BoundaryKind kind = boundary.Kind switch
            {
                "left" or "leftEdge" => BoundaryKind.LeftEdge,
                "right" or "rightEdge" => BoundaryKind.RightEdge,
                "portal" => BoundaryKind.Portal,
                _ => BoundaryKind.Unknown,
            };
            if (kind == BoundaryKind.Unknown) world.UnresolvedStructures.Add("BOUNDARY_KIND_UNKNOWN:" + boundary.BoundaryId);
            world.Boundaries.Add(new MapBoundary { BoundaryId = boundary.BoundaryId, PlatformId = boundary.PlatformId, X = boundary.X, Kind = kind });
        }
        foreach (MapAnnotationConnection connection in annotation.Connections)
        {
            TopologyEdgeType? type = connection.Type switch
            {
                "walk" => TopologyEdgeType.Walk,
                "jump" => TopologyEdgeType.Jump,
                "climb" => TopologyEdgeType.Climb,
                "drop" => TopologyEdgeType.Drop,
                _ => null,
            };
            if (!type.HasValue)
            {
                world.UnresolvedStructures.Add("CONNECTION_TYPE_UNKNOWN:" + connection.ConnectionId);
                continue;
            }
            world.Edges.Add(new TopologyEdge { EdgeId = connection.ConnectionId, FromPlatformId = connection.FromPlatformId, ToPlatformId = connection.ToPlatformId, Type = type.Value, MaximumDistancePx = 300 });
        }

        TopologyValidationReport validation = validator.Validate(world);
        lock (sync)
        {
            candidate = world;
            candidateValidation = validation;
            return Status(world, validation)!;
        }
    }

    public ActiveMapStatus ConfirmCandidate(string mapId)
    {
        lock (sync)
        {
            if (candidate is null || !string.Equals(candidate.MapId, mapId, StringComparison.Ordinal))
                throw new InvalidOperationException("MAP_CANDIDATE_NOT_FOUND");
            candidateValidation = validator.Validate(candidate);
            if (!candidateValidation.IsValid) return Status(candidate, candidateValidation)!;
            candidate.ApplyValidation(candidateValidation);
            archives?.SaveValidated(candidate);
            active = candidate;
            candidate = null;
            candidateValidation = null;
            return Status(active, active.ValidationReport)!;
        }
    }

    public bool TryGetValidated(string mapId, out MapWorld? world)
    {
        lock (sync)
        {
            world = active is not null && string.Equals(active.MapId, mapId, StringComparison.Ordinal) && active.CanProduceActions ? active : null;
            return world is not null;
        }
    }

    public MapWorld? LoadStoredForRelocalization(string mapId) => archives?.LoadValidated(mapId);

    private static ActiveMapStatus? Status(MapWorld? world, TopologyValidationReport? validation) => world is null ? null : new(
        world.MapId,
        world.State,
        world.Coverage,
        world.CalibrationErrorPx,
        world.Platforms.Count,
        world.Ladders.Count,
        validation?.Errors.ToArray() ?? [],
        world.CanProduceActions);
}
