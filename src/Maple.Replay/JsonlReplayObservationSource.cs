#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Maple.Contracts;
using Maple.Runtime;

namespace Maple.Replay;

public sealed class JsonlReplayObservationSource : IObservationSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Queue<RuntimeObservationContext> observations;

    public JsonlReplayObservationSource(Stream stream)
        : this(new StreamReader(stream ?? throw new ArgumentNullException(nameof(stream)), leaveOpen: true))
    {
    }

    public JsonlReplayObservationSource(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        observations = ReadAll(reader);
    }

    public ValueTask<RuntimeObservationContext> ReadNextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (observations.Count == 0) throw new EndOfStreamException("Replay 中没有更多观察帧");
        return ValueTask.FromResult(observations.Dequeue());
    }

    private static Queue<RuntimeObservationContext> ReadAll(TextReader reader)
    {
        var result = new Queue<RuntimeObservationContext>();
        string? line;
        long previousFrameId = -1;
        long previousCapturedAt = -1;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            ReplayObservationEnvelope envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<ReplayObservationEnvelope>(line, JsonOptions)
                    ?? throw new JsonException("Replay envelope 为空");
            }
            catch (JsonException error)
            {
                throw new InvalidDataException("Replay JSONL 格式无效", error);
            }

            if (envelope.SchemaVersion != ContractConstants.SchemaVersion)
            {
                throw new InvalidDataException("Replay schemaVersion 不兼容");
            }
            if (!string.Equals(envelope.Type, "runtime.observation", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Replay 事件类型不受支持");
            }
            if (envelope.Payload is null || !ContractValidation.ValidateObservation(envelope.Payload.Snapshot).IsValid)
            {
                throw new InvalidDataException("Replay 观察快照无效");
            }
            if (envelope.Payload.Snapshot.FrameId <= previousFrameId || envelope.Payload.Snapshot.CapturedAtMonoMs < previousCapturedAt)
            {
                throw new InvalidDataException("Replay 帧和时间必须单调递增");
            }

            previousFrameId = envelope.Payload.Snapshot.FrameId;
            previousCapturedAt = envelope.Payload.Snapshot.CapturedAtMonoMs;
            result.Enqueue(envelope.Payload);
        }

        if (result.Count == 0) throw new InvalidDataException("Replay 不包含观察帧");
        return result;
    }

    private sealed record ReplayObservationEnvelope(int SchemaVersion, string Type, RuntimeObservationContext Payload);
}
