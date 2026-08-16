using Maple.Capture;

namespace Maple.Vision;

public sealed record FrameCameraTransform(long FrameId, double OffsetX, double OffsetY, double Confidence, bool Ready, string Diagnostic);

public sealed class CameraTransformTracker
{
    private const int Downsample = 4;
    private readonly object sync = new();
    private readonly Dictionary<long, FrameCameraTransform> transforms = [];
    private byte[]? previous;
    private int previousWidth;
    private int previousHeight;
    private double offsetX;
    private double offsetY;

    public FrameCameraTransform Track(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        byte[] gray = DownsampleGray(frame, out int width, out int height);
        lock (sync)
        {
            FrameCameraTransform result;
            if (previous is null || width != previousWidth || height != previousHeight)
            {
                offsetX = 0;
                offsetY = 0;
                result = new(frame.Metadata.FrameId, offsetX, offsetY, 1, true, "CAMERA_ORIGIN");
            }
            else
            {
                TranslationEstimate estimate = Estimate(previous, gray, width, height);
                if (estimate.Ready)
                {
                    offsetX -= estimate.ScreenDx * Downsample;
                    offsetY -= estimate.ScreenDy * Downsample;
                    result = new(frame.Metadata.FrameId, offsetX, offsetY, estimate.Confidence, true, "OK");
                }
                else
                {
                    result = new(frame.Metadata.FrameId, offsetX, offsetY, estimate.Confidence, false, estimate.Diagnostic);
                }
            }
            previous = gray;
            previousWidth = width;
            previousHeight = height;
            transforms[frame.Metadata.FrameId] = result;
            while (transforms.Count > 32) transforms.Remove(transforms.Keys.Min());
            return result;
        }
    }

    public bool TryGet(long frameId, out FrameCameraTransform? transform)
    {
        lock (sync) return transforms.TryGetValue(frameId, out transform);
    }

    public void Reset()
    {
        lock (sync)
        {
            previous = null;
            transforms.Clear();
            previousWidth = previousHeight = 0;
            offsetX = offsetY = 0;
        }
    }

    private static byte[] DownsampleGray(CapturedFrame frame, out int width, out int height)
    {
        width = Math.Max(1, frame.Width / Downsample);
        height = Math.Max(1, frame.Height / Downsample);
        byte[] result = new byte[width * height];
        ReadOnlySpan<byte> pixels = frame.Pixels.Span;
        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(frame.Height - 1, y * Downsample);
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(frame.Width - 1, x * Downsample);
                int source = sourceY * frame.Stride + sourceX * (frame.PixelFormat == CapturedPixelFormat.Gray8 ? 1 : 4);
                result[y * width + x] = frame.PixelFormat == CapturedPixelFormat.Gray8
                    ? pixels[source]
                    : (byte)((pixels[source] * 29 + pixels[source + 1] * 150 + pixels[source + 2] * 77) >> 8);
            }
        }
        return result;
    }

    private static TranslationEstimate Estimate(byte[] previous, byte[] current, int width, int height)
    {
        int left = Math.Max(2, width / 10);
        int right = Math.Min(width - 2, width * 9 / 10);
        int top = Math.Max(2, height / 10);
        int bottom = Math.Min(height - 2, height * 4 / 5);
        if (right - left < 16 || bottom - top < 12) return TranslationEstimate.Failed("CAMERA_FRAME_TOO_SMALL");

        double mean = 0;
        double square = 0;
        int textureSamples = 0;
        for (int y = top; y < bottom; y += 3)
        for (int x = left; x < right; x += 3)
        {
            double value = current[y * width + x];
            mean += value;
            square += value * value;
            textureSamples++;
        }
        mean /= Math.Max(1, textureSamples);
        double variance = square / Math.Max(1, textureSamples) - mean * mean;
        if (variance < 80) return TranslationEstimate.Failed("CAMERA_TEXTURE_INSUFFICIENT");

        double best = double.MaxValue;
        double second = double.MaxValue;
        int bestDx = 0;
        int bestDy = 0;
        const int maxDx = 12;
        const int maxDy = 6;
        for (int dy = -maxDy; dy <= maxDy; dy++)
        for (int dx = -maxDx; dx <= maxDx; dx++)
        {
            long sad = 0;
            int count = 0;
            int x0 = Math.Max(left, left + dx);
            int x1 = Math.Min(right, right + dx);
            int y0 = Math.Max(top, top + dy);
            int y1 = Math.Min(bottom, bottom + dy);
            for (int y = y0; y < y1; y += 2)
            for (int x = x0; x < x1; x += 2)
            {
                sad += Math.Abs(current[y * width + x] - previous[(y - dy) * width + (x - dx)]);
                count++;
            }
            double score = count == 0 ? double.MaxValue : sad / (double)count;
            if (score < best)
            {
                second = best;
                best = score;
                bestDx = dx;
                bestDy = dy;
            }
            else if (score < second) second = score;
        }
        double separation = second <= 0 || double.IsInfinity(second) ? 0 : Math.Clamp((second - best) / second, 0, 1);
        double quality = Math.Clamp(1 - best / 48.0, 0, 1);
        double confidence = quality * 0.7 + separation * 0.3;
        return confidence < 0.55
            ? TranslationEstimate.Failed("CAMERA_MATCH_LOW_CONFIDENCE", confidence)
            : new(bestDx, bestDy, confidence, true, "OK");
    }

    private sealed record TranslationEstimate(int ScreenDx, int ScreenDy, double Confidence, bool Ready, string Diagnostic)
    {
        public static TranslationEstimate Failed(string diagnostic, double confidence = 0) => new(0, 0, confidence, false, diagnostic);
    }
}
