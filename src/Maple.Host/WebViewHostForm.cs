using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maple.Contracts;
using Maple.Core;
using Maple.Input;
using Maple.Preview;
using Maple.Capture;

namespace Maple.Host
{
    public sealed class WebViewRuntimeMessageEventArgs : EventArgs
    {
        public string Json { get; set; } = string.Empty;
    }

    public interface IWebViewRuntime : IDisposable
    {
        event EventHandler<WebViewRuntimeMessageEventArgs> MessageReceived;
        event EventHandler RuntimeCrashed;
        event EventHandler ContentReset;
        void Attach(Control parent, string localAssetFolder);
        void Send(string json);
        void ReloadLocalContent();
    }

    public class WebViewHostForm : Form
    {
        private readonly IWebViewRuntime webViewRuntime;
        private readonly BridgeMessageRouter router = new BridgeMessageRouter();
        private readonly HostSafetyCoordinator safety;
        private readonly NativePreviewSurface preview = new NativePreviewSurface();
        private readonly Panel browserPanel = new Panel();
        private readonly Button emergencyButton = new Button();
        private readonly string assetFolder;
        private readonly System.Windows.Forms.Timer captureTimer = new() { Interval = CapturePollingPolicy.ActiveIntervalMs };
        private readonly System.Windows.Forms.Timer foregroundTimer = new() { Interval = 100 };
        private CaptureCoordinator? captureCoordinator;
        private ForegroundSessionController? foregroundSession;
        private IForegroundWindowController? foregroundWindow;
        private GlobalHotKeyManager? globalHotKeys;
        private IDisposable? inputLifetime;
        private PreviewBoundsIntent? previewBoundsIntent;
        private bool captureInProgress;
        private bool foregroundCheckInProgress;
        private LatestVisionFrameQueue? visionFrames;
        private VisionRuntimeService? visionService;
        private CancellationTokenSource? visionCancellation;
        private Task? visionTask;
        private VisionStatusPayload pendingVisionStatus = new()
        {
            Status = VisionModelStatus.NotConfigured,
            ModelId = null!,
            Provider = InferenceProvider.None,
            Diagnostic = "MODEL_NOT_CONFIGURED",
        };
        private CombatConfiguration pendingCombatConfiguration = CombatConfiguration.Default;
        private AutomaticCombatController? automaticCombat;
        private SamePlatformCombatTrialController? combatTrial;

        public WebViewHostForm(IWebViewRuntime webViewRuntime, IInputAdapter inputAdapter, string assetFolder)
            : this(
                webViewRuntime,
                new HostSafetyCoordinator(inputAdapter ?? throw new ArgumentNullException(nameof(inputAdapter))),
                assetFolder)
        {
        }

        public WebViewHostForm(
            IWebViewRuntime webViewRuntime,
            HostSafetyCoordinator safety,
            string assetFolder)
        {
            this.webViewRuntime = webViewRuntime ?? throw new ArgumentNullException("webViewRuntime");
            this.safety = safety ?? throw new ArgumentNullException(nameof(safety));
            this.assetFolder = ValidateAssetFolder(assetFolder);
            Text = "Maple 工作台";
            ClientSize = new Size(1440, 900);
            BuildLayout();
            this.webViewRuntime.MessageReceived += OnMessageReceived;
            this.webViewRuntime.RuntimeCrashed += OnRuntimeCrashed;
            this.webViewRuntime.ContentReset += OnContentReset;
            Shown += OnShown;
            captureTimer.Tick += OnCaptureTick;
            foregroundTimer.Tick += OnForegroundTick;
        }

        public event EventHandler<BridgeRouteResult>? CommandReceived;
        public NativePreviewSurface PreviewSurface { get { return preview; } }
        public TargetBinding? CurrentCaptureTarget => captureCoordinator?.ActiveTargetBinding;

