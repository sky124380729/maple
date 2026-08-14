using System.Text.Json;
using Maple.Cloud;
using Maple.Contracts;

namespace Maple.Host;

public sealed class BridgeRouteResult
{
    public bool Accepted { get; init; }
    public UiCommandType? CommandType { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? PayloadJson { get; init; }
}

public sealed class BridgeMessageRouter
{
    private static readonly HashSet<string> EnvelopeFields = new(StringComparer.Ordinal)
    {
        "schemaVersion", "type", "timestamp", "payload",
    };

    private static readonly IReadOnlyDictionary<string, UiCommandType> Allowed = new Dictionary<string, UiCommandType>(StringComparer.Ordinal)
    {
        ["snapshot.request"] = UiCommandType.SnapshotRequest,
        ["session.arm"] = UiCommandType.SessionArm,
        ["session.pause"] = UiCommandType.SessionPause,
        ["session.resume"] = UiCommandType.SessionResume,
        ["session.emergencyStop"] = UiCommandType.SessionEmergencyStop,
        ["map.scan.start"] = UiCommandType.MapScanStart,
        ["map.calibration.start"] = UiCommandType.MapCalibrationStart,
        ["config.update"] = UiCommandType.ConfigUpdate,
        ["cloud.credential.set"] = UiCommandType.CloudCredentialSet,
        ["cloud.credential.clear"] = UiCommandType.CloudCredentialClear,
        ["cloud.config.update"] = UiCommandType.CloudConfigUpdate,
        ["cloud.connection.test"] = UiCommandType.CloudConnectionTest,
        ["cloud.map.annotate"] = UiCommandType.CloudMapAnnotate,
    };

    private static readonly HashSet<string> ForbiddenFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "action", "actions", "key", "keys", "hid", "report", "image", "frame", "base64", "url", "hwnd",
    };

