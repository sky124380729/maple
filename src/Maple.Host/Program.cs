using System.Windows.Forms;

namespace Maple.Host;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && string.Equals(args[0], "--target-vision-test", StringComparison.Ordinal))
        {
            return LiveVisionDiagnostics
                .RunAsync(args[1], CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        if (args.Length == 3 && string.Equals(args[0], "--vision-image-diagnostics", StringComparison.Ordinal))
        {
            OcrRuntimeSelection ocr = OcrRuntime.TryCreate();
            return OfflineVisionDiagnostics
                .RunAsync(args[1], args[2], ocr.Engine, ocr.ResourceEngine, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        if (args.Length == 2 && string.Equals(args[0], "--vision-bootstrap-diagnostics", StringComparison.Ordinal))
        {
            OcrRuntimeSelection ocr = OcrRuntime.TryCreate();
            VisionRuntimeBootstrapResult result = VisionRuntimeBootstrap.Load(ocrEngine: ocr.Engine, resourceOcrEngine: ocr.ResourceEngine);
            WindowsRuntimeDiagnostics.WriteJson(args[1], new
            {
                schemaVersion = 1,
                result.Ready,
                result.Diagnostic,
                result.ModelId,
                provider = RuntimeTelemetryCollector.ProviderLabel(result.Provider),
                ocrReady = ocr.Engine is not null,
                ocrProvider = ocr.Provider,
                canDriveActions = false,
            });
            return result.Ready ? 0 : 2;
        }

        if (args.Length == 4 && string.Equals(args[0], "--inspect-model", StringComparison.Ordinal) && string.Equals(args[2], "--output", StringComparison.Ordinal))
        {
            try
            {
                Maple.Vision.OnnxModelInspectionReport report = Maple.Vision.OnnxModelInspector.Inspect(args[1]);
                Maple.Vision.OnnxModelInspector.Write(args[3], report);
                return report.ModelReady ? 0 : 2;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                WindowsRuntimeDiagnostics.WriteJson(args[3], new { schemaVersion = 1, modelReady = false, canDriveActions = false, diagnostic = exception.GetType().Name, message = exception.Message });
                return 2;
            }
        }

        if (args.Length == 2 && string.Equals(args[0], "--windows-diagnostics", StringComparison.Ordinal))
        {
            WindowsRuntimeDiagnosticReport report = WindowsRuntimeDiagnostics.Create(
                new WindowsTargetWindowLocator(new Win32WindowSystem()),
                WebView2Runtime.ProbeInstalledEnvironment());
            WindowsRuntimeDiagnostics.Write(args[1], report);
            return 0;
        }

        if (args.Length == 2 && string.Equals(args[0], "--wgc-self-test", StringComparison.Ordinal))
        {
            ApplicationConfiguration.Initialize();
            WindowsWgcSelfTestReport report = WindowsWgcSelfTest
                .RunAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            WindowsRuntimeDiagnostics.WriteJson(args[1], report);
            return report.Success ? 0 : 2;
        }

        if (args.Length is 2 or 3 && string.Equals(args[0], "--target-capture-test", StringComparison.Ordinal))
        {
            int frameCount = args.Length == 3 && int.TryParse(args[2], out int parsed) ? parsed : 60;
            if (frameCount is < 10 or > 600) return 1;
            TargetCaptureEvidenceReport report = CreateTargetCaptureRunner()
                .RunAsync(frameCount, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            WindowsRuntimeDiagnostics.WriteJson(args[1], report);
            return report.Success ? 0 : 2;
        }

        if (args.Length is 1 or 2 && string.Equals(args[0], "--input-broker-evidence", StringComparison.Ordinal))
        {
            ApplicationConfiguration.Initialize();
            string evidenceRoot = args.Length == 2
                ? args[1]
                : InputBrokerEvidenceForm.CreateDefaultSessionRoot();
            using var form = new InputBrokerEvidenceForm(evidenceRoot, AppContext.BaseDirectory);
            Application.Run(form);
            return form.ExitCode;
        }

        if (args.Length == 2 && string.Equals(args[0], "--same-platform-combat-trial", StringComparison.Ordinal))
        {
            ApplicationConfiguration.Initialize();
            string evidenceAssetFolder = Path.Combine(AppContext.BaseDirectory, "ui");
            HostApplication application = HostCompositionRoot.CreateApplication(evidenceAssetFolder, registerGlobalHotKeys: false);
            if (application.CombatTrial is null || application.Observations is null)
            {
                WindowsRuntimeDiagnostics.WriteJson(args[1], new
                {
                    schemaVersion = 1,
                    success = false,
                    code = "COMBAT_TRIAL_UNAVAILABLE",
                    allKeysReleased = true,
                });
                application.Window.Dispose();
                return 2;
            }
            using var evidence = new CombatTrialEvidenceRunner(
                application.Window,
                application.CombatTrial,
                application.Observations,
                args[1]);
            Application.Run(application.Window);
            return evidence.ExitCode;
        }

        ApplicationConfiguration.Initialize();
        string assetFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        Application.Run(HostCompositionRoot.CreateMainWindow(assetFolder));
        return 0;
    }

    private static TargetCaptureEvidenceRunner CreateTargetCaptureRunner()
    {
        Maple.Capture.IWindowFrameSource? wgc = null;
        try { wgc = new WindowsWgcFrameSource(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        return new TargetCaptureEvidenceRunner(
            new WindowsTargetWindowLocator(new Win32WindowSystem()),
            new Maple.Capture.WindowsGraphicsCaptureBackend(wgc, new Maple.Capture.WindowsBitBltFrameSource()));
    }
}
