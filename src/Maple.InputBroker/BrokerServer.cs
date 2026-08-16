using System;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;
using Maple.Input;

namespace Maple.InputBroker;

public sealed class BrokerServer
{
    private readonly string pipeName;
    private readonly int expectedParentPid;
    private readonly BrokerInputSession session;
    private readonly BrokerMessageCodec codec;
    private readonly BrokerRequestValidator validator;
    private readonly int watchdogPollMs;

    public BrokerServer(
        string pipeName,
        int expectedParentPid,
        BrokerInputSession session,
        BrokerMessageCodec codec,
        BrokerRequestValidator validator,
        int watchdogPollMs = 100)
    {
        if (string.IsNullOrWhiteSpace(pipeName)) throw new ArgumentException("PIPE_NAME_REQUIRED", nameof(pipeName));
        if (expectedParentPid <= 0) throw new ArgumentOutOfRangeException(nameof(expectedParentPid));
        if (watchdogPollMs <= 0) throw new ArgumentOutOfRangeException(nameof(watchdogPollMs));
        this.pipeName = pipeName;
        this.expectedParentPid = expectedParentPid;
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.watchdogPollMs = watchdogPollMs;
    }

    public async Task RunAsync(CancellationToken token)
    {
        using var pipe = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            BrokerMessageCodec.MaximumMessageBytes,
            BrokerMessageCodec.MaximumMessageBytes,
            BrokerPipeSecurity.CreateForCurrentUser());
        await pipe.WaitForConnectionAsync(token);
        int clientPid = BrokerClientIdentity.GetClientProcessId(pipe.SafePipeHandle);
        if (clientPid != expectedParentPid)
            throw new BrokerRejectedException("CLIENT_PID_MISMATCH");

        using var watchdogCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task watchdog = RunWatchdogAsync(watchdogCancellation.Token);
        try
        {
            while (!token.IsCancellationRequested && pipe.IsConnected)
            {
                BrokerRequest request = await codec.ReadRequestAsync(pipe, token);
                if (request == null) break;
                BrokerValidationResult validation = validator.Validate(request);
                BrokerResponse response;
                if (validation.Accepted)
                {
                    response = await session.HandleAsync(request, token);
                }
                else
                {
                    await session.HandleAsync(new BrokerRequest(
                        BrokerProtocol.Version,
                        long.MaxValue,
                        BrokerRequestKind.ReleaseAll,
                        null), CancellationToken.None);
                    response = new BrokerResponse(
                        BrokerProtocol.Version,
                        request.Sequence,
                        false,
                        validation.Code,
                        Array.Empty<string>());
                }
                await codec.WriteResponseAsync(pipe, response, token);
                if (request.Kind == BrokerRequestKind.Shutdown) break;
            }
        }
        finally
        {
            watchdogCancellation.Cancel();
            try { await watchdog; } catch (OperationCanceledException) { }
            await session.HandleAsync(new BrokerRequest(
                BrokerProtocol.Version,
                long.MaxValue,
                BrokerRequestKind.ReleaseAll,
                null), CancellationToken.None);
        }
    }

    private async Task RunWatchdogAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(watchdogPollMs, token);
            await session.CheckWatchdogAsync();
        }
    }
}

public sealed class BrokerRejectedException : Exception
{
    public BrokerRejectedException(string code) : base(code) { }
}
