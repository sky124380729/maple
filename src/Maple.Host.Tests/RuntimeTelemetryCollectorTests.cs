using Maple.Contracts;
using Maple.Vision;
using Xunit;

namespace Maple.Host.Tests;

public sealed class RuntimeTelemetryCollectorTests
{
    [Fact]
    public void CollectUsesRollingSecondAndActualRuntimeLabels()
    {
        long now = 100;
        var collector = new RuntimeTelemetryCollector(InferenceProvider.DirectMl, () => now, () => DateTimeOffset.UnixEpoch, () => 384);

        RuntimeTelemetrySnapshot first = collector.Collect(Publication(1, capturedAt: 80), SessionState.Observing, "MoveRight", null);
        now = 600;
        RuntimeTelemetrySnapshot second = collector.Collect(Publication(2, capturedAt: 560), SessionState.Attacking, "Attack", null);
        now = 1201;
        RuntimeTelemetrySnapshot third = collector.Collect(Publication(3, capturedAt: 1160), SessionState.Attacking, "Attack", "VISION_REPAIRING");

        Assert.Equal(1, first.Contract.CaptureFps);
        Assert.Equal(2, second.Contract.RecognitionFps);
        Assert.Equal(2, third.Contract.CaptureFps);
        Assert.Equal(InferenceProvider.DirectMl, third.Contract.InferenceProvider);
        Assert.Equal(CaptureBackend.Wgc, third.Contract.CaptureBackend);
        Assert.Equal(384, third.Contract.ProcessMemoryMb);
        Assert.Equal("VISION_REPAIRING", third.Contract.WarningCode);
        Assert.Equal("directml", third.Preview.InferenceProvider);
    }

    private static VisionRuntimePublication Publication(long frameId, long capturedAt) => new(
        new VisionPipelineResult { Status = VisionPipelineStatus.Ready, Diagnostic = "OK" },
        new CaptureFrameMetadata { SchemaVersion = 2, FrameId = frameId, CapturedAtMonoMs = capturedAt, ClientWidth = 1280, ClientHeight = 720, Dpi = 96, CaptureBackend = CaptureBackend.Wgc, DroppedReason = DroppedFrameReason.None },
        new TargetBinding { SchemaVersion = 2, Hwnd = "0x1", Pid = 7, ClientWidth = 1280, ClientHeight = 720, Dpi = 96 },
        DetectorLatencyMs: 20, QueueAgeMs: 5, DroppedFrames: 0);
}
