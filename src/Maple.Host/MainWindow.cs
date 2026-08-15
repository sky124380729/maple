using Maple.Input;

namespace Maple.Host;

public sealed class MainWindow : WebViewHostForm
{
    public MainWindow(IWebViewRuntime webViewRuntime, IInputAdapter inputAdapter, string assetFolder)
        : base(webViewRuntime, inputAdapter, assetFolder)
    {
    }

    public MainWindow(
        IWebViewRuntime webViewRuntime,
        HostSafetyCoordinator safety,
        string assetFolder)
        : base(webViewRuntime, safety, assetFolder)
    {
    }
}
