using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Maple.Cloud;
using Maple.Contracts;
using Xunit;

namespace Maple.Runtime.Tests.Cloud;

public sealed class BailianMapHttpClientTests
{
    private const string ValidKey = "abcdefghijklmnop";

    [Fact]
    public async Task RefusesImagesWithoutExplicitUploadApproval()
    {
        var handler = new RecordingHandler(_ => Response(HttpStatusCode.OK, "{}"));
        var client = await CreateClientAsync(handler);

        BailianMapResult result = await client.AnnotateAsync(
            Request(approved: false),
            [new BailianMapImage(42, "image/png", new byte[] { 1, 2, 3 })],
            "qwen3-vl-plus",
            CancellationToken.None);

        Assert.Equal(BailianMapStatus.UploadNotApproved, result.Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendsBoundedImagesToTheFixedEndpointAndValidatesTheResponse()
    {
        string annotation = JsonSerializer.Serialize(ValidAnnotation());
        var handler = new RecordingHandler(_ => Response(HttpStatusCode.OK, Envelope(annotation)));
        var client = await CreateClientAsync(handler);

        BailianMapResult result = await client.AnnotateAsync(
            Request(approved: true),
            [new BailianMapImage(42, "image/png", new byte[] { 1, 2, 3 })],
            "qwen3-vl-plus",
            CancellationToken.None);

        Assert.Equal(BailianMapStatus.Success, result.Status);
        Assert.Equal(BailianMapHttpClient.Endpoint, handler.RequestUri);
        Assert.Equal("Bearer", handler.AuthenticationScheme);
        Assert.Contains("data:image/png;base64,AQID", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain(ValidKey, handler.RequestBody, StringComparison.Ordinal);
        Assert.Equal([42L], result.Annotation!.SourceFrameIds);
    }

    [Fact]
    public async Task RejectsAResponseWhoseFrameProvenanceDoesNotMatchTheRequest()
    {
        InitialMapAnnotation annotation = ValidAnnotation();
        annotation.SourceFrameIds = [99];
        var handler = new RecordingHandler(_ => Response(HttpStatusCode.OK, Envelope(JsonSerializer.Serialize(annotation))));
        var client = await CreateClientAsync(handler);

        BailianMapResult result = await client.AnnotateAsync(
            Request(approved: true),
            [new BailianMapImage(42, "image/png", new byte[] { 1, 2, 3 })],
            "qwen3-vl-plus",
            CancellationToken.None);

        Assert.Equal(BailianMapStatus.MalformedResponse, result.Status);
    }

    [Fact]
    public async Task AuthenticationFailureIsNotRetried()
    {
        var handler = new RecordingHandler(_ => Response(HttpStatusCode.Unauthorized, "{}"));
        var client = await CreateClientAsync(handler);

        BailianMapResult result = await client.AnnotateAsync(
            Request(approved: true),
            [new BailianMapImage(42, "image/png", new byte[] { 1, 2, 3 })],
            "qwen3-vl-plus",
            CancellationToken.None);

        Assert.Equal(BailianMapStatus.AuthRejected, result.Status);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RejectsUnknownModelsBeforeSendingImages()
    {
        var handler = new RecordingHandler(_ => Response(HttpStatusCode.OK, "{}"));
        var client = await CreateClientAsync(handler);

        BailianMapResult result = await client.AnnotateAsync(
            Request(approved: true),
            [new BailianMapImage(42, "image/png", new byte[] { 1, 2, 3 })],
            "custom-model",
            CancellationToken.None);

        Assert.Equal(BailianMapStatus.ModelUnavailable, result.Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task AnnotationServiceResolvesOnlyTheRequestedFrameIds()
    {
        string annotation = JsonSerializer.Serialize(ValidAnnotation());
        var handler = new RecordingHandler(_ => Response(HttpStatusCode.OK, Envelope(annotation)));
        BailianMapHttpClient client = await CreateClientAsync(handler);
        var source = new RecordingImageSource([new BailianMapImage(42, "image/png", new byte[] { 1, 2, 3 })]);
        var service = new BailianMapAnnotationService(client, source);

        BailianMapResult result = await service.AnnotateAsync(Request(approved: true), "qwen3-vl-plus", CancellationToken.None);

        Assert.Equal(BailianMapStatus.Success, result.Status);
        Assert.Equal("forest-east", source.MapId);
        Assert.Equal([42L], source.FrameIds);
    }

    private static async Task<BailianMapHttpClient> CreateClientAsync(RecordingHandler handler)
    {
        var store = new InMemoryBailianCredentialStore();
        await store.SetAsync(ValidKey.AsMemory(), CancellationToken.None);
        return new BailianMapHttpClient(new HttpClient(handler), store, (_, _) => ValueTask.CompletedTask);
    }

    private static MapAnnotationRequest Request(bool approved) => new()
    {
        SchemaVersion = ContractConstants.SchemaVersion,
        MapId = "forest-east",
        SourceFrameIds = [42],
        CloudUploadApproved = approved,
    };

    private static InitialMapAnnotation ValidAnnotation() => new()
    {
        SchemaVersion = ContractConstants.SchemaVersion,
        CoordinateSystem = "mapworld-px",
        SourceFrameIds = [42],
        Platforms = [new MapAnnotationPlatform { PlatformId = "p-1", X1 = 0, X2 = 300, Y = 100, Confidence = 0.98 }],
        Ladders = [],
        Boundaries = [],
        Connections = [],
        Confidence = 0.95,
        Coverage = 0.9,
        CalibrationErrorPx = 2,
    };

    private static string Envelope(string content) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content } } },
    });

    private static HttpResponseMessage Response(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AuthenticationScheme { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            AuthenticationHeaderValue? authorization = request.Headers.Authorization;
            AuthenticationScheme = authorization?.Scheme;
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }

    private sealed class RecordingImageSource(IReadOnlyList<BailianMapImage> images) : IMapImageSource
    {
        public string? MapId { get; private set; }
        public IReadOnlyList<long> FrameIds { get; private set; } = [];

        public ValueTask<IReadOnlyList<BailianMapImage>> ReadAsync(
            string mapId,
            IReadOnlyList<long> frameIds,
            CancellationToken cancellationToken)
        {
            MapId = mapId;
            FrameIds = frameIds.ToArray();
            return ValueTask.FromResult(images);
        }
    }
}
