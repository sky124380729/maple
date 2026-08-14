using System.Collections.Generic;
using Maple.Contracts;

namespace Maple.Preview
{
    /// <summary>
    /// Native preview overlay payload. It intentionally contains only dynamic
    /// detections; fixed HUD, HP/MP and loot never reach the drawing surface.
    /// </summary>
    public sealed class OverlaySnapshot
    {
        public int SchemaVersion { get; set; }
        public long FrameId { get; set; }
        public long GeneratedAtMonoMs { get; set; }
        public SelfObservation Self { get; set; }
        public List<PlayerObservation> Players { get; set; }
        public List<MonsterObservation> Monsters { get; set; }
    }

    public static class OverlayColors
    {
        public const string Self = "#42d392";
        public const string Player = "#55c7f7";
        public const string Monster = "#ff6474";
    }
}
