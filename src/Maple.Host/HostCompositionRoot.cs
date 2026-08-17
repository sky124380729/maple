using Maple.Input;
using Maple.Cloud;
using Maple.Capture;
using Maple.Contracts;
using Maple.Core;
using Maple.Runtime;
using Maple.Map;
using Maple.Vision;

namespace Maple.Host;

public sealed record HostApplication(
    MainWindow Window,
    SamePlatformCombatTrialController? CombatTrial,
    LiveObservationSource? Observations);

public static class HostCompositionRoot
{
    public static MainWindow CreateMainWindow(string assetFolder) => CreateApplication(assetFolder).Window;

    public static HostApplication CreateApplication(string assetFolder, bool registerGlobalHotKeys = true)
    {
        var targetLocator = new WindowsTargetWindowLocator(new Win32WindowSystem());
        var brokerFactory = new LaunchingBrokerClientFactory(
            new BrokerProcessLauncher(),
            Path.Combine(AppContext.BaseDirectory, "Maple.InputBroker.exe"));
        var inputAdapter = new BrokerInputAdapter(brokerFactory);
        var safety = new HostSafetyCoordinator(inputAdapter);
        var foregroundWindow = new WindowsForegroundWindowController();
        var foregroundSession = new ForegroundSessionController(
            targetLocator,
            foregroundWindow,
            inputAdapter,
            safety,
            new SystemForegroundSessionDelay());
        AutomaticCombatController? automaticCombat = null;
        SamePlatformCombatTrialController? combatTrial = null;
        StationaryAttackController? stationaryAttack = null;
        GlobalHotKeyManager? globalHotKeys = registerGlobalHotKeys ? new GlobalHotKeyManager(
            new WindowsGlobalHotKeyRegistrar(),
            () =>
            {
                if (stationaryAttack?.IsRunning == true) _ = stationaryAttack.StopAsync(PauseReason.OperatorRequested);
                else if (combatTrial?.IsRunning == true) _ = combatTrial.PauseAsync(PauseReason.OperatorRequested);
                else if (automaticCombat is not null) _ = automaticCombat.ToggleAsync(CancellationToken.None);
                else foregroundSession.Pause(PauseReason.CalibrationRequired);
            },
            () =>
            {
                if (automaticCombat is not null) _ = automaticCombat.EmergencyStopAsync();
                else foregroundSession.EmergencyStop();
            }) : null;
        var store = new WindowsBailianCredentialStore();
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var mapFrames = new MapScanFrameStore(new WindowsPngMapFrameEncoder());
        var mapAnnotation = new BailianMapAnnotationService(new BailianMapHttpClient(httpClient, store), mapFrames);
        var cameraTracker = new CameraTransformTracker();
        var mapScan = new MapScanRuntimeController(mapFrames, cameraTracker);
        var archiveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Maple", "maps");
        var activeMaps = new ActiveMapRuntime(archives: new MapArchiveRepository(archiveDirectory));
        var combatConfiguration = new CombatConfigurationStore();
        CombatConfiguration activeCombatConfiguration = combatConfiguration.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        var actionExecutor = new BrokerActionExecutor(inputAdapter, () => combatConfiguration.Current);
        var inputAcceptance = new InputAcceptanceController(
            foregroundSession,
            actionExecutor,
            new SystemInputAcceptanceDelay());
        var dispatcher = new HostCommandDispatcher(
            store,
            new BailianHttpClient(httpClient, store),
            mapAnnotation,
            mapScan,
            combatConfiguration,
            () => foregroundSession.Pause(PauseReason.OperatorRequested),
            activeMaps,
            inputAcceptance);
        var window = new MainWindow(new WebView2Runtime(), safety, assetFolder);
        dispatcher.MapAnnotationCompleted += (_, completed) => window.SendMapStatus(activeMaps.PrepareCandidate(
            completed.MapId,
            completed.Result.Annotation,
            frameId => cameraTracker.TryGet(frameId, out FrameCameraTransform? transform) ? transform : null));
        window.ConfigureInputSession(foregroundSession, foregroundWindow, globalHotKeys, inputAdapter);
        window.ConfigureCombatConfiguration(activeCombatConfiguration);
        OcrRuntimeSelection ocr = OcrRuntime.TryCreate();
        VisionRuntimeBootstrapResult vision = VisionRuntimeBootstrap.Load(ocrEngine: ocr.Engine, resourceOcrEngine: ocr.ResourceEngine);
        LiveObservationSource? liveObservations = null;
        if (vision.Ready && vision.Pipeline is not null)
        {
            var visionFrames = new LatestVisionFrameQueue(1);
            var frameObservers = new CompositeCaptureFrameObserver(mapFrames, visionFrames);
            window.ConfigureCapture(
                targetLocator,
                new WindowsGraphicsCaptureBackend(TryCreateWgcSource(), new WindowsBitBltFrameSource()),
                frameObservers);
            var telemetry = new RuntimeTelemetryCollector(vision.Provider);
            var platformResolver = new ValidatedMapPlatformResolver();
            var observations = new LiveObservationSource((snapshot, _) => CreateLiveContext(
                snapshot, targetLocator, inputAdapter, safety, platformResolver, cameraTracker, activeMaps));
            liveObservations = observations;
            ObservationEventPublisher publisher = window.CreateVisionPublisher(telemetry, vision.ModelId, observations);
            var service = new VisionRuntimeService(visionFrames, new VisionPipelineProcessor(vision.Pipeline), () => window.CurrentCaptureTarget, safety, publisher, cameraTracker: cameraTracker);
            window.ConfigureVision(visionFrames, service, new VisionStatusPayload { Status = VisionModelStatus.Ready, ModelId = vision.ModelId, Provider = vision.Provider, Diagnostic = "WAITING_FIRST_FRAME" });
            automaticCombat = new AutomaticCombatController(
                foregroundSession,
                observations,
                actionExecutor,
                actionAccepted => CreateOrchestrator(observations, actionExecutor, combatConfiguration.Current, actionAccepted),
                () => vision.Ready);
            window.ConfigureAutomaticCombat(automaticCombat);
            combatTrial = new SamePlatformCombatTrialController(
                foregroundSession,
                observations,
                actionExecutor,
                () => combatConfiguration.Current);
            window.ConfigureCombatTrial(combatTrial);
            stationaryAttack = new StationaryAttackController(foregroundSession, actionExecutor);
            window.ConfigureStationaryAttack(stationaryAttack);
        }
        else
        {
            window.ConfigureCapture(
                targetLocator,
                new WindowsGraphicsCaptureBackend(TryCreateWgcSource(), new WindowsBitBltFrameSource()),
                mapFrames);
            window.ConfigureVisionStatus(new VisionStatusPayload { Status = VisionModelStatus.NotConfigured, ModelId = string.IsNullOrWhiteSpace(vision.ModelId) ? null! : vision.ModelId, Provider = InferenceProvider.None, Diagnostic = vision.Diagnostic });
        }
        window.CommandReceived += (_, route) => dispatcher.Handle(route);
        dispatcher.StatusChanged += (_, status) => window.SendCloudStatus(status);
        dispatcher.CombatConfigurationChanged += (_, configuration) => window.SendCombatConfiguration(configuration);
        dispatcher.MapStatusChanged += (_, status) => window.SendMapStatus(status);
        dispatcher.InputResultPublished += (_, result) => window.SendInputResult(result);
        mapFrames.StatusChanged += (_, status) => window.SendMapScanStatus(status);
        window.SendMapScanStatus(mapFrames.Status);
        window.FormClosed += (_, _) => { dispatcher.Dispose(); httpClient.Dispose(); };
        return new HostApplication(window, combatTrial, liveObservations);
    }

