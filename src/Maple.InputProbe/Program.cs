using System.Drawing;
using System.Windows.Forms;

namespace Maple.InputProbe;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var message = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "Diagnostic-only scaffold. This application sends no input.",
            TextAlign = ContentAlignment.MiddleCenter
        };

        var window = new Form
        {
            ClientSize = new Size(440, 140),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            Text = "Maple Input Probe"
        };
        window.Controls.Add(message);

        Application.Run(window);
    }
}
