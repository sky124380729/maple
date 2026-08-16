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

public sealed record MapAnnotationCompletedEvent(string MapId, BailianMapResult Result);

/// <summary>Consumes routed UI intent while keeping credentials and cloud responses native-only.</summary>
public sealed class HostCommandDispatcher : IDisposable
{
    private readonly IBailianCredentialStore credentialStore;
    private readonly BailianHttpClient bailian;
    private readonly BailianMapAnnotationService? mapAnnotation;
    private readonly IMapScanController? mapScan;
    private readonly ICombatConfigurationStore? combatConfiguration;
    private readonly Action? pauseBeforeConfiguration;
    private readonly ActiveMapRuntime? activeMapRuntime;
    private readonly IInputAcceptanceController? inputAcceptance;
    private bool enabled;
    private bool uploadConsent;
    private string modelId = BailianModelCatalog.DefaultModelId;
    private bool disposed;

    public HostCommandDispatcher(
        IBailianCredentialStore credentialStore,
        BailianHttpClient bailian,
        BailianMapAnnotationService? mapAnnotation = null,
        IMapScanController? mapScan = null,
        ICombatConfigurationStore? combatConfiguration = null,
        Action? pauseBeforeConfiguration = null,
        ActiveMapRuntime? activeMapRuntime = null,
        IInputAcceptanceController? inputAcceptance = null)
    {
        this.credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        this.bailian = bailian ?? throw new ArgumentNullException(nameof(bailian));
        this.mapAnnotation = mapAnnotation;
        this.mapScan = mapScan;
        this.combatConfiguration = combatConfiguration;
        this.pauseBeforeConfiguration = pauseBeforeConfiguration;
        this.activeMapRuntime = activeMapRuntime;
        this.inputAcceptance = inputAcceptance;
    }

    public CloudRuntimeStatus Status { get; private set; } = new(false, false, BailianModelCatalog.DefaultModelId, "notConfigured", false, null, false);
    public event EventHandler<CloudRuntimeStatus>? StatusChanged;
    public event EventHandler<MapAnnotationCompletedEvent>? MapAnnotationCompleted;
    public event EventHandler<CombatConfiguration>? CombatConfigurationChanged;
    public event EventHandler<ActiveMapStatus>? MapStatusChanged;
    public event EventHandler<InputResult>? InputResultPublished;

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
                case UiCommandType.MapCalibrationConfirm:
                    ConfirmMap(payload.RootElement);
                    break;
                case UiCommandType.CloudCredentialSet:
                    await SetCredentialAsync(payload.RootElement, cancellationToken).ConfigureAwait(false);
                    break;
                case UiCommandType.ConfigUpdate:
                    await UpdateCombatConfigurationAsync(payload.RootElement, cancellationToken).ConfigureAwait(false);
                    break;
                case UiCommandType.InputTest:
                    await RunInputTestAsync(payload.RootElement, cancellationToken).ConfigureAwait(false);
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

    private async Task RunInputTestAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (inputAcceptance is null) throw new ArgumentException("input acceptance unavailable");
        if (!payload.TryGetProperty("kind", out JsonElement kindElement)
            || !payload.TryGetProperty("holdMs", out JsonElement holdElement))
            throw new ArgumentException("input test payload");

