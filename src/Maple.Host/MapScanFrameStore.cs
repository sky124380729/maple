using Maple.Capture;
using Maple.Cloud;

namespace Maple.Host;

public interface IMapFrameEncoder
{
    byte[] EncodePng(CapturedFrame frame);
}

public interface IMapScanController
{
    void StartScan();
    void StopScan();
}

public sealed class MapFrameSourceException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed class MapScanFrameStore : IMapImageSource, ICaptureFrameObserver, IMapScanController, IDisposable
{
    private const int MaximumImageBytes = 10 * 1024 * 1024;
    private readonly object sync = new();
    private readonly IMapFrameEncoder encoder;
    private readonly int minimumFrameIntervalMs;
    private readonly int capacity;
    private readonly SortedDictionary<long, BailianMapImage> images = [];
    private string? boundMapId;
    private long lastRecordedAtMonoMs = long.MinValue;
    private int generation;
    private bool scanning;
    private bool disposed;

    public MapScanFrameStore(IMapFrameEncoder encoder, int minimumFrameIntervalMs = 1000, int capacity = 32)
    {
        this.encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        if (minimumFrameIntervalMs < 0) throw new ArgumentOutOfRangeException(nameof(minimumFrameIntervalMs));
        if (capacity is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.minimumFrameIntervalMs = minimumFrameIntervalMs;
        this.capacity = capacity;
    }

    public void StartScan()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            generation++;
            scanning = true;
            boundMapId = null;
            lastRecordedAtMonoMs = long.MinValue;
            images.Clear();
        }
    }

    public void StopScan()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            scanning = false;
        }
    }

    public void Observe(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int observedGeneration;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!scanning || frame.PixelFormat != CapturedPixelFormat.Bgra32) return;
            long capturedAt = frame.Metadata.CapturedAtMonoMs;
            if (lastRecordedAtMonoMs != long.MinValue
                && capturedAt >= lastRecordedAtMonoMs
                && capturedAt - lastRecordedAtMonoMs < minimumFrameIntervalMs)
                return;
            lastRecordedAtMonoMs = capturedAt;
            observedGeneration = generation;
        }

        byte[] png = encoder.EncodePng(frame);
        if (png.Length is 0 or > MaximumImageBytes)
            throw new MapFrameSourceException("MAP_FRAME_SIZE_INVALID", "Encoded map frame size is invalid");

        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!scanning || observedGeneration != generation) return;
            images[frame.Metadata.FrameId] = new BailianMapImage(frame.Metadata.FrameId, "image/png", png);
            while (images.Count > capacity) images.Remove(images.First().Key);
        }
    }

    public ValueTask<IReadOnlyList<BailianMapImage>> ReadAsync(
        string mapId,
        IReadOnlyList<long> frameIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(mapId) || mapId.Length > 256)
            throw new MapFrameSourceException("MAP_FRAME_MAP_INVALID", "Map identity is invalid");
        if (frameIds is null || frameIds.Count is < 1 or > 4 || frameIds.Any(id => id < 0) || frameIds.Distinct().Count() != frameIds.Count)
            throw new MapFrameSourceException("MAP_FRAME_IDS_INVALID", "Map frame ids are invalid");

        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (boundMapId is not null && !string.Equals(boundMapId, mapId, StringComparison.Ordinal))
                throw new MapFrameSourceException("MAP_FRAME_MAP_MISMATCH", "Map frame scan is already bound to another map");
            var selected = new List<BailianMapImage>(frameIds.Count);
            foreach (long frameId in frameIds)
            {
                if (!images.TryGetValue(frameId, out BailianMapImage? image))
                    throw new MapFrameSourceException("MAP_FRAME_MISSING", $"Map frame {frameId} is not available");
                selected.Add(image);
            }
            boundMapId = mapId;
            return ValueTask.FromResult<IReadOnlyList<BailianMapImage>>(selected);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            scanning = false;
            images.Clear();
        }
    }
}
