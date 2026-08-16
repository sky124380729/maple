using System.Buffers;
using Maple.Capture;
using Maple.Contracts;
using Maple.Vision;
using Xunit;

namespace Maple.Runtime.Tests.Vision;

public sealed class VisualMapFingerprintTests
{
    [Fact]
    public void SmallDynamicMarkerChangesKeepTheSameStructuralIdentity()
    {
        using CapturedFrame baseline = MapFrame(markerX: 24, alternateTopology: false);
        using CapturedFrame changedMarker = MapFrame(markerX: 96, alternateTopology: false);

        VisualMapFingerprint first = VisualMapFingerprint.Compute(baseline, new PixelRegion(0, 0, 128, 80));
        VisualMapFingerprint second = VisualMapFingerprint.Compute(changedMarker, new PixelRegion(0, 0, 128, 80));

        Assert.InRange(first.DistanceTo(second), 0, 12);
    }

    [Fact]
    public void DifferentPlatformTopologyProducesADifferentIdentity()
    {
        using CapturedFrame baseline = MapFrame(markerX: 24, alternateTopology: false);
        using CapturedFrame different = MapFrame(markerX: 24, alternateTopology: true);

        VisualMapFingerprint first = VisualMapFingerprint.Compute(baseline, new PixelRegion(0, 0, 128, 80));
        VisualMapFingerprint second = VisualMapFingerprint.Compute(different, new PixelRegion(0, 0, 128, 80));

        Assert.True(first.DistanceTo(second) > 12);
    }

    [Fact]
    public void TrackerRequiresThreeConsistentFramesBeforePublishingIdentity()
    {
        using CapturedFrame firstFrame = MapFrame(markerX: 24, alternateTopology: false);
        using CapturedFrame secondFrame = MapFrame(markerX: 96, alternateTopology: false);
        VisualMapFingerprint first = VisualMapFingerprint.Compute(firstFrame, new PixelRegion(0, 0, 128, 80));
        VisualMapFingerprint second = VisualMapFingerprint.Compute(secondFrame, new PixelRegion(0, 0, 128, 80));
        var tracker = new StableVisualMapIdentityTracker(requiredStableFrames: 3, maximumDistance: 12);

        VisualMapIdentity pending1 = tracker.Update(first);
        VisualMapIdentity pending2 = tracker.Update(second);
        VisualMapIdentity ready = tracker.Update(first);

        Assert.False(pending1.Ready);
        Assert.False(pending2.Ready);
        Assert.True(ready.Ready);
        Assert.StartsWith("visual-", ready.MapId, StringComparison.Ordinal);
        Assert.InRange(ready.Confidence, 0.75, 1);
    }

    private static CapturedFrame MapFrame(int markerX, bool alternateTopology)
    {
        const int width = 128;
        const int height = 80;
        byte[] pixels = new byte[width * height * 4];
        DrawRect(pixels, width, 8, 14, alternateTopology ? 48 : 96, 8, 220);
        DrawRect(pixels, width, alternateTopology ? 70 : 20, 42, alternateTopology ? 48 : 82, 8, 180);
        DrawRect(pixels, width, alternateTopology ? 18 : 82, 20, 6, 42, 150);
        DrawRect(pixels, width, markerX, 31, 4, 4, 255);
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(pixels.Length);
        pixels.CopyTo(owner.Memory.Span);
        return new CapturedFrame(new CaptureFrameMetadata
        {
            SchemaVersion = 2,
            FrameId = markerX,
            CapturedAtMonoMs = markerX,
            ClientWidth = width,
            ClientHeight = height,
            Dpi = 96,
            CaptureBackend = CaptureBackend.Wgc,
            DroppedReason = DroppedFrameReason.None,
        }, width, height, width * 4, CapturedPixelFormat.Bgra32, owner, pixels.Length);
    }

    private static void DrawRect(byte[] pixels, int strideWidth, int x, int y, int width, int height, byte value)
    {
        for (int row = y; row < y + height; row++)
        for (int column = x; column < x + width; column++)
        {
            int offset = (row * strideWidth + column) * 4;
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
    }
}
