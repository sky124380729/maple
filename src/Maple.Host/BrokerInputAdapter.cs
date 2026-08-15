using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maple.Contracts;
using Maple.Input;

namespace Maple.Host;

public sealed class BrokerInputAdapter : IInputAdapter, IDisposable
{
    private readonly IBrokerClient client;
    private readonly BlockingCollection<WorkItem> work = new();
    private readonly Thread worker;
    private readonly object sync = new();
    private readonly HashSet<string> activeKeys = new(StringComparer.OrdinalIgnoreCase);
    private InputAdapterStatus status = new()
    {
        AdapterName = "BrokerInputAdapter",
        Code = "INPUT_BROKER_STARTING",
        IsHealthy = false,
        InjectionEnabled = false,
        ActiveKeys = new List<string>()
    };
    private bool disposed;

    public BrokerInputAdapter(IBrokerClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        worker = new Thread(WorkerMain)
        {
            IsBackground = true,
            Name = "Maple.InputBroker.ClientWorker"
        };
        worker.Start();
    }

    public void ArmTarget(ArmTargetPayload target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        Invoke(BrokerRequestKind.ArmTarget, target);
        SetStatus(true, true, "INPUT_BROKER_READY");
    }

    public InputResult KeyDown(AbstractAction action, string key, long nowMonoMs)
    {
        BrokerActionPayload payload = CreatePayload(action, key);
        Invoke(BrokerRequestKind.KeyDownAction, payload);
        lock (sync) activeKeys.Add(payload.Action.ToString());
        return Result(action, InputStatus.Accepted, nowMonoMs, "BROKER_KEY_DOWN_ACK");
    }

    public InputResult KeyUp(AbstractAction action, string key, long nowMonoMs)
    {
        BrokerActionPayload payload = CreatePayload(action, key);
        Invoke(BrokerRequestKind.KeyUpAction, payload);
        lock (sync) activeKeys.Remove(payload.Action.ToString());
        return Result(action, InputStatus.Completed, nowMonoMs, "BROKER_KEY_UP_ACK");
    }

    public InputResult Press(AbstractAction action, string key, long nowMonoMs)
    {
        Invoke(BrokerRequestKind.PressAction, CreatePayload(action, key));
        return Result(action, InputStatus.Completed, nowMonoMs, "BROKER_PRESS_ACK");
    }

    public InputResult ReleaseAll(long nowMonoMs)
    {
        try
        {
            Invoke(BrokerRequestKind.ReleaseAll, null);
            lock (sync) activeKeys.Clear();
            return Result(null, InputStatus.Completed, nowMonoMs, "BROKER_RELEASE_ALL_ACK");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            lock (sync) activeKeys.Clear();
            SetStatus(false, false, "BROKER_RELEASE_ALL_FAILED");
            return Result(null, InputStatus.Failed, nowMonoMs, "BROKER_RELEASE_ALL_FAILED");
        }
    }

    public bool Heartbeat(long nowMonoMs)
    {
        try
        {
            Invoke(BrokerRequestKind.Heartbeat, null);
            lock (sync) status.LastHeartbeatMonoMs = nowMonoMs;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SetStatus(false, false, "BROKER_HEARTBEAT_FAILED");
            return false;
        }
    }

    public InputAdapterStatus GetStatus()
    {
        lock (sync)
        {
            return new InputAdapterStatus
            {
                AdapterName = status.AdapterName,
                Code = status.Code,
                IsHealthy = status.IsHealthy,
                InjectionEnabled = status.InjectionEnabled,
                LastHeartbeatMonoMs = status.LastHeartbeatMonoMs,
                ActiveKeys = activeKeys.ToArray()
            };
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        ReleaseAll(Environment.TickCount64);
        disposed = true;
        work.CompleteAdding();
        worker.Join(TimeSpan.FromSeconds(2));
        work.Dispose();
    }

    private BrokerResponse Invoke(BrokerRequestKind kind, BrokerPayload? payload)
    {
        if (disposed) throw new InputUnavailableException("INPUT_BROKER_ADAPTER_DISPOSED");
        var completion = new TaskCompletionSource<BrokerResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            work.Add(new WorkItem(kind, payload, completion));
        }
        catch (InvalidOperationException exception)
        {
            throw new InputUnavailableException("INPUT_BROKER_WORKER_STOPPED", exception);
        }

        BrokerResponse response = completion.Task
            .WaitAsync(TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();
        SetStatus(true, true, "INPUT_BROKER_READY");
        return response;
    }

    private void WorkerMain()
    {
        try
        {
            foreach (WorkItem item in work.GetConsumingEnumerable())
            {
                try
                {
                    BrokerResponse response = client.SendAsync(
                        item.Kind,
                        item.Payload,
                        CancellationToken.None).GetAwaiter().GetResult();
                    item.Completion.TrySetResult(response);
                }
                catch (Exception exception)
                {
                    item.Completion.TrySetException(exception);
                }
            }
        }
        finally
        {
            client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static BrokerActionPayload CreatePayload(AbstractAction action, string logicalKey)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        ContractValidationResult validation = ContractValidation.ValidateAction(action);
        if (!validation.IsValid) throw new InputUnavailableException("ACTION_CONTRACT_INVALID:" + validation.Error);
        return new BrokerActionPayload(
            action.ActionId,
            BrokerActionMapping.ToBrokerAction(action),
            logicalKey,
            action.HoldMs,
            action.MaxDurationMs,
            checked(action.IssuedAtMonoMs + action.MaxDurationMs));
    }

    private void SetStatus(bool healthy, bool enabled, string code)
    {
        lock (sync)
        {
            status.IsHealthy = healthy;
            status.InjectionEnabled = enabled;
            status.Code = code;
        }
    }

    private static InputResult Result(
        AbstractAction? action,
        InputStatus status,
        long nowMonoMs,
        string message) => new()
    {
        SchemaVersion = ContractConstants.SchemaVersion,
        ActionId = action?.ActionId ?? "release-all",
        Status = status,
        StartedAtMonoMs = nowMonoMs,
        EndedAtMonoMs = status == InputStatus.Accepted ? null : nowMonoMs,
        ReleasedKeys = new List<string>(),
        Message = message
    };

    private sealed record WorkItem(
        BrokerRequestKind Kind,
        BrokerPayload? Payload,
        TaskCompletionSource<BrokerResponse> Completion);
}