    private static RuntimeObservationContext CreateLiveContext(
        ObservationSnapshot snapshot,
        ITargetWindowLocator targetLocator,
        IInputAdapter inputAdapter,
        HostSafetyCoordinator safety,
        ValidatedMapPlatformResolver platformResolver,
        CameraTransformTracker cameraTracker,
        ActiveMapRuntime activeMaps)
    {
        long now = Environment.TickCount64;
        WindowIdentity? located = targetLocator.Locate().Target;
        bool targetBound = located is not null
            && string.Equals(located.Hwnd, snapshot.Target.Hwnd, StringComparison.OrdinalIgnoreCase)
            && located.Pid == snapshot.Target.Pid;
        InputAdapterStatus input = inputAdapter.GetStatus();
        CameraTransform? transform = cameraTracker.TryGet(snapshot.FrameId, out FrameCameraTransform? tracked) && tracked is { Ready: true }
            ? new CameraTransform { FrameId = tracked.FrameId, OffsetX = tracked.OffsetX, OffsetY = tracked.OffsetY, Confidence = tracked.Confidence }
            : null;
        activeMaps.TryGetValidated(snapshot.Map?.MapId ?? string.Empty, out MapWorld? world);
        if (world is not null && snapshot.Map is not null) snapshot.Map.State = MapArchiveState.Validated;
        PlatformResolution platform = platformResolver.Resolve(snapshot, world, transform);
        return new RuntimeObservationContext(
            snapshot,
            platform.Context,
            targetBound,
            targetBound && located!.IsForeground && !located.IsMinimized,
            snapshot.Self is not null && snapshot.Self.FreshUntilMonoMs >= now,
            Fresh(snapshot.Hp, now),
            Fresh(snapshot.Mp, now),
            input.IsHealthy && input.InjectionEnabled,
            safety.State == SessionState.EmergencyStop);
    }

