using Maple.Contracts;

namespace Maple.Capture;

public interface IWindowFrameSource
{
    ValueTask<CapturedFrame?> TryCaptureAsync(CaptureTarget target, long frameId, long nowMonoMs, CancellationToken cancellationToken);
}

public sealed class WindowsGraphicsCaptureBackend(IWindowFrameSource? wgcSource, IWindowFrameSource? bitBltSource = null) : ICaptureBackend
{
    public CaptureBackend Backend => CaptureBackend.Wgc;

    public async ValueTask<CaptureResult> CaptureAsync(CaptureTarget target, long frameId, long nowMonoMs, CancellationToken cancellationToken)
    {
        string? validation = CaptureValidation.ValidateTarget(target);
        if (validation is not null) return Failure(target, frameId, nowMonoMs, validation, CaptureBackend.Wgc);
        if (wgcSource is not null)
        {
            CapturedFrame? frame = await wgcSource.TryCaptureAsync(target, frameId, nowMonoMs, cancellationToken).ConfigureAwait(false);
            if (frame is not null) return new CaptureResult { Success = true, Frame = frame, Metadata = frame.Metadata, Reason = "OK" };
        }
        if (bitBltSource is not null)
        {
            CapturedFrame? fallback = await bitBltSource.TryCaptureAsync(target, frameId, nowMonoMs, cancellationToken).ConfigureAwait(false);
            if (fallback is not null) return new CaptureResult { Success = true, Frame = fallback, Metadata = fallback.Metadata, Reason = "BITBLT_FALLBACK" };
        }
        return Failure(target, frameId, nowMonoMs, wgcSource is null ? "WGC_RUNTIME_NOT_BOUND" : "CAPTURE_FRAME_UNAVAILABLE", bitBltSource is null ? CaptureBackend.Wgc : CaptureBackend.BitBlt);
    }

    private static CaptureResult Failure(CaptureTarget target, long frameId, long nowMonoMs, string reason, CaptureBackend backend) => new()
    {
        Success = false,
        Reason = reason,
        Metadata = CaptureValidation.Metadata(target, frameId, nowMonoMs, backend, DroppedFrameReason.Invalid),
    };
}

internal static class CaptureValidation
{
    internal static string? ValidateTarget(CaptureTarget? target)
    {
        if (target is null) return "TARGET_NOT_BOUND";
        if (target.IsMinimized) return "TARGET_MINIMIZED";
        if (!target.IsForeground) return "TARGET_NOT_FOREGROUND";
        if (target.ClientWidth < 100 || target.ClientHeight < 100) return "INVALID_CLIENT_BOUNDS";
        return null;
    }

    internal static CaptureFrameMetadata Metadata(CaptureTarget? target, long frameId, long nowMonoMs, CaptureBackend backend, DroppedFrameReason dropped) => new()
    {
        SchemaVersion = ContractConstants.SchemaVersion,
        FrameId = frameId,
        CapturedAtMonoMs = nowMonoMs,
        ClientWidth = target?.ClientWidth ?? 0,
        ClientHeight = target?.ClientHeight ?? 0,
        Dpi = target?.Dpi ?? 96,
        CaptureBackend = backend,
        CaptureDurationMs = 0,
        DroppedReason = dropped,
    };
}
