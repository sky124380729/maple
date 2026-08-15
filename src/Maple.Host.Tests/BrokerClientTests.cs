using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Maple.Host;
using Maple.Input;
using Xunit;

namespace Maple.Host.Tests;

public sealed class BrokerClientTests
{
    [Fact]
    public async Task ClientRejectsMismatchedResponseSequence()
    {
        var transport = new FakeTransport(request => new BrokerResponse(
            BrokerProtocol.Version,
            request.Sequence + 1,
            true,
            "OK",
            Array.Empty<string>()));
        await using var client = new BrokerClient(transport);

        InputUnavailableException exception = await Assert.ThrowsAsync<InputUnavailableException>(() =>
            client.SendAsync(BrokerRequestKind.Heartbeat, null, CancellationToken.None));

        Assert.Equal("BROKER_RESPONSE_SEQUENCE_MISMATCH", exception.Code);
    }

    [Fact]
    public async Task DisposalAlwaysRequestsReleaseAll()
    {
        var transport = new FakeTransport(request => new BrokerResponse(
            BrokerProtocol.Version,
            request.Sequence,
            true,
            "OK",
            Array.Empty<string>()));
        var client = new BrokerClient(transport);

        await client.DisposeAsync();

        Assert.Contains(transport.Requests, request => request.Kind == BrokerRequestKind.ReleaseAll);
        Assert.True(transport.Disposed);
    }

    private sealed class FakeTransport : IBrokerTransport
    {
        private readonly Func<BrokerRequest, BrokerResponse> respond;
        public FakeTransport(Func<BrokerRequest, BrokerResponse> respond) => this.respond = respond;
        public List<BrokerRequest> Requests { get; } = new();
        public bool Disposed { get; private set; }

        public Task<BrokerResponse> ExchangeAsync(BrokerRequest request, CancellationToken token)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
