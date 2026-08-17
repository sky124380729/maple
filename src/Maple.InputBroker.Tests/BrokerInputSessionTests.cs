using System.Collections.Generic;
using System.Threading.Tasks;
using Maple.Input;
using Maple.InputBroker;
using Xunit;

namespace Maple.InputBroker.Tests;

public sealed class BrokerInputSessionTests
{
    [Fact]
    public async Task HeartbeatTimeoutReleasesEveryActiveKey()
    {
        var sender = new RecordingSender();
        var clock = new FakeClock(1_000);
        await using var session = TestSession(sender, clock, heartbeatTimeoutMs: 500);
        await session.HandleAsync(KeyDown(1, BrokerActionKind.MoveLeft, 200));
        clock.NowMonoMs = 1_501;

        await session.CheckWatchdogAsync();

        Assert.Equal(new[] { "MoveLeft" }, session.LastReleasedKeys);
        Assert.Equal(0x0003u, sender.Events[^1].Flags);
        Assert.Empty(session.ActiveKeys);
    }

    [Theory]
    [InlineData(false, true, true, "TARGET_NOT_FOREGROUND")]
    [InlineData(true, false, true, "TARGET_IDENTITY_CHANGED")]
    [InlineData(true, true, false, "FRAME_STALE")]
    public async Task SafetyFailureRejectsAndReleases(
        bool foreground,
        bool identity,
        bool fresh,
        string code)
    {
        var sender = new RecordingSender();
        var clock = new FakeClock(1_000);
        var safety = new MutableSafetyGate();
        await using var session = new BrokerInputSession(sender, safety, clock, 500);
        await ArmAsync(session);
        await session.HandleAsync(KeyDown(1, BrokerActionKind.MoveLeft, 200));
        safety.Foreground = foreground;
        safety.IdentityMatches = identity;
        safety.FrameFresh = fresh;

        BrokerResponse response = await session.HandleAsync(
            KeyDown(2, BrokerActionKind.MoveRight, 200));

        Assert.False(response.Accepted);
        Assert.Equal(code, response.Code);
        Assert.Empty(session.ActiveKeys);
        Assert.Contains(sender.Events, item => item.Flags == 0x0003u);
    }

    [Fact]
    public async Task OppositeDirectionIsReleasedBeforeNextDirectionIsPressed()
    {
        var sender = new RecordingSender();
        await using var session = TestSession(sender, new FakeClock(1_000));

        await session.HandleAsync(KeyDown(1, BrokerActionKind.MoveLeft, 200));
        await session.HandleAsync(KeyDown(2, BrokerActionKind.MoveRight, 200));

        Assert.Collection(sender.Events,
            item => Assert.Equal((0x25, 0x4Bu, 0x0001u), item),
            item => Assert.Equal((0x25, 0x4Bu, 0x0003u), item),
            item => Assert.Equal((0x27, 0x4Du, 0x0001u), item));
        Assert.Equal(new[] { "MoveRight" }, session.ActiveKeys);
    }

    [Fact]
    public async Task WatchdogRechecksForegroundWhileKeyIsHeld()
    {
        var sender = new RecordingSender();
        var clock = new FakeClock(1_000);
        var safety = new MutableSafetyGate();
        await using var session = new BrokerInputSession(sender, safety, clock, 500);
        await ArmAsync(session);
        await session.HandleAsync(KeyDown(1, BrokerActionKind.MoveLeft, 200));
        safety.Foreground = false;

        await session.CheckWatchdogAsync();

        Assert.Empty(session.ActiveKeys);
        Assert.Equal(new[] { "MoveLeft" }, session.LastReleasedKeys);
        Assert.Equal(0x0003u, sender.Events[^1].Flags);
    }

    [Fact]
    public async Task RepeatedKeyDownRefreshesLeaseWithoutRepeatingPhysicalKeyDown()
    {
        var sender = new RecordingSender();
        var clock = new FakeClock(1_000);
        var safety = new ExpiringSafetyGate(clock);
        await using var session = new BrokerInputSession(sender, safety, clock, 500);
        await ArmAsync(session);

        await session.HandleAsync(KeyDown(1, BrokerActionKind.SingleAttack, 0, 1_300));
        clock.NowMonoMs = 1_250;
        await session.HandleAsync(KeyDown(2, BrokerActionKind.SingleAttack, 0, 1_550));
        clock.NowMonoMs = 1_400;
        await session.CheckWatchdogAsync();

        Assert.Single(sender.Events);
        Assert.Equal(new[] { "SingleAttack" }, session.ActiveKeys);
    }

