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
    public async Task LazyAdapterStartsOnlyWhenResumeFlowRequestsIt()
    {
        var client = new RecordingClient();
        var factory = new RecordingClientFactory(client);
        using var adapter = new BrokerInputAdapter(factory);

        Assert.Equal("INPUT_BROKER_DISCONNECTED", adapter.GetStatus().Code);
        Assert.Equal(0, factory.CreateCalls);

        await adapter.EnsureStartedAsync(CancellationToken.None);
        adapter.ArmTarget(new ArmTargetPayload(1, 2, 3, "C:\\game.exe"));

        Assert.Equal(1, factory.CreateCalls);
        Assert.True(adapter.GetStatus().InjectionEnabled);
        Assert.Contains(client.Requests, request => request.Kind == BrokerRequestKind.ArmTarget);
    }

    [Fact]
    public void ReleaseAllBeforeStartDoesNotCreateBrokerOrEnableInput()
    {
        var factory = new RecordingClientFactory(new RecordingClient());
        using var adapter = new BrokerInputAdapter(factory);

        InputResult result = adapter.ReleaseAll(50);

        Assert.Equal(InputStatus.Completed, result.Status);
        Assert.Equal(0, factory.CreateCalls);
        Assert.False(adapter.GetStatus().InjectionEnabled);
    }

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

    private sealed class RecordingClientFactory(IBrokerClient client) : IBrokerClientFactory
    {
        public int CreateCalls { get; private set; }

        public Task<IBrokerClient> CreateAsync(CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(client);
        }
    }
}
