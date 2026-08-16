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

    public static VisionRuntimeBootstrapResult Load(
        string? manifestPath = null,
        IOcrEngine? ocrEngine = null,
        IOcrEngine? resourceOcrEngine = null,
        TimeSpan? dynamicTimeout = null)
    {
        string path = ResolveManifestPath(
            manifestPath,
            Environment.GetEnvironmentVariable("MAPLE_MODEL_MANIFEST"),
            AppContext.BaseDirectory);
        ModelManifestValidation validation;
        try { validation = ModelManifestLoader.Load(path); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new VisionRuntimeBootstrapResult(
                false,
                "MODEL_MANIFEST_LOAD_FAILED:" + exception.GetType().Name,
                string.Empty,
                InferenceProvider.None);
        }
        if (!validation.IsValid || validation.Manifest is null)
            return new VisionRuntimeBootstrapResult(false, validation.Diagnostic, string.Empty, InferenceProvider.None);
        try
        {
            var engine = new OnnxRuntimeInferenceEngine(validation);
            double displayThreshold = validation.Manifest.DisplayConfidenceThreshold
                ?? validation.Manifest.ConfidenceThreshold;
            var detector = new OnnxDynamicDetector(
                engine,
                validation.Manifest.ConfidenceThreshold,
                observationTtlMs: 180,
                identityTracker: new SelfIdentityTracker(new SelfIdentityOptions
                {
                    WarmupFrames = 3,
                    DetectionFloor = displayThreshold,
                    MinimumConfidence = validation.Manifest.ConfidenceThreshold,
                    MotionConfirmationConfidence = 0.95,
                    OcclusionTtlMs = 180,
                }),
                displayConfidenceThreshold: displayThreshold,
                nameMatcher: ocrEngine is null ? null : new OcrCharacterNameMatcher(ocrEngine));
            var fixedUi = new AdaptiveFixedUiVisionProvider(ocrEngine, resourceOcrEngine);
            var pipeline = new VisionPipeline(
                fixedUi,
                detector,
                new ObservationFusion(new ObservationFusionOptions { ResourceConflictTolerance = 0.08 }),
                new VisionPipelineOptions
                {
                    DynamicTimeout = dynamicTimeout ?? TimeSpan.FromMilliseconds(250),
                    CriticalHealthPercent = 0.2,
                });
            return new VisionRuntimeBootstrapResult(true, "OK", validation.Manifest.ModelId, InferenceProvider.Cpu, pipeline);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new VisionRuntimeBootstrapResult(false, "MODEL_LOAD_FAILED:" + exception.GetType().Name, validation.Manifest.ModelId, InferenceProvider.None);
        }
    }

    public static string ResolveManifestPath(
        string? explicitManifestPath,
        string? environmentManifestPath,
        string applicationDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitManifestPath)) return explicitManifestPath;
        if (!string.IsNullOrWhiteSpace(environmentManifestPath)) return environmentManifestPath;
        if (!string.IsNullOrWhiteSpace(applicationDirectory))
        {
            string local = Path.Combine(Path.GetFullPath(applicationDirectory), "model-manifest.json");
            if (File.Exists(local)) return local;
        }
        return DefaultManifestPath;
    }

}