    [Fact]
    public async Task ReleaseAllDisarmsUntilTargetIsArmedAgain()
    {
        var sender = new RecordingSender();
        await using var session = TestSession(sender, new FakeClock(1_000));
        await session.HandleAsync(KeyDown(1, BrokerActionKind.MoveLeft, 200));
        await session.HandleAsync(new BrokerRequest(
            BrokerProtocol.Version,
            2,
            BrokerRequestKind.ReleaseAll,
            null));

        BrokerResponse response = await session.HandleAsync(
            KeyDown(3, BrokerActionKind.MoveRight, 200));

        Assert.False(response.Accepted);
        Assert.Equal("TARGET_NOT_ARMED", response.Code);
        Assert.Equal(2, sender.Events.Count);
    }

    [Theory]
    [InlineData(-1, 100, "INVALID_DURATION")]
    [InlineData(101, 100, "INVALID_DURATION")]
    [InlineData(100, 5001, "INVALID_DURATION")]
    public async Task InvalidDurationsAreRejected(int holdMs, int maximumMs, string code)
    {
        var sender = new RecordingSender();
        await using var session = TestSession(sender, new FakeClock(1_000));
        BrokerRequest request = new(
            BrokerProtocol.Version,
            1,
            BrokerRequestKind.KeyDownAction,
            new BrokerActionPayload("a-1", BrokerActionKind.Jump, null, holdMs, maximumMs));

        BrokerResponse response = await session.HandleAsync(request);

        Assert.False(response.Accepted);
        Assert.Equal(code, response.Code);
        Assert.Empty(sender.Events);
    }

    private static BrokerInputSession TestSession(
        RecordingSender sender,
        FakeClock clock,
        int heartbeatTimeoutMs = 500)
    {
        var session = new BrokerInputSession(sender, new MutableSafetyGate(), clock, heartbeatTimeoutMs);
        ArmAsync(session).GetAwaiter().GetResult();
        return session;
    }

    private static async Task ArmAsync(BrokerInputSession session)
    {
        BrokerResponse response = await session.HandleAsync(new BrokerRequest(
            BrokerProtocol.Version,
            0,
            BrokerRequestKind.ArmTarget,
            new ArmTargetPayload(1, 1, 1, "C:\\authorized-test-client.exe")));
        Assert.True(response.Accepted);
    }

    private static BrokerRequest KeyDown(long sequence, BrokerActionKind action, int holdMs, long frameFreshUntilMonoMs = 0)
    {
        return new BrokerRequest(
            BrokerProtocol.Version,
            sequence,
            BrokerRequestKind.KeyDownAction,
            new BrokerActionPayload("a-" + sequence, action, null, holdMs, 300, frameFreshUntilMonoMs));
    }

    private sealed class RecordingSender : IBrokerKeySender
    {
        public List<(ushort VirtualKey, uint ScanCode, uint Flags)> Events { get; } = new();

        public void Send(BrokerKeyEncoding encoding, bool isKeyUp)
        {
            uint flags = encoding.Extended ? WindowsKeybdEventSender.KeyEventFExtendedKey : 0;
            if (isKeyUp) flags |= WindowsKeybdEventSender.KeyEventFKeyUp;
            Events.Add((encoding.VirtualKey, encoding.ScanCode, flags));
        }
    }

    private sealed class FakeClock : IBrokerClock
    {
        public FakeClock(long nowMonoMs) => NowMonoMs = nowMonoMs;
        public long NowMonoMs { get; set; }
    }

    private sealed class MutableSafetyGate : IBrokerSafetyGate
    {
        public bool Foreground { get; set; } = true;
        public bool IdentityMatches { get; set; } = true;
        public bool FrameFresh { get; set; } = true;

        public BrokerSafetyResult Arm(ArmTargetPayload target) => BrokerSafetyResult.Allow();

        public BrokerSafetyResult Evaluate(BrokerActionPayload action)
        {
            if (!Foreground) return BrokerSafetyResult.Reject("TARGET_NOT_FOREGROUND");
            if (!IdentityMatches) return BrokerSafetyResult.Reject("TARGET_IDENTITY_CHANGED");
            if (!FrameFresh) return BrokerSafetyResult.Reject("FRAME_STALE");
            return BrokerSafetyResult.Allow();
        }
    }

    private sealed class ExpiringSafetyGate(FakeClock clock) : IBrokerSafetyGate
    {
        public BrokerSafetyResult Arm(ArmTargetPayload target) => BrokerSafetyResult.Allow();
        public BrokerSafetyResult Evaluate(BrokerActionPayload action) =>
            action.FrameFreshUntilMonoMs >= clock.NowMonoMs
                ? BrokerSafetyResult.Allow()
                : BrokerSafetyResult.Reject("FRAME_STALE");
    }
}
