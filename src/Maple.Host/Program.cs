using System.Windows.Forms;

namespace Maple.Host;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
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

        if (args.Length == 2 && string.Equals(args[0], "--hid-device-self-test", StringComparison.Ordinal))
        {
            HidDeviceSelfTestReport report = new HidDeviceSelfTestRunner(
                new Maple.Input.WindowsMapleHidDeviceLocator(),
                () => new Maple.Input.WindowsVirtualHidTransport())
                .Run();
            WindowsRuntimeDiagnostics.WriteJson(args[1], report);
            return report.Success ? 0 : 2;
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
