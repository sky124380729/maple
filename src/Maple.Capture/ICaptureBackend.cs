using Maple.Contracts;

namespace Maple.Capture;

public sealed class CaptureTarget
{
    public required string Hwnd { get; init; }
    public int Pid { get; init; }
    public int ClientLeft { get; init; }
    public int ClientTop { get; init; }
    public int ClientWidth { get; init; }
    public int ClientHeight { get; init; }
    public int Dpi { get; init; } = 96;
    public bool IsForeground { get; init; }
    public bool IsMinimized { get; init; }
}

public sealed class CaptureResult
{
    public bool Success { get; init; }
    public CapturedFrame? Frame { get; init; }
    public required CaptureFrameMetadata Metadata { get; init; }
    public required string Reason { get; init; }
}

public interface ICaptureBackend
{
    CaptureBackend Backend { get; }
    ValueTask<CaptureResult> CaptureAsync(CaptureTarget target, long frameId, long nowMonoMs, CancellationToken cancellationToken);
}
