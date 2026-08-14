using Maple.Input;
using Maple.Cloud;
using Maple.Capture;

namespace Maple.Host;

public static class HostCompositionRoot
{
    public static MainWindow CreateMainWindow(string assetFolder)
    {
        // Real HID is intentionally not selected until the Windows three-layer evidence is complete.
        var store = new WindowsBailianCredentialStore();
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var mapFrames = new MapScanFrameStore(new WindowsPngMapFrameEncoder());
        var mapAnnotation = new BailianMapAnnotationService(new BailianMapHttpClient(httpClient, store), mapFrames);
        var dispatcher = new HostCommandDispatcher(store, new BailianHttpClient(httpClient, store), mapAnnotation, mapFrames);
        var window = new MainWindow(new WebView2Runtime(), new NullInputAdapter(), assetFolder);
        window.ConfigureCapture(
            new WindowsTargetWindowLocator(new Win32WindowSystem()),
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
