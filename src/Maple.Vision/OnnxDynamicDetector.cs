using Maple.Capture;
using Maple.Contracts;

namespace Maple.Vision;

public sealed record DetectionCandidate(string Class, double Confidence, double[] Box, DetectionRole Role = DetectionRole.Ignore);

public interface IOnnxInferenceEngine : IAsyncDisposable
{
    ValueTask<IReadOnlyList<DetectionCandidate>> DetectAsync(CapturedFrame frame, CancellationToken cancellationToken);
}

public sealed class OnnxDynamicDetector : IDynamicVisionProvider
{
    private readonly IOnnxInferenceEngine engine;
    private readonly double confidenceThreshold;
    private readonly double displayConfidenceThreshold;
    private readonly long observationTtlMs;
    private readonly SelfIdentityTracker identityTracker;
    private readonly OcrCharacterNameMatcher? nameMatcher;

    public OnnxDynamicDetector(
        IOnnxInferenceEngine engine,
        double confidenceThreshold,
        long observationTtlMs,
        SelfIdentityTracker? identityTracker = null,
        double? displayConfidenceThreshold = null,
        OcrCharacterNameMatcher? nameMatcher = null)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.confidenceThreshold = confidenceThreshold is >= 0 and <= 1 ? confidenceThreshold : throw new ArgumentOutOfRangeException(nameof(confidenceThreshold));
        this.displayConfidenceThreshold = displayConfidenceThreshold ?? confidenceThreshold;
        if (this.displayConfidenceThreshold is < 0 or > 1 || this.displayConfidenceThreshold > confidenceThreshold)
            throw new ArgumentOutOfRangeException(nameof(displayConfidenceThreshold));
        this.observationTtlMs = observationTtlMs is > 0 and <= 5_000 ? observationTtlMs : throw new ArgumentOutOfRangeException(nameof(observationTtlMs));
        this.identityTracker = identityTracker ?? new SelfIdentityTracker(new SelfIdentityOptions { WarmupFrames = 1, MinimumConfidence = confidenceThreshold, OcclusionTtlMs = observationTtlMs });
        this.nameMatcher = nameMatcher;
    }

    public async ValueTask<DynamicVisionResult> ObserveDynamicAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        IReadOnlyList<DetectionCandidate> candidates = await engine.DetectAsync(frame, cancellationToken).ConfigureAwait(false);
        List<DetectionCandidate> valid = candidates
            .Where(candidate => candidate.Confidence >= displayConfidenceThreshold && ValidBox(candidate.Box))
            .ToList();
        if (valid.All(candidate => candidate.Role == DetectionRole.Ignore)) return LegacyResult(valid, frame);
        List<DetectionCandidate> monsters = valid.Where(candidate => candidate.Role == DetectionRole.Monster).ToList();
        DetectionCandidate? namedSelf = nameMatcher is null
            ? null
            : await nameMatcher.FindSelfAsync(frame, valid, frame.Metadata.CapturedAtMonoMs, cancellationToken).ConfigureAwait(false);
        SelfIdentityResult identity = identityTracker.Update(valid, frame.Metadata.CapturedAtMonoMs, monsters.Count > 0, namedSelf?.Box);
        DetectionCandidate? self = valid.Where(candidate => candidate.Class.Equals("self", StringComparison.OrdinalIgnoreCase)).OrderByDescending(candidate => candidate.Confidence).FirstOrDefault();
        long freshUntil = frame.Metadata.CapturedAtMonoMs + observationTtlMs;
        return new DynamicVisionResult
        {
            FrameId = frame.Metadata.FrameId,
            ModelVersion = "onnx",
            Self = identity.Self,
            Players = identity.Players,
            Monsters = monsters.Select((candidate, index) => new MonsterObservation { TargetId = $"monster-{frame.Metadata.FrameId}-{index}", Class = candidate.Class, Box = candidate.Box, Confidence = candidate.Confidence, FreshUntilMonoMs = freshUntil }).ToList(),
            CanDriveActions = identity.CanDriveActions
                && identity.Self?.Confidence >= confidenceThreshold
                && monsters.Any(candidate => candidate.Confidence >= confidenceThreshold),
            Diagnostic = identity.Diagnostic,
        };
    }

    private DynamicVisionResult LegacyResult(List<DetectionCandidate> valid, CapturedFrame frame)
    {
        DetectionCandidate? self = valid.Where(candidate => candidate.Class.Equals("self", StringComparison.OrdinalIgnoreCase)).OrderByDescending(candidate => candidate.Confidence).FirstOrDefault();
        long freshUntil = frame.Metadata.CapturedAtMonoMs + observationTtlMs;
        return new DynamicVisionResult
        {
            FrameId = frame.Metadata.FrameId,
            ModelVersion = "onnx-legacy",
            Self = self is null ? null : new SelfObservation { Box = self.Box, Confidence = self.Confidence, FreshUntilMonoMs = freshUntil },
            Players = valid.Where(candidate => candidate.Class.Equals("player", StringComparison.OrdinalIgnoreCase)).Select((candidate, index) => new PlayerObservation { TrackId = $"player-{frame.Metadata.FrameId}-{index}", Box = candidate.Box, Confidence = candidate.Confidence, FreshUntilMonoMs = freshUntil }).ToList(),
            Monsters = valid.Where(candidate => candidate.Class.Equals("monster", StringComparison.OrdinalIgnoreCase)).Select((candidate, index) => new MonsterObservation { TargetId = $"monster-{frame.Metadata.FrameId}-{index}", Class = candidate.Class, Box = candidate.Box, Confidence = candidate.Confidence, FreshUntilMonoMs = freshUntil }).ToList(),
            CanDriveActions = self is not null && valid.Any(candidate => candidate.Class.Equals("monster", StringComparison.OrdinalIgnoreCase)),
            Diagnostic = self is null ? "SELF_NOT_FOUND" : "OK",
        };
    }

    private static bool ValidBox(double[]? box) => box is { Length: 4 } && box.All(value => value is >= 0 and <= 1) && box[0] + box[2] <= 1 && box[1] + box[3] <= 1;
}
