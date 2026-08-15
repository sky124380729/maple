using Maple.Contracts;
using Maple.Preview;

namespace Maple.Host;

public sealed record RuntimeTelemetrySnapshot(TelemetrySnapshot Contract, PreviewTelemetrySnapshot Preview);

public sealed class RuntimeTelemetryCollector
{
    private readonly object sync = new();
    private readonly Queue<long> captureTimes = new();
    private readonly Queue<long> recognitionTimes = new();
    private readonly InferenceProvider provider;
    private readonly Func<long> clock;
    private readonly Func<DateTimeOffset> utcClock;
    private readonly Func<double> memoryProvider;

    public RuntimeTelemetryCollector(
        InferenceProvider provider,
        Func<long>? clock = null,
        Func<DateTimeOffset>? utcClock = null,
        Func<double>? memoryProvider = null)
    {
        this.provider = provider;
        this.clock = clock ?? (() => Environment.TickCount64);
        this.utcClock = utcClock ?? (() => DateTimeOffset.UtcNow);
        this.memoryProvider = memoryProvider ?? (() => Environment.WorkingSet / 1024d / 1024d);
    }

    public RuntimeTelemetrySnapshot Collect(
        VisionRuntimePublication publication,
        SessionState state,
        string? lastAction,
        string? warningCode,
        PauseReason pauseReason = PauseReason.None)
    {
        long now = clock();
        double captureFps;
        double recognitionFps;
        lock (sync)
        {
            captureTimes.Enqueue(now);
            recognitionTimes.Enqueue(now);
            Prune(captureTimes, now);
            Prune(recognitionTimes, now);
            captureFps = captureTimes.Count;
            recognitionFps = recognitionTimes.Count;
        }
        double memory = Math.Max(0, memoryProvider());
        var contract = new TelemetrySnapshot
        {
            SchemaVersion = ContractConstants.SchemaVersion,
            Timestamp = utcClock(),
            CaptureFps = captureFps,
            RenderFps = 0,
            RecognitionFps = recognitionFps,
            FrameLatencyMs = Math.Max(0, now - publication.FrameMetadata.CapturedAtMonoMs),
            DetectorLatencyMs = publication.DetectorLatencyMs,
            DroppedFrames = publication.DroppedFrames,
            QueueAgeMs = publication.QueueAgeMs,
            ProcessMemoryMb = memory,
            InferenceProvider = provider,
            CaptureBackend = publication.FrameMetadata.CaptureBackend,
            LastAction = lastAction,
            WarningCode = warningCode,
            State = state,
            PauseReason = pauseReason,
        };
        var preview = new PreviewTelemetrySnapshot(
            captureFps, 0, recognitionFps, contract.FrameLatencyMs, publication.DetectorLatencyMs,
            publication.QueueAgeMs, CaptureBackendLabel(publication.FrameMetadata.CaptureBackend), ProviderLabel(provider),
            publication.DroppedFrames, memory, state.ToString(), lastAction ?? "无", warningCode);
        return new RuntimeTelemetrySnapshot(contract, preview);
    }

    private static void Prune(Queue<long> samples, long now)
    {
        while (samples.Count > 0 && now - samples.Peek() >= 1000) samples.Dequeue();
    }

    internal static string ProviderLabel(InferenceProvider value) => value switch
    {
        InferenceProvider.Cpu => "cpu",
        InferenceProvider.DirectMl => "directml",
        InferenceProvider.Cuda => "cuda",
        _ => "none",
    };

    internal static string CaptureBackendLabel(CaptureBackend value) => value switch
    {
        CaptureBackend.Wgc => "WGC",
        CaptureBackend.BitBlt => "BitBlt",
        CaptureBackend.PrintWindow => "PrintWindow",
        _ => value.ToString(),
    };
}
