using Maple.Input;
using Maple.Cloud;
using Maple.Capture;
using Maple.Contracts;

namespace Maple.Host;

public static class HostCompositionRoot
{
    public static MainWindow CreateMainWindow(string assetFolder)
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
        var globalHotKeys = new GlobalHotKeyManager(
            new WindowsGlobalHotKeyRegistrar(),
            () => _ = foregroundSession.ToggleAsync(CancellationToken.None),
            foregroundSession.EmergencyStop);
        var store = new WindowsBailianCredentialStore();
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var mapFrames = new MapScanFrameStore(new WindowsPngMapFrameEncoder());
        var mapAnnotation = new BailianMapAnnotationService(new BailianMapHttpClient(httpClient, store), mapFrames);
        var dispatcher = new HostCommandDispatcher(store, new BailianHttpClient(httpClient, store), mapAnnotation, mapFrames);
        var window = new MainWindow(new WebView2Runtime(), safety, assetFolder);
        window.ConfigureInputSession(foregroundSession, foregroundWindow, globalHotKeys, inputAdapter);
        VisionRuntimeBootstrapResult vision = VisionRuntimeBootstrap.Load();
        if (vision.Ready && vision.Pipeline is not null)
        {
            var visionFrames = new LatestVisionFrameQueue(1);
            var frameObservers = new CompositeCaptureFrameObserver(mapFrames, visionFrames);
            window.ConfigureCapture(
                targetLocator,
                new WindowsGraphicsCaptureBackend(TryCreateWgcSource(), new WindowsBitBltFrameSource()),
                frameObservers);
            var telemetry = new RuntimeTelemetryCollector(vision.Provider);
            ObservationEventPublisher publisher = window.CreateVisionPublisher(telemetry, vision.ModelId);
            var service = new VisionRuntimeService(visionFrames, new VisionPipelineProcessor(vision.Pipeline), () => window.CurrentCaptureTarget, safety, publisher);
            window.ConfigureVision(visionFrames, service, new VisionStatusPayload { Status = VisionModelStatus.Ready, ModelId = vision.ModelId, Provider = vision.Provider, Diagnostic = "WAITING_FIRST_FRAME" });
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
        window.FormClosed += (_, _) => { dispatcher.Dispose(); httpClient.Dispose(); };
        return window;
    }

    private static IWindowFrameSource? TryCreateWgcSource()
    {
        try { return new WindowsWgcFrameSource(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
    }
}
