using System.Buffers;
using System.Drawing.Imaging;
using Maple.Capture;

namespace Maple.Host;

public sealed class WindowsPngMapFrameEncoder : IMapFrameEncoder
{
    public unsafe byte[] EncodePng(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.PixelFormat != CapturedPixelFormat.Bgra32)
            throw new NotSupportedException("Map frame encoding requires BGRA32 pixels");

        using var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, frame.Width, frame.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            using MemoryHandle source = frame.Pixels.Pin();
            int rowBytes = checked(frame.Width * 4);
            for (int row = 0; row < frame.Height; row++)
            {
                byte* sourceRow = (byte*)source.Pointer + (row * frame.Stride);
                byte* destinationRow = (byte*)data.Scan0 + (row * data.Stride);
                Buffer.MemoryCopy(sourceRow, destinationRow, Math.Abs(data.Stride), rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }
}