        InputAcceptanceKind kind = kindElement.GetString() switch
        {
            "moveLeft" => InputAcceptanceKind.MoveLeft,
            "moveRight" => InputAcceptanceKind.MoveRight,
            "climbUp" => InputAcceptanceKind.ClimbUp,
            "climbDown" => InputAcceptanceKind.ClimbDown,
            "jump" => InputAcceptanceKind.Jump,
            "attack" => InputAcceptanceKind.Attack,
            "pickup" => InputAcceptanceKind.Pickup,
            "hpPotion" => InputAcceptanceKind.HpPotion,
            "mpPotion" => InputAcceptanceKind.MpPotion,
            _ => throw new ArgumentException("input test kind"),
        };
        InputResult result = await inputAcceptance.RunAsync(kind, holdElement.GetInt32(), cancellationToken).ConfigureAwait(false);
        InputResultPublished?.Invoke(this, result);
    }

    private void ConfirmMap(JsonElement payload)
    {
        if (activeMapRuntime is null || !payload.TryGetProperty("mapId", out JsonElement mapIdElement))
            throw new ArgumentException("map calibration unavailable");
        string mapId = mapIdElement.GetString() ?? throw new ArgumentException("mapId");
        ActiveMapStatus status = activeMapRuntime.ConfirmCandidate(mapId);
        MapStatusChanged?.Invoke(this, status);
    }

    private async Task UpdateCombatConfigurationAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (combatConfiguration is null) throw new ArgumentException("combat configuration unavailable");
        pauseBeforeConfiguration?.Invoke();
        CombatConfiguration current = combatConfiguration.Current;
        CombatConfiguration updated = current with
        {
            AttackMode = ReadAttackMode(payload, "attackMode", current.AttackMode),
            HpThresholdMode = ReadResourceMode(payload, "hpThresholdMode", current.HpThresholdMode),
            HpThreshold = ReadDouble(payload, "hpThreshold", current.HpThreshold),
            MpThresholdMode = ReadResourceMode(payload, "mpThresholdMode", current.MpThresholdMode),
            MpThreshold = ReadDouble(payload, "mpThreshold", current.MpThreshold),
            SingleAttackKey = ReadString(payload, "singleAttackKey", ReadString(payload, "attackKey", current.SingleAttackKey)),
            AreaAttackKey = ReadString(payload, "areaAttackKey", current.AreaAttackKey),
            HpPotionKey = ReadString(payload, "hpPotionKey", current.HpPotionKey),
            MpPotionKey = ReadString(payload, "mpPotionKey", current.MpPotionKey),
            JumpKey = ReadString(payload, "jumpKey", current.JumpKey),
            PickupEnabled = ReadBool(payload, "pickupEnabled", current.PickupEnabled),
            PickupKey = ReadString(payload, "pickupKey", current.PickupKey),
            PreferredDistancePx = ReadInt(payload, "preferredDistancePx", current.PreferredDistancePx),
            AreaTargetCount = ReadInt(payload, "areaTargetCount", current.AreaTargetCount),
            SwitchCooldownMs = ReadInt(payload, "switchCooldownMs", current.SwitchCooldownMs),
        };
        await combatConfiguration.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        CombatConfigurationChanged?.Invoke(this, combatConfiguration.Current);
    }

    private static CombatAttackMode ReadAttackMode(JsonElement payload, string name, CombatAttackMode fallback) =>
        payload.TryGetProperty(name, out JsonElement value) ? value.GetString() switch
        {
            "single" => CombatAttackMode.Single,
            "auto" => CombatAttackMode.Auto,
            "group" => CombatAttackMode.Group,
            _ => throw new ArgumentException(name),
        } : fallback;

    private static ResourceMode ReadResourceMode(JsonElement payload, string name, ResourceMode fallback) =>
        payload.TryGetProperty(name, out JsonElement value) ? value.GetString() switch
        {
            "percent" => ResourceMode.Percent,
            "absolute" => ResourceMode.Absolute,
            _ => throw new ArgumentException(name),
        } : fallback;

    private static string ReadString(JsonElement payload, string name, string fallback) => payload.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? throw new ArgumentException(name) : fallback;
    private static double ReadDouble(JsonElement payload, string name, double fallback) => payload.TryGetProperty(name, out JsonElement value) ? value.GetDouble() : fallback;
    private static int ReadInt(JsonElement payload, string name, int fallback) => payload.TryGetProperty(name, out JsonElement value) ? value.GetInt32() : fallback;
    private static bool ReadBool(JsonElement payload, string name, bool fallback) => payload.TryGetProperty(name, out JsonElement value) ? value.GetBoolean() : fallback;

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
            MapAnnotationCompleted?.Invoke(this, new MapAnnotationCompletedEvent(request.MapId, result));
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
