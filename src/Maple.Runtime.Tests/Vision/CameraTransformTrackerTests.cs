using System.Buffers;
using Maple.Capture;
using Maple.Contracts;
using Maple.Vision;
using Xunit;

namespace Maple.Runtime.Tests.Vision;

public sealed class CameraTransformTrackerTests
{
    [Fact]
    public void TracksScreenTranslationAsOppositeMapWorldCameraOffset()
    {
        const int width = 320;
        const int height = 180;
        byte[] baseline = Texture(width, height);
        byte[] shifted = Shift(baseline, width, height, screenDx: -12, screenDy: 8);
        var tracker = new CameraTransformTracker();

        using CapturedFrame first = Frame(1, baseline, width, height);
        using CapturedFrame second = Frame(2, shifted, width, height);
        FrameCameraTransform initial = tracker.Track(first);
        FrameCameraTransform result = tracker.Track(second);

        Assert.True(initial.Ready);
        Assert.Equal("CAMERA_ORIGIN", initial.Diagnostic);
        Assert.True(result.Ready, result.Diagnostic);
        Assert.InRange(result.OffsetX, 8, 16);
        Assert.InRange(result.OffsetY, -12, -4);
        Assert.True(result.Confidence >= 0.55);
        Assert.True(tracker.TryGet(2, out FrameCameraTransform? stored));
        Assert.Equal(result, stored);
    }

    [Fact]
    public void RejectsFlatFramesInsteadOfInventingMotion()
    {
        byte[] flat = Enumerable.Repeat((byte)80, 320 * 180 * 4).ToArray();
        var tracker = new CameraTransformTracker();
        using CapturedFrame first = Frame(1, flat, 320, 180);
        using CapturedFrame second = Frame(2, flat, 320, 180);

        tracker.Track(first);
        FrameCameraTransform result = tracker.Track(second);

        Assert.False(result.Ready);
        Assert.Equal("CAMERA_TEXTURE_INSUFFICIENT", result.Diagnostic);
    }

    private static byte[] Texture(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte value = (byte)((x * 17 + y * 31 + (x * y % 97)) & 255);
            int offset = (y * width + x) * 4;
            pixels[offset] = value;
            pixels[offset + 1] = (byte)(value ^ 0x5a);
            pixels[offset + 2] = (byte)(255 - value);
            pixels[offset + 3] = 255;
        }
        return pixels;
    }

    private static byte[] Shift(byte[] source, int width, int height, int screenDx, int screenDy)
    {
        byte[] shifted = new byte[source.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int sourceX = x - screenDx;
            int sourceY = y - screenDy;
            int destination = (y * width + x) * 4;
            if (sourceX < 0 || sourceX >= width || sourceY < 0 || sourceY >= height) continue;
            Buffer.BlockCopy(source, (sourceY * width + sourceX) * 4, shifted, destination, 4);
        }
        return shifted;
    }

    private static CapturedFrame Frame(long frameId, byte[] pixels, int width, int height)
    {
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(pixels.Length);
        pixels.CopyTo(owner.Memory.Span);
        return new CapturedFrame(new CaptureFrameMetadata
        {
            SchemaVersion = 2,
            FrameId = frameId,
            CapturedAtMonoMs = frameId * 100,
            ClientWidth = width,
            ClientHeight = height,
            Dpi = 96,
            CaptureBackend = CaptureBackend.Wgc,
            DroppedReason = DroppedFrameReason.None,
        }, width, height, width * 4, CapturedPixelFormat.Bgra32, owner, pixels.Length);
    }
}
