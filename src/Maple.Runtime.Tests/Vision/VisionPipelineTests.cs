using System.Buffers;
using System.Security.Cryptography;
using Maple.Capture;
using Maple.Contracts;
using Maple.Vision;
using Xunit;

namespace Maple.Runtime.Tests.Vision;

public sealed class VisionPipelineTests
{
    [Fact]
    public void CapturedFrameReleasesItsOwnedPixelBufferExactlyOnce()
    {
        var owner = new TrackingMemoryOwner(16);
        var frame = new CapturedFrame(Metadata(7), 2, 2, 8, CapturedPixelFormat.Bgra32, owner, 16);

        Assert.Equal(16, frame.Pixels.Length);
        frame.Dispose();
        frame.Dispose();

        Assert.Equal(1, owner.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => frame.Pixels);
    }

    [Fact]
    public async Task ReplayCaptureOwnsFramesAndReportsExhaustionWithoutGrowingAQueue()
    {
        byte[] pixels = new byte[16];
        var backend = new ReplayCaptureBackend([new ReplayFrame(2, 2, 8, CapturedPixelFormat.Bgra32, pixels)]);
        var target = new CaptureTarget { Hwnd = "0x1", Pid = 7, ClientWidth = 2, ClientHeight = 2, IsForeground = true };

        CaptureResult first = await backend.CaptureAsync(target, 1, 1000, CancellationToken.None);
        CaptureResult second = await backend.CaptureAsync(target, 2, 1040, CancellationToken.None);

        Assert.True(first.Success);
        Assert.NotNull(first.Frame);
        Assert.False(second.Success);
        Assert.Equal("REPLAY_EXHAUSTED", second.Reason);
        first.Frame!.Dispose();
    }

    [Fact]
    public void WgcFramePoolKeepsTwoSlotsAndDisposesReplacedFrames()
    {
        using var pool = new WgcFramePool();
        var firstOwner = new TrackingMemoryOwner(16);
        var secondOwner = new TrackingMemoryOwner(16);
        var thirdOwner = new TrackingMemoryOwner(16);
        pool.Publish(new CapturedFrame(Metadata(1), 2, 2, 8, CapturedPixelFormat.Bgra32, firstOwner, 16));
        pool.Publish(new CapturedFrame(Metadata(2), 2, 2, 8, CapturedPixelFormat.Bgra32, secondOwner, 16));
        pool.Publish(new CapturedFrame(Metadata(3), 2, 2, 8, CapturedPixelFormat.Bgra32, thirdOwner, 16));

        Assert.Equal(2, pool.Capacity);
        Assert.Equal(1, pool.DroppedFrames);
        Assert.True(pool.TryTakeLatest(out CapturedFrame? latest));
        Assert.Equal(3, latest!.Metadata.FrameId);
        latest.Dispose();
        Assert.Equal(1, firstOwner.DisposeCount);
        Assert.Equal(1, thirdOwner.DisposeCount);
    }

    [Fact]
    public async Task FixedAndDynamicProvidersStartTogetherAndFuseOnlyTheSameFrame()
    {
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixedProvider = new CoordinatedFixedProvider(bothStarted);
        var dynamicProvider = new CoordinatedDynamicProvider(bothStarted, frameId: 21);
        fixedProvider.Dynamic = dynamicProvider;
        dynamicProvider.Fixed = fixedProvider;
        var pipeline = Pipeline(fixedProvider, dynamicProvider, TimeSpan.FromSeconds(1));
        using var frame = Frame(21);

        VisionPipelineResult result = await pipeline.ProcessAsync(frame, Target(), nowMonoMs: 1_020);

        Assert.True(fixedProvider.SawDynamicStart);
        Assert.True(dynamicProvider.SawFixedStart);
        Assert.Equal(VisionPipelineStatus.Ready, result.Status);
        Assert.NotNull(result.Observation);
        Assert.Single(result.Observation!.Players);
        Assert.Single(result.Observation.Monsters);
        Assert.Equal("other-player", result.Observation.Players[0].TrackId);
        Assert.Equal("slime", result.Observation.Monsters[0].Class);
    }

