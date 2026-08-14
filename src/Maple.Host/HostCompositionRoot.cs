using Maple.Input;
using Maple.Cloud;

namespace Maple.Host;

public static class HostCompositionRoot
{
    public static MainWindow CreateMainWindow(string assetFolder)
    {
        // Real HID is intentionally not selected until the Windows three-layer evidence is complete.
        var store = new WindowsBailianCredentialStore();
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var dispatcher = new HostCommandDispatcher(store, new BailianHttpClient(httpClient, store));
        var window = new MainWindow(new WebView2Runtime(), new NullInputAdapter(), assetFolder);
        window.CommandReceived += (_, route) => dispatcher.Handle(route);
        dispatcher.StatusChanged += (_, status) => window.SendCloudStatus(status);
        window.FormClosed += (_, _) => { dispatcher.Dispose(); httpClient.Dispose(); };
        return window;
    }
}
