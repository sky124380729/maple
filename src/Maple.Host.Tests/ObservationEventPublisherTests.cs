using System.Text.Json;
using Maple.Contracts;
using Maple.Preview;
using Maple.Vision;
using Xunit;
using PreviewOverlay = Maple.Preview.OverlaySnapshot;

namespace Maple.Host.Tests;

public sealed class ObservationEventPublisherTests
{
    [Fact]
    public void PublishUpdatesNativeSurfaceAndSendsMatchingStructuredEvents()
    {
        var sink = new RecordingSink();
        List<string> messages = [];
        long now = 1050;
        var telemetry = new RuntimeTelemetryCollector(InferenceProvider.Cpu, () => now, () => DateTimeOffset.UnixEpoch, () => 256);
        var publisher = new ObservationEventPublisher(sink, messages.Add, telemetry, () => SessionState.Observing, () => now, "maple-yolo-v1");

        publisher.Publish(Publication(Result(frameId: 42, freshUntil: 1150), frameId: 42));

        Assert.Equal(42, sink.Overlay!.FrameId);
        Assert.Equal(42, sink.TelemetryFrameId);
        Assert.Contains(messages, json => EventType(json) == "observation.updated" && PayloadFrameId(json) == 42);
        Assert.Contains(messages, json => EventType(json) == "overlay.updated" && PayloadFrameId(json) == 42);
        Assert.Contains(messages, json => EventType(json) == "telemetry.updated");
        Assert.Contains(messages, json => EventType(json) == "vision.status.updated" && json.Contains("\"status\":\"ready\""));
        Assert.Contains(messages, json => json.Contains("\"captureBackend\":\"WGC\"") && json.Contains("\"inferenceProvider\":\"cpu\""));
    }

    [Fact]
    public void StaleResultClearsOverlayAndReportsRepairing()
    {
        var sink = new RecordingSink();
        List<string> messages = [];
        long now = 2000;
        var telemetry = new RuntimeTelemetryCollector(InferenceProvider.Cpu, () => now, () => DateTimeOffset.UnixEpoch, () => 256);
        var publisher = new ObservationEventPublisher(sink, messages.Add, telemetry, () => SessionState.Observing, () => now, "maple-yolo-v1");

        VisionPipelineResult stale = WithStatus(Result(51, freshUntil: 1500), VisionPipelineStatus.StaleResult, "VISION_FRAME_ID_MISMATCH");
        publisher.Publish(Publication(stale, 51));

        Assert.Null(sink.Overlay!.Self);
        Assert.Empty(sink.Overlay.Monsters);
        Assert.DoesNotContain(messages, json => EventType(json) == "observation.updated");
        Assert.Contains(messages, json => EventType(json) == "vision.status.updated" && json.Contains("\"status\":\"repairing\""));
    }

    [Fact]
    public void TransientMissRetainsLastGoodOverlayWithinDisplayGracePeriod()
    {
        var sink = new RecordingSink();
        List<string> messages = [];
        long now = 1050;
        var telemetry = new RuntimeTelemetryCollector(InferenceProvider.Cpu, () => now, () => DateTimeOffset.UnixEpoch, () => 256);
        var publisher = new ObservationEventPublisher(sink, messages.Add, telemetry, () => SessionState.Observing, () => now, "maple-yolo-v1");
        publisher.Publish(Publication(Result(42, freshUntil: 1150), 42));

        now = 1220;
        VisionPipelineResult stale = WithStatus(Result(43, freshUntil: 1150), VisionPipelineStatus.StaleResult, "VISION_FRAME_ID_MISMATCH");
        publisher.Publish(Publication(stale, 43));

        Assert.NotNull(sink.Overlay!.Self);
        Assert.Single(sink.Overlay.Monsters);
        Assert.True(sink.Overlay.Self!.FreshUntilMonoMs > now);
        Assert.DoesNotContain(messages, json => EventType(json) == "observation.updated" && PayloadFrameId(json) == 43);
    }

    private static VisionRuntimePublication Publication(VisionPipelineResult result, long frameId) => new(
        result,
        new CaptureFrameMetadata { SchemaVersion = 2, FrameId = frameId, CapturedAtMonoMs = 1000, ClientWidth = 1280, ClientHeight = 720, Dpi = 96, CaptureBackend = CaptureBackend.Wgc, DroppedReason = DroppedFrameReason.None },
        Target(), 24, 8, 0);

    private static VisionPipelineResult Result(long frameId, long freshUntil)
    {
        SelfObservation self = new() { Box = [0.4, 0.5, 0.08, 0.16], Confidence = 0.95, FreshUntilMonoMs = freshUntil };
        MonsterObservation monster = new() { TargetId = "mob-1", Class = "mob", Box = [0.7, 0.5, 0.1, 0.1], Confidence = 0.9, FreshUntilMonoMs = freshUntil };
        var observation = new ObservationSnapshot
        {
            SchemaVersion = 2, FrameId = frameId, CapturedAtMonoMs = 1000, Target = Target(), Self = self, Players = [], Monsters = [monster],
            Loot = new LootObservation { Visible = false, Confidence = 0.9, FreshUntilMonoMs = freshUntil },
            Hp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.8, Confidence = 0.9, FreshUntilMonoMs = freshUntil },
            Mp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.7, Confidence = 0.9, FreshUntilMonoMs = freshUntil },
            Map = new MapObservation { MapId = "forest-east", State = MapArchiveState.Validated, Confidence = 0.9, FreshUntilMonoMs = freshUntil }, State = SessionState.Observing,
        };
        return new VisionPipelineResult
        {
            Status = VisionPipelineStatus.Ready, Observation = observation, Diagnostic = "OK",
            Dynamic = new DynamicVisionResult { FrameId = frameId, Self = self, Monsters = [monster], ModelVersion = "maple-yolo-v1", CanDriveActions = true, Diagnostic = "OK" },
        };
    }

    private static VisionPipelineResult WithStatus(VisionPipelineResult source, VisionPipelineStatus status, string diagnostic) => new()
    {
        Status = status, Observation = source.Observation, Dynamic = source.Dynamic, Diagnostic = diagnostic,
    };

    private static TargetBinding Target() => new() { SchemaVersion = 2, Hwnd = "0x1", Pid = 7, ClientWidth = 1280, ClientHeight = 720, Dpi = 96 };
    private static string EventType(string json) => JsonDocument.Parse(json).RootElement.GetProperty("type").GetString()!;
    private static long PayloadFrameId(string json) => JsonDocument.Parse(json).RootElement.GetProperty("payload").GetProperty("frameId").GetInt64();

    private sealed class RecordingSink : INativeVisionSink
    {
        public PreviewOverlay? Overlay { get; private set; }
        public long? TelemetryFrameId { get; private set; }
        public void PublishOverlay(PreviewOverlay snapshot) => Overlay = snapshot;
        public void PublishTelemetry(PreviewTelemetrySnapshot snapshot, long frameId) => TelemetryFrameId = frameId;
    }
}
