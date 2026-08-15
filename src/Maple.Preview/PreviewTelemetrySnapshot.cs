#nullable enable
namespace Maple.Preview;

public sealed record PreviewTelemetrySnapshot(
    double CaptureFps,
    double RenderFps,
    double RecognitionFps,
    double FrameLatencyMs,
    double DetectorLatencyMs,
    double QueueAgeMs,
    string CaptureBackend,
    string InferenceProvider,
    long DroppedFrames,
    double ProcessMemoryMb,
    string SessionState,
    string LastAction,
    string? WarningCode);
