using Maple.Vision;

namespace Maple.Host;

public sealed class MapScanRuntimeController(MapScanFrameStore frames, CameraTransformTracker cameraTracker) : IMapScanController
{
    private readonly MapScanFrameStore frames = frames ?? throw new ArgumentNullException(nameof(frames));
    private readonly CameraTransformTracker cameraTracker = cameraTracker ?? throw new ArgumentNullException(nameof(cameraTracker));

    public void StartScan()
    {
        cameraTracker.Reset();
        frames.StartScan();
    }

    public void StopScan() => frames.StopScan();
}