    [Fact]
    public async Task RejectsDynamicResultsProducedForAnOlderFrame()
    {
        var fixedProvider = new ImmediateFixedProvider(frameId: 33, hp: 0.8);
        var dynamicProvider = new ImmediateDynamicProvider(frameId: 32);
        var pipeline = Pipeline(fixedProvider, dynamicProvider, TimeSpan.FromSeconds(1));
        using var frame = Frame(33);

        VisionPipelineResult result = await pipeline.ProcessAsync(frame, Target(), nowMonoMs: 1_020);

        Assert.Equal(VisionPipelineStatus.StaleResult, result.Status);
        Assert.Null(result.Observation);
    }

    [Fact]
    public async Task DynamicTimeoutDoesNotDelayCriticalHealthResult()
    {
        var fixedProvider = new ImmediateFixedProvider(frameId: 45, hp: 0.10);
        var dynamicProvider = new NeverCompletingDynamicProvider();
        var pipeline = Pipeline(fixedProvider, dynamicProvider, TimeSpan.FromMilliseconds(25));
        using var frame = Frame(45);

        VisionPipelineResult result = await pipeline.ProcessAsync(frame, Target(), nowMonoMs: 1_020);

        Assert.Equal(VisionPipelineStatus.DynamicTimedOut, result.Status);
        Assert.True(result.HealthCritical);
        Assert.Equal(0.10, result.FixedUi!.HpCandidates.Single().Value, precision: 2);
        Assert.Null(result.Observation);
    }

