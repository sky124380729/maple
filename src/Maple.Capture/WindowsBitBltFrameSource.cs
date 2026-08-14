using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Maple.Contracts;

namespace Maple.Capture;

public sealed class WindowsBitBltFrameSource : IWindowFrameSource
{
    private const uint SourceCopy = 0x00CC0020;
    private const uint CaptureLayeredWindows = 0x40000000;
    private const uint DibRgbColors = 0;

    public ValueTask<CapturedFrame?> TryCaptureAsync(
        CaptureTarget target,
        long frameId,
        long nowMonoMs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return ValueTask.FromResult<CapturedFrame?>(null);
        string? validation = CaptureValidation.ValidateTarget(target);
        if (validation is not null) return ValueTask.FromResult<CapturedFrame?>(null);
        return ValueTask.FromResult(Capture(target, frameId, nowMonoMs));
    }

    private static unsafe CapturedFrame? Capture(CaptureTarget target, long frameId, long nowMonoMs)
    {
        int width = target.ClientWidth;
        int height = target.ClientHeight;
        int stride = checked(width * 4);
        int length = checked(stride * height);
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(length);
        nint screenDc = nint.Zero;
        nint memoryDc = nint.Zero;
        nint bitmap = nint.Zero;
        nint previous = nint.Zero;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            screenDc = GetDC(nint.Zero);
            if (screenDc == nint.Zero) return null;
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == nint.Zero) return null;
            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (bitmap == nint.Zero) return null;
            previous = SelectObject(memoryDc, bitmap);
            if (previous == nint.Zero) return null;
            if (!BitBlt(
                    memoryDc,
                    0,
                    0,
                    width,
                    height,
                    screenDc,
                    target.ClientLeft,
                    target.ClientTop,
                    SourceCopy | CaptureLayeredWindows))
            {
                return null;
            }

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = unchecked((uint)Marshal.SizeOf<BitmapInfoHeader>()),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = unchecked((uint)length),
                },
            };
            using MemoryHandle handle = owner.Memory.Pin();
            int lines = GetDIBits(
                memoryDc,
                bitmap,
                0,
                unchecked((uint)height),
                (nint)handle.Pointer,
                ref info,
                DibRgbColors);
            if (lines != height) return null;

            stopwatch.Stop();
            var metadata = CaptureValidation.Metadata(
                target,
                frameId,
                nowMonoMs,
                CaptureBackend.BitBlt,
                DroppedFrameReason.None,
                stopwatch.Elapsed.TotalMilliseconds);
            CapturedFrame frame = new(metadata, width, height, stride, CapturedPixelFormat.Bgra32, owner, length);
            owner = null!;
            return frame;
        }
        finally
        {
            if (previous != nint.Zero && memoryDc != nint.Zero) _ = SelectObject(memoryDc, previous);
            if (bitmap != nint.Zero) _ = DeleteObject(bitmap);
            if (memoryDc != nint.Zero) _ = DeleteDC(memoryDc);
            if (screenDc != nint.Zero) _ = ReleaseDC(nint.Zero, screenDc);
            owner?.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        nint destination,
        int xDestination,
        int yDestination,
        int width,
        int height,
        nint source,
        int xSource,
        int ySource,
        uint operation);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        nint dc,
        nint bitmap,
        uint firstScan,
        uint scanLines,
        nint bits,
        ref BitmapInfo info,
        uint usage);
}
