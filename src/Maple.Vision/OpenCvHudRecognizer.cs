using OpenCvSharp;
using Maple.Capture;
using Maple.Contracts;
using System.Text.RegularExpressions;

namespace Maple.Vision;

public readonly record struct PixelRegion(int X, int Y, int Width, int Height)
{
    public Rect ToRect() => new(X, Y, Width, Height);
}

public sealed record HudLayout(
    PixelRegion Hp,
    PixelRegion Mp,
    PixelRegion MapName,
    PixelRegion? Minimap = null,
    PixelRegion? HpText = null,
    PixelRegion? MpText = null);

public readonly record struct ResourceNumbers(double Current, double Maximum);

public static partial class ResourceTextParser
{
    [GeneratedRegex(@"(?:HP|MP)?\s*(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResourcePattern();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    public static bool TryParse(string? text, out ResourceNumbers numbers)
    {
        numbers = default;
        string normalized = (text ?? string.Empty)
            .Replace('O', '0')
            .Replace('o', '0')
            .Replace('I', '1')
            .Replace('l', '1')
            .Replace('i', '1');
        Match match = ResourcePattern().Match(normalized);
        if (!match.Success
            || !double.TryParse(match.Groups[1].Value, out double current)
            || !double.TryParse(match.Groups[2].Value, out double maximum)
            || maximum <= 0
            || current < 0
            || current > maximum)
            return false;
        numbers = new ResourceNumbers(current, maximum);
        return true;
    }

    public static bool TryParseAgainstFill(string? text, double fill, out ResourceNumbers numbers)
    {
        if (TryParse(text, out numbers) && Consistent(numbers, fill)) return true;
        string normalized = (text ?? string.Empty)
            .Replace('O', '0').Replace('o', '0')
            .Replace('I', '1').Replace('l', '1').Replace('i', '1')
            .Replace('S', '5').Replace('s', '5');
        string[] groups = NumberPattern().Matches(normalized)
            .Select(match => match.Value)
            .ToArray();
        if (groups.Length == 0) { numbers = default; return false; }

        ResourceNumbers? best = null;
        double bestError = double.MaxValue;
        foreach ((int left, int right) in CandidatePairs(groups))
        foreach (double current in Variants(left))
        foreach (double maximum in Variants(right))
        {
            var candidate = new ResourceNumbers(current, maximum);
            if (maximum <= 0 || current < 0 || current > maximum) continue;
            double error = Math.Abs(current / maximum - fill);
            if (error < bestError - 0.002
                || (Math.Abs(error - bestError) <= 0.002 && (!best.HasValue || maximum > best.Value.Maximum)))
            {
                best = candidate;
                bestError = error;
            }
        }
        numbers = best ?? default;
        return best.HasValue && bestError <= 0.15;
    }

    private static IEnumerable<(int Left, int Right)> CandidatePairs(string[] groups)
    {
        if (groups.Length >= 2)
        {
            yield return (Parse(groups[^2]), Parse(groups[^1]));
            yield break;
        }
        string digits = groups[0];
        for (int split = 1; split < digits.Length; split++)
            yield return (Parse(digits[..split]), Parse(digits[split..]));
    }

    private static int Parse(string value) => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static IEnumerable<double> Variants(int value)
    {
        yield return value;
        if (value >= 10) yield return value / 10;
        int divisor = 1;
        while (divisor <= value / 10) divisor *= 10;
        if (divisor > 1) yield return value % divisor;
    }

    private static bool Consistent(ResourceNumbers numbers, double fill) =>
        numbers.Maximum > 0 && Math.Abs(numbers.Current / numbers.Maximum - fill) <= 0.15;
}

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
        ResourceNumbers? hpNumbers = await ReadNumbersAsync(frame, layout.HpText, hp, cancellationToken).ConfigureAwait(false);
        ResourceNumbers? mpNumbers = await ReadNumbersAsync(frame, layout.MpText, mp, cancellationToken).ConfigureAwait(false);
        if (!ConsistentWithFill(hp, hpNumbers)) hpNumbers = null;
        if (!ConsistentWithFill(mp, mpNumbers)) mpNumbers = null;
        if (hpNumbers is { } exactHp) hp = exactHp.Current / exactHp.Maximum;
        if (mpNumbers is { } exactMp) mp = exactMp.Current / exactMp.Maximum;
        long freshUntil = frame.Metadata.CapturedAtMonoMs + 120;
        return new FixedUiVisionResult
        {
            FrameId = frame.Metadata.FrameId,
            HpCandidates = [Observation(hp, hpNumbers, freshUntil)],
            MpCandidates = [Observation(mp, mpNumbers, freshUntil)],
            Loot = new LootObservation { Visible = false, Confidence = 0.9, FreshUntilMonoMs = freshUntil },
            Map = new MapObservation { MapId = mapName.Length == 0 ? "unknown" : mapName, State = MapArchiveState.Candidate, Confidence = mapName.Length == 0 ? 0 : 0.9, FreshUntilMonoMs = freshUntil },
        };
    }

    private static double FillRatio(Mat source, PixelRegion region, Scalar lower, Scalar upper)
    {
        using Mat roi = new(source, region.ToRect());
        using Mat mask = new();
        Cv2.InRange(roi, lower, upper, mask);
        int minimumVerticalPixels = Math.Max(1, (int)Math.Ceiling(region.Height * 0.2));
        int filledColumns = 0;
        for (int x = 0; x < region.Width; x++)
        {
            using Mat column = mask.Col(x);
            if (Cv2.CountNonZero(column) >= minimumVerticalPixels) filledColumns++;
        }
        return (double)filledColumns / region.Width;
    }

    private async ValueTask<ResourceNumbers?> ReadNumbersAsync(CapturedFrame frame, PixelRegion? region, double fill, CancellationToken cancellationToken)
    {
        if (region is null) return null;
        string text = await ocr.RecognizeResourceAsync(frame, region.Value, cancellationToken).ConfigureAwait(false);
        return ResourceTextParser.TryParseAgainstFill(text, fill, out ResourceNumbers numbers) ? numbers : null;
    }

    private static bool ConsistentWithFill(double fill, ResourceNumbers? numbers) =>
        numbers is null || fill <= 0 || Math.Abs(numbers.Value.Current / numbers.Value.Maximum - fill) <= 0.15;

    private static ResourceObservation Observation(double ratio, ResourceNumbers? numbers, long freshUntil) => new()
    {
        Mode = ResourceMode.Percent,
        Value = Math.Clamp(ratio, 0, 1),
        CurrentValue = numbers?.Current,
        MaximumValue = numbers?.Maximum,
        Confidence = numbers.HasValue ? 0.98 : ratio > 0 ? 0.95 : 0.7,
        FreshUntilMonoMs = freshUntil,
    };
}
