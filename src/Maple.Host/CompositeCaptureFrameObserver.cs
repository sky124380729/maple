using Maple.Capture;

namespace Maple.Host;

public sealed class CompositeCaptureFrameObserver : ICaptureFrameObserver, IDisposable
{
    private readonly ICaptureFrameObserver[] observers;
    private bool disposed;

    public CompositeCaptureFrameObserver(params ICaptureFrameObserver[] observers)
    {
        ArgumentNullException.ThrowIfNull(observers);
        if (observers.Any(observer => observer is null)) throw new ArgumentException("观察者不能为空", nameof(observers));
        this.observers = observers.ToArray();
    }

    public void Observe(CapturedFrame frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        foreach (ICaptureFrameObserver observer in observers) observer.Observe(frame);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var seen = new HashSet<IDisposable>(ReferenceEqualityComparer.Instance);
        foreach (ICaptureFrameObserver observer in observers)
        {
            if (observer is IDisposable disposable && seen.Add(disposable)) disposable.Dispose();
        }
    }
}
