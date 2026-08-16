using System.IO.Compression;
using System.Text.Json;
using Maple.Contracts;

namespace Maple.Map;

/// <summary>
/// Reads the documented mapzip structure as a candidate seed. Package coordinates are minimap pixels
/// and are intentionally marked unresolved until a visual scan supplies the MapWorld transform.
/// </summary>
public sealed class MapPackageImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public MapWorld LoadCandidate(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath)) throw new ArgumentException("MAP_PACKAGE_PATH_MISSING", nameof(packagePath));
        if (!File.Exists(packagePath)) throw new FileNotFoundException("MAP_PACKAGE_NOT_FOUND", packagePath);
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        using JsonDocument manifest = ReadJson(archive, "manifest.json");
        using JsonDocument map = ReadJson(archive, "map.json");
        string mapName = StringValue(map.RootElement, "name")
            ?? StringValue(manifest.RootElement, "map_name")
            ?? Path.GetFileNameWithoutExtension(packagePath);
        var world = new MapWorld(mapName, ContractConstants.SchemaVersion)
        {
            Coverage = 0,
            CalibrationErrorPx = double.PositiveInfinity,
        };
        world.SourceFrames.Add(new MapSourceFrame { FrameId = 0, CapturedAtMonoMs = 0, ImageReference = packagePath });
        world.CameraTransforms.Add(new CameraTransform { FrameId = 0, OffsetX = 0, OffsetY = 0, Confidence = 0 });
        foreach (JsonElement platform in ArrayOrEmpty(map.RootElement, "platforms"))
        {
            JsonElement range = platform.GetProperty("x_range");
            world.Platforms.Add(new PlatformNode
            {
                PlatformId = "p-" + platform.GetProperty("id").GetInt32(),
                X1 = range[0].GetDouble(),
                X2 = range[1].GetDouble(),
                Y = platform.GetProperty("y").GetDouble(),
                SafeMarginPx = 0,
            });
        }
        foreach (JsonElement ladder in ArrayOrEmpty(map.RootElement, "ladders"))
        {
            JsonElement ids = ladder.GetProperty("platform_ids");
            string from = "p-" + ids[0].GetInt32();
            string to = "p-" + ids[1].GetInt32();
            string id = "ladder-" + ladder.GetProperty("id").GetInt32();
            world.Ladders.Add(new LadderNode { LadderId = id, FromPlatformId = from, ToPlatformId = to, X = ladder.GetProperty("x").GetDouble() });
            world.Edges.Add(new TopologyEdge { EdgeId = id + "-edge", FromPlatformId = from, ToPlatformId = to, Type = TopologyEdgeType.Climb, MaximumDistancePx = 200 });
        }
        AddEdges(world, map.RootElement, "platform_links", TopologyEdgeType.Walk);
        AddEdges(world, map.RootElement, "jump_links", TopologyEdgeType.Jump);
        AddEdges(world, map.RootElement, "drop_links", TopologyEdgeType.Drop);
        world.UnresolvedStructures.Add("PACKAGE_COORDINATE_SYSTEM_MINIMAP_PIXELS");
        world.UnresolvedStructures.Add("VISUAL_SCAN_REQUIRED_BEFORE_VALIDATION");
        return world;
    }

    private static void AddEdges(MapWorld world, JsonElement root, string property, TopologyEdgeType type)
    {
        foreach (JsonElement edge in ArrayOrEmpty(root, property))
        {
            string from = "p-" + (edge.TryGetProperty("from_platform", out JsonElement fromElement) ? fromElement.GetInt32() : edge.GetProperty("from").GetInt32());
            string to = "p-" + (edge.TryGetProperty("to_platform", out JsonElement toElement) ? toElement.GetInt32() : edge.GetProperty("to").GetInt32());
            int id = edge.TryGetProperty("id", out JsonElement idElement) ? idElement.GetInt32() : world.Edges.Count;
            world.Edges.Add(new TopologyEdge { EdgeId = property + "-" + id, FromPlatformId = from, ToPlatformId = to, Type = type, MaximumDistancePx = 300 });
        }
    }

    private static JsonDocument ReadJson(ZipArchive archive, string name)
    {
        ZipArchiveEntry entry = archive.GetEntry(name) ?? throw new InvalidDataException("MAP_PACKAGE_ENTRY_MISSING:" + name);
        using Stream stream = entry.Open();
        return JsonDocument.Parse(stream);
    }

    private static IEnumerable<JsonElement> ArrayOrEmpty(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private static string StringValue(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