    private static ProductionOrchestrator CreateOrchestrator(
        IObservationSource observations,
        IActionExecutor executor,
        CombatConfiguration configuration,
        Action<AbstractAction> actionAccepted)
    {
        double hpThreshold = configuration.HpThresholdMode == ResourceMode.Percent ? configuration.HpThreshold / 100 : configuration.HpThreshold;
        double mpThreshold = configuration.MpThresholdMode == ResourceMode.Percent ? configuration.MpThreshold / 100 : configuration.MpThreshold;
        var settings = new ActionPolicySettings
        {
            ClientWidthPx = 1280,
            AttackRangePx = configuration.PreferredDistancePx,
            SelfConfidenceThreshold = 0.9,
            TargetConfidenceThreshold = 0.8,
            ObservedSpeedPxPerSecond = 320,
            MinMoveHoldMs = 60,
            MaxMoveHoldMs = 400,
            AttackHoldMs = 80,
            AttackMode = configuration.AttackMode switch
            {
                CombatAttackMode.Single => AttackSelectionMode.Single,
                CombatAttackMode.Group => AttackSelectionMode.Group,
                _ => AttackSelectionMode.Auto,
            },
            AreaTargetCount = configuration.AreaTargetCount,
            AttackProfileSwitchCooldownMs = configuration.SwitchCooldownMs,
            HpPotionThreshold = hpThreshold,
            MpPotionThreshold = mpThreshold,
            HpPotionThresholdMode = configuration.HpThresholdMode,
            MpPotionThresholdMode = configuration.MpThresholdMode,
            PickupEnabled = configuration.PickupEnabled,
            MaxAttackNoFeedbackAttempts = 2,
        };
        return new ProductionOrchestrator(
            observations,
            executor,
            new SafetyGate(settings.SelfConfidenceThreshold),
            new ActionPolicy(new MovementDurationEstimator()),
            settings,
            new OrchestratorOptions { MaximumFeedbackFramesPerAction = 16 },
            actionAccepted: actionAccepted);
    }

    private static bool Fresh(ResourceObservation? value, long nowMonoMs) =>
        value is not null && value.Confidence > 0 && value.FreshUntilMonoMs >= nowMonoMs;

    private static IWindowFrameSource? TryCreateWgcSource()
    {
        try { return new WindowsWgcFrameSource(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
    }
}