        public void ConfigureInputSession(
            ForegroundSessionController session,
            IForegroundWindowController windowController,
            GlobalHotKeyManager? hotKeys,
            IDisposable lifetime)
        {
            if (foregroundSession is not null) throw new InvalidOperationException("Input session is already configured");
            foregroundSession = session ?? throw new ArgumentNullException(nameof(session));
            foregroundWindow = windowController ?? throw new ArgumentNullException(nameof(windowController));
            globalHotKeys = hotKeys;
            inputLifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            foregroundSession.CountdownChanged += OnInputCountdownChanged;
            foregroundSession.StatusChanged += OnInputStatusChanged;
        }

        public void ConfigureCapture(ITargetWindowLocator locator, ICaptureBackend backend, ICaptureFrameObserver? observer = null)
        {
            if (captureCoordinator is not null) throw new InvalidOperationException("Capture is already configured");
            captureCoordinator = new CaptureCoordinator(
                locator,
                backend,
                new NativePreviewFrameSink(preview),
                safety,
                frameObserver: observer);
        }

        public ObservationEventPublisher CreateVisionPublisher(RuntimeTelemetryCollector telemetry, string modelId, LiveObservationSource? observations = null)
        {
            return new ObservationEventPublisher(
                new NativePreviewVisionSink(preview, SendOnUiThread),
                json => SendOnUiThread(() => webViewRuntime.Send(json)),
                telemetry,
                () => safety.State,
                () => Environment.TickCount64,
                modelId,
                () => safety.PauseReason,
                observations is null ? null : observations.Publish);
        }

        public void ConfigureVision(LatestVisionFrameQueue frames, VisionRuntimeService service, VisionStatusPayload status)
        {
            if (visionService is not null) throw new InvalidOperationException("Vision is already configured");
            visionFrames = frames ?? throw new ArgumentNullException(nameof(frames));
            visionService = service ?? throw new ArgumentNullException(nameof(service));
            pendingVisionStatus = status ?? throw new ArgumentNullException(nameof(status));
        }

        public void ConfigureVisionStatus(VisionStatusPayload status) => pendingVisionStatus = status ?? throw new ArgumentNullException(nameof(status));

        public void ConfigureCombatConfiguration(CombatConfiguration configuration) =>
            pendingCombatConfiguration = CombatConfigurationValidator.ValidateAndNormalize(configuration);

        public void ConfigureAutomaticCombat(AutomaticCombatController controller)
        {
            if (automaticCombat is not null) throw new InvalidOperationException("Automatic combat is already configured");
            automaticCombat = controller ?? throw new ArgumentNullException(nameof(controller));
            automaticCombat.StatusChanged += OnAutomaticCombatStatusChanged;
        }

        public void ConfigureCombatTrial(SamePlatformCombatTrialController controller)
        {
            if (combatTrial is not null) throw new InvalidOperationException("Combat trial is already configured");
            combatTrial = controller ?? throw new ArgumentNullException(nameof(controller));
            combatTrial.StatusChanged += OnAutomaticCombatStatusChanged;
        }

        public void SendCloudStatus(CloudRuntimeStatus status)
        {
            var message = new
            {
                schemaVersion = ContractConstants.SchemaVersion,
                type = "cloud.status.updated",
                payload = new
                {
                    provider = "bailian",
                    status.Enabled,
                    status.CredentialConfigured,
                    modelId = status.ModelId,
                    connectionStatus = status.ConnectionStatus,
                    requestInFlight = status.RequestInFlight,
                    lastErrorCode = status.LastErrorCode,
                },
            };
            webViewRuntime.Send(JsonSerializer.Serialize(message));
        }

        private void BuildLayout()
        {
            emergencyButton.Text = "紧急停止";
            emergencyButton.Dock = DockStyle.Top;
            emergencyButton.Height = 30;
            emergencyButton.BackColor = Color.FromArgb(197, 58, 74);
            emergencyButton.ForeColor = Color.White;
            emergencyButton.Click += delegate { EmergencyStop("原生紧急停止按钮"); };
            preview.Visible = false;
            browserPanel.Dock = DockStyle.Fill;
            browserPanel.ClientSizeChanged += OnBrowserClientSizeChanged;
            browserPanel.Controls.Add(preview);
            Controls.Add(browserPanel);
            Controls.Add(emergencyButton);
        }

