using System.Buffers;
using Maple.Capture;
using Maple.Contracts;
using Xunit;

namespace Maple.Host.Tests;

public sealed class MapScanFrameStoreTests
{
    [Fact]
    public async Task RecordsOnlyActiveScanFramesAtTheConfiguredInterval()
    {
        var encoder = new RecordingEncoder();
        using var store = new MapScanFrameStore(encoder, minimumFrameIntervalMs: 100, capacity: 3);
        using CapturedFrame beforeScan = Frame(1, 100);
        store.Observe(beforeScan);

        store.StartScan();
        using CapturedFrame first = Frame(2, 200);
        using CapturedFrame tooSoon = Frame(3, 250);
        using CapturedFrame second = Frame(4, 300);
        store.Observe(first);
        store.Observe(tooSoon);
        store.Observe(second);

        IReadOnlyList<Maple.Cloud.BailianMapImage> images = await store.ReadAsync("forest-east", [4, 2], CancellationToken.None);

        Assert.Equal([2L, 4L], encoder.EncodedFrameIds);
        Assert.Equal([4L, 2L], images.Select(image => image.FrameId));
        Assert.All(images, image => Assert.Equal("image/png", image.MediaType));
    }

    [Fact]
    public async Task NewScanInvalidatesOldFramesAndMapBinding()
    {
        using var store = new MapScanFrameStore(new RecordingEncoder(), minimumFrameIntervalMs: 1, capacity: 3);
        store.StartScan();
        using CapturedFrame oldFrame = Frame(10, 100);
        store.Observe(oldFrame);
        _ = await store.ReadAsync("forest-east", [10], CancellationToken.None);

        store.StartScan();

        MapFrameSourceException missing = await Assert.ThrowsAsync<MapFrameSourceException>(
            async () => await store.ReadAsync("forest-east", [10], CancellationToken.None));
        Assert.Equal("MAP_FRAME_MISSING", missing.Code);

        using CapturedFrame newFrame = Frame(11, 200);
        store.Observe(newFrame);
        _ = await store.ReadAsync("forest-west", [11], CancellationToken.None);
        MapFrameSourceException mismatch = await Assert.ThrowsAsync<MapFrameSourceException>(
            async () => await store.ReadAsync("forest-east", [11], CancellationToken.None));
        Assert.Equal("MAP_FRAME_MAP_MISMATCH", mismatch.Code);
    }

    [Fact]
    public async Task RejectsDuplicateFrameIdsAndEvictsOldestFrame()
    {
        using var store = new MapScanFrameStore(new RecordingEncoder(), minimumFrameIntervalMs: 1, capacity: 2);
        store.StartScan();
        using CapturedFrame first = Frame(1, 100);
        using CapturedFrame second = Frame(2, 101);
        using CapturedFrame third = Frame(3, 102);
        store.Observe(first);
        store.Observe(second);
        store.Observe(third);

        MapFrameSourceException duplicate = await Assert.ThrowsAsync<MapFrameSourceException>(
            async () => await store.ReadAsync("forest", [2, 2], CancellationToken.None));
        Assert.Equal("MAP_FRAME_IDS_INVALID", duplicate.Code);
        MapFrameSourceException evicted = await Assert.ThrowsAsync<MapFrameSourceException>(
            async () => await store.ReadAsync("forest", [1], CancellationToken.None));
        Assert.Equal("MAP_FRAME_MISSING", evicted.Code);
    }

    private static CapturedFrame Frame(long frameId, long capturedAtMonoMs)
    {
        const int width = 4;
        const int height = 3;
        const int stride = width * 4;
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(stride * height);
        owner.Memory.Span[..(stride * height)].Fill((byte)frameId);
        return new CapturedFrame(
            new CaptureFrameMetadata
            {
                SchemaVersion = ContractConstants.SchemaVersion,
                FrameId = frameId,
                CapturedAtMonoMs = capturedAtMonoMs,
                ClientWidth = width,
                ClientHeight = height,
                Dpi = 96,
                CaptureBackend = CaptureBackend.Wgc,
                DroppedReason = DroppedFrameReason.None,
            },
            width,
            height,
            stride,
            CapturedPixelFormat.Bgra32,
            owner,
            stride * height);
    }

    private sealed class RecordingEncoder : IMapFrameEncoder
    {
        public List<long> EncodedFrameIds { get; } = [];

        public byte[] EncodePng(CapturedFrame frame)
        {
            EncodedFrameIds.Add(frame.Metadata.FrameId);
            return [137, 80, 78, 71, (byte)frame.Metadata.FrameId];
        }
    }
}
