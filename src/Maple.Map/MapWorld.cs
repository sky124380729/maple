using System;
using System.Collections.Generic;
using Maple.Contracts;

namespace Maple.Map
{
    public sealed class MapWorld
    {
        public MapWorld(string mapId, int schemaVersion)
        {
            if (string.IsNullOrWhiteSpace(mapId)) throw new ArgumentException("mapId");
            MapId = mapId;
            SchemaVersion = schemaVersion;
            State = MapArchiveState.Candidate;
            SourceFrames = new List<MapSourceFrame>();
            CameraTransforms = new List<CameraTransform>();
            Platforms = new List<PlatformNode>();
            Ladders = new List<LadderNode>();
            Boundaries = new List<MapBoundary>();
            Edges = new List<TopologyEdge>();
            UnresolvedStructures = new List<string>();
        }

        public int SchemaVersion { get; private set; }
        public string MapId { get; private set; }
        public MapArchiveState State { get; private set; }
        public double Coverage { get; set; }
        public double CalibrationErrorPx { get; set; }
        public List<MapSourceFrame> SourceFrames { get; private set; }
        public List<CameraTransform> CameraTransforms { get; private set; }
        public List<PlatformNode> Platforms { get; private set; }
        public List<LadderNode> Ladders { get; private set; }
        public List<MapBoundary> Boundaries { get; private set; }
        public List<TopologyEdge> Edges { get; private set; }
        public List<string> UnresolvedStructures { get; private set; }
        public TopologyValidationReport ValidationReport { get; private set; }
        public bool CanProduceActions { get { return State == MapArchiveState.Validated && ValidationReport != null && ValidationReport.IsValid; } }

        public void ApplyValidation(TopologyValidationReport report)
        {
            if (report == null) throw new ArgumentNullException("report");
            if (State != MapArchiveState.Candidate) throw new InvalidOperationException("只有候选地图可以应用验证报告");
            ValidationReport = report;
            if (report.IsValid) State = MapArchiveState.Validated;
        }

        public void Archive()
        {
            State = MapArchiveState.Archived;
        }
    }

    public sealed class MapSourceFrame
    {
        public long FrameId { get; set; }
        public long CapturedAtMonoMs { get; set; }
        public string ImageReference { get; set; }
    }

    public sealed class CameraTransform
    {
        public long FrameId { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double Confidence { get; set; }
    }

    public sealed class PlatformNode
    {
        public string PlatformId { get; set; }
        public double X1 { get; set; }
        public double X2 { get; set; }
        public double Y { get; set; }
        public double SafeMarginPx { get; set; }
    }

    public sealed class LadderNode
    {
        public string LadderId { get; set; }
        public string FromPlatformId { get; set; }
        public string ToPlatformId { get; set; }
        public double X { get; set; }
    }

    public sealed class MapBoundary
    {
        public string BoundaryId { get; set; }
        public string PlatformId { get; set; }
        public double X { get; set; }
        public BoundaryKind Kind { get; set; }
    }

    public sealed class TopologyEdge
    {
        public string EdgeId { get; set; }
        public string FromPlatformId { get; set; }
        public string ToPlatformId { get; set; }
        public TopologyEdgeType Type { get; set; }
        public double MaximumDistancePx { get; set; }
    }

    public enum BoundaryKind { LeftEdge, RightEdge, Portal, Unknown }
    public enum TopologyEdgeType { Walk, Jump, Climb, Drop }
}
