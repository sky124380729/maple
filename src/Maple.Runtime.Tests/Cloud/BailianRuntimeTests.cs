using System.Net;
using System.Net.Http.Headers;
using Maple.Cloud;
using Xunit;

namespace Maple.Runtime.Tests.Cloud;

public sealed class BailianRuntimeTests
{
    private const string ValidKey = "abcdefghijklmnop";

    [Fact]
    public void ModelCatalogAllowsOnlyPublishedVisualModels()
    {
        Assert.Equal("qwen3-vl-plus", BailianModelCatalog.DefaultModelId);
        Assert.True(BailianModelCatalog.IsSupported("qwen3-vl-flash"));
        Assert.True(BailianModelCatalog.IsSupported("qwen-vl-max"));
        Assert.False(BailianModelCatalog.IsSupported("custom-model"));
    }

    [Fact]
    public async Task InMemoryCredentialsCanBeReplacedAndClearedWithoutPersistence()
    {
        var store = new InMemoryBailianCredentialStore();
        await store.SetAsync(ValidKey.AsMemory(), CancellationToken.None);
        Assert.True(await store.IsConfiguredAsync(CancellationToken.None));

        await store.SetAsync("ponmlkjihgfedcba".AsMemory(), CancellationToken.None);
        using BailianCredentialLease? lease = await store.LeaseAsync(CancellationToken.None);
        Assert.NotNull(lease);
        Assert.Equal("ponmlkjihgfedcba", lease.Reveal());

        await store.ClearAsync(CancellationToken.None);
        Assert.False(await store.IsConfiguredAsync(CancellationToken.None));
        Assert.Null(await store.LeaseAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConnectionTestUsesTheFixedEndpointAndBearerAuthentication()
    {
        var handler = new RecordingHandler(_ => Response(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"READY\"}}]}"));
        var store = new InMemoryBailianCredentialStore();
        await store.SetAsync(ValidKey.AsMemory(), CancellationToken.None);
        var client = new BailianHttpClient(new HttpClient(handler), store, NoDelay);

        BailianConnectionResult result = await client.TestConnectionAsync("qwen3-vl-plus", CancellationToken.None);

        Assert.Equal(BailianConnectionStatus.Ready, result.Status);
        Assert.Equal(new Uri("https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions"), handler.RequestUri);
        Assert.Equal("Bearer", handler.AuthenticationScheme);
        Assert.True(handler.HadCredential);
        Assert.DoesNotContain(ValidKey, handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticationFailuresAreNotRetried()
    {
        var handler = new RecordingHandler(_ => Response(HttpStatusCode.Unauthorized, "{}"));
        var store = new InMemoryBailianCredentialStore();
        await store.SetAsync(ValidKey.AsMemory(), CancellationToken.None);
        var client = new BailianHttpClient(new HttpClient(handler), store, NoDelay);

        BailianConnectionResult result = await client.TestConnectionAsync("qwen3-vl-plus", CancellationToken.None);

        Assert.Equal(BailianConnectionStatus.AuthRejected, result.Status);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task TransientFailuresRetryAtMostTwice()
    {
        var responses = new Queue<HttpStatusCode>([HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK]);
        var handler = new RecordingHandler(_ => responses.Peek() == HttpStatusCode.OK
            ? Response(responses.Dequeue(), "{\"choices\":[{\"message\":{\"content\":\"READY\"}}]}")
            : Response(responses.Dequeue(), "{}"));
        var store = new InMemoryBailianCredentialStore();
        await store.SetAsync(ValidKey.AsMemory(), CancellationToken.None);
        var client = new BailianHttpClient(new HttpClient(handler), store, NoDelay);

        BailianConnectionResult result = await client.TestConnectionAsync("qwen3-vl-plus", CancellationToken.None);

        Assert.Equal(BailianConnectionStatus.Ready, result.Status);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task TransportTimeoutsRetryAtMostTwiceAndReturnUnavailable()
    {
        var handler = new TimeoutHandler();
        var store = new InMemoryBailianCredentialStore();
        await store.SetAsync(ValidKey.AsMemory(), CancellationToken.None);
        var client = new BailianHttpClient(new HttpClient(handler), store, NoDelay);

        BailianConnectionResult result = await client.TestConnectionAsync("qwen3-vl-plus", CancellationToken.None);

        Assert.Equal(BailianConnectionStatus.ServiceUnavailable, result.Status);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task CallerCancellationIsPropagatedWithoutRetry()
    {
        var handler = new CancellationHandler();
        var store = new InMemoryBailianCredentialStore();
        await store.SetAsync(ValidKey.AsMemory(), CancellationToken.None);
        var client = new BailianHttpClient(new HttpClient(handler), store, NoDelay);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.TestConnectionAsync("qwen3-vl-plus", cancellation.Token));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task InvalidJsonIsReportedAsAnInvalidResponse()
    {
        var handler = new RecordingHandler(_ => Response(HttpStatusCode.OK, "not-json"));
        var store = new InMemoryBailianCredentialStore();
        await store.SetAsync(ValidKey.AsMemory(), CancellationToken.None);
        var client = new BailianHttpClient(new HttpClient(handler), store, NoDelay);

        BailianConnectionResult result = await client.TestConnectionAsync("qwen3-vl-plus", CancellationToken.None);

        Assert.Equal(BailianConnectionStatus.InvalidResponse, result.Status);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void RedactorRemovesCredentialFieldsAndBearerValues()
    {
        string sensitive = string.Join(string.Empty, "api", "Key", "=", ValidKey, "; Authorization: Bearer ", ValidKey);

        string redacted = BailianSecretRedactor.Redact(sensitive);

        Assert.DoesNotContain(ValidKey, redacted, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WindowsCredentialStoreNeverFallsBackToPlaintextOnMacOs()
    {
        if (OperatingSystem.IsWindows()) return;
        var store = new WindowsBailianCredentialStore(Path.Combine(Path.GetTempPath(), "maple-test-credential.bin"));

        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
            await store.SetAsync(ValidKey.AsMemory(), CancellationToken.None));
    }

    private static ValueTask NoDelay(TimeSpan delay, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    private static HttpResponseMessage Response(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AuthenticationScheme { get; private set; }
        public bool HadCredential { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            AuthenticationHeaderValue? authorization = request.Headers.Authorization;
            AuthenticationScheme = authorization?.Scheme;
            HadCredential = !string.IsNullOrEmpty(authorization?.Parameter);
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromException<HttpResponseMessage>(new TaskCanceledException("transport timeout"));
        }
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Response(HttpStatusCode.OK, "{}"));
        }
    }
}
