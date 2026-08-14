using System;
using System.Collections.Generic;
using System.Linq;

namespace Maple.Map
{
    public sealed class TopologyValidationOptions
    {
        public int SupportedSchemaVersion { get; set; }
        public double MinimumCoverage { get; set; }
        public double MaximumCalibrationErrorPx { get; set; }
        public double MinimumPlatformLengthPx { get; set; }
    }

    public sealed class TopologyValidationReport
    {
        public TopologyValidationReport()
        {
            Errors = new List<string>();
            Warnings = new List<string>();
        }

        public bool IsValid { get { return Errors.Count == 0; } }
        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }
    }

    public sealed class TopologyValidator
    {
        private readonly TopologyValidationOptions options;

        public TopologyValidator(TopologyValidationOptions options)
        {
            this.options = options ?? throw new ArgumentNullException("options");
            if (this.options.MinimumPlatformLengthPx <= 0) this.options.MinimumPlatformLengthPx = 32;
        }

        public TopologyValidationReport Validate(MapWorld world)
        {
            var report = new TopologyValidationReport();
            if (world == null)
            {
                report.Errors.Add("地图为空");
                return report;
            }
            if (world.SchemaVersion != options.SupportedSchemaVersion) report.Errors.Add("地图 schemaVersion 不兼容");
            if (world.Coverage < options.MinimumCoverage) report.Errors.Add("地图覆盖率不足");
            if (world.CalibrationErrorPx > options.MaximumCalibrationErrorPx) report.Errors.Add("地图标定误差超限");
            if (world.SourceFrames.Count == 0 || world.CameraTransforms.Count == 0) report.Errors.Add("缺少来源帧或相机变换");
            if (world.UnresolvedStructures.Count > 0) report.Errors.Add("仍有未解析地图结构");

            var platformIds = new HashSet<string>(world.Platforms.Where(IsValidPlatform).Select(platform => platform.PlatformId), StringComparer.Ordinal);
            if (platformIds.Count != world.Platforms.Count) report.Errors.Add("平台编号重复或平台尺寸无效");
            foreach (LadderNode ladder in world.Ladders)
            {
                if (ladder == null || string.IsNullOrWhiteSpace(ladder.LadderId) || !platformIds.Contains(ladder.FromPlatformId) || !platformIds.Contains(ladder.ToPlatformId))
                {
                    report.Errors.Add("梯子端点未连接到有效平台");
                }
            }
            foreach (MapBoundary boundary in world.Boundaries)
            {
                if (boundary == null || !platformIds.Contains(boundary.PlatformId)) report.Errors.Add("边界未绑定有效平台");
            }
            foreach (TopologyEdge edge in world.Edges)
            {
                if (edge == null || string.IsNullOrWhiteSpace(edge.EdgeId) || !platformIds.Contains(edge.FromPlatformId) || !platformIds.Contains(edge.ToPlatformId) || edge.MaximumDistancePx <= 0)
                {
                    report.Errors.Add("拓扑边无效");
                }
                else if (edge.Type == TopologyEdgeType.Climb && !world.Ladders.Any(ladder => ladder.FromPlatformId == edge.FromPlatformId && ladder.ToPlatformId == edge.ToPlatformId))
                {
                    report.Errors.Add("攀爬边缺少对应梯子");
                }
            }
            if (world.Edges.Count == 0) report.Warnings.Add("地图没有可达边，运行时只能保持观察");
            return report;
        }

        private bool IsValidPlatform(PlatformNode platform)
        {
            return platform != null && !string.IsNullOrWhiteSpace(platform.PlatformId) && platform.X2 - platform.X1 >= options.MinimumPlatformLengthPx && platform.SafeMarginPx >= 0 && platform.SafeMarginPx * 2 < platform.X2 - platform.X1;
        }
    }
}
