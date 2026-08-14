using System.Windows.Forms;

namespace Maple.Host;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        string assetFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        Application.Run(HostCompositionRoot.CreateMainWindow(assetFolder));
    }
}
