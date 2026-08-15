using System.Buffers;
using Maple.Capture;
using Maple.Contracts;
using Xunit;

namespace Maple.Host.Tests;

public sealed class LatestVisionFrameQueueTests
{
    [Fact]
    public void ObserveOwnsCopyAndKeepsOnlyLatestFrame()
    {
        using var queue = new LatestVisionFrameQueue(capacity: 1);
        using CapturedFrame first = Frame(1, 11);
        queue.Observe(first);
        first.Dispose();
        using CapturedFrame second = Frame(2, 22);
        queue.Observe(second);

        using CapturedFrame read = queue.TakeLatest(CancellationToken.None);

        Assert.Equal(2, read.Metadata.FrameId);
        Assert.Equal(22, read.Pixels.Span[0]);
        Assert.Equal(1, queue.DroppedFrames);
    }

    [Fact]
    public async Task DisposeReleasesQueuedCopiesAndRejectsNewFrames()
    {
        var queue = new LatestVisionFrameQueue(capacity: 1);
        using CapturedFrame source = Frame(7, 70);
        queue.Observe(source);

        queue.Dispose();

        Assert.Throws<ObjectDisposedException>(() => queue.Observe(source));
        Assert.False(await queue.WaitToReadAsync(CancellationToken.None));
    }

    internal static CapturedFrame Frame(long frameId, byte value)
    {
        const int width = 2, height = 2, stride = 8, length = 16;
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(length);
        owner.Memory.Span[..length].Fill(value);
        return new CapturedFrame(new CaptureFrameMetadata
        {
            SchemaVersion = 2, FrameId = frameId, CapturedAtMonoMs = 1000 + frameId,
            ClientWidth = width, ClientHeight = height, Dpi = 96, CaptureBackend = CaptureBackend.Wgc,
            DroppedReason = DroppedFrameReason.None,
        }, width, height, stride, CapturedPixelFormat.Bgra32, owner, length);
    }
}
