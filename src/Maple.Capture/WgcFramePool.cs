namespace Maple.Capture;

/// <summary>Latest-frame-wins pool. It owns at most two frames and never grows an unbounded queue.</summary>
public sealed class WgcFramePool : IDisposable
{
    private readonly CapturedFrame?[] slots = new CapturedFrame?[2];
    private int publishedSlot = -1;
    private long droppedFrames;
    private bool disposed;

    public long DroppedFrames => Interlocked.Read(ref droppedFrames);
    public int Capacity => slots.Length;

    public void Publish(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (slots)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            int next = publishedSlot < 0 ? 0 : (publishedSlot + 1) & 1;
            if (slots[next] is not null)
            {
                slots[next]!.Dispose();
                slots[next] = null;
                Interlocked.Increment(ref droppedFrames);
            }
            slots[next] = frame;
            Volatile.Write(ref publishedSlot, next);
        }
    }

    public bool TryTakeLatest(out CapturedFrame? frame)
    {
        lock (slots)
        {
            if (disposed || publishedSlot < 0 || slots[publishedSlot] is null) { frame = null; return false; }
            int slot = publishedSlot;
            frame = slots[slot];
            slots[slot] = null;
            publishedSlot = -1;
            return true;
        }
    }

    public void Dispose()
    {
        lock (slots)
        {
            if (disposed) return;
            disposed = true;
            for (int index = 0; index < slots.Length; index++) { slots[index]?.Dispose(); slots[index] = null; }
            publishedSlot = -1;
        }
    }
}
