using System.Net;
using System.Buffers;
using Maple.Cloud;
using Maple.Capture;
using Maple.Contracts;
using Xunit;

namespace Maple.Host.Tests;

public sealed class HostCommandDispatcherTests
{
    [Fact]
    public async Task MapAnnotationWithoutAFrameSourceIsExplicitlyRejected()
    {
        var store = new InMemoryBailianCredentialStore();
        var connectionClient = new BailianHttpClient(new HttpClient(new NeverCalledHandler()), store, (_, _) => ValueTask.CompletedTask);
        using var dispatcher = new HostCommandDispatcher(store, connectionClient);
        var router = new BridgeMessageRouter();
        await dispatcher.HandleAsync(router.Route("""
            {"schemaVersion":2,"type":"cloud.config.update","payload":{"enabled":true,"modelId":"qwen3-vl-plus","uploadConsent":true}}
            """));

        await dispatcher.HandleAsync(router.Route("""
            {"schemaVersion":2,"type":"cloud.map.annotate","payload":{"mapId":"forest-east","sourceFrameIds":[42]}}
            """));

        Assert.Equal("MAP_FRAME_SOURCE_UNAVAILABLE", dispatcher.Status.LastErrorCode);
        Assert.False(dispatcher.Status.RequestInFlight);
    }

    [Fact]
    public async Task MapFrameSourceFailureIsReportedExplicitly()
    {
        var store = new InMemoryBailianCredentialStore();
        var connectionClient = new BailianHttpClient(new HttpClient(new NeverCalledHandler()), store, (_, _) => ValueTask.CompletedTask);
        var annotation = new BailianMapAnnotationService(new NeverCalledMapClient(), new FailingImageSource());
        using var dispatcher = new HostCommandDispatcher(store, connectionClient, annotation);
        var router = new BridgeMessageRouter();
        await dispatcher.HandleAsync(router.Route("""
            {"schemaVersion":2,"type":"cloud.config.update","payload":{"enabled":true,"modelId":"qwen3-vl-plus","uploadConsent":true}}
            """));

        await dispatcher.HandleAsync(router.Route("""
            {"schemaVersion":2,"type":"cloud.map.annotate","payload":{"mapId":"forest-east","sourceFrameIds":[42]}}
            """));

        Assert.Equal("MAP_FRAME_MISSING", dispatcher.Status.LastErrorCode);
        Assert.False(dispatcher.Status.RequestInFlight);
    }

    [Fact]
    public async Task MapScanCommandsControlFrameSelectionSession()
    {
        var store = new InMemoryBailianCredentialStore();
        var connectionClient = new BailianHttpClient(new HttpClient(new NeverCalledHandler()), store, (_, _) => ValueTask.CompletedTask);
        var scan = new RecordingMapScanController();
        using var dispatcher = new HostCommandDispatcher(store, connectionClient, mapScan: scan);
        var router = new BridgeMessageRouter();

        await dispatcher.HandleAsync(router.Route("""
            {"schemaVersion":2,"type":"map.scan.start","payload":{}}
            """));
        await dispatcher.HandleAsync(router.Route("""
            {"schemaVersion":2,"type":"map.calibration.start","payload":{}}
            """));

        Assert.Equal(1, scan.StartCalls);
        Assert.Equal(1, scan.StopCalls);
    }

    [Fact]
    public async Task RecordedMapFrameReachesAnnotationClientWithItsProvenance()
    {
        var credentialStore = new InMemoryBailianCredentialStore();
        var connectionClient = new BailianHttpClient(new HttpClient(new NeverCalledHandler()), credentialStore, (_, _) => ValueTask.CompletedTask);
        var mapClient = new RecordingMapClient();
        using var frames = new MapScanFrameStore(new FixedPngEncoder(), minimumFrameIntervalMs: 0, capacity: 4);
        frames.StartScan();
        using CapturedFrame frame = CreateFrame(42);
        frames.Observe(frame);
        var annotation = new BailianMapAnnotationService(mapClient, frames);
        using var dispatcher = new HostCommandDispatcher(credentialStore, connectionClient, annotation, frames);
        var router = new BridgeMessageRouter();
        await dispatcher.HandleAsync(router.Route("""
            {"schemaVersion":2,"type":"cloud.config.update","payload":{"enabled":true,"modelId":"qwen3-vl-plus","uploadConsent":true}}
            """));

        await dispatcher.HandleAsync(router.Route("""
            {"schemaVersion":2,"type":"cloud.map.annotate","payload":{"mapId":"forest-east","sourceFrameIds":[42]}}
            """));

        Assert.Equal("ready", dispatcher.Status.ConnectionStatus);
        BailianMapImage image = Assert.Single(mapClient.Images);
        Assert.Equal(42, image.FrameId);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 42 }, image.Bytes.ToArray());
    }

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private sealed class NeverCalledMapClient : IBailianMapClient
    {
        public Task<BailianMapResult> AnnotateAsync(MapAnnotationRequest request, IReadOnlyList<BailianMapImage> images, string modelId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Map client must not be called when the frame source fails");
    }

    private sealed class FailingImageSource : IMapImageSource
    {
        public ValueTask<IReadOnlyList<BailianMapImage>> ReadAsync(string mapId, IReadOnlyList<long> frameIds, CancellationToken cancellationToken) =>
            ValueTask.FromException<IReadOnlyList<BailianMapImage>>(new MapFrameSourceException("MAP_FRAME_MISSING", "frame missing"));
    }

    private sealed class RecordingMapScanController : IMapScanController
    {
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public void StartScan() => StartCalls++;
        public void StopScan() => StopCalls++;
    }

    private sealed class FixedPngEncoder : IMapFrameEncoder
    {
        public byte[] EncodePng(CapturedFrame frame) => [137, 80, 78, 71, (byte)frame.Metadata.FrameId];
    }

    private sealed class RecordingMapClient : IBailianMapClient
    {
        public IReadOnlyList<BailianMapImage> Images { get; private set; } = [];

        public Task<BailianMapResult> AnnotateAsync(MapAnnotationRequest request, IReadOnlyList<BailianMapImage> images, string modelId, CancellationToken cancellationToken)
        {
            Images = images;
            var response = new InitialMapAnnotation
            {
                SchemaVersion = ContractConstants.SchemaVersion,
                CoordinateSystem = "mapworld-px",
                SourceFrameIds = request.SourceFrameIds,
                Platforms = [new MapAnnotationPlatform { PlatformId = "p1", X1 = 0, X2 = 100, Y = 200, Confidence = 0.9 }],
                Ladders = [],
                Boundaries = [],
                Connections = [],
                Confidence = 0.9,
                Coverage = 0.5,
                CalibrationErrorPx = 2,
            };
            return new MockBailianMapClient(MockBailianMode.Success, response).AnnotateAsync(request, images, modelId, cancellationToken);
        }
    }

    private static CapturedFrame CreateFrame(long frameId)
    {
        const int width = 4;
        const int height = 3;
        const int stride = width * 4;
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(stride * height);
        owner.Memory.Span[..(stride * height)].Fill(32);
        return new CapturedFrame(
            new CaptureFrameMetadata
            {
                SchemaVersion = ContractConstants.SchemaVersion,
                FrameId = frameId,
                CapturedAtMonoMs = 100,
                ClientWidth = width,
                ClientHeight = height,
                Dpi = 96,
                CaptureBackend = CaptureBackend.Wgc,
                DroppedReason = DroppedFrameReason.None,
            },
            width,
            height,
            stride,
            CapturedPixelFormat.Bgra32,
            owner,
            stride * height);
    }
}
