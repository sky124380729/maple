using Maple.Contracts;
using Maple.Core;
using Maple.Runtime;
using Xunit;

namespace Maple.Host.Tests;

public sealed class LiveObservationSourceTests
{
    [Fact]
    public async Task PublishKeepsOnlyNewestContextAndActionReadiness()
    {
        using var source = new LiveObservationSource((snapshot, _) => Context(snapshot));
        source.Publish(Snapshot(1), canDriveActions: false);
        source.Publish(Snapshot(2), canDriveActions: true);

        RuntimeObservationContext current = await source.ReadNextAsync(CancellationToken.None);

        Assert.Equal(2, current.Snapshot.FrameId);
        Assert.Equal(1, source.DroppedObservations);
        Assert.True(source.LatestCanDriveActions);
        Assert.Equal(2, source.Latest?.Snapshot.FrameId);
    }

    [Fact]
    public async Task CancellationDoesNotReturnAnOldObservation()
    {
        using var source = new LiveObservationSource((snapshot, _) => Context(snapshot));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await source.ReadNextAsync(cancellation.Token));
    }

    private static ObservationSnapshot Snapshot(long frameId) => new()
    {
        SchemaVersion = 2,
        FrameId = frameId,
        CapturedAtMonoMs = frameId * 100,
        Target = new TargetBinding { SchemaVersion = 2, Hwnd = "0x1", Pid = 1, ClientWidth = 1280, ClientHeight = 720, Dpi = 96 },
        Self = new SelfObservation { Box = [0.2, 0.5, 0.08, 0.18], Confidence = 0.98, FreshUntilMonoMs = 10_000 },
        Players = [],
        Monsters = [],
        Loot = new LootObservation { FreshUntilMonoMs = 10_000 },
        Hp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.9, Confidence = 0.99, FreshUntilMonoMs = 10_000 },
        Mp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.8, Confidence = 0.99, FreshUntilMonoMs = 10_000 },
        Map = new MapObservation { MapId = "forest-east", State = MapArchiveState.Validated, Confidence = 0.99, FreshUntilMonoMs = 10_000 },
        State = SessionState.Observing,
    };

    private static RuntimeObservationContext Context(ObservationSnapshot snapshot) => new(
        snapshot,
        new PlatformContext { CurrentPlatformId = "p1", TargetPlatformId = "p1", SamePlatform = true, CameraStable = true, DistanceToBoundaryPx = 300 },
        true, true, true, true, true, true, false);
}
