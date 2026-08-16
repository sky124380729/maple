using Maple.Capture;
using Maple.Contracts;

namespace Maple.Vision;

public static class AdaptiveHudLayout
{
    public static HudLayout Resolve(int width, int height)
    {
        if (width < 800 || height < 600) throw new ArgumentOutOfRangeException(nameof(width), "HUD_RESOLUTION_UNSUPPORTED");
        return new HudLayout(
            Region(width, height, 0.367, 0.966, 0.077, 0.021),
            Region(width, height, 0.447, 0.966, 0.078, 0.021),
            Region(width, height, 0.035, 0.035, 0.100, 0.065),
            Region(width, height, 0.045, 0.085, 0.090, 0.130),
            Region(width, height, 0.355, 0.950, 0.095, 0.021),
            Region(width, height, 0.438, 0.950, 0.095, 0.021));
    }

    private static PixelRegion Region(int width, int height, double x, double y, double w, double h)
    {
        int left = Math.Clamp((int)Math.Round(width * x), 0, width - 1);
        int top = Math.Clamp((int)Math.Round(height * y), 0, height - 1);
        int regionWidth = Math.Clamp((int)Math.Round(width * w), 1, width - left);
        int regionHeight = Math.Clamp((int)Math.Round(height * h), 1, height - top);
        return new PixelRegion(left, top, regionWidth, regionHeight);
    }
}

public sealed class AdaptiveFixedUiVisionProvider : IFixedUiVisionProvider
{
    private const int ResourceOcrIntervalMs = 500;
    private readonly OcrTextRecognizer ocr;
    private readonly StableVisualMapIdentityTracker mapIdentity = new();
    private ResourceNumbers? lastHpNumbers;
    private ResourceNumbers? lastMpNumbers;
    private long lastResourceNumbersAtMonoMs;
    private long lastResourceOcrAtMonoMs = long.MinValue;

    public AdaptiveFixedUiVisionProvider(IOcrEngine? ocr = null, IOcrEngine? resourceOcr = null) =>
        this.ocr = new OcrTextRecognizer(ocr ?? EmptyOcrEngine.Instance, resourceOcr);

    public async ValueTask<FixedUiVisionResult> ObserveFixedUiAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        HudLayout layout;
        try { layout = AdaptiveHudLayout.Resolve(frame.Width, frame.Height); }
        catch (ArgumentOutOfRangeException)
        {
            return Unavailable(frame);
        }

        long capturedAtMonoMs = frame.Metadata.CapturedAtMonoMs;
        bool readResourceNumbers = lastResourceOcrAtMonoMs == long.MinValue
            || capturedAtMonoMs - lastResourceOcrAtMonoMs >= ResourceOcrIntervalMs;
        if (readResourceNumbers) lastResourceOcrAtMonoMs = capturedAtMonoMs;
        HudLayout activeLayout = readResourceNumbers ? layout : layout with { HpText = null, MpText = null };
        FixedUiVisionResult recognized = await new OpenCvHudRecognizer(activeLayout, ocr)
            .ObserveFixedUiAsync(frame, cancellationToken)
            .ConfigureAwait(false);
        StabilizeResourceNumbers(recognized, capturedAtMonoMs);
        if (!ContainsMapText(recognized.Map.MapId))
        {
            VisualMapIdentity identity = layout.Minimap is { } minimap
                ? mapIdentity.Update(VisualMapFingerprint.Compute(frame, minimap))
                : new VisualMapIdentity("unknown", 0, false, 128);
            recognized.Map.MapId = identity.Ready ? identity.MapId : "unknown";
            recognized.Map.Confidence = identity.Ready ? identity.Confidence : 0;
        }
        double hp = recognized.HpCandidates.FirstOrDefault()?.Value ?? 0;
        double mp = recognized.MpCandidates.FirstOrDefault()?.Value ?? 0;
        if (hp <= 0 && mp <= 0) return Unavailable(frame);
        return recognized;
    }

    private void StabilizeResourceNumbers(FixedUiVisionResult result, long nowMonoMs)
    {
        ResourceObservation? hp = result.HpCandidates.FirstOrDefault();
        ResourceObservation? mp = result.MpCandidates.FirstOrDefault();
        bool updated = false;
        if (hp?.CurrentValue is double hpCurrent && hp.MaximumValue is double hpMaximum)
        {
            lastHpNumbers = new ResourceNumbers(hpCurrent, hpMaximum);
            updated = true;
        }
        if (mp?.CurrentValue is double mpCurrent && mp.MaximumValue is double mpMaximum)
        {
            lastMpNumbers = new ResourceNumbers(mpCurrent, mpMaximum);
            updated = true;
        }
        if (updated) lastResourceNumbersAtMonoMs = nowMonoMs;
        if (nowMonoMs - lastResourceNumbersAtMonoMs > 5_000) return;
        ApplyNumbers(hp, lastHpNumbers);
        ApplyNumbers(mp, lastMpNumbers);
    }

    private static void ApplyNumbers(ResourceObservation? observation, ResourceNumbers? numbers)
    {
        if (observation is null || observation.CurrentValue.HasValue || numbers is null) return;
        observation.CurrentValue = numbers.Value.Current;
        observation.MaximumValue = numbers.Value.Maximum;
    }

    public static bool ContainsMapText(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Any(character => character is >= '\u3400' and <= '\u9fff');

    private static FixedUiVisionResult Unavailable(CapturedFrame frame)
    {
        long freshUntil = frame.Metadata.CapturedAtMonoMs + 120;
        return new FixedUiVisionResult
        {
            FrameId = frame.Metadata.FrameId,
            HpCandidates = [],
            MpCandidates = [],
            Loot = new LootObservation { Visible = false, Confidence = 0, FreshUntilMonoMs = freshUntil },
            Map = new MapObservation { MapId = "unknown", State = MapArchiveState.Candidate, Confidence = 0, FreshUntilMonoMs = freshUntil },
        };
    }

    private sealed class EmptyOcrEngine : IOcrEngine
    {
        public static EmptyOcrEngine Instance { get; } = new();
        public ValueTask<string> RecognizeAsync(ReadOnlyMemory<byte> encodedPng, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(string.Empty);
        }
    }
}
