using Maple.Capture;
using Maple.Contracts;

namespace Maple.Host;

public interface ICaptureFrameSink
{
    void Publish(CapturedFrame frame);
}

public interface ICaptureFrameObserver
{
    void Observe(CapturedFrame frame);
}

public sealed record CaptureTickResult(bool Success, string Code, long? FrameId = null);

public sealed class CaptureCoordinator : IDisposable
{
    private readonly ITargetWindowLocator targetLocator;
    private readonly ICaptureBackend captureBackend;
    private readonly ICaptureFrameSink frameSink;
    private readonly HostSafetyCoordinator safety;
    private readonly Func<long> clock;
    private readonly ICaptureFrameObserver? frameObserver;
    private WindowIdentity? activeTarget;
    private long frameId;
    private string? activePauseCode;
    private bool disposed;

    public TargetBinding? ActiveTargetBinding => activeTarget is null ? null : new TargetBinding
    {
        SchemaVersion = ContractConstants.SchemaVersion,
        Hwnd = activeTarget.Hwnd,
        Pid = activeTarget.Pid,
        StartedAtUtc = activeTarget.ProcessStartedAtUtc,
        ExecutablePath = activeTarget.ProcessPath,
        ClientWidth = activeTarget.ClientWidth,
        ClientHeight = activeTarget.ClientHeight,
        Dpi = activeTarget.Dpi,
    };

    public CaptureCoordinator(
        ITargetWindowLocator targetLocator,
        ICaptureBackend captureBackend,
        ICaptureFrameSink frameSink,
        HostSafetyCoordinator safety,
        Func<long>? clock = null,
        ICaptureFrameObserver? frameObserver = null)
    {
        this.targetLocator = targetLocator ?? throw new ArgumentNullException(nameof(targetLocator));
        this.captureBackend = captureBackend ?? throw new ArgumentNullException(nameof(captureBackend));
        this.frameSink = frameSink ?? throw new ArgumentNullException(nameof(frameSink));
        this.safety = safety ?? throw new ArgumentNullException(nameof(safety));
        this.clock = clock ?? (() => Environment.TickCount64);
        this.frameObserver = frameObserver;
    }

    public async ValueTask<CaptureTickResult> CaptureOnceAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        TargetWindowDiscoveryResult discovery = targetLocator.Locate();
        WindowIdentity? target = discovery.Target;
        if (target is null) return Pause(discovery.DiagnosticCode);
        if (target.IsMinimized) return Pause("TARGET_MINIMIZED");
        if (!target.IsForeground) return Pause("TARGET_NOT_FOREGROUND");
        if (target.ClientWidth < 640 || target.ClientHeight < 360) return Pause("INVALID_CLIENT_BOUNDS");
        if (activeTarget is null) activeTarget = target;
        else if (!SameIdentity(activeTarget, target)) return Pause("TARGET_IDENTITY_CHANGED");
        else if (!SameClientGeometry(activeTarget, target)) return Pause("TARGET_CLIENT_CHANGED");

        long nextFrameId = Interlocked.Increment(ref frameId);
        long nowMonoMs = clock();
        var captureTarget = new CaptureTarget
        {
            Hwnd = target.Hwnd,
            Pid = target.Pid,
            ClientLeft = target.ClientLeft,
            ClientTop = target.ClientTop,
            ClientWidth = target.ClientWidth,
            ClientHeight = target.ClientHeight,
            Dpi = target.Dpi,
            IsForeground = target.IsForeground,
            IsMinimized = target.IsMinimized,
        };

        CaptureResult result;
        try
        {
            result = await captureBackend
                .CaptureAsync(captureTarget, nextFrameId, nowMonoMs, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return Pause("CAPTURE_EXCEPTION:" + exception.GetType().Name); }

        if (!result.Success || result.Frame is null)
        {
            result.Frame?.Dispose();
            return Pause(result.Reason);
        }

        CapturedFrame frame = result.Frame;
        if (IsBlackFrame(frame))
        {
            frame.Dispose();
            return Pause("CAPTURE_BLACK_FRAME");
        }

        try { frameObserver?.Observe(frame); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            frame.Dispose();
            return Pause("MAP_FRAME_OBSERVER_FAILED");
        }

        try { frameSink.Publish(frame); }
        catch (Exception exception)
        {
            frame.Dispose();
            return Pause("PREVIEW_PUBLISH_FAILED:" + exception.GetType().Name);
        }

        activePauseCode = null;
        return new CaptureTickResult(true, result.Reason, frame.Metadata.FrameId);
    }

    private CaptureTickResult Pause(string code)
    {
        string normalizedCode = string.IsNullOrWhiteSpace(code) ? "CAPTURE_FAILED" : code;
        if (!string.Equals(activePauseCode, normalizedCode, StringComparison.Ordinal))
        {
            safety.PauseAndRelease();
            activePauseCode = normalizedCode;
        }
        return new CaptureTickResult(false, normalizedCode);
    }

    private static bool IsBlackFrame(CapturedFrame frame)
    {
        if (frame.PixelFormat is not CapturedPixelFormat.Bgra32 and not CapturedPixelFormat.Rgba32) return false;
        ReadOnlySpan<byte> pixels = frame.Pixels.Span;
        int pixelCount = frame.Width * frame.Height;
        int sampleStride = Math.Max(1, pixelCount / 4096) * 4;
        for (int offset = 0; offset + 2 < pixels.Length; offset += sampleStride)
        {
            if (pixels[offset] > 2 || pixels[offset + 1] > 2 || pixels[offset + 2] > 2) return false;
        }
        return true;
    }

    private static bool SameIdentity(WindowIdentity expected, WindowIdentity actual)
    {
        return string.Equals(expected.Hwnd, actual.Hwnd, StringComparison.OrdinalIgnoreCase)
            && expected.Pid == actual.Pid
            && expected.ProcessStartedAtUtc == actual.ProcessStartedAtUtc
            && string.Equals(expected.ProcessPathSha256, actual.ProcessPathSha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(expected.ClassName, actual.ClassName, StringComparison.Ordinal)
            && string.Equals(expected.ProcessVersion, actual.ProcessVersion, StringComparison.Ordinal);
    }

    private static bool SameClientGeometry(WindowIdentity expected, WindowIdentity actual)
    {
        return expected.ClientWidth == actual.ClientWidth
            && expected.ClientHeight == actual.ClientHeight
            && expected.Dpi == actual.Dpi;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (captureBackend is IDisposable disposable) disposable.Dispose();
        if (!ReferenceEquals(frameObserver, captureBackend) && frameObserver is IDisposable observerDisposable) observerDisposable.Dispose();
    }
}
