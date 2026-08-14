using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Maple.Contracts;

namespace Maple.Capture
{
    public sealed class BitBltCaptureBackend : ICaptureBackend
    {
        public CaptureBackend Backend { get { return CaptureBackend.BitBlt; } }

        public CaptureResult Capture(CaptureTarget target, long frameId, long nowMonoMs)
        {
            string targetError = CaptureValidation.ValidateTarget(target);
            if (targetError != null) return new CaptureResult { Success = false, Reason = targetError, Metadata = CaptureValidation.Metadata(target, frameId, nowMonoMs, Backend, 0, DroppedFrameReason.Invalid) };
            var stopwatch = Stopwatch.StartNew();
            Bitmap frame = null;
            try
            {
                Rectangle bounds = target.ClientScreenBounds;
                frame = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(frame)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                stopwatch.Stop();
                return new CaptureResult { Success = true, Frame = frame, Reason = "OK", Metadata = CaptureValidation.Metadata(target, frameId, nowMonoMs, Backend, stopwatch.Elapsed.TotalMilliseconds, DroppedFrameReason.None) };
            }
            catch (Exception exception)
            {
                if (frame != null) frame.Dispose();
                stopwatch.Stop();
                return new CaptureResult { Success = false, Reason = "BITBLT_CAPTURE_FAILED:" + exception.GetType().Name, Metadata = CaptureValidation.Metadata(target, frameId, nowMonoMs, Backend, stopwatch.Elapsed.TotalMilliseconds, DroppedFrameReason.Invalid) };
            }
        }
    }
}