    public BridgeRouteResult Route(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Reject("EMPTY_COMMAND", "命令为空");
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Reject("INVALID_COMMAND", "命令必须是 JSON 对象");
            if (!HasOnlyFields(root, EnvelopeFields)) return Reject("INVALID_COMMAND", "命令包含未知字段");
            if (!root.TryGetProperty("schemaVersion", out JsonElement schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int schemaVersion) || schemaVersion != ContractConstants.SchemaVersion)
                return Reject("SCHEMA_VERSION_REJECTED", "schemaVersion 不兼容");
            if (!root.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String || !Allowed.TryGetValue(type.GetString() ?? string.Empty, out UiCommandType commandType))
                return Reject("UNKNOWN_COMMAND_REJECTED", "未知命令已拒绝");
            if (ContainsForbiddenField(root)) return Reject("UNSAFE_PAYLOAD_REJECTED", "命令包含禁止的原生控制字段");
            if (root.TryGetProperty("timestamp", out JsonElement timestamp)
                && (timestamp.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(timestamp.GetString(), out _)))
                return Reject("INVALID_COMMAND", "timestamp 格式无效");
            if (!root.TryGetProperty("payload", out JsonElement payload) || payload.ValueKind != JsonValueKind.Object)
                return Reject("INVALID_PAYLOAD", "payload 必须是对象");
            if (!ValidatePayload(commandType, payload)) return Reject("INVALID_PAYLOAD", "payload 与命令契约不匹配");
            return new BridgeRouteResult { Accepted = true, CommandType = commandType, Code = "ACCEPTED", Message = "命令已进入原生安全检查", PayloadJson = payload.GetRawText() };
        }
        catch (JsonException) { return Reject("INVALID_JSON", "命令不是有效 JSON"); }
        catch (FormatException) { return Reject("INVALID_COMMAND", "命令字段格式无效"); }
    }

    private static bool ValidatePayload(UiCommandType commandType, JsonElement payload)
    {
        return commandType switch
        {
            UiCommandType.SnapshotRequest or
            UiCommandType.SessionArm or
            UiCommandType.SessionPause or
            UiCommandType.SessionResume or
            UiCommandType.MapScanStart or
            UiCommandType.MapCalibrationStart or
            UiCommandType.CloudCredentialClear or
            UiCommandType.CloudConnectionTest => !payload.EnumerateObject().Any(),
            UiCommandType.SessionEmergencyStop => ValidateEmergencyStop(payload),
            UiCommandType.ConfigUpdate => ValidateConfiguration(payload),
            UiCommandType.CloudCredentialSet => ValidateCredential(payload),
            UiCommandType.CloudConfigUpdate => ValidateCloudConfiguration(payload),
            UiCommandType.CloudMapAnnotate => ValidateCloudMapAnnotation(payload),
            _ => false,
        };
    }

    private static bool ValidateEmergencyStop(JsonElement payload)
    {
        if (!HasOnlyFields(payload, ["message"]) || !payload.TryGetProperty("message", out JsonElement message)) return false;
        return IsString(message, 1, 200, trim: true);
    }

    private static bool ValidateConfiguration(JsonElement payload)
    {
        string[] fields = ["attackMode", "hpThresholdMode", "hpThreshold", "mpThresholdMode", "mpThreshold", "attackKey", "jumpKey", "pickupEnabled", "pickupKey"];
        if (!HasOnlyFields(payload, fields)) return false;
        if (payload.TryGetProperty("attackMode", out JsonElement attackMode) && !IsOneOf(attackMode, "single", "auto", "group")) return false;
        if (payload.TryGetProperty("hpThresholdMode", out JsonElement hpMode) && !IsOneOf(hpMode, "percent", "absolute")) return false;
        if (payload.TryGetProperty("mpThresholdMode", out JsonElement mpMode) && !IsOneOf(mpMode, "percent", "absolute")) return false;
        if (payload.TryGetProperty("hpThreshold", out JsonElement hp) && !IsThreshold(hp, hpMode)) return false;
        if (payload.TryGetProperty("mpThreshold", out JsonElement mp) && !IsThreshold(mp, mpMode)) return false;
        if (payload.TryGetProperty("attackKey", out JsonElement attackKey) && !IsString(attackKey, 1, 32)) return false;
        if (payload.TryGetProperty("jumpKey", out JsonElement jumpKey) && !IsString(jumpKey, 1, 32)) return false;
        if (payload.TryGetProperty("pickupKey", out JsonElement pickupKey) && !IsString(pickupKey, 1, 32)) return false;
        return !payload.TryGetProperty("pickupEnabled", out JsonElement pickupEnabled) || pickupEnabled.ValueKind is JsonValueKind.True or JsonValueKind.False;
    }

    private static bool ValidateCredential(JsonElement payload)
    {
        if (!HasOnlyFields(payload, ["apiKey"]) || !payload.TryGetProperty("apiKey", out JsonElement apiKey)) return false;
        return IsString(apiKey, 16, 256) && !apiKey.GetString()!.Any(char.IsWhiteSpace);
    }

    private static bool ValidateCloudConfiguration(JsonElement payload)
    {
        if (!HasOnlyFields(payload, ["enabled", "modelId", "uploadConsent"])) return false;
        if (!payload.TryGetProperty("enabled", out JsonElement enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        if (!payload.TryGetProperty("uploadConsent", out JsonElement consent) || consent.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        return payload.TryGetProperty("modelId", out JsonElement model)
            && model.ValueKind == JsonValueKind.String
            && BailianModelCatalog.IsSupported(model.GetString());
    }

    private static bool ValidateCloudMapAnnotation(JsonElement payload)
    {
        if (!HasOnlyFields(payload, ["mapId", "sourceFrameIds"])) return false;
        if (!payload.TryGetProperty("mapId", out JsonElement mapId) || !IsString(mapId, 1, 256)) return false;
        if (!payload.TryGetProperty("sourceFrameIds", out JsonElement ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() is < 1 or > 4) return false;
        return ids.EnumerateArray().All(id => id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out long value) && value >= 0);
    }

    private static bool IsThreshold(JsonElement value, JsonElement mode)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || number < 0 || double.IsNaN(number) || double.IsInfinity(number)) return false;
        return mode.ValueKind != JsonValueKind.String || mode.GetString() != "percent" || number <= 100;
    }

    private static bool IsString(JsonElement value, int minimumLength, int maximumLength, bool trim = false)
    {
        if (value.ValueKind != JsonValueKind.String) return false;
        string text = value.GetString() ?? string.Empty;
        if (trim) text = text.Trim();
        return text.Length >= minimumLength && text.Length <= maximumLength;
    }

    private static bool IsOneOf(JsonElement value, params string[] choices)
    {
        return value.ValueKind == JsonValueKind.String && choices.Contains(value.GetString(), StringComparer.Ordinal);
    }

    private static bool HasOnlyFields(JsonElement value, IEnumerable<string> allowed)
    {
        var names = allowed as ISet<string> ?? new HashSet<string>(allowed, StringComparer.Ordinal);
        return value.EnumerateObject().All(property => names.Contains(property.Name));
    }

    private static bool ContainsForbiddenField(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Any(ContainsForbiddenField);
        if (value.ValueKind != JsonValueKind.Object) return false;
        return value.EnumerateObject().Any(property => ForbiddenFields.Contains(property.Name) || ContainsForbiddenField(property.Value));
    }

    private static BridgeRouteResult Reject(string code, string message) => new() { Accepted = false, Code = code, Message = message };
}
