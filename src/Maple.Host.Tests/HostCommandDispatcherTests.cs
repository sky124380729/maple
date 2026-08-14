using System.Net;
using Maple.Cloud;
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

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
