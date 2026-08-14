#nullable enable

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Maple.Cloud;

public enum BailianConnectionStatus
{
    Ready,
    CredentialMissing,
    AuthRejected,
    ModelUnavailable,
    RateLimited,
    ServiceUnavailable,
    InvalidResponse
}

public sealed record BailianConnectionResult(BailianConnectionStatus Status, string? RequestId = null);

public sealed class BailianHttpClient
{
    public static readonly Uri Endpoint = new("https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions");

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];
    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes =
    [
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    ];

    private readonly HttpClient httpClient;
    private readonly IBailianCredentialStore credentialStore;
    private readonly Func<TimeSpan, CancellationToken, ValueTask> delay;
    private readonly SemaphoreSlim requestGate = new(1, 1);

    public BailianHttpClient(
        HttpClient httpClient,
        IBailianCredentialStore credentialStore,
        Func<TimeSpan, CancellationToken, ValueTask>? delay = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        this.delay = delay ?? ((duration, cancellationToken) => new ValueTask(Task.Delay(duration, cancellationToken)));
    }

    public async Task<BailianConnectionResult> TestConnectionAsync(string modelId, CancellationToken cancellationToken)
    {
        if (!BailianModelCatalog.IsSupported(modelId)) return new BailianConnectionResult(BailianConnectionStatus.ModelUnavailable);

        using BailianCredentialLease? credential = await credentialStore.LeaseAsync(cancellationToken).ConfigureAwait(false);
        if (credential is null) return new BailianConnectionResult(BailianConnectionStatus.CredentialMissing);

        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; attempt <= RetryDelays.Length; attempt++)
            {
                using HttpRequestMessage request = CreateConnectionTestRequest(modelId, credential.Reveal());
                HttpResponseMessage response;
                try
                {
                    response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (attempt == RetryDelays.Length) return new BailianConnectionResult(BailianConnectionStatus.ServiceUnavailable);
                    await delay(RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
                    continue;
                }
                using (response)
                {
                    string? requestId = ReadRequestId(response);

                    if (response.IsSuccessStatusCode)
                    {
                        return await HasValidConnectionResponseAsync(response, cancellationToken).ConfigureAwait(false)
                            ? new BailianConnectionResult(BailianConnectionStatus.Ready, requestId)
                            : new BailianConnectionResult(BailianConnectionStatus.InvalidResponse, requestId);
                    }
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        return new BailianConnectionResult(BailianConnectionStatus.AuthRejected, requestId);
                    }
                    if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                    {
                        return new BailianConnectionResult(BailianConnectionStatus.ModelUnavailable, requestId);
                    }
                    if (!RetryableStatusCodes.Contains(response.StatusCode) || attempt == RetryDelays.Length)
                    {
                        BailianConnectionStatus status = response.StatusCode == HttpStatusCode.TooManyRequests
                            ? BailianConnectionStatus.RateLimited
                            : BailianConnectionStatus.ServiceUnavailable;
                        return new BailianConnectionResult(status, requestId);
                    }

                    await delay(RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            requestGate.Release();
        }

        return new BailianConnectionResult(BailianConnectionStatus.ServiceUnavailable);
    }

    private static HttpRequestMessage CreateConnectionTestRequest(string modelId, string credential)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new
            {
                model = modelId,
                messages = new[] { new { role = "user", content = "仅回复 READY" } },
                max_tokens = 4
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        return request;
    }

    private static async Task<bool> HasValidConnectionResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 1_048_576) return false;
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return document.RootElement.TryGetProperty("choices", out JsonElement choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("message", out JsonElement message)
                && message.TryGetProperty("content", out JsonElement content)
                && content.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(content.GetString());
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadRequestId(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("x-request-id", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;
    }
}
