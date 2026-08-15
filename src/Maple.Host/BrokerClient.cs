using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Maple.Input;

namespace Maple.Host;

public interface IBrokerClient : IAsyncDisposable
{
    BrokerResponse? LastResponse { get; }
    Task<BrokerResponse> SendAsync(
        BrokerRequestKind kind,
        BrokerPayload? payload,
        CancellationToken cancellationToken);
}

public interface IBrokerTransport : IAsyncDisposable
{
    Task<BrokerResponse> ExchangeAsync(BrokerRequest request, CancellationToken token);
}

public sealed class BrokerClient : IBrokerClient
{
    private readonly IBrokerTransport transport;
    private readonly CancellationTokenSource heartbeatCancellation = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly Task? heartbeatTask;
    private long sequence;
    private bool disposed;

    public BrokerClient(IBrokerTransport transport, TimeSpan? heartbeatInterval = null)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        if (heartbeatInterval.HasValue)
        {
            if (heartbeatInterval.Value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(heartbeatInterval));
            heartbeatTask = RunHeartbeatAsync(heartbeatInterval.Value, heartbeatCancellation.Token);
        }
    }

    public BrokerResponse? LastResponse { get; private set; }

    public async Task<BrokerResponse> SendAsync(
        BrokerRequestKind kind,
        BrokerPayload? payload,
        CancellationToken cancellationToken)
    {
        if (disposed) throw new InputUnavailableException("INPUT_BROKER_DISPOSED");
        await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long requestSequence = ++sequence;
            var request = new BrokerRequest(BrokerProtocol.Version, requestSequence, kind, payload);
            BrokerResponse response;
            try
            {
                response = await transport.ExchangeAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (InputUnavailableException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InputUnavailableException("INPUT_BROKER_IPC_FAILED", exception);
            }

            if (response.Version != BrokerProtocol.Version)
                throw new InputUnavailableException("BROKER_RESPONSE_VERSION_MISMATCH");
            if (response.Sequence != requestSequence)
                throw new InputUnavailableException("BROKER_RESPONSE_SEQUENCE_MISMATCH");
            LastResponse = response;
            if (!response.Accepted) throw new InputUnavailableException(response.Code);
            return response;
        }
        finally
        {
            sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        heartbeatCancellation.Cancel();
        if (heartbeatTask != null)
        {
            try { await heartbeatTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (InputUnavailableException) { }
        }
        try
        {
            await SendAsync(BrokerRequestKind.ReleaseAll, null, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch { }
        disposed = true;
        heartbeatCancellation.Dispose();
        await transport.DisposeAsync().ConfigureAwait(false);
        sendLock.Dispose();
    }

    private async Task RunHeartbeatAsync(TimeSpan interval, CancellationToken token)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            await SendAsync(BrokerRequestKind.Heartbeat, null, token).ConfigureAwait(false);
        }
    }
}

public sealed class NamedPipeBrokerTransport : IBrokerTransport
{
    private const int MaximumMessageBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly NamedPipeClientStream pipe;
    private readonly SemaphoreSlim exchangeLock = new(1, 1);

    private NamedPipeBrokerTransport(NamedPipeClientStream pipe) => this.pipe = pipe;

    public static async Task<NamedPipeBrokerTransport> ConnectAsync(
        string pipeName,
        TimeSpan timeout,
        CancellationToken token)
    {
        var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            await pipe.ConnectAsync(timeoutCancellation.Token).ConfigureAwait(false);
            return new NamedPipeBrokerTransport(pipe);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    public async Task<BrokerResponse> ExchangeAsync(BrokerRequest request, CancellationToken token)
    {
        await exchangeLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await WriteAsync(request, token).ConfigureAwait(false);
            return await ReadAsync(token).ConfigureAwait(false);
        }
        finally
        {
            exchangeLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        pipe.Dispose();
        exchangeLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task WriteAsync(BrokerRequest request, CancellationToken token)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        if (payload.Length <= 0 || payload.Length > MaximumMessageBytes)
            throw new InvalidDataException("MESSAGE_SIZE_INVALID");
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await pipe.WriteAsync(prefix, token).ConfigureAwait(false);
        await pipe.WriteAsync(payload, token).ConfigureAwait(false);
        await pipe.FlushAsync(token).ConfigureAwait(false);
    }

    private async Task<BrokerResponse> ReadAsync(CancellationToken token)
    {
        byte[] prefix = new byte[4];
        await ReadExactlyAsync(prefix, token).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > MaximumMessageBytes)
            throw new InvalidDataException("MESSAGE_SIZE_INVALID");
        byte[] payload = new byte[length];
        await ReadExactlyAsync(payload, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<BrokerResponse>(payload, JsonOptions)
            ?? throw new InvalidDataException("BROKER_RESPONSE_EMPTY");
    }

    private async Task ReadExactlyAsync(byte[] buffer, CancellationToken token)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await pipe.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset), token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
