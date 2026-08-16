#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Maple.Contracts;

namespace Maple.Preview;

public enum PreviewHudSeverity { Normal, Warning, Critical }
public enum PreviewHudCorner { TopLeft, TopRight, BottomLeft, BottomRight }

public sealed record PreviewRenderMarker(
    string Kind,
    double[] Box,
    double Confidence,
    string Label,
    string? TargetId,
    bool Selected);

public sealed record PreviewHudBand(string Key, string Label, string Value, PreviewHudSeverity Severity);

public sealed class PreviewRenderModel
{
    private PreviewRenderModel(
        IReadOnlyList<PreviewRenderMarker> markers,
        IReadOnlyList<PreviewHudBand> hudBands,
        PreviewHudCorner hudCorner,
        PreviewHudSeverity telemetrySeverity,
        string modelVersion)
    {
        Markers = markers;
        HudBands = hudBands;
        HudCorner = hudCorner;
        TelemetrySeverity = telemetrySeverity;
        ModelVersion = modelVersion;
        Self = markers.FirstOrDefault(marker => marker.Kind == "self");
        Players = markers.Where(marker => marker.Kind == "player").ToArray();
        Monsters = markers.Where(marker => marker.Kind == "monster").ToArray();
    }

    public IReadOnlyList<PreviewRenderMarker> Markers { get; }
    public PreviewRenderMarker? Self { get; }
    public IReadOnlyList<PreviewRenderMarker> Players { get; }
    public IReadOnlyList<PreviewRenderMarker> Monsters { get; }
    public IReadOnlyList<PreviewHudBand> HudBands { get; }
    public PreviewHudCorner HudCorner { get; }
    public PreviewHudSeverity TelemetrySeverity { get; }
    public string ModelVersion { get; }

    public static PreviewRenderModel Build(OverlaySnapshot? snapshot, PreviewTelemetrySnapshot? telemetry, long nowMonoMs)
    {
        List<PreviewRenderMarker> markers = [];
        if (snapshot is not null)
        {
            AddSelf(markers, snapshot.Self, nowMonoMs);
            AddPlayers(markers, snapshot.Players, nowMonoMs);
            AddMonsters(markers, snapshot.Monsters, snapshot.SelectedTargetId, nowMonoMs);
        }

        PreviewHudSeverity severity = ResolveSeverity(telemetry);
        IReadOnlyList<PreviewHudBand> bands = BuildHudBands(telemetry, severity);
        return new PreviewRenderModel(markers, bands, PreviewHudCorner.TopLeft, severity, snapshot?.ModelVersion ?? "未加载");
    }

    private static void AddSelf(List<PreviewRenderMarker> markers, SelfObservation? self, long nowMonoMs)
    {
        if (self is null || self.FreshUntilMonoMs <= nowMonoMs || !ValidBox(self.Box)) return;
        markers.Add(new PreviewRenderMarker("self", self.Box, self.Confidence, $"自己 {FormatConfidence(self.Confidence)}", null, false));
    }

    private static void AddPlayers(List<PreviewRenderMarker> markers, List<PlayerObservation>? players, long nowMonoMs)
    {
        if (players is null) return;
        foreach (PlayerObservation player in players)
        {
            if (player is null || player.FreshUntilMonoMs <= nowMonoMs || !ValidBox(player.Box)) continue;
            markers.Add(new PreviewRenderMarker("player", player.Box, player.Confidence, $"玩家 {FormatConfidence(player.Confidence)}", null, false));
        }
    }

    private static void AddMonsters(List<PreviewRenderMarker> markers, List<MonsterObservation>? monsters, string? selectedTargetId, long nowMonoMs)
    {
        if (monsters is null) return;
        foreach (MonsterObservation monster in monsters)
        {
            if (monster is null || monster.FreshUntilMonoMs <= nowMonoMs || !ValidBox(monster.Box)) continue;
            bool selected = !string.IsNullOrWhiteSpace(selectedTargetId)
                && string.Equals(monster.TargetId, selectedTargetId, StringComparison.Ordinal);
            markers.Add(new PreviewRenderMarker("monster", monster.Box, monster.Confidence, $"怪 {FormatConfidence(monster.Confidence)}", monster.TargetId, selected));
        }
    }

    private static IReadOnlyList<PreviewHudBand> BuildHudBands(PreviewTelemetrySnapshot? telemetry, PreviewHudSeverity severity)
    {
        if (telemetry is null) return [];
        return
        [
            new PreviewHudBand(
                "telemetry",
                "FPS",
                $"{telemetry.CaptureFps:0}  |  识别 {telemetry.RecognitionFps:0}  |  延迟 {telemetry.FrameLatencyMs:0} ms",
                severity),
        ];
    }

    private static PreviewHudSeverity ResolveSeverity(PreviewTelemetrySnapshot? telemetry)
    {
        if (telemetry is null) return PreviewHudSeverity.Normal;
        string warning = telemetry.WarningCode ?? string.Empty;
        if (ContainsSafetyWarning(warning) || string.Equals(telemetry.SessionState, "EmergencyStop", StringComparison.OrdinalIgnoreCase))
            return PreviewHudSeverity.Critical;
        double detectorLimit = string.Equals(telemetry.InferenceProvider, "cpu", StringComparison.OrdinalIgnoreCase) ? 250 : 100;
        if (!string.IsNullOrWhiteSpace(warning)
            || telemetry.FrameLatencyMs > 100
            || telemetry.QueueAgeMs > 100
            || telemetry.DetectorLatencyMs > detectorLimit)
            return PreviewHudSeverity.Warning;
        return PreviewHudSeverity.Normal;
    }

    private static bool ContainsSafetyWarning(string warning)
    {
        string[] critical = ["STALE", "SAFETY", "FOREGROUND", "BLACK_FRAME", "WATCHDOG", "HEALTH_UNKNOWN", "INPUT_UNAVAILABLE"];
        return critical.Any(code => warning.Contains(code, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ValidBox(double[]? box) => box is { Length: 4 }
        && box.All(value => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1)
        && box[2] > 0 && box[3] > 0
        && box[0] + box[2] <= 1
        && box[1] + box[3] <= 1;

    private static string FormatConfidence(double value) => value.ToString("P0", CultureInfo.GetCultureInfo("zh-CN"));
}
