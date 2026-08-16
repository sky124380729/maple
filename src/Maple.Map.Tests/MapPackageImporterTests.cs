using System.IO.Compression;
using Maple.Contracts;
using Maple.Map;
using Xunit;

namespace Maple.Map.Tests;

public sealed class MapPackageImporterTests
{
    [Fact]
    public void ImportsPlatformsLaddersAndLinksAsCandidateOnly()
    {
        string path = Path.Combine(Path.GetTempPath(), $"maple-{Guid.NewGuid():N}.mapzip");
        try
        {
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Write(archive, "manifest.json", "{\"map_name\":\"森林东部\"}");
                Write(archive, "map.json", """
                    {"name":"森林东部","platforms":[{"id":0,"x_range":[10,100],"y":40},{"id":1,"x_range":[20,90],"y":20}],"ladders":[{"id":0,"x":50,"platform_ids":[1,0]}],"platform_links":[{"id":0,"from_platform":0,"to_platform":1}],"jump_links":[],"drop_links":[]}
                    """);
            }

            MapWorld world = new MapPackageImporter().LoadCandidate(path);

            Assert.Equal("森林东部", world.MapId);
            Assert.Equal(MapArchiveState.Candidate, world.State);
            Assert.Equal(2, world.Platforms.Count);
            Assert.Single(world.Ladders);
            Assert.Contains(world.UnresolvedStructures, item => item.Contains("MINIMAP", StringComparison.Ordinal));
            Assert.False(world.CanProduceActions);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using StreamWriter writer = new(archive.CreateEntry(name).Open());
        writer.Write(content);
    }
}