        private void OnShown(object? sender, EventArgs e)
        {
            webViewRuntime.Attach(browserPanel, assetFolder);
            if (foregroundSession is not null) PublishInputSessionStatus(foregroundSession.CurrentStatus);
            SendVisionStatus(pendingVisionStatus);
            SendCombatConfiguration(pendingCombatConfiguration);
            if (captureCoordinator is not null) captureTimer.Start();
            if (visionService is not null)
            {
                visionCancellation = new CancellationTokenSource();
                visionTask = Task.Run(() => visionService.RunAsync(visionCancellation.Token));
            }
            if (foregroundSession is not null && globalHotKeys is not null)
            {
                HotKeyRegistrationResult registration = globalHotKeys.Register(Handle);
                if (!registration.Success) foregroundSession.Disable(registration.Code);
                else PublishInputSessionStatus(foregroundSession.CurrentStatus);
                foregroundTimer.Start();
            }
        }

        public void SendMapStatus(ActiveMapStatus status)
        {
            string json = JsonSerializer.Serialize(new
            {
                schemaVersion = ContractConstants.SchemaVersion,
                type = "map.status.updated",
                payload = new
                {
                    mapId = status.MapId,
                    state = status.State.ToString().ToLowerInvariant(),
                    coverage = status.Coverage,
                    calibrationErrorPx = status.CalibrationErrorPx,
                    platformCount = status.PlatformCount,
                    ladderCount = status.LadderCount,
                    errors = status.Errors,
                    canProduceActions = status.CanProduceActions,
                },
            }, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            SendOnUiThread(() => webViewRuntime.Send(json));
        }

        public void SendMapScanStatus(MapScanStatus status)
        {
            string json = JsonSerializer.Serialize(new
            {
                schemaVersion = ContractConstants.SchemaVersion,
                type = "map.scan.updated",
                payload = new { scanning = status.Scanning, frameIds = status.FrameIds },
            });
            SendOnUiThread(() => webViewRuntime.Send(json));
        }

        public void SendInputResult(InputResult result)
        {
            if (result is null) throw new ArgumentNullException(nameof(result));
            string json = JsonSerializer.Serialize(new
            {
                schemaVersion = ContractConstants.SchemaVersion,
                type = "input.result",
                payload = new
                {
                    schemaVersion = result.SchemaVersion,
                    actionId = result.ActionId,
                    status = result.Status.ToString().ToLowerInvariant(),
                    startedAtMonoMs = result.StartedAtMonoMs,
                    endedAtMonoMs = result.EndedAtMonoMs,
                    releasedKeys = result.ReleasedKeys,
                    message = result.Message,
                },
            });
            SendOnUiThread(() => webViewRuntime.Send(json));
        }

        private async void OnCaptureTick(object? sender, EventArgs e)
        {
            if (captureCoordinator is null || captureInProgress) return;
            captureInProgress = true;
            try
            {
                CaptureTickResult result = await captureCoordinator.CaptureOnceAsync(CancellationToken.None);
                captureTimer.Interval = CapturePollingPolicy.NextIntervalMs(result);
                if (!result.Success && result.Code == "TARGET_NOT_FOREGROUND" && foregroundSession is not null)
                    await foregroundSession.OnForegroundChangedAsync(nint.Zero, CancellationToken.None);
            }
            catch (OperationCanceledException) { }
            finally { captureInProgress = false; }
        }

        private async void OnForegroundTick(object? sender, EventArgs e)
        {
            if (foregroundCheckInProgress || foregroundSession is null || foregroundWindow is null) return;
            foregroundCheckInProgress = true;
            try
            {
                await foregroundSession.OnForegroundChangedAsync(
                    foregroundWindow.GetForegroundWindow(),
                    CancellationToken.None);
                if (automaticCombat?.IsRunning == true && !foregroundSession.IsArmed)
                    await automaticCombat.PauseAsync(PauseReason.WindowNotForeground);
                if (combatTrial?.IsRunning == true && !foregroundSession.IsArmed)
                    await combatTrial.PauseAsync(PauseReason.WindowNotForeground);
                await Task.Run(foregroundSession.RefreshStatus);
            }
            catch (OperationCanceledException) { }
            finally { foregroundCheckInProgress = false; }
        }

        private void OnMessageReceived(object? sender, WebViewRuntimeMessageEventArgs e)
        {
            BridgeRouteResult result = router.Route(e.Json);
            if (!result.Accepted)
            {
                if (PreviewLayout.IsPreviewBoundsCommand(e.Json))
                {
                    previewBoundsIntent = null;
                    HidePreviewAndPause();
                    return;
                }
                if (foregroundSession is not null) foregroundSession.Pause(PauseReason.SafetyViolation);
                else safety.PauseAndRelease();
                return;
            }
            if (result.CommandType == UiCommandType.SnapshotRequest && foregroundSession is not null)
                PublishInputSessionStatus(foregroundSession.CurrentStatus);
            else if (result.CommandType == UiCommandType.SessionEmergencyStop) EmergencyStop("React 请求紧急停止");
            else if (result.CommandType is UiCommandType.SessionArm or UiCommandType.SessionResume)
                _ = ResumeInputAsync();
            else if (result.CommandType == UiCommandType.CombatTrialStart)
                _ = StartCombatTrialAsync();
            else if (result.CommandType == UiCommandType.SessionPause)
                _ = PauseAutomaticCombatAsync(PauseReason.OperatorRequested);
            else if (result.CommandType == UiCommandType.PreviewBoundsChanged)
                ApplyPreviewBounds(result.PayloadJson);
            CommandReceived?.Invoke(this, result);
        }

        private void ApplyPreviewBounds(string? payloadJson)
        {
            try
            {
                PreviewBoundsPayload? payload = JsonSerializer.Deserialize<PreviewBoundsPayload>(
                    payloadJson ?? string.Empty,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (payload is null) throw new JsonException("preview bounds payload is missing");
                previewBoundsIntent = new PreviewBoundsIntent(payload.Left, payload.Top, payload.Width, payload.Height, payload.DevicePixelRatio);
                ApplyPreviewBounds();
            }
            catch (Exception exception) when (exception is JsonException or ArgumentOutOfRangeException)
            {
                previewBoundsIntent = null;
                HidePreviewAndPause();
            }
        }

        private void ApplyPreviewBounds()
        {
            if (previewBoundsIntent is not PreviewBoundsIntent intent) return;
            try
            {
                Rectangle resolved = PreviewLayout.Resolve(intent, browserPanel.ClientSize);
                preview.Bounds = new Rectangle(
                    browserPanel.Left + resolved.Left,
                    browserPanel.Top + resolved.Top,
                    resolved.Width,
                    resolved.Height);
                preview.Visible = true;
                preview.BringToFront();
            }
            catch (ArgumentOutOfRangeException)
            {
                HidePreviewAndPause();
            }
        }

        private void OnBrowserClientSizeChanged(object? sender, EventArgs e) => ApplyPreviewBounds();

        private void HidePreviewAndPause()
        {
            preview.Visible = false;
            if (foregroundSession is not null) foregroundSession.Pause(PauseReason.SafetyViolation);
            else safety.PauseAndRelease(PauseReason.SafetyViolation);
        }

        private async Task ResumeInputAsync()
        {
            if (automaticCombat is null)
            {
                foregroundSession?.Pause(PauseReason.CalibrationRequired);
                if (foregroundSession is null) safety.PauseAndRelease(PauseReason.CalibrationRequired);
                SendSessionState(SessionState.Paused, PauseReason.CalibrationRequired, null);
                return;
            }
            await automaticCombat.ArmAsync(CancellationToken.None);
        }

        private async Task StartCombatTrialAsync()
        {
            if (combatTrial is null)
            {
                foregroundSession?.Pause(PauseReason.CalibrationRequired);
                SendSessionState(SessionState.Paused, PauseReason.CalibrationRequired, null);
                return;
            }
            if (automaticCombat?.IsRunning == true) await automaticCombat.PauseAsync(PauseReason.OperatorRequested);
            await combatTrial.StartAsync(CancellationToken.None);
        }

        private async Task PauseAutomaticCombatAsync(PauseReason reason)
        {
            if (automaticCombat is not null) await automaticCombat.PauseAsync(reason);
            if (combatTrial is not null) await combatTrial.PauseAsync(reason);
            if (automaticCombat is null && combatTrial is null) foregroundSession?.Pause(reason);
        }

        private void OnAutomaticCombatStatusChanged(object? sender, AutomaticCombatStatus status) =>
            SendOnUiThread(() => SendSessionState(status.State, status.PauseReason, null));

        private void OnInputCountdownChanged(object? sender, int secondsRemaining)
        {
            SendOnUiThread(() => SendSessionState(SessionState.Arming, PauseReason.None, secondsRemaining));
        }

        private void OnInputStatusChanged(object? sender, InputSessionStatus status)
        {
            SendOnUiThread(() => PublishInputSessionStatus(status));
        }

        private void PublishInputSessionStatus(InputSessionStatus status)
        {
            var inputEvent = new
            {
                schemaVersion = ContractConstants.SchemaVersion,
                type = "input.status.updated",
                payload = new
                {
                    provider = "inputBroker",
                    status = status.Status,
                    integrity = status.Integrity,
                    activeKeys = status.ActiveKeys,
                    lastReleaseSucceeded = status.LastReleaseSucceeded,
                    hotkeys = new
                    {
                        pauseResume = globalHotKeys?.PauseResumeLabel ?? "F9",
                        emergencyStop = globalHotKeys?.EmergencyStopLabel ?? "F12",
                    },
                    errorCode = status.ErrorCode,
                },
            };
            webViewRuntime.Send(JsonSerializer.Serialize(inputEvent));
            SendSessionState(status.SessionState, status.PauseReason, null);
        }

        private void SendSessionState(SessionState state, PauseReason pauseReason, int? resumeCountdown)
        {
            var sessionEvent = new
            {
                schemaVersion = ContractConstants.SchemaVersion,
                type = "session.stateChanged",
                payload = new
                {
                    state = state.ToString(),
                    pauseReason = pauseReason.ToString(),
                    resumeCountdown,
                },
            };
            webViewRuntime.Send(JsonSerializer.Serialize(sessionEvent));
        }

        private void SendVisionStatus(VisionStatusPayload status)
        {
            string json = JsonSerializer.Serialize(new
            {
                schemaVersion = ContractConstants.SchemaVersion,
                type = "vision.status.updated",
                payload = new
                {
                    status = status.Status switch { VisionModelStatus.NotConfigured => "notConfigured", VisionModelStatus.Inspecting => "inspecting", VisionModelStatus.Ready => "ready", VisionModelStatus.Repairing => "repairing", _ => "faulted" },
                    modelId = status.ModelId,
                    provider = RuntimeTelemetryCollector.ProviderLabel(status.Provider),
                    diagnostic = status.Diagnostic,
                },
            });
            webViewRuntime.Send(json);
        }

        public void SendCombatConfiguration(CombatConfiguration configuration)
        {
            pendingCombatConfiguration = CombatConfigurationValidator.ValidateAndNormalize(configuration);
            string json = JsonSerializer.Serialize(new
            {
                schemaVersion = ContractConstants.SchemaVersion,
                type = "config.updated",
                payload = new
                {
                    schemaVersion = configuration.SchemaVersion,
                    attackMode = configuration.AttackMode.ToString().ToLowerInvariant(),
                    hpThresholdMode = configuration.HpThresholdMode.ToString().ToLowerInvariant(),
                    hpThreshold = configuration.HpThreshold,
                    mpThresholdMode = configuration.MpThresholdMode.ToString().ToLowerInvariant(),
                    mpThreshold = configuration.MpThreshold,
                    singleAttackKey = configuration.SingleAttackKey,
                    areaAttackKey = configuration.AreaAttackKey,
                    hpPotionKey = configuration.HpPotionKey,
                    mpPotionKey = configuration.MpPotionKey,
                    jumpKey = configuration.JumpKey,
                    pickupEnabled = configuration.PickupEnabled,
                    pickupKey = configuration.PickupKey,
                    preferredDistancePx = configuration.PreferredDistancePx,
                    areaTargetCount = configuration.AreaTargetCount,
                    switchCooldownMs = configuration.SwitchCooldownMs,
                },
            });
            SendOnUiThread(() => webViewRuntime.Send(json));
        }

        private void SendOnUiThread(Action send)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) BeginInvoke(send);
            else send();
        }

