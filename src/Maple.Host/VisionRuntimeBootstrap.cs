using Maple.Contracts;
using Maple.Vision;

namespace Maple.Host;

public sealed record VisionRuntimeBootstrapResult(
    bool Ready,
    string Diagnostic,
    string ModelId,
    InferenceProvider Provider,
    VisionPipeline? Pipeline = null);

public static class VisionRuntimeBootstrap
{
    public static string DefaultManifestPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Maple", "models", "active", "manifest.json");

    public static VisionRuntimeBootstrapResult Load(string? manifestPath = null)
    {
        string path = string.IsNullOrWhiteSpace(manifestPath) ? DefaultManifestPath : manifestPath;
        ModelManifestValidation validation = ModelManifestLoader.Load(path);
        if (!validation.IsValid || validation.Manifest is null)
            return new VisionRuntimeBootstrapResult(false, validation.Diagnostic, string.Empty, InferenceProvider.None);
        try
        {
            var engine = new OnnxRuntimeInferenceEngine(validation);
            var detector = new OnnxDynamicDetector(
                engine,
                validation.Manifest.ConfidenceThreshold,
                observationTtlMs: 180,
                identityTracker: new SelfIdentityTracker(new SelfIdentityOptions { WarmupFrames = 3, MinimumConfidence = validation.Manifest.ConfidenceThreshold, OcclusionTtlMs = 180 }));
            var fixedUi = new UnavailableFixedUiVisionProvider();
            var pipeline = new VisionPipeline(
                fixedUi,
                detector,
                new ObservationFusion(new ObservationFusionOptions { ResourceConflictTolerance = 0.08 }),
                new VisionPipelineOptions { DynamicTimeout = TimeSpan.FromMilliseconds(250), CriticalHealthPercent = 0.2 });
            return new VisionRuntimeBootstrapResult(true, "OK", validation.Manifest.ModelId, InferenceProvider.Cpu, pipeline);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new VisionRuntimeBootstrapResult(false, "MODEL_LOAD_FAILED:" + exception.GetType().Name, validation.Manifest.ModelId, InferenceProvider.None);
        }
    }

    private sealed class UnavailableFixedUiVisionProvider : IFixedUiVisionProvider
    {
        public ValueTask<FixedUiVisionResult> ObserveFixedUiAsync(Maple.Capture.CapturedFrame frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long freshUntil = frame.Metadata.CapturedAtMonoMs + 120;
            return ValueTask.FromResult(new FixedUiVisionResult
            {
                FrameId = frame.Metadata.FrameId,
                HpCandidates = [],
                MpCandidates = [],
                Loot = new LootObservation { Visible = false, Confidence = 0, FreshUntilMonoMs = freshUntil },
                Map = new MapObservation { MapId = "unknown", State = MapArchiveState.Candidate, Confidence = 0, FreshUntilMonoMs = freshUntil },
            });
        }
    }
}
