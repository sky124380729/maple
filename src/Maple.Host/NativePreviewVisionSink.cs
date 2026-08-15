using Maple.Preview;

namespace Maple.Host;

public sealed class NativePreviewVisionSink(
    NativePreviewSurface surface,
    Action<Action> dispatch) : INativeVisionSink
{
    private readonly NativePreviewSurface surface = surface ?? throw new ArgumentNullException(nameof(surface));
    private readonly Action<Action> dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
    public void PublishOverlay(OverlaySnapshot snapshot) => dispatch(() => surface.PublishOverlay(snapshot, snapshot.GeneratedAtMonoMs));
    public void PublishTelemetry(PreviewTelemetrySnapshot snapshot, long frameId) => dispatch(() => surface.PublishTelemetry(snapshot));
}
