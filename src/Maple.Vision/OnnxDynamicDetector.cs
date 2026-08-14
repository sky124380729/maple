using Maple.Capture;
using Maple.Contracts;

namespace Maple.Vision;

public sealed record DetectionCandidate(string Class, double Confidence, double[] Box);

public interface IOnnxInferenceEngine : IAsyncDisposable
{
    ValueTask<IReadOnlyList<DetectionCandidate>> DetectAsync(CapturedFrame frame, CancellationToken cancellationToken);
}

public sealed class OnnxDynamicDetector(IOnnxInferenceEngine engine, double confidenceThreshold, long observationTtlMs) : IDynamicVisionProvider
{
    private readonly IOnnxInferenceEngine engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly double confidenceThreshold = confidenceThreshold is >= 0 and <= 1 ? confidenceThreshold : throw new ArgumentOutOfRangeException(nameof(confidenceThreshold));
    private readonly long observationTtlMs = observationTtlMs is > 0 and <= 5_000 ? observationTtlMs : throw new ArgumentOutOfRangeException(nameof(observationTtlMs));

    public async ValueTask<DynamicVisionResult> ObserveDynamicAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        IReadOnlyList<DetectionCandidate> candidates = await engine.DetectAsync(frame, cancellationToken).ConfigureAwait(false);
        List<DetectionCandidate> valid = candidates.Where(candidate => candidate.Confidence >= confidenceThreshold && ValidBox(candidate.Box)).ToList();
        DetectionCandidate? self = valid.Where(candidate => candidate.Class.Equals("self", StringComparison.OrdinalIgnoreCase)).OrderByDescending(candidate => candidate.Confidence).FirstOrDefault();
        long freshUntil = frame.Metadata.CapturedAtMonoMs + observationTtlMs;
        return new DynamicVisionResult
        {
            FrameId = frame.Metadata.FrameId,
            ModelVersion = "onnx",
            Self = self is null ? null : new SelfObservation { Box = self.Box, Confidence = self.Confidence, FreshUntilMonoMs = freshUntil },
            Players = valid.Where(candidate => candidate.Class.Equals("player", StringComparison.OrdinalIgnoreCase)).Select((candidate, index) => new PlayerObservation { TrackId = $"player-{frame.Metadata.FrameId}-{index}", Box = candidate.Box, Confidence = candidate.Confidence, FreshUntilMonoMs = freshUntil }).ToList(),
            Monsters = valid.Where(candidate => candidate.Class.Equals("monster", StringComparison.OrdinalIgnoreCase)).Select((candidate, index) => new MonsterObservation { TargetId = $"monster-{frame.Metadata.FrameId}-{index}", Class = candidate.Class, Box = candidate.Box, Confidence = candidate.Confidence, FreshUntilMonoMs = freshUntil }).ToList(),
        };
    }

    private static bool ValidBox(double[]? box) => box is { Length: 4 } && box.All(value => value is >= 0 and <= 1) && box[0] + box[2] <= 1 && box[1] + box[3] <= 1;
}
