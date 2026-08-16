using Maple.Capture;

namespace Maple.Vision;

public sealed class OcrCharacterNameMatcher
{
    private readonly OcrTextRecognizer ocr;
    private readonly long scanIntervalMs;
    private string ownName = string.Empty;
    private long lastScanMonoMs;
    private bool hasScanned;
    private double[]? lastMatchedBox;

    public OcrCharacterNameMatcher(IOcrEngine engine, long scanIntervalMs = 750)
    {
        ocr = new OcrTextRecognizer(engine ?? throw new ArgumentNullException(nameof(engine)));
        this.scanIntervalMs = scanIntervalMs is >= 0 and <= 10_000
            ? scanIntervalMs
            : throw new ArgumentOutOfRangeException(nameof(scanIntervalMs));
    }

    public async ValueTask<DetectionCandidate?> FindSelfAsync(
        CapturedFrame frame,
        IReadOnlyList<DetectionCandidate> candidates,
        long nowMonoMs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(candidates);
        DetectionCandidate[] characters = candidates
            .Where(candidate => candidate.Role == DetectionRole.CharacterCandidate && ValidBox(candidate.Box))
            .ToArray();
        if (characters.Length == 0) return null;

        DetectionCandidate? cached = MatchCached(characters);
        if (cached is not null && hasScanned && nowMonoMs - lastScanMonoMs < scanIntervalMs) return cached;
        if (hasScanned && nowMonoMs - lastScanMonoMs < scanIntervalMs) return null;
        lastScanMonoMs = nowMonoMs;
        hasScanned = true;

        string discovered = Normalize(await ocr.RecognizeAsync(frame, HudNameRegion(frame), cancellationToken).ConfigureAwait(false));
        if (discovered.Length >= 3) ownName = discovered;
        if (ownName.Length < 3) return cached;

        foreach (DetectionCandidate character in characters)
        {
            string nameplate = Normalize(await ocr.RecognizeAsync(frame, NameplateRegion(frame, character.Box), cancellationToken).ConfigureAwait(false));
            if (!NamesMatch(ownName, nameplate)) continue;
            lastMatchedBox = character.Box.ToArray();
            return character;
        }
        return cached;
    }

    private DetectionCandidate? MatchCached(IEnumerable<DetectionCandidate> candidates)
    {
        if (lastMatchedBox is null) return null;
        DetectionCandidate? nearest = candidates.OrderBy(candidate => Distance(candidate.Box, lastMatchedBox)).FirstOrDefault();
        return nearest is not null && Distance(nearest.Box, lastMatchedBox) <= 0.12 ? nearest : null;
    }

    private static PixelRegion HudNameRegion(CapturedFrame frame) => Region(frame, 0.245, 0.948, 0.145, 0.050);

    private static PixelRegion NameplateRegion(CapturedFrame frame, double[] box)
    {
        double width = Math.Clamp(box[2] * 2.8, 0.09, 0.24);
        double left = Math.Clamp(box[0] + box[2] / 2 - width / 2, 0, 1 - width);
        double top = Math.Clamp(box[1] + box[3] - 0.005, 0, 0.94);
        return Region(frame, left, top, width, Math.Min(0.055, 1 - top));
    }

    private static PixelRegion Region(CapturedFrame frame, double x, double y, double width, double height)
    {
        int left = Math.Clamp((int)Math.Round(frame.Width * x), 0, frame.Width - 1);
        int top = Math.Clamp((int)Math.Round(frame.Height * y), 0, frame.Height - 1);
        int regionWidth = Math.Clamp((int)Math.Round(frame.Width * width), 1, frame.Width - left);
        int regionHeight = Math.Clamp((int)Math.Round(frame.Height * height), 1, frame.Height - top);
        return new PixelRegion(left, top, regionWidth, regionHeight);
    }

    private static string Normalize(string? value) => new((value ?? string.Empty)
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant)
        .ToArray());

    private static bool NamesMatch(string expected, string actual) => actual.Length >= 3
        && (string.Equals(expected, actual, StringComparison.Ordinal)
            || expected.Contains(actual, StringComparison.Ordinal)
            || actual.Contains(expected, StringComparison.Ordinal));

    private static double Distance(double[] first, double[] second)
    {
        double dx = first[0] + first[2] / 2 - second[0] - second[2] / 2;
        double dy = first[1] + first[3] / 2 - second[1] - second[3] / 2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool ValidBox(double[]? box) => box is { Length: 4 }
        && box.All(double.IsFinite)
        && box[0] >= 0 && box[1] >= 0 && box[2] > 0 && box[3] > 0
        && box[0] + box[2] <= 1 && box[1] + box[3] <= 1;
}