    [Fact]
    public void ManifestRequiresExpectedClassesAndAnUntamperedModel()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"maple-model-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string modelPath = Path.Combine(directory, "detector.onnx");
            File.WriteAllBytes(modelPath, [1, 2, 3, 4]);
            string checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(modelPath))).ToLowerInvariant();
            string manifestPath = Path.Combine(directory, "manifest.json");
            File.WriteAllText(manifestPath, $$"""
                {
                  "schemaVersion": 1,
                  "modelId": "maple-dynamic-v1",
                  "version": "1.0.0",
                  "modelFile": "detector.onnx",
                  "sha256": "{{checksum}}",
                  "runtime": "onnx",
                  "inputWidth": 640,
                  "inputHeight": 640,
                  "confidenceThreshold": 0.75,
                  "nmsThreshold": 0.45,
                  "classes": ["self", "player", "monster"]
                }
                """);

            ModelManifestValidation valid = ModelManifestLoader.Load(manifestPath);
            Assert.True(valid.IsValid, valid.Diagnostic);

            File.AppendAllText(modelPath, "tampered");
            ModelManifestValidation tampered = ModelManifestLoader.Load(manifestPath);
            Assert.False(tampered.IsValid);
            Assert.Equal("MODEL_HASH_MISMATCH", tampered.Diagnostic);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DynamicPostProcessingKeepsOneSelfAndNeverTurnsPlayersIntoMonsters()
    {
        var engine = new StaticInferenceEngine([
            new DetectionCandidate("self", 0.83, [0.1, 0.2, 0.1, 0.2]),
            new DetectionCandidate("self", 0.96, [0.4, 0.2, 0.1, 0.2]),
            new DetectionCandidate("player", 0.91, [0.5, 0.2, 0.1, 0.2]),
            new DetectionCandidate("monster", 0.89, [0.7, 0.2, 0.1, 0.2]),
        ]);
        var detector = new OnnxDynamicDetector(engine, confidenceThreshold: 0.75, observationTtlMs: 120);
        using var frame = Frame(52);

        DynamicVisionResult result = await detector.ObserveDynamicAsync(frame, CancellationToken.None);

        Assert.Equal(0.96, result.Self!.Confidence, precision: 2);
        Assert.Single(result.Players);
        Assert.Single(result.Monsters);
        Assert.Equal("player-52-0", result.Players[0].TrackId);
        Assert.Equal("monster-52-0", result.Monsters[0].TargetId);
    }

    [Fact]
    public async Task HudAndOcrAdaptersConsumeRealPixelsThroughReplaceableEngines()
    {
        byte[] pixels = new byte[4 * 4 * 4];
        FillBgra(pixels, width: 4, x: 0, y: 0, count: 2, b: 20, g: 20, r: 240);
        FillBgra(pixels, width: 4, x: 0, y: 1, count: 3, b: 240, g: 80, r: 20);
        using var frame = Frame(61, pixels, width: 4, height: 4);
        var ocrEngine = new RecordingOcrEngine("森林东部");
        var recognizer = new OpenCvHudRecognizer(
            new HudLayout(new PixelRegion(0, 0, 4, 1), new PixelRegion(0, 1, 4, 1), new PixelRegion(0, 2, 4, 2)),
            new OcrTextRecognizer(ocrEngine));

        FixedUiVisionResult result = await recognizer.ObserveFixedUiAsync(frame, CancellationToken.None);

        Assert.Equal(0.5, result.HpCandidates.Single().Value, precision: 2);
        Assert.Equal(0.75, result.MpCandidates.Single().Value, precision: 2);
        Assert.Equal("森林东部", result.Map.MapId);
        Assert.True(ocrEngine.ReceivedPng);
    }

    private static VisionPipeline Pipeline(IFixedUiVisionProvider fixedProvider, IDynamicVisionProvider dynamicProvider, TimeSpan timeout) =>
        new(fixedProvider, dynamicProvider, new ObservationFusion(new ObservationFusionOptions { ResourceConflictTolerance = 0.08 }),
            new VisionPipelineOptions { DynamicTimeout = timeout, CriticalHealthPercent = 0.2 });

    private static CapturedFrame Frame(long frameId, byte[]? pixels = null, int width = 2, int height = 2)
    {
        int length = width * height * 4;
        var owner = MemoryPool<byte>.Shared.Rent(length);
        (pixels ?? new byte[length]).CopyTo(owner.Memory.Span);
        return new CapturedFrame(Metadata(frameId, width, height), width, height, width * 4, CapturedPixelFormat.Bgra32, owner, length);
    }

    private static CaptureFrameMetadata Metadata(long frameId, int width = 2, int height = 2) => new()
    {
        SchemaVersion = 2, FrameId = frameId, CapturedAtMonoMs = 1_000, ClientWidth = width, ClientHeight = height,
        Dpi = 96, CaptureBackend = CaptureBackend.Wgc, CaptureDurationMs = 2, DroppedReason = DroppedFrameReason.None,
    };

    private static TargetBinding Target() => new()
    {
        SchemaVersion = 2, Hwnd = "0x1", Pid = 7, ClientWidth = 1280, ClientHeight = 720, Dpi = 96,
    };

    private static FixedUiVisionResult Fixed(long frameId, double hp = 0.8) => new()
    {
        FrameId = frameId,
        Loot = new LootObservation { Visible = false, Confidence = 0.9, FreshUntilMonoMs = 1_100 },
        Map = new MapObservation { MapId = "forest-east", State = MapArchiveState.Validated, Confidence = 0.95, FreshUntilMonoMs = 1_100 },
        HpCandidates = [new ResourceObservation { Mode = ResourceMode.Percent, Value = hp, Confidence = 0.99, FreshUntilMonoMs = 1_100 }],
        MpCandidates = [new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.7, Confidence = 0.99, FreshUntilMonoMs = 1_100 }],
    };

    private static DynamicVisionResult Dynamic(long frameId) => new()
    {
        FrameId = frameId,
        Self = new SelfObservation { Box = [0.4, 0.4, 0.1, 0.2], Confidence = 0.96, FreshUntilMonoMs = 1_100 },
        Players = [new PlayerObservation { TrackId = "other-player", Box = [0.2, 0.4, 0.1, 0.2], Confidence = 0.9, FreshUntilMonoMs = 1_100 }],
        Monsters = [new MonsterObservation { TargetId = "monster-1", Class = "slime", Box = [0.7, 0.4, 0.1, 0.2], Confidence = 0.9, FreshUntilMonoMs = 1_100 }],
    };

    private static void FillBgra(byte[] pixels, int width, int x, int y, int count, byte b, byte g, byte r)
    {
        for (int offset = 0; offset < count; offset++)
        {
            int index = ((y * width) + x + offset) * 4;
            pixels[index] = b; pixels[index + 1] = g; pixels[index + 2] = r; pixels[index + 3] = 255;
        }
    }

    private sealed class TrackingMemoryOwner(int length) : IMemoryOwner<byte>
    {
        private readonly byte[] buffer = new byte[length];
        public int DisposeCount { get; private set; }
        public Memory<byte> Memory => buffer;
        public void Dispose() => DisposeCount++;
    }

    private sealed class ImmediateFixedProvider(long frameId, double hp) : IFixedUiVisionProvider
    {
        public ValueTask<FixedUiVisionResult> ObserveFixedUiAsync(CapturedFrame frame, CancellationToken cancellationToken) => ValueTask.FromResult(Fixed(frameId, hp));
    }

    private sealed class ImmediateDynamicProvider(long frameId) : IDynamicVisionProvider
    {
        public ValueTask<DynamicVisionResult> ObserveDynamicAsync(CapturedFrame frame, CancellationToken cancellationToken) => ValueTask.FromResult(Dynamic(frameId));
    }

    private sealed class NeverCompletingDynamicProvider : IDynamicVisionProvider
    {
        public async ValueTask<DynamicVisionResult> ObserveDynamicAsync(CapturedFrame frame, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class CoordinatedFixedProvider(TaskCompletionSource bothStarted) : IFixedUiVisionProvider
    {
        public bool Started { get; private set; }
        public bool SawDynamicStart { get; private set; }
        public CoordinatedDynamicProvider? Dynamic { get; set; }
        public async ValueTask<FixedUiVisionResult> ObserveFixedUiAsync(CapturedFrame frame, CancellationToken cancellationToken)
        {
            Started = true;
            if (Dynamic?.Started == true) bothStarted.TrySetResult();
            await bothStarted.Task.WaitAsync(cancellationToken);
            SawDynamicStart = Dynamic?.Started == true;
            return Fixed(frame.Metadata.FrameId);
        }
    }

    private sealed class CoordinatedDynamicProvider(TaskCompletionSource bothStarted, long frameId) : IDynamicVisionProvider
    {
        public CoordinatedFixedProvider? Fixed { get; set; }
        public bool Started { get; private set; }
        public bool SawFixedStart { get; private set; }
        public async ValueTask<DynamicVisionResult> ObserveDynamicAsync(CapturedFrame frame, CancellationToken cancellationToken)
        {
            Started = true;
            if (Fixed?.Started == true) bothStarted.TrySetResult();
            await bothStarted.Task.WaitAsync(cancellationToken);
            SawFixedStart = Fixed?.Started == true;
            return Dynamic(frameId);
        }
    }

    private sealed class StaticInferenceEngine(IReadOnlyList<DetectionCandidate> detections) : IOnnxInferenceEngine
    {
        public ValueTask<IReadOnlyList<DetectionCandidate>> DetectAsync(CapturedFrame frame, CancellationToken cancellationToken) => ValueTask.FromResult(detections);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingOcrEngine(string value) : IOcrEngine
    {
        public bool ReceivedPng { get; private set; }
        public ValueTask<string> RecognizeAsync(ReadOnlyMemory<byte> encodedPng, CancellationToken cancellationToken)
        {
            ReceivedPng = encodedPng.Length >= 4 && encodedPng[..4].Span.SequenceEqual(new byte[] { 137, 80, 78, 71 });
            return ValueTask.FromResult(value);
        }
    }
}
