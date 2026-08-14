using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MapleVisualPrototype
{
    internal static class Theme
    {
        internal static readonly Color Window = Color.FromArgb(14, 23, 24);
        internal static readonly Color Surface = Color.FromArgb(21, 34, 34);
        internal static readonly Color Surface2 = Color.FromArgb(25, 43, 42);
        internal static readonly Color Border = Color.FromArgb(47, 76, 72);
        internal static readonly Color Text = Color.FromArgb(229, 241, 237);
        internal static readonly Color Muted = Color.FromArgb(144, 174, 166);
        internal static readonly Color Accent = Color.FromArgb(45, 171, 145);
        internal static readonly Color AccentDark = Color.FromArgb(27, 98, 83);
        internal static readonly Color Success = Color.FromArgb(75, 202, 157);
        internal static readonly Color Warning = Color.FromArgb(242, 181, 96);
        internal static readonly Color Danger = Color.FromArgb(219, 91, 79);
        internal static readonly Color Cyan = Color.FromArgb(80, 190, 207);
        internal static readonly Color Red = Color.FromArgb(236, 103, 94);
        internal static readonly Color Green = Color.FromArgb(89, 213, 151);

        internal static void StyleButton(Button button, bool danger = false)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = danger ? Danger : Border;
            button.FlatAppearance.MouseOverBackColor = danger ? Color.FromArgb(155, 57, 51) : Color.FromArgb(36, 123, 103);
            button.FlatAppearance.MouseDownBackColor = danger ? Color.FromArgb(180, 68, 58) : Color.FromArgb(42, 143, 119);
            button.BackColor = danger ? Color.FromArgb(111, 42, 40) : AccentDark;
            button.ForeColor = Text;
            button.Cursor = Cursors.Hand;
            button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
        }

        internal static void StyleLabel(Label label, bool muted = false, bool bold = false)
        {
            label.ForeColor = muted ? Muted : Text;
            label.Font = new Font("Microsoft YaHei UI", 9F, bold ? FontStyle.Bold : FontStyle.Regular);
        }

        internal static Panel Section(string title, int height, Control content)
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = height, BackColor = Surface };
            panel.Padding = new Padding(12, 9, 12, 9);
            var label = new Label { Text = title, Dock = DockStyle.Top, Height = 24 };
            StyleLabel(label, false, true);
            panel.Controls.Add(content);
            panel.Controls.Add(label);
            return panel;
        }

        internal static void DrawPanelBorder(Graphics graphics, Rectangle bounds)
        {
            using (var pen = new Pen(Border))
            using (var path = RoundedRect(bounds, 5))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawPath(pen, path);
            }
        }

        internal static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
