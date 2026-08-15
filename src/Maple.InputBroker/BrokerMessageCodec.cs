using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Maple.Input;

namespace Maple.InputBroker;

public sealed class BrokerMessageCodec
{
    public const int MaximumMessageBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<BrokerRequest> ReadRequestAsync(Stream stream, CancellationToken token)
    {
        byte[] lengthBytes = new byte[4];
        int first = await stream.ReadAsync(lengthBytes.AsMemory(0, 4), token);
        if (first == 0) return null;
        await ReadRemainingAsync(stream, lengthBytes, first, token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length <= 0 || length > MaximumMessageBytes)
            throw new InvalidDataException("MESSAGE_SIZE_INVALID");
        byte[] payload = new byte[length];
        await ReadRemainingAsync(stream, payload, 0, token);
        return JsonSerializer.Deserialize<BrokerRequest>(payload, JsonOptions)
            ?? throw new InvalidDataException("REQUEST_DESERIALIZATION_FAILED");
    }

    public Task WriteResponseAsync(Stream stream, BrokerResponse response, CancellationToken token) =>
        WriteAsync(stream, response, token);

    private static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (payload.Length > MaximumMessageBytes) throw new InvalidDataException("MESSAGE_SIZE_INVALID");
        byte[] lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, payload.Length);
        await stream.WriteAsync(lengthBytes, token);
        await stream.WriteAsync(payload, token);
        await stream.FlushAsync(token);
    }

    private static async Task ReadRemainingAsync(
        Stream stream,
        byte[] buffer,
        int offset,
        CancellationToken token)
    {
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), token);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
