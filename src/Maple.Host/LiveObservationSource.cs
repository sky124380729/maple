using System.Threading.Channels;
using Maple.Contracts;
using Maple.Runtime;

namespace Maple.Host;

public sealed class LiveObservationSource : IObservationSource, IDisposable
{
    private readonly Channel<RuntimeObservationContext> channel;
    private readonly Func<ObservationSnapshot, bool, RuntimeObservationContext> contextFactory;
    private readonly object sync = new();
    private RuntimeObservationContext? latest;
    private bool latestCanDriveActions;
    private long dropped;
    private bool disposed;

    public LiveObservationSource(Func<ObservationSnapshot, bool, RuntimeObservationContext> contextFactory)
    {
        this.contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        channel = Channel.CreateBounded<RuntimeObservationContext>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public RuntimeObservationContext? Latest
    {
        get { lock (sync) return latest; }
    }

    public bool LatestCanDriveActions
    {
        get { lock (sync) return latestCanDriveActions; }
    }

    public long DroppedObservations => Interlocked.Read(ref dropped);

    public void Publish(ObservationSnapshot snapshot, bool canDriveActions)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        RuntimeObservationContext context = contextFactory(snapshot, canDriveActions);
        lock (sync)
        {
            latest = context;
            latestCanDriveActions = canDriveActions;
        }

        if (channel.Reader.TryPeek(out _)) Interlocked.Increment(ref dropped);
        if (!channel.Writer.TryWrite(context)) throw new InvalidOperationException("LIVE_OBSERVATION_CHANNEL_CLOSED");
    }

    public RuntimeObservationContext? RefreshLatest()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (sync)
        {
            if (latest is null) return null;
            latest = contextFactory(latest.Snapshot, latestCanDriveActions);
            return latest;
        }
    }

    public async ValueTask<RuntimeObservationContext> ReadNextAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return await channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        channel.Writer.TryComplete();
    }
}
