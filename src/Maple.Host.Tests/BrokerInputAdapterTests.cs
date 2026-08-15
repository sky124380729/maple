using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Maple.Contracts;
using Maple.Host;
using Maple.Input;
using Xunit;

namespace Maple.Host.Tests;

public sealed class BrokerInputAdapterTests
{
    [Fact]
    public void PipeWorkRunsOnDedicatedWorkerAndCarriesNoRawKeyFields()
    {
        int callerThread = Environment.CurrentManagedThreadId;
        var client = new RecordingClient();
        using var adapter = new BrokerInputAdapter(client);
        var action = new AbstractAction
        {
            ActionId = "a-1",
            Type = ActionType.MoveLeft,
            IssuedAtMonoMs = 100,
            HoldMs = 120,
            MaxDurationMs = 300
        };

        InputResult result = adapter.KeyDown(action, null!, 100);

        Assert.Equal(InputStatus.Accepted, result.Status);
        Assert.NotEqual(callerThread, client.SendThreadId);
        BrokerActionPayload payload = Assert.IsType<BrokerActionPayload>(client.Requests[0].Payload);
        Assert.Equal(BrokerActionKind.MoveLeft, payload.Action);
        Assert.Null(payload.LogicalKey);
        Assert.Equal(400, payload.FrameFreshUntilMonoMs);
    }

    private sealed class RecordingClient : IBrokerClient
    {
        private long sequence;
        public int SendThreadId { get; private set; }
        public List<(BrokerRequestKind Kind, BrokerPayload? Payload)> Requests { get; } = new();
        public BrokerResponse? LastResponse { get; private set; }

        public Task<BrokerResponse> SendAsync(
            BrokerRequestKind kind,
            BrokerPayload? payload,
            CancellationToken cancellationToken)
        {
            SendThreadId = Environment.CurrentManagedThreadId;
            Requests.Add((kind, payload));
            LastResponse = new BrokerResponse(
                BrokerProtocol.Version,
                ++sequence,
                true,
                "OK",
                Array.Empty<string>());
            return Task.FromResult(LastResponse);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
