using System.Buffers;
using System.Threading.Channels;
using Maple.Capture;
using Maple.Contracts;

namespace Maple.Host;

public sealed class LatestVisionFrameQueue : ICaptureFrameObserver, IDisposable
{
    private readonly object sync = new();
    private readonly Channel<CapturedFrame> channel;
    private bool disposed;
    private long droppedFrames;

    public LatestVisionFrameQueue(int capacity = 1)
    {
        if (capacity is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(capacity));
        channel = Channel.CreateBounded<CapturedFrame>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    }

    public long DroppedFrames => Interlocked.Read(ref droppedFrames);

    public void Observe(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        CapturedFrame owned = Copy(frame);
        lock (sync)
        {
            if (disposed)
            {
                owned.Dispose();
                throw new ObjectDisposedException(nameof(LatestVisionFrameQueue));
            }
            while (!channel.Writer.TryWrite(owned))
            {
                if (!channel.Reader.TryRead(out CapturedFrame? dropped)) continue;
                dropped.Dispose();
                Interlocked.Increment(ref droppedFrames);
            }
        }
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) => channel.Reader.WaitToReadAsync(cancellationToken);

    public CapturedFrame TakeLatest(CancellationToken cancellationToken)
    {
        CapturedFrame latest = channel.Reader.ReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
        while (channel.Reader.TryRead(out CapturedFrame? newer))
        {
            latest.Dispose();
            latest = newer;
            Interlocked.Increment(ref droppedFrames);
        }
        return latest;
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            channel.Writer.TryComplete();
            while (channel.Reader.TryRead(out CapturedFrame? frame)) frame.Dispose();
        }
    }

    private static CapturedFrame Copy(CapturedFrame source)
    {
        int length = source.Pixels.Length;
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(length);
        try
        {
            source.Pixels.Span.CopyTo(owner.Memory.Span);
            return new CapturedFrame(Clone(source.Metadata), source.Width, source.Height, source.Stride, source.PixelFormat, owner, length);
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    private static CaptureFrameMetadata Clone(CaptureFrameMetadata source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        FrameId = source.FrameId,
        CapturedAtMonoMs = source.CapturedAtMonoMs,
        ClientWidth = source.ClientWidth,
        ClientHeight = source.ClientHeight,
        Dpi = source.Dpi,
        CaptureBackend = source.CaptureBackend,
        CaptureDurationMs = source.CaptureDurationMs,
        DroppedReason = source.DroppedReason,
    };
}
