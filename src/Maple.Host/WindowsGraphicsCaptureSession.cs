using Maple.Capture;

namespace Maple.Host;

public sealed record WgcNativeFrame(object NativeFrame, int Width, int Height, long CapturedAtMonoMs);

public interface IWgcFrameEncoder
{
    CapturedFrame Encode(WgcNativeFrame frame, long frameId, int dpi);
}

/// <summary>
/// WGC lifecycle boundary. The Windows adapter binds GraphicsCaptureItem and
/// Direct3D11CaptureFramePool; this class owns Maple's two-slot latest-frame handoff.
/// </summary>
public interface IWgcRuntimeAdapter : IDisposable
{
    void Start(object captureItem, object direct3DDevice, int width, int height, Action<WgcNativeFrame> frameArrived, Action<int, int> sizeChanged, Action<Exception> failed);
    void Stop();
}

public sealed class WindowsGraphicsCaptureSession(IWgcRuntimeAdapter runtime, IWgcFrameEncoder encoder) : IDisposable
{
    private readonly IWgcRuntimeAdapter runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly IWgcFrameEncoder encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
    private int dpi = 96;
    private long frameId;
    private bool disposed;

    public WgcFramePool LatestFrames { get; } = new();
    public string Status { get; private set; } = "WGC_STOPPED";
    public event EventHandler<string>? Diagnostic;

    public void Start(object captureItem, object direct3DDevice, int width, int height, int targetDpi = 96)
    {
        ArgumentNullException.ThrowIfNull(captureItem);
        ArgumentNullException.ThrowIfNull(direct3DDevice);
        ObjectDisposedException.ThrowIf(disposed, this);
        Stop();
        dpi = targetDpi;
        runtime.Start(captureItem, direct3DDevice, width, height, OnFrameArrived, OnSizeChanged, OnFailed);
        Status = "WGC_RUNNING";
        Diagnostic?.Invoke(this, Status);
    }

    private void OnFrameArrived(WgcNativeFrame frame)
    {
        try
        {
            LatestFrames.Publish(encoder.Encode(frame, Interlocked.Increment(ref frameId), dpi));
        }
        catch (Exception exception) { OnFailed(exception); }
    }

    private void OnSizeChanged(int width, int height) => Diagnostic?.Invoke(this, $"WGC_FRAME_POOL_RECREATED:{width}x{height}");

    private void OnFailed(Exception exception)
    {
        Status = "WGC_FRAME_FAILED";
        Diagnostic?.Invoke(this, Status + ":" + exception.GetType().Name);
        if (LatestFrames.TryTakeLatest(out CapturedFrame? abandoned)) abandoned?.Dispose();
    }

    public void Stop()
    {
        runtime.Stop();
        if (LatestFrames.TryTakeLatest(out CapturedFrame? frame)) frame?.Dispose();
        if (!disposed) Status = "WGC_STOPPED";
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
        runtime.Dispose();
        LatestFrames.Dispose();
    }
}
