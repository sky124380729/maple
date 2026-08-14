using OpenCvSharp;
using Maple.Capture;
using Maple.Contracts;

namespace Maple.Vision;

public readonly record struct PixelRegion(int X, int Y, int Width, int Height)
{
    public Rect ToRect() => new(X, Y, Width, Height);
}

public sealed record HudLayout(PixelRegion Hp, PixelRegion Mp, PixelRegion MapName);

public sealed class OpenCvHudRecognizer(HudLayout layout, OcrTextRecognizer ocr)
    : IFixedUiVisionProvider
{
    private readonly HudLayout layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly OcrTextRecognizer ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));

    public async ValueTask<FixedUiVisionResult> ObserveFixedUiAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        using Mat source = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC4, frame.Pixels.ToArray(), frame.Stride);
        double hp = FillRatio(source, layout.Hp, new Scalar(0, 0, 120, 200), new Scalar(110, 130, 255, 255));
        double mp = FillRatio(source, layout.Mp, new Scalar(120, 0, 0, 200), new Scalar(255, 180, 120, 255));
        string mapName = await ocr.RecognizeAsync(frame, layout.MapName, cancellationToken).ConfigureAwait(false);
        long freshUntil = frame.Metadata.CapturedAtMonoMs + 120;
        return new FixedUiVisionResult
        {
            FrameId = frame.Metadata.FrameId,
            HpCandidates = [new ResourceObservation { Mode = ResourceMode.Percent, Value = hp, Confidence = hp > 0 ? 0.95 : 0.7, FreshUntilMonoMs = freshUntil }],
            MpCandidates = [new ResourceObservation { Mode = ResourceMode.Percent, Value = mp, Confidence = mp > 0 ? 0.95 : 0.7, FreshUntilMonoMs = freshUntil }],
            Loot = new LootObservation { Visible = false, Confidence = 0.9, FreshUntilMonoMs = freshUntil },
            Map = new MapObservation { MapId = mapName.Length == 0 ? "unknown" : mapName, State = MapArchiveState.Candidate, Confidence = mapName.Length == 0 ? 0 : 0.9, FreshUntilMonoMs = freshUntil },
        };
    }

    private static double FillRatio(Mat source, PixelRegion region, Scalar lower, Scalar upper)
    {
        using Mat roi = new(source, region.ToRect());
        using Mat mask = new();
        Cv2.InRange(roi, lower, upper, mask);
        return (double)Cv2.CountNonZero(mask) / (region.Width * region.Height);
    }
}
