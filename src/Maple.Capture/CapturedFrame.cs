using System.Buffers;
using Maple.Contracts;

namespace Maple.Capture;

public enum CapturedPixelFormat
{
    Bgra32,
    Rgba32,
    Gray8,
}

public sealed class CapturedFrame : IDisposable
{
    private IMemoryOwner<byte>? owner;
    private readonly int length;

    public CapturedFrame(
        CaptureFrameMetadata metadata,
        int width,
        int height,
        int stride,
        CapturedPixelFormat pixelFormat,
        IMemoryOwner<byte> owner,
        int length)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(owner);
        int bytesPerPixel = pixelFormat == CapturedPixelFormat.Gray8 ? 1 : 4;
        if (width <= 0 || height <= 0 || stride < width * bytesPerPixel) throw new ArgumentOutOfRangeException(nameof(stride));
        if (length < stride * height || length > owner.Memory.Length) throw new ArgumentOutOfRangeException(nameof(length));
        Metadata = metadata;
        Width = width;
        Height = height;
        Stride = stride;
        PixelFormat = pixelFormat;
        this.owner = owner;
        this.length = length;
    }

    public CaptureFrameMetadata Metadata { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public CapturedPixelFormat PixelFormat { get; }
    public ReadOnlyMemory<byte> Pixels => (owner ?? throw new ObjectDisposedException(nameof(CapturedFrame))).Memory[..length];

    public void Dispose()
    {
        Interlocked.Exchange(ref owner, null)?.Dispose();
    }
}
