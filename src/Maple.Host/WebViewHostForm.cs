using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Text.Json;
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
        private readonly System.Windows.Forms.Timer captureTimer = new() { Interval = 33 };
        private CaptureCoordinator? captureCoordinator;
        private bool captureInProgress;

        public WebViewHostForm(IWebViewRuntime webViewRuntime, IInputAdapter inputAdapter, string assetFolder)
        {
            this.webViewRuntime = webViewRuntime ?? throw new ArgumentNullException("webViewRuntime");
            safety = new HostSafetyCoordinator(inputAdapter ?? throw new ArgumentNullException("inputAdapter"));
            this.assetFolder = ValidateAssetFolder(assetFolder);
            Text = "Maple 工作台";
            ClientSize = new Size(1440, 900);
            BuildLayout();
            this.webViewRuntime.MessageReceived += OnMessageReceived;
            this.webViewRuntime.RuntimeCrashed += OnRuntimeCrashed;
            this.webViewRuntime.ContentReset += OnContentReset;
            Shown += OnShown;
            captureTimer.Tick += OnCaptureTick;
        }

        public event EventHandler<BridgeRouteResult>? CommandReceived;
        public NativePreviewSurface PreviewSurface { get { return preview; } }

        public void ConfigureCapture(ITargetWindowLocator locator, ICaptureBackend backend)
        {
            if (captureCoordinator is not null) throw new InvalidOperationException("Capture is already configured");
            captureCoordinator = new CaptureCoordinator(
                locator,
                backend,
                new NativePreviewFrameSink(preview),
                safety);
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
            emergencyButton.Height = 42;
            emergencyButton.BackColor = Color.FromArgb(197, 58, 74);
            emergencyButton.ForeColor = Color.White;
            emergencyButton.Click += delegate { EmergencyStop("原生紧急停止按钮"); };
            preview.Dock = DockStyle.Fill;
            preview.Width = 760;
            browserPanel.Dock = DockStyle.Fill;
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 760, FixedPanel = FixedPanel.None };
            split.Panel1.Controls.Add(preview);
            split.Panel2.Controls.Add(browserPanel);
            Controls.Add(split);
            Controls.Add(emergencyButton);
        }

        private void OnShown(object? sender, EventArgs e)
        {
            webViewRuntime.Attach(browserPanel, assetFolder);
            if (captureCoordinator is not null) captureTimer.Start();
        }

        private async void OnCaptureTick(object? sender, EventArgs e)
        {
            if (captureCoordinator is null || captureInProgress) return;
            captureInProgress = true;
            try { await captureCoordinator.CaptureOnceAsync(CancellationToken.None); }
            catch (OperationCanceledException) { }
            finally { captureInProgress = false; }
        }

        private void OnMessageReceived(object? sender, WebViewRuntimeMessageEventArgs e)
        {
            BridgeRouteResult result = router.Route(e.Json);
            if (!result.Accepted)
            {
                safety.PauseAndRelease();
                return;
            }
            if (result.CommandType == UiCommandType.SessionEmergencyStop) EmergencyStop("React 请求紧急停止");
            CommandReceived?.Invoke(this, result);
        }

        private void OnRuntimeCrashed(object? sender, EventArgs e)
        {
            safety.PauseAndRelease();
        }

        private void OnContentReset(object? sender, EventArgs e) => safety.PauseAndRelease();

        private void EmergencyStop(string reason)
        {
            safety.EmergencyStop();
            webViewRuntime.Send("{\"schemaVersion\":" + ContractConstants.SchemaVersion + ",\"type\":\"session.stateChanged\",\"payload\":{\"state\":\"EmergencyStop\",\"pauseReason\":\"OperatorRequested\"}}");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                captureTimer.Stop();
                captureTimer.Tick -= OnCaptureTick;
                captureTimer.Dispose();
                captureCoordinator?.Dispose();
                webViewRuntime.MessageReceived -= OnMessageReceived;
                webViewRuntime.RuntimeCrashed -= OnRuntimeCrashed;
                webViewRuntime.ContentReset -= OnContentReset;
                safety.ReleaseForShutdown();
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