        private void OnRuntimeCrashed(object? sender, EventArgs e)
        {
            previewBoundsIntent = null;
            preview.Visible = false;
            if (foregroundSession is not null) foregroundSession.Pause(PauseReason.SafetyViolation);
            else safety.PauseAndRelease();
        }

        private void OnContentReset(object? sender, EventArgs e)
        {
            previewBoundsIntent = null;
            preview.Visible = false;
            if (foregroundSession is not null) foregroundSession.Pause(PauseReason.SafetyViolation);
            else safety.PauseAndRelease();
        }

        private void EmergencyStop(string reason)
        {
            if (automaticCombat is not null) _ = automaticCombat.EmergencyStopAsync();
            if (combatTrial is not null) _ = combatTrial.EmergencyStopAsync();
            if (automaticCombat is null && combatTrial is null && foregroundSession is not null) foregroundSession.EmergencyStop();
            else safety.EmergencyStop();
            SendSessionState(SessionState.EmergencyStop, PauseReason.OperatorRequested, null);
        }

        protected override void WndProc(ref Message message)
        {
            if (globalHotKeys?.Dispatch(message.Msg, message.WParam) == true) return;
            base.WndProc(ref message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                captureTimer.Stop();
                captureTimer.Tick -= OnCaptureTick;
                captureTimer.Dispose();
                foregroundTimer.Stop();
                foregroundTimer.Tick -= OnForegroundTick;
                foregroundTimer.Dispose();
                browserPanel.ClientSizeChanged -= OnBrowserClientSizeChanged;
                globalHotKeys?.Dispose();
                if (foregroundSession is not null)
                {
                    foregroundSession.CountdownChanged -= OnInputCountdownChanged;
                    foregroundSession.StatusChanged -= OnInputStatusChanged;
                }
                if (automaticCombat is not null)
                {
                    automaticCombat.StatusChanged -= OnAutomaticCombatStatusChanged;
                    automaticCombat.Dispose();
                }
                if (combatTrial is not null)
                {
                    combatTrial.StatusChanged -= OnAutomaticCombatStatusChanged;
                    combatTrial.Dispose();
                }
                foregroundSession?.Dispose();
                visionCancellation?.Cancel();
                if (visionTask is not null)
                {
                    try { visionTask.GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { }
                }
                visionCancellation?.Dispose();
                captureCoordinator?.Dispose();
                webViewRuntime.MessageReceived -= OnMessageReceived;
                webViewRuntime.RuntimeCrashed -= OnRuntimeCrashed;
                webViewRuntime.ContentReset -= OnContentReset;
                safety.ReleaseForShutdown();
                inputLifetime?.Dispose();
                webViewRuntime.Dispose();
            }
            base.Dispose(disposing);
        }

        private static string ValidateAssetFolder(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("React 静态资源目录为空", "assetFolder");
            string fullPath = Path.GetFullPath(value);
            if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException(fullPath);
            return fullPath;
        }

        private static string Escape(string value) { return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\""); }
    }
}
