using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maple.Input;

namespace Maple.InputBroker;

public sealed class BrokerInputSession : IAsyncDisposable
{
    private const int MaximumAllowedDurationMs = 5_000;
    private const int MaximumAllowedAttackDurationMs = 30_000;
    private readonly IBrokerKeySender sender;
    private readonly IBrokerSafetyGate safety;
    private readonly IBrokerClock clock;
    private readonly int heartbeatTimeoutMs;
    private readonly object sync = new();
    private readonly Dictionary<BrokerActionKind, ActiveBrokerKey> active = new();
    private long lastHeartbeatMonoMs;
    private bool disposed;
    private bool armed;
    private IReadOnlyList<string> lastReleasedKeys = Array.Empty<string>();

    public BrokerInputSession(
        IBrokerKeySender sender,
        IBrokerSafetyGate safety,
        IBrokerClock clock,
        int heartbeatTimeoutMs)
    {
        this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
        this.safety = safety ?? throw new ArgumentNullException(nameof(safety));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (heartbeatTimeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(heartbeatTimeoutMs));
        this.heartbeatTimeoutMs = heartbeatTimeoutMs;
        lastHeartbeatMonoMs = clock.NowMonoMs;
    }

    public IReadOnlyList<string> ActiveKeys
    {
        get { lock (sync) return active.Keys.Select(item => item.ToString()).ToArray(); }
    }

    public IReadOnlyList<string> LastReleasedKeys
    {
        get { lock (sync) return lastReleasedKeys.ToArray(); }
    }

    public async Task<BrokerResponse> HandleAsync(
        BrokerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (disposed) return Reject(request, "SESSION_DISPOSED");
        if (request == null) return Reject(null, "REQUEST_REQUIRED");

        try
        {
            switch (request.Kind)
            {
                case BrokerRequestKind.ArmTarget:
                    if (request.Payload is not ArmTargetPayload target) return Reject(request, "PAYLOAD_KIND_MISMATCH");
                    await ReleaseAllAsync();
                    BrokerSafetyResult armed = safety.Arm(target);
                    if (!armed.Allowed) await ReleaseAllAsync();
                    else
                    {
                        this.armed = true;
                        lastHeartbeatMonoMs = clock.NowMonoMs;
                    }
                    return Response(request, armed.Allowed, armed.Code);

                case BrokerRequestKind.Heartbeat:
                    if (request.Payload != null) return Reject(request, "PAYLOAD_KIND_MISMATCH");
                    lastHeartbeatMonoMs = clock.NowMonoMs;
                    return Response(request, true, "HEARTBEAT_OK");

                case BrokerRequestKind.KeyDownAction:
                case BrokerRequestKind.KeyUpAction:
                case BrokerRequestKind.PressAction:
                    if (request.Payload is not BrokerActionPayload action) return Reject(request, "PAYLOAD_KIND_MISMATCH");
                    return await HandleActionAsync(request, action, cancellationToken);

                case BrokerRequestKind.ReleaseAll:
                    if (request.Payload != null) return Reject(request, "PAYLOAD_KIND_MISMATCH");
                    bool released = await ReleaseAllAsync();
                    return Response(request, released, released ? "ALL_KEYS_RELEASED" : "RELEASE_FAILED");

                case BrokerRequestKind.Shutdown:
                    if (request.Payload != null) return Reject(request, "PAYLOAD_KIND_MISMATCH");
                    bool shutdownReleased = await ReleaseAllAsync();
                    return Response(request, shutdownReleased, shutdownReleased ? "SHUTDOWN" : "RELEASE_FAILED");

                default:
                    await ReleaseAllAsync();
                    return Reject(request, "REQUEST_KIND_UNSUPPORTED");
            }
        }
        catch (OperationCanceledException)
        {
            await ReleaseAllAsync();
            throw;
        }
        catch (Exception exception)
        {
            await ReleaseAllAsync();
            return Reject(request, "INPUT_EXCEPTION:" + exception.GetType().Name);
        }
    }

