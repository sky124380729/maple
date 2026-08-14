#nullable enable

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maple.Contracts;

namespace Maple.Cloud;

public sealed record BailianMapImage(long FrameId, string MediaType, ReadOnlyMemory<byte> Bytes);

public sealed class BailianMapHttpClient : IBailianMapClient
{
    public static readonly Uri Endpoint = BailianHttpClient.Endpoint;

    private const int MaximumImageBytes = 10 * 1024 * 1024;
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];
    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes =
    [
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    ];
    private static readonly HashSet<string> ImageMediaTypes = new(StringComparer.Ordinal)
    {
        "image/jpeg", "image/png", "image/webp",
    };
    private static readonly JsonSerializerOptions ResponseJson = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient httpClient;
    private readonly IBailianCredentialStore credentialStore;
    private readonly Func<TimeSpan, CancellationToken, ValueTask> delay;
    private readonly SemaphoreSlim requestGate = new(1, 1);

    public BailianMapHttpClient(
        HttpClient httpClient,
        IBailianCredentialStore credentialStore,
        Func<TimeSpan, CancellationToken, ValueTask>? delay = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        this.delay = delay ?? ((duration, cancellationToken) => new ValueTask(Task.Delay(duration, cancellationToken)));
    }

    public async Task<BailianMapResult> AnnotateAsync(
        MapAnnotationRequest request,
        IReadOnlyList<BailianMapImage> images,
        string modelId,
        CancellationToken cancellationToken)
    {
        if (!BailianModelCatalog.IsSupported(modelId)) return Result(BailianMapStatus.ModelUnavailable, "模型不在应用白名单中");
        if (request is null || !request.CloudUploadApproved) return Result(BailianMapStatus.UploadNotApproved, "未批准上传地图关键帧");
        if (!ValidateRequest(request, images)) return Result(BailianMapStatus.InvalidRequest, "地图标注请求无效");

        using BailianCredentialLease? credential = await credentialStore.LeaseAsync(cancellationToken).ConfigureAwait(false);
        if (credential is null) return Result(BailianMapStatus.CredentialMissing, "未配置百炼凭据");

        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; attempt <= RetryDelays.Length; attempt++)
            {
                using HttpRequestMessage httpRequest = CreateRequest(request, images, modelId, credential.Reveal());
                HttpResponseMessage response;
                try
                {
                    response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (attempt == RetryDelays.Length) return Result(BailianMapStatus.Timeout, "百炼地图标注请求超时");
                    await delay(RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
                    continue;
                }
                catch (HttpRequestException)
                {
                    if (attempt == RetryDelays.Length) return Result(BailianMapStatus.ServiceUnavailable, "百炼地图标注服务不可用");
                    await delay(RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
                    continue;
                }

                using (response)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        InitialMapAnnotation? annotation = await ReadAnnotationAsync(response, cancellationToken).ConfigureAwait(false);
                        if (annotation is null || !HasMatchingProvenance(annotation, request.SourceFrameIds))
                            return Result(BailianMapStatus.MalformedResponse, "百炼地图标注响应未通过结构或来源校验");
                        return new BailianMapResult { Status = BailianMapStatus.Success, Annotation = annotation, Message = "初始地图结构标注已返回，仍需本地验证" };
                    }
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        return Result(BailianMapStatus.AuthRejected, "百炼凭据被拒绝");
                    if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                        return Result(BailianMapStatus.ModelUnavailable, "百炼模型不可用或请求不兼容");
                    if (!RetryableStatusCodes.Contains(response.StatusCode) || attempt == RetryDelays.Length)
                        return Result(response.StatusCode == HttpStatusCode.TooManyRequests ? BailianMapStatus.RateLimited : BailianMapStatus.ServiceUnavailable, "百炼地图标注服务不可用");
                }

                await delay(RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            requestGate.Release();
        }

        return Result(BailianMapStatus.ServiceUnavailable, "百炼地图标注服务不可用");
    }

    private static HttpRequestMessage CreateRequest(MapAnnotationRequest request, IReadOnlyList<BailianMapImage> images, string modelId, string credential)
    {
        var content = new List<object>(images.Count + 1);
        foreach (BailianMapImage image in images)
        {
            string dataUrl = $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Bytes.Span)}";
            content.Add(new { type = "image_url", image_url = new { url = dataUrl } });
        }
        content.Add(new
        {
            type = "text",
            text = $"分析地图 {request.MapId}。仅返回 JSON 对象：schemaVersion=2、coordinateSystem=mapworld-px、sourceFrameIds、platforms、ladders、boundaries、connections、confidence、coverage、calibrationErrorPx。sourceFrameIds 必须原样返回 [{string.Join(',', request.SourceFrameIds)}]。不得输出路线、按键或动作。",
        });

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new
            {
                model = modelId,
                messages = new[] { new { role = "user", content } },
                temperature = 0,
            }),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        return httpRequest;
    }

    private static bool ValidateRequest(MapAnnotationRequest request, IReadOnlyList<BailianMapImage> images)
    {
        if (request.SchemaVersion != ContractConstants.SchemaVersion || string.IsNullOrWhiteSpace(request.MapId) || request.MapId.Length > 256) return false;
        if (request.SourceFrameIds is null || request.SourceFrameIds.Count is < 1 or > 4 || request.SourceFrameIds.Any(id => id < 0) || request.SourceFrameIds.Distinct().Count() != request.SourceFrameIds.Count) return false;
        if (images is null || images.Count != request.SourceFrameIds.Count) return false;
        for (int index = 0; index < images.Count; index++)
        {
            BailianMapImage image = images[index];
            if (image.FrameId != request.SourceFrameIds[index] || !ImageMediaTypes.Contains(image.MediaType) || image.Bytes.IsEmpty || image.Bytes.Length > MaximumImageBytes) return false;
        }
        return true;
    }

    private static async Task<InitialMapAnnotation?> ReadAnnotationAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes) return null;
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument envelope = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken).ConfigureAwait(false);
            if (!envelope.RootElement.TryGetProperty("choices", out JsonElement choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0
                || !choices[0].TryGetProperty("message", out JsonElement message)
                || !message.TryGetProperty("content", out JsonElement content)
                || content.ValueKind != JsonValueKind.String)
                return null;
            string? json = content.GetString();
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumResponseBytes) return null;
            InitialMapAnnotation? annotation = JsonSerializer.Deserialize<InitialMapAnnotation>(json, ResponseJson);
            return BailianSchemaValidation.Validate(annotation).IsValid ? annotation : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasMatchingProvenance(InitialMapAnnotation annotation, IReadOnlyList<long> expected)
    {
        return annotation.SourceFrameIds.Count == expected.Count && annotation.SourceFrameIds.SequenceEqual(expected);
    }

    private static BailianMapResult Result(BailianMapStatus status, string message) => new()
    {
        Status = status,
        Message = message,
    };
}
