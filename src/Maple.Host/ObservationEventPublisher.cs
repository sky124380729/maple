using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maple.Contracts;
using Maple.Preview;
using Maple.Vision;
using PreviewOverlay = Maple.Preview.OverlaySnapshot;

namespace Maple.Host;

public interface INativeVisionSink
{
    void PublishOverlay(PreviewOverlay snapshot);
    void PublishTelemetry(PreviewTelemetrySnapshot snapshot, long frameId);
}

public sealed class ObservationEventPublisher : IVisionRuntimePublisher
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly INativeVisionSink nativeSink;
    private readonly Action<string> send;
    private readonly RuntimeTelemetryCollector telemetry;
    private readonly Func<SessionState> stateProvider;
    private readonly Func<long> clock;
    private readonly string modelId;
    private readonly Func<PauseReason> pauseReasonProvider;

    public ObservationEventPublisher(
        INativeVisionSink nativeSink,
        Action<string> send,
        RuntimeTelemetryCollector telemetry,
        Func<SessionState> stateProvider,
        Func<long> clock,
        string modelId,
        Func<PauseReason>? pauseReasonProvider = null)
    {
        this.nativeSink = nativeSink ?? throw new ArgumentNullException(nameof(nativeSink));
        this.send = send ?? throw new ArgumentNullException(nameof(send));
        this.telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        this.stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.modelId = string.IsNullOrWhiteSpace(modelId) ? "unconfigured" : modelId;
        this.pauseReasonProvider = pauseReasonProvider ?? (() => PauseReason.None);
    }

    public void Publish(VisionRuntimePublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        long now = clock();
        bool ready = publication.Result.Status == VisionPipelineStatus.Ready
            && publication.Result.Observation is not null
            && publication.Result.Observation.Self.FreshUntilMonoMs > now;
        PreviewOverlay overlay = ready ? BuildOverlay(publication, now) : EmptyOverlay(publication.FrameMetadata.FrameId, now);
        string? warning = ready ? null : publication.Result.Diagnostic;
        RuntimeTelemetrySnapshot metrics = telemetry.Collect(publication, stateProvider(), null, warning, pauseReasonProvider());

        nativeSink.PublishOverlay(overlay);
        nativeSink.PublishTelemetry(metrics.Preview, publication.FrameMetadata.FrameId);
        SendEvent("overlay.updated", ToContractOverlay(overlay));
        SendEvent("telemetry.updated", metrics.Contract);
        if (ready) SendEvent("observation.updated", publication.Result.Observation!);
        SendEvent("vision.status.updated", new VisionStatusPayload
        {
            Status = ready ? VisionModelStatus.Ready : VisionModelStatus.Repairing,
            ModelId = modelId,
            Provider = metrics.Contract.InferenceProvider,
            Diagnostic = ready ? "OK" : publication.Result.Diagnostic,
        });
    }

    public void PublishFault(string code, long droppedFrames)
    {
        long now = clock();
        nativeSink.PublishOverlay(EmptyOverlay(0, now));
        SendEvent("vision.status.updated", new VisionStatusPayload
        {
            Status = VisionModelStatus.Faulted,
            ModelId = modelId,
            Provider = InferenceProvider.None,
            Diagnostic = code,
        });
    }

    private PreviewOverlay BuildOverlay(VisionRuntimePublication publication, long now)
    {
        DynamicVisionResult? dynamic = publication.Result.Dynamic;
        ObservationSnapshot observation = publication.Result.Observation!;
        return new PreviewOverlay
        {
            SchemaVersion = ContractConstants.SchemaVersion,
            FrameId = observation.FrameId,
            GeneratedAtMonoMs = now,
            Self = observation.Self,
            Players = observation.Players,
            Monsters = observation.Monsters,
            ModelVersion = dynamic?.ModelVersion ?? modelId,
        };
    }

    private static PreviewOverlay EmptyOverlay(long frameId, long now) => new()
    {
        SchemaVersion = ContractConstants.SchemaVersion,
        FrameId = frameId,
        GeneratedAtMonoMs = now,
        Players = [],
        Monsters = [],
        ModelVersion = string.Empty,
    };

    private static Maple.Contracts.OverlaySnapshot ToContractOverlay(PreviewOverlay value) => new()
    {
        SchemaVersion = value.SchemaVersion,
        FrameId = value.FrameId,
        GeneratedAtMonoMs = value.GeneratedAtMonoMs,
        Self = value.Self,
        Players = value.Players,
        Monsters = value.Monsters,
        SelectedTargetId = value.SelectedTargetId,
        ModelVersion = value.ModelVersion,
    };

    private void SendEvent(string type, object payload) => send(JsonSerializer.Serialize(new
    {
        schemaVersion = ContractConstants.SchemaVersion,
        type,
        timestamp = DateTimeOffset.UtcNow,
        payload,
    }, JsonOptions));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new EnumMemberJsonConverterFactory());
        return options;
    }
}

internal sealed class EnumMemberJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(EnumMemberJsonConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class EnumMemberJsonConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        private static readonly Dictionary<TEnum, string> ToWire = Enum.GetValues<TEnum>().ToDictionary(value => value, WireName);
        private static readonly Dictionary<string, TEnum> FromWire = ToWire.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && FromWire.TryGetValue(reader.GetString() ?? string.Empty, out TEnum value)
                ? value : throw new JsonException($"Invalid {typeof(TEnum).Name}");
        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) => writer.WriteStringValue(ToWire[value]);
        private static string WireName(TEnum value)
        {
            string name = value.ToString();
            return typeof(TEnum).GetField(name)?.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? name;
        }
    }
}
