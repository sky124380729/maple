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

        ApplicationConfiguration.Initialize();
        string assetFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        Application.Run(HostCompositionRoot.CreateMainWindow(assetFolder));
        return 0;
    }
}
