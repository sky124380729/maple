using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maple.Contracts;
using Maple.Input;

namespace Maple.Host;

public sealed class BrokerInputAdapter : IInputAdapter, IInputBrokerSession, IDisposable
{
    private IBrokerClient? client;
    private readonly IBrokerClientFactory? clientFactory;
    private readonly BlockingCollection<WorkItem> work = new();
    private Thread worker = null!;
    private readonly object sync = new();
    private readonly SemaphoreSlim startLock = new(1, 1);
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
        status.Code = "INPUT_BROKER_CONNECTED";
        status.IsHealthy = true;
        StartWorker();
    }

    public BrokerInputAdapter(IBrokerClientFactory clientFactory)
    {
        this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        status.Code = "INPUT_BROKER_DISCONNECTED";
        StartWorker();
    }

    private void StartWorker()
    {
        worker = new Thread(WorkerMain)
        {
            IsBackground = true,
            Name = "Maple.InputBroker.ClientWorker"
        };
        worker.Start();
    }

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (disposed) throw new InputUnavailableException("INPUT_BROKER_ADAPTER_DISPOSED");
        if (client is not null) return;
        if (clientFactory is null) throw new InputUnavailableException("INPUT_BROKER_FACTORY_MISSING");

        await startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (client is not null) return;
            SetStatus(false, false, "INPUT_BROKER_STARTING");
            try
            {
                client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
                SetStatus(true, false, "INPUT_BROKER_CONNECTED");
            }
            catch
            {
                SetStatus(false, false, "INPUT_BROKER_START_FAILED");
                throw;
            }
        }
        finally { startLock.Release(); }
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
        SetStatus(true, true, "INPUT_BROKER_READY");
        lock (sync) activeKeys.Add(payload.Action.ToString());
        return Result(action, InputStatus.Accepted, nowMonoMs, "BROKER_KEY_DOWN_ACK");
    }

    public InputResult KeyUp(AbstractAction action, string key, long nowMonoMs)
    {
        BrokerActionPayload payload = CreatePayload(action, key);
        Invoke(BrokerRequestKind.KeyUpAction, payload);
        SetStatus(true, true, "INPUT_BROKER_READY");
        lock (sync) activeKeys.Remove(payload.Action.ToString());
        return Result(action, InputStatus.Completed, nowMonoMs, "BROKER_KEY_UP_ACK");
    }

    public InputResult Press(AbstractAction action, string key, long nowMonoMs)
    {
        Invoke(BrokerRequestKind.PressAction, CreatePayload(action, key));
        SetStatus(true, true, "INPUT_BROKER_READY");
        return Result(action, InputStatus.Completed, nowMonoMs, "BROKER_PRESS_ACK");
    }

    public InputResult ReleaseAll(long nowMonoMs)
    {
        if (client is null)
        {
            lock (sync) activeKeys.Clear();
            SetStatus(false, false, "INPUT_BROKER_DISCONNECTED");
            return Result(null, InputStatus.Completed, nowMonoMs, "BROKER_NOT_STARTED_RELEASED");
        }
        try
        {
            Invoke(BrokerRequestKind.ReleaseAll, null);
            lock (sync) activeKeys.Clear();
            SetStatus(true, false, "INPUT_BROKER_RELEASED");
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
        if (client is not null) ReleaseAll(Environment.TickCount64);
        disposed = true;
        work.CompleteAdding();
        worker.Join(TimeSpan.FromSeconds(2));
        work.Dispose();
        startLock.Dispose();
        if (clientFactory is IDisposable disposableFactory) disposableFactory.Dispose();
    }

    private BrokerResponse Invoke(BrokerRequestKind kind, BrokerPayload? payload)
    {
        if (disposed) throw new InputUnavailableException("INPUT_BROKER_ADAPTER_DISPOSED");
        if (client is null) throw new InputUnavailableException("INPUT_BROKER_NOT_STARTED");
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
                    IBrokerClient currentClient = client
                        ?? throw new InputUnavailableException("INPUT_BROKER_NOT_STARTED");
                    BrokerResponse response = currentClient.SendAsync(
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
            IBrokerClient? currentClient = client;
            if (currentClient is not null)
                currentClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
