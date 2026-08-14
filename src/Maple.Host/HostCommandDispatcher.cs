using System.Text.Json;
using Maple.Cloud;
using Maple.Contracts;

namespace Maple.Host;

public sealed record CloudRuntimeStatus(
    bool Enabled,
    bool CredentialConfigured,
    string ModelId,
    string ConnectionStatus,
    bool RequestInFlight,
    string? LastErrorCode,
    bool UploadConsent);

/// <summary>Consumes routed UI intent while keeping credentials and cloud responses native-only.</summary>
public sealed class HostCommandDispatcher : IDisposable
{
    private readonly IBailianCredentialStore credentialStore;
    private readonly BailianHttpClient bailian;
    private readonly BailianMapAnnotationService? mapAnnotation;
    private readonly IMapScanController? mapScan;
    private bool enabled;
    private bool uploadConsent;
    private string modelId = BailianModelCatalog.DefaultModelId;
    private bool disposed;

    public HostCommandDispatcher(
        IBailianCredentialStore credentialStore,
        BailianHttpClient bailian,
        BailianMapAnnotationService? mapAnnotation = null,
        IMapScanController? mapScan = null)
    {
        this.credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        this.bailian = bailian ?? throw new ArgumentNullException(nameof(bailian));
        this.mapAnnotation = mapAnnotation;
        this.mapScan = mapScan;
    }

    public CloudRuntimeStatus Status { get; private set; } = new(false, false, BailianModelCatalog.DefaultModelId, "notConfigured", false, null, false);
    public event EventHandler<CloudRuntimeStatus>? StatusChanged;
    public event EventHandler<BailianMapResult>? MapAnnotationCompleted;

    public void Handle(BridgeRouteResult route)
    {
        if (disposed || !route.Accepted || route.CommandType is null) return;
        _ = HandleAsync(route, CancellationToken.None);
    }

    public async Task HandleAsync(BridgeRouteResult route, CancellationToken cancellationToken = default)
    {
        if (disposed || !route.Accepted || route.CommandType is not UiCommandType commandType) return;
        try
        {
            using JsonDocument payload = JsonDocument.Parse(route.PayloadJson ?? "{}");
            switch (commandType)
            {
                case UiCommandType.MapScanStart:
                    mapScan?.StartScan();
                    break;
                case UiCommandType.MapCalibrationStart:
                    mapScan?.StopScan();
                    break;
                case UiCommandType.CloudCredentialSet:
                    await SetCredentialAsync(payload.RootElement, cancellationToken).ConfigureAwait(false);
                    break;
                case UiCommandType.CloudCredentialClear:
                    await credentialStore.ClearAsync(cancellationToken).ConfigureAwait(false);
                    enabled = false;
                    Publish("notConfigured", null, false);
                    break;
                case UiCommandType.CloudConfigUpdate:
                    UpdateConfiguration(payload.RootElement);
                    break;
                case UiCommandType.CloudConnectionTest:
                    await TestConnectionAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case UiCommandType.CloudMapAnnotate:
                    await AnnotateMapAsync(payload.RootElement, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (MapFrameSourceException exception) { Publish(Status.ConnectionStatus, exception.Code, false); }
        catch (JsonException) { Publish(Status.ConnectionStatus, "INVALID_PAYLOAD", false); }
        catch (ArgumentException) { Publish(Status.ConnectionStatus, "INVALID_CONFIGURATION", false); }
        catch (Exception) { Publish("unavailable", "CLOUD_REQUEST_FAILED", false); }
    }

    private async Task SetCredentialAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (!payload.TryGetProperty("apiKey", out JsonElement key) || key.ValueKind != JsonValueKind.String) throw new ArgumentException("apiKey");
        string value = key.GetString() ?? string.Empty;
        BailianCredentialValidation.Validate(value.AsSpan());
        await credentialStore.SetAsync(value.AsMemory(), cancellationToken).ConfigureAwait(false);
        Publish("notConfigured", null, false);
    }

    private void UpdateConfiguration(JsonElement payload)
    {
        if (!payload.TryGetProperty("modelId", out JsonElement model) || !BailianModelCatalog.IsSupported(model.GetString() ?? string.Empty)) throw new ArgumentException("modelId");
        modelId = model.GetString()!;
        enabled = payload.TryGetProperty("enabled", out JsonElement on) && on.ValueKind == JsonValueKind.True;
        uploadConsent = payload.TryGetProperty("uploadConsent", out JsonElement consent) && consent.ValueKind == JsonValueKind.True;
        Publish(Status.ConnectionStatus, null, false);
    }

    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (!await credentialStore.IsConfiguredAsync(cancellationToken).ConfigureAwait(false)) { Publish("notConfigured", "CREDENTIAL_MISSING", false); return; }
        Publish("checking", null, true);
        BailianConnectionResult result = await bailian.TestConnectionAsync(modelId, cancellationToken).ConfigureAwait(false);
        Publish(result.Status switch
        {
            BailianConnectionStatus.Ready => "ready",
            BailianConnectionStatus.CredentialMissing => "notConfigured",
            _ => "unavailable",
        }, result.Status.ToString(), false);
    }

    private async Task AnnotateMapAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (!enabled) { Publish(Status.ConnectionStatus, "CLOUD_DISABLED", false); return; }
        if (!uploadConsent) { Publish(Status.ConnectionStatus, "UPLOAD_CONSENT_REQUIRED", false); return; }
        if (mapAnnotation is null) { Publish(Status.ConnectionStatus, "MAP_FRAME_SOURCE_UNAVAILABLE", false); return; }
        if (!payload.TryGetProperty("mapId", out JsonElement mapIdElement)
            || !payload.TryGetProperty("sourceFrameIds", out JsonElement idsElement))
            throw new ArgumentException("map annotation payload");

        var request = new MapAnnotationRequest
        {
            SchemaVersion = ContractConstants.SchemaVersion,
            MapId = mapIdElement.GetString() ?? string.Empty,
            SourceFrameIds = idsElement.EnumerateArray().Select(item => item.GetInt64()).ToList(),
            CloudUploadApproved = true,
        };
        Publish("checking", null, true);
        BailianMapResult result = await mapAnnotation.AnnotateAsync(request, modelId, cancellationToken).ConfigureAwait(false);
        if (result.Status == BailianMapStatus.Success)
        {
            Publish("ready", null, false);
            MapAnnotationCompleted?.Invoke(this, result);
            return;
        }
        Publish("unavailable", "MAP_ANNOTATION_" + result.Status.ToString().ToUpperInvariant(), false);
    }

    private void Publish(string connectionStatus, string? error, bool inFlight)
    {
        Status = new CloudRuntimeStatus(enabled, credentialStore.IsConfiguredAsync(CancellationToken.None).GetAwaiter().GetResult(), modelId, connectionStatus, inFlight, error, uploadConsent);
        StatusChanged?.Invoke(this, Status);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (credentialStore is IDisposable disposable) disposable.Dispose();
    }
}
