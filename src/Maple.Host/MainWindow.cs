using Maple.Input;

namespace Maple.Host;

public sealed class MainWindow(IWebViewRuntime webViewRuntime, IInputAdapter inputAdapter, string assetFolder)
    : WebViewHostForm(webViewRuntime, inputAdapter, assetFolder)
{
}
