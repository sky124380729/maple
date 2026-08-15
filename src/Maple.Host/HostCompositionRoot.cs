using Maple.Input;
using Maple.Cloud;
using Maple.Capture;

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
        window.ConfigureCapture(
            targetLocator,
            new WindowsGraphicsCaptureBackend(TryCreateWgcSource(), new WindowsBitBltFrameSource()),
            mapFrames);
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
