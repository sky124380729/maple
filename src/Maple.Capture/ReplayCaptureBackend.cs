using System.Buffers;
using Maple.Contracts;

namespace Maple.Capture;

public sealed record ReplayFrame(int Width, int Height, int Stride, CapturedPixelFormat PixelFormat, ReadOnlyMemory<byte> Pixels);

public sealed class ReplayCaptureBackend(IEnumerable<ReplayFrame> frames) : ICaptureBackend
{
    private readonly Queue<ReplayFrame> frames = new(frames ?? throw new ArgumentNullException(nameof(frames)));

    public CaptureBackend Backend => CaptureBackend.BitBlt;

    public ValueTask<CaptureResult> CaptureAsync(CaptureTarget target, long frameId, long nowMonoMs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (frames.Count == 0) return ValueTask.FromResult(Failure(target, frameId, nowMonoMs, "REPLAY_EXHAUSTED"));
        ReplayFrame source = frames.Dequeue();
        var owner = MemoryPool<byte>.Shared.Rent(source.Pixels.Length);
        source.Pixels.CopyTo(owner.Memory);
        var metadata = Metadata(target, frameId, nowMonoMs, source.Width, source.Height, DroppedFrameReason.None);
        var frame = new CapturedFrame(metadata, source.Width, source.Height, source.Stride, source.PixelFormat, owner, source.Pixels.Length);
        return ValueTask.FromResult(new CaptureResult { Success = true, Frame = frame, Metadata = metadata, Reason = "OK" });
    }

    private CaptureResult Failure(CaptureTarget target, long frameId, long nowMonoMs, string reason)
    {
        CaptureFrameMetadata metadata = Metadata(target, frameId, nowMonoMs, target.ClientWidth, target.ClientHeight, DroppedFrameReason.Invalid);
        return new CaptureResult { Success = false, Metadata = metadata, Reason = reason };
    }

    private CaptureFrameMetadata Metadata(CaptureTarget target, long frameId, long nowMonoMs, int width, int height, DroppedFrameReason dropped) => new()
    {
        SchemaVersion = ContractConstants.SchemaVersion,
        FrameId = frameId,
        CapturedAtMonoMs = nowMonoMs,
        ClientWidth = width,
        ClientHeight = height,
        Dpi = target.Dpi,
        CaptureBackend = Backend,
        CaptureDurationMs = 0,
        DroppedReason = dropped,
    };
}
