using System.Numerics;
using Maple.Capture;
using OpenCvSharp;

namespace Maple.Vision;

public readonly record struct VisualMapFingerprint(ulong High, ulong Low)
{
    private const int HashWidth = 17;
    private const int HashHeight = 8;

    public string MapId => $"visual-{High:x16}{Low:x16}";

    public int DistanceTo(VisualMapFingerprint other) =>
        BitOperations.PopCount(High ^ other.High) + BitOperations.PopCount(Low ^ other.Low);

    public static VisualMapFingerprint Compute(CapturedFrame frame, PixelRegion region)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateRegion(frame, region);
        MatType sourceType = frame.PixelFormat == CapturedPixelFormat.Gray8 ? MatType.CV_8UC1 : MatType.CV_8UC4;
        using Mat source = Mat.FromPixelData(frame.Height, frame.Width, sourceType, frame.Pixels.ToArray(), frame.Stride);
        using Mat crop = new(source, region.ToRect());
        using Mat gray = new();
        if (frame.PixelFormat == CapturedPixelFormat.Gray8) crop.CopyTo(gray);
        else Cv2.CvtColor(crop, gray, frame.PixelFormat == CapturedPixelFormat.Rgba32 ? ColorConversionCodes.RGBA2GRAY : ColorConversionCodes.BGRA2GRAY);
        using Mat blurred = new();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
        using Mat reduced = new();
        Cv2.Resize(blurred, reduced, new Size(HashWidth, HashHeight), 0, 0, InterpolationFlags.Area);

        ulong high = 0;
        ulong low = 0;
        int bit = 0;
        for (int y = 0; y < HashHeight; y++)
        for (int x = 0; x < HashWidth - 1; x++, bit++)
        {
            bool set = reduced.At<byte>(y, x) > reduced.At<byte>(y, x + 1);
            if (!set) continue;
            if (bit < 64) high |= 1UL << bit;
            else low |= 1UL << (bit - 64);
        }
        return new VisualMapFingerprint(high, low);
    }

    private static void ValidateRegion(CapturedFrame frame, PixelRegion region)
    {
        if (region.Width < HashWidth || region.Height < HashHeight || region.X < 0 || region.Y < 0
            || region.X + region.Width > frame.Width || region.Y + region.Height > frame.Height)
            throw new ArgumentOutOfRangeException(nameof(region), "MINIMAP_REGION_INVALID");
    }
}

public readonly record struct VisualMapIdentity(string MapId, double Confidence, bool Ready, int Distance);

public sealed class StableVisualMapIdentityTracker
{
    private readonly int requiredStableFrames;
    private readonly int maximumDistance;
    private VisualMapFingerprint? active;
    private VisualMapFingerprint? candidate;
    private int stableFrames;

    public StableVisualMapIdentityTracker(int requiredStableFrames = 3, int maximumDistance = 12)
    {
        if (requiredStableFrames < 2) throw new ArgumentOutOfRangeException(nameof(requiredStableFrames));
        if (maximumDistance is < 0 or > 64) throw new ArgumentOutOfRangeException(nameof(maximumDistance));
        this.requiredStableFrames = requiredStableFrames;
        this.maximumDistance = maximumDistance;
    }

    public VisualMapIdentity Update(VisualMapFingerprint fingerprint)
    {
        if (active is { } current)
        {
            int activeDistance = current.DistanceTo(fingerprint);
            if (activeDistance <= maximumDistance)
            {
                candidate = null;
                stableFrames = requiredStableFrames;
                return Ready(current, activeDistance);
            }
        }

        int candidateDistance = candidate?.DistanceTo(fingerprint) ?? int.MaxValue;
        if (candidate is null || candidateDistance > maximumDistance)
        {
            candidate = fingerprint;
            stableFrames = 1;
        }
        else
        {
            stableFrames++;
        }

        if (stableFrames < requiredStableFrames)
            return new VisualMapIdentity("unknown", stableFrames / (double)requiredStableFrames, false, candidateDistance == int.MaxValue ? 128 : candidateDistance);

        active = candidate;
        candidate = null;
        return Ready(active.Value, Math.Min(maximumDistance, candidateDistance));
    }

    public void Reset()
    {
        active = null;
        candidate = null;
        stableFrames = 0;
    }

    private VisualMapIdentity Ready(VisualMapFingerprint fingerprint, int distance)
    {
        double confidence = Math.Clamp(1 - distance / (double)Math.Max(1, maximumDistance * 2), 0.75, 0.98);
        return new VisualMapIdentity(fingerprint.MapId, confidence, true, distance);
    }
}