    public async Task CheckWatchdogAsync()
    {
        if (disposed) return;
        if (clock.NowMonoMs - lastHeartbeatMonoMs > heartbeatTimeoutMs)
        {
            await ReleaseAllAsync();
            return;
        }

        BrokerActionPayload[] activeActions;
        lock (sync) activeActions = active.Values.Select(item => item.Action).ToArray();
        foreach (BrokerActionPayload action in activeActions)
        {
            if (!safety.Evaluate(action).Allowed)
            {
                await ReleaseAllAsync();
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await ReleaseAllAsync();
        disposed = true;
    }

    private async Task<BrokerResponse> HandleActionAsync(
        BrokerRequest request,
        BrokerActionPayload action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.ActionId) ||
            action.HoldMs < 0 ||
            action.MaximumDurationMs <= 0 ||
            action.HoldMs > action.MaximumDurationMs ||
            action.MaximumDurationMs > MaximumAllowedDuration(action.Action))
        {
            return Reject(request, "INVALID_DURATION");
        }

        if (!armed) return Reject(request, "TARGET_NOT_ARMED");

        if (clock.NowMonoMs - lastHeartbeatMonoMs > heartbeatTimeoutMs)
        {
            await ReleaseAllAsync();
            return Reject(request, "HEARTBEAT_TIMEOUT");
        }

        BrokerSafetyResult gate = safety.Evaluate(action);
        if (!gate.Allowed)
        {
            await ReleaseAllAsync();
            return Reject(request, gate.Code);
        }

        BrokerKeyEncoding encoding;
        try
        {
            encoding = BrokerKeyProfile.For(action.Action, action.LogicalKey);
        }
        catch (ArgumentException)
        {
            await ReleaseAllAsync();
            return Reject(request, "KEY_PROFILE_REJECTED");
        }

        if (request.Kind == BrokerRequestKind.KeyUpAction)
        {
            bool keyUp = Release(action.Action, encoding);
            return Response(request, keyUp, keyUp ? "KEY_UP_SENT" : "KEY_UP_FAILED");
        }

        lock (sync)
        {
            ReleaseOpposite(action.Action);
            if (active.TryGetValue(action.Action, out ActiveBrokerKey current))
            {
                if (!current.Encoding.Equals(encoding))
                {
                    sender.Send(current.Encoding, isKeyUp: true);
                    sender.Send(encoding, isKeyUp: false);
                }
                active[action.Action] = new ActiveBrokerKey(encoding, action);
            }
            else
            {
                sender.Send(encoding, isKeyUp: false);
                active.Add(action.Action, new ActiveBrokerKey(encoding, action));
            }
        }

        if (request.Kind == BrokerRequestKind.PressAction)
        {
            try
            {
                await Task.Delay(action.HoldMs, cancellationToken);
            }
            finally
            {
                Release(action.Action, encoding);
            }
            return Response(request, true, "PRESS_COMPLETED");
        }

        return Response(request, true, "KEY_DOWN_SENT");
    }

    private static int MaximumAllowedDuration(BrokerActionKind action) =>
        action is BrokerActionKind.SingleAttack or BrokerActionKind.AreaAttack
            ? MaximumAllowedAttackDurationMs
            : MaximumAllowedDurationMs;

    private void ReleaseOpposite(BrokerActionKind action)
    {
        lock (sync)
        {
            BrokerActionKind? opposite = action switch
            {
                BrokerActionKind.MoveLeft => BrokerActionKind.MoveRight,
                BrokerActionKind.MoveRight => BrokerActionKind.MoveLeft,
                BrokerActionKind.ClimbUp => BrokerActionKind.ClimbDown,
                BrokerActionKind.ClimbDown => BrokerActionKind.ClimbUp,
                _ => null
            };
            if (opposite.HasValue && active.TryGetValue(opposite.Value, out ActiveBrokerKey activeKey))
            {
                Release(opposite.Value, activeKey.Encoding);
            }
        }
    }

    private bool Release(BrokerActionKind action, BrokerKeyEncoding encoding)
    {
        lock (sync)
        {
            try
            {
                sender.Send(encoding, isKeyUp: true);
                active.Remove(action);
                return true;
            }
            catch
            {
                active.Remove(action);
                return false;
            }
        }
    }

    private Task<bool> ReleaseAllAsync()
    {
        lock (sync)
        {
            string[] keys = active.Keys.Select(item => item.ToString()).ToArray();
            bool succeeded = true;
            foreach ((BrokerActionKind action, ActiveBrokerKey activeKey) in active.ToArray())
            {
                succeeded &= Release(action, activeKey.Encoding);
            }
            lastReleasedKeys = keys;
            armed = false;
            return Task.FromResult(succeeded);
        }
    }

    private static BrokerResponse Reject(BrokerRequest request, string code) =>
        new(BrokerProtocol.Version, request?.Sequence ?? 0, false, code, Array.Empty<string>());

    private BrokerResponse Response(BrokerRequest request, bool accepted, string code) =>
        new(BrokerProtocol.Version, request.Sequence, accepted, code, LastReleasedKeys.ToArray());

    private sealed record ActiveBrokerKey(BrokerKeyEncoding Encoding, BrokerActionPayload Action);
}
