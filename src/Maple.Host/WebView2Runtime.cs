using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Maple.Host;

public sealed class WebView2Runtime : IWebViewRuntime
{
    private readonly WebView2 webView = new() { Dock = System.Windows.Forms.DockStyle.Fill };
    private bool disposed;
    private bool initialNavigationStarted;

    public WebView2EnvironmentStatus? EnvironmentStatus { get; private set; }

    public event EventHandler<WebViewRuntimeMessageEventArgs>? MessageReceived;
    public event EventHandler? RuntimeCrashed;
    public event EventHandler? ContentReset;

    public void Attach(System.Windows.Forms.Control parent, string localAssetFolder)
    {
        ArgumentNullException.ThrowIfNull(parent);
        string folder = ValidateAssetFolder(localAssetFolder);
        EnvironmentStatus = ProbeInstalledEnvironment();
        if (!EnvironmentStatus.IsReady)
        {
            RuntimeCrashed?.Invoke(this, EventArgs.Empty);
            return;
        }
        parent.Controls.Add(webView);
        _ = InitializeAsync(folder);
    }

    public static WebView2EnvironmentStatus ProbeInstalledEnvironment() =>
        new WebView2EnvironmentProbe(() => CoreWebView2Environment.GetAvailableBrowserVersionString()).Probe();

    public void Send(string json)
    {
        if (disposed || webView.CoreWebView2 is null) return;
        if (webView.InvokeRequired) { webView.BeginInvoke(() => Send(json)); return; }
        webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    public void ReloadLocalContent()
    {
        if (!disposed && webView.CoreWebView2 is not null) webView.CoreWebView2.Reload();
    }

    private async Task InitializeAsync(string folder)
    {
        try
        {
            await webView.EnsureCoreWebView2Async().ConfigureAwait(true);
            CoreWebView2 core = webView.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("maple.local", folder, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.NavigationStarting += OnNavigationStarting;
            core.WebMessageReceived += OnWebMessageReceived;
            core.ProcessFailed += OnProcessFailed;
            webView.Source = new Uri("https://maple.local/index.html", UriKind.Absolute);
        }
        catch
        {
            RuntimeCrashed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        bool isLocal = LocalNavigationPolicy.IsAllowed(e.Uri);
        if (!isLocal)
        {
            e.Cancel = true;
            ContentReset?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (initialNavigationStarted) ContentReset?.Invoke(this, EventArgs.Empty);
        else initialNavigationStarted = true;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        MessageReceived?.Invoke(this, new WebViewRuntimeMessageEventArgs { Json = e.WebMessageAsJson });
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e) => RuntimeCrashed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
            webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            webView.CoreWebView2.ProcessFailed -= OnProcessFailed;
        }
        webView.Dispose();
    }

    private static string ValidateAssetFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("React 静态资源目录为空", nameof(folder));
        string fullPath = Path.GetFullPath(folder);
        if (!Directory.Exists(fullPath) || !File.Exists(Path.Combine(fullPath, "index.html"))) throw new DirectoryNotFoundException(fullPath);
        return fullPath;
    }
}
