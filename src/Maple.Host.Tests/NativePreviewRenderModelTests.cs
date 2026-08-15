using System.Collections.Generic;
using Maple.Contracts;
using Maple.Preview;
using Xunit;
using PreviewOverlaySnapshot = Maple.Preview.OverlaySnapshot;

namespace Maple.Host.Tests;

public sealed class NativePreviewRenderModelTests
{
    [Fact]
    public void BuildHidesExpiredBoxesAndEmphasizesSelectedMonster()
    {
        PreviewOverlaySnapshot snapshot = CreateSnapshot();
        snapshot.SelectedTargetId = "monster-7";
        snapshot.Monsters.Add(new MonsterObservation
        {
            Class = "蜗牛",
            TargetId = "expired",
            Confidence = 0.75,
            Box = [0.1, 0.7, 0.1, 0.1],
            FreshUntilMonoMs = 500,
        });

        PreviewRenderModel model = PreviewRenderModel.Build(snapshot, CreateTelemetry(), nowMonoMs: 500);

        Assert.Single(model.Monsters);
        Assert.Equal("monster-7", model.Monsters[0].TargetId);
        Assert.True(model.Monsters[0].Selected);
        Assert.DoesNotContain(model.Markers, marker => marker.Kind == "loot");
        Assert.Equal("snail-v1", model.ModelVersion);
    }

    [Fact]
    public void BuildRejectsInvalidBoxesAndSelectsUnoccupiedHudCorner()
    {
        PreviewOverlaySnapshot snapshot = CreateSnapshot();
        snapshot.Self!.Box = [0.02, 0.02, 0.18, 0.2];
        snapshot.Players =
        [
            new PlayerObservation { TrackId = "player-1", Confidence = 0.9, Box = [0.8, 0.02, 0.18, 0.2], FreshUntilMonoMs = 700 },
            new PlayerObservation { TrackId = "invalid", Confidence = 0.9, Box = [0.95, 0.5, 0.2, 0.2], FreshUntilMonoMs = 700 },
        ];
        snapshot.Monsters[0].Box = [0.02, 0.78, 0.18, 0.2];

        PreviewRenderModel model = PreviewRenderModel.Build(snapshot, CreateTelemetry(), nowMonoMs: 500);

        Assert.Single(model.Players);
        Assert.Equal(PreviewHudCorner.BottomRight, model.HudCorner);
    }

    [Fact]
    public void BuildUsesWarningForSlowFramesAndCriticalForSafetyWarnings()
    {
        PreviewTelemetrySnapshot slow = CreateTelemetry() with { QueueAgeMs = 140, FrameLatencyMs = 120 };
        PreviewRenderModel warning = PreviewRenderModel.Build(CreateSnapshot(), slow, nowMonoMs: 500);
        Assert.Equal(PreviewHudSeverity.Warning, warning.TelemetrySeverity);

        PreviewTelemetrySnapshot stale = slow with { WarningCode = "STALE_FRAME" };
        PreviewRenderModel critical = PreviewRenderModel.Build(CreateSnapshot(), stale, nowMonoMs: 500);
        Assert.Equal(PreviewHudSeverity.Critical, critical.TelemetrySeverity);
        Assert.Equal(4, critical.HudBands.Count);
    }

    private static PreviewOverlaySnapshot CreateSnapshot() => new()
    {
        SchemaVersion = 2,
        FrameId = 42,
        GeneratedAtMonoMs = 400,
        ModelVersion = "snail-v1",
        Self = new SelfObservation { Confidence = 0.98, Box = [0.45, 0.45, 0.08, 0.16], FreshUntilMonoMs = 700 },
        Players = [],
        Monsters =
        [
            new MonsterObservation { Class = "蜗牛", TargetId = "monster-7", Confidence = 0.91, Box = [0.7, 0.5, 0.1, 0.1], FreshUntilMonoMs = 700 },
        ],
    };

    private static PreviewTelemetrySnapshot CreateTelemetry() => new(
        CaptureFps: 60,
        RenderFps: 59,
        RecognitionFps: 28,
        FrameLatencyMs: 45,
        DetectorLatencyMs: 24,
        QueueAgeMs: 10,
        CaptureBackend: "WGC",
        InferenceProvider: "directml",
        DroppedFrames: 0,
        ProcessMemoryMb: 420,
        SessionState: "Observing",
        LastAction: "MoveRight",
        WarningCode: null);
}
