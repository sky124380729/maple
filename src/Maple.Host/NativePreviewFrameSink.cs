using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using Maple.Capture;
using Maple.Preview;

namespace Maple.Host;

public sealed class NativePreviewFrameSink(NativePreviewSurface surface) : ICaptureFrameSink
{
    private readonly NativePreviewSurface surface = surface ?? throw new ArgumentNullException(nameof(surface));

    public unsafe void Publish(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Bitmap? bitmap = null;
        try
        {
            if (frame.PixelFormat != CapturedPixelFormat.Bgra32)
                throw new NotSupportedException("Native preview requires BGRA32 frames");
            bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppPArgb);
            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, frame.Width, frame.Height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppPArgb);
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
            finally { bitmap.UnlockBits(data); }

            Bitmap published = bitmap;
            long capturedAtMonoMs = frame.Metadata.CapturedAtMonoMs;
            bitmap = null;
            if (surface.IsDisposed)
            {
                published.Dispose();
                return;
            }
            if (surface.InvokeRequired)
            {
                surface.BeginInvoke(() => PublishOnUiThread(published, capturedAtMonoMs));
            }
            else
            {
                PublishOnUiThread(published, capturedAtMonoMs);
            }
        }
        finally
        {
            bitmap?.Dispose();
            frame.Dispose();
        }
    }

    private void PublishOnUiThread(Bitmap bitmap, long capturedAtMonoMs)
    {
        if (surface.IsDisposed) { bitmap.Dispose(); return; }
        surface.PublishFrame(bitmap, capturedAtMonoMs);
    }
}
