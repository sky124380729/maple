#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Maple.Map;

public interface IMapArchiveRepository
{
    void SaveValidated(MapWorld world);
    MapWorld? LoadValidated(string mapId);
}

public sealed class MapArchiveRepository : IMapArchiveRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private readonly string directory;
    private readonly TopologyValidator validator;

    public MapArchiveRepository(string directory, TopologyValidator? validator = null)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("MAP_ARCHIVE_DIRECTORY_REQUIRED", nameof(directory));
        this.directory = directory;
        this.validator = validator ?? new TopologyValidator(new TopologyValidationOptions { SupportedSchemaVersion = Maple.Contracts.ContractConstants.SchemaVersion, MinimumCoverage = 0.85, MaximumCalibrationErrorPx = 5, MinimumPlatformLengthPx = 24 });
    }

    public void SaveValidated(MapWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.CanProduceActions || world.State != Maple.Contracts.MapArchiveState.Validated) throw new InvalidOperationException("MAP_ARCHIVE_MUST_BE_VALIDATED");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileName(world.MapId));
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(ArchiveDto.From(world), JsonOptions), Encoding.UTF8);
        File.Move(temp, path, true);
    }

    public MapWorld? LoadValidated(string mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId)) return null;
        string path = Path.Combine(directory, FileName(mapId));
        if (!File.Exists(path)) return null;
        try
        {
            ArchiveDto? dto = JsonSerializer.Deserialize<ArchiveDto>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
            MapWorld? world = dto?.ToWorld();
            if (world is null || !string.Equals(world.MapId, mapId, StringComparison.Ordinal) || world.State != Maple.Contracts.MapArchiveState.Validated) return null;
            TopologyValidationReport report = validator.Validate(world);
            return report.IsValid && world.CanProduceActions ? world : null;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
        catch (InvalidDataException) { return null; }
    }

    private static string FileName(string mapId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(mapId));
        string safe = new(mapId.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
        return $"{safe[..Math.Min(48, safe.Length)]}-{Convert.ToHexString(hash[..6]).ToLowerInvariant()}.map.json";
    }

    private sealed class ArchiveDto
    {
        public int SchemaVersion { get; set; }
        public string MapId { get; set; } = string.Empty;
        public double Coverage { get; set; }
        public double CalibrationErrorPx { get; set; }
        public List<MapSourceFrame> SourceFrames { get; set; } = [];
        public List<CameraTransform> CameraTransforms { get; set; } = [];
        public List<PlatformNode> Platforms { get; set; } = [];
        public List<LadderNode> Ladders { get; set; } = [];
        public List<MapBoundary> Boundaries { get; set; } = [];
        public List<TopologyEdge> Edges { get; set; } = [];
        public List<string> UnresolvedStructures { get; set; } = [];
        public List<string> ValidationWarnings { get; set; } = [];

        public static ArchiveDto From(MapWorld world) => new()
        {
            SchemaVersion = world.SchemaVersion,
            MapId = world.MapId,
            Coverage = world.Coverage,
            CalibrationErrorPx = world.CalibrationErrorPx,
            SourceFrames = world.SourceFrames,
            CameraTransforms = world.CameraTransforms,
            Platforms = world.Platforms,
            Ladders = world.Ladders,
            Boundaries = world.Boundaries,
            Edges = world.Edges,
            UnresolvedStructures = world.UnresolvedStructures,
            ValidationWarnings = world.ValidationReport?.Warnings ?? [],
        };

        public MapWorld? ToWorld()
        {
            var world = new MapWorld(MapId, SchemaVersion)
            {
                Coverage = Coverage,
                CalibrationErrorPx = CalibrationErrorPx,
            };
            world.SourceFrames.AddRange(SourceFrames ?? []);
            world.CameraTransforms.AddRange(CameraTransforms ?? []);
            world.Platforms.AddRange(Platforms ?? []);
            world.Ladders.AddRange(Ladders ?? []);
            world.Boundaries.AddRange(Boundaries ?? []);
            world.Edges.AddRange(Edges ?? []);
            world.UnresolvedStructures.AddRange(UnresolvedStructures ?? []);
            var report = new TopologyValidationReport();
            report.Warnings.AddRange(ValidationWarnings ?? []);
            world.ApplyValidation(report);
            return world;
        }
    }
}
