using System;

namespace Maple.Map
{
    public sealed class MapScanRegistrar
    {
        private readonly MapWorld world;

        public MapScanRegistrar(MapWorld world)
        {
            this.world = world ?? throw new ArgumentNullException("world");
        }

        public MapWorld World { get { return world; } }

        public void RegisterFrame(MapSourceFrame frame, CameraTransform transform)
        {
            EnsureCandidate();
            if (frame == null || transform == null) throw new ArgumentNullException(frame == null ? "frame" : "transform");
            if (frame.FrameId != transform.FrameId) throw new InvalidOperationException("来源帧与相机变换必须引用同一 frameId");
            world.SourceFrames.Add(frame);
            world.CameraTransforms.Add(transform);
        }

        public void UpdateScanMetrics(double coverage, double calibrationErrorPx)
        {
            EnsureCandidate();
            if (coverage < 0 || coverage > 1) throw new ArgumentOutOfRangeException("coverage");
            if (calibrationErrorPx < 0) throw new ArgumentOutOfRangeException("calibrationErrorPx");
            world.Coverage = coverage;
            world.CalibrationErrorPx = calibrationErrorPx;
        }

        private void EnsureCandidate()
        {
            if (world.State != Maple.Contracts.MapArchiveState.Candidate) throw new InvalidOperationException("已验证或归档地图不能继续写入扫描结果");
        }
    }
}
