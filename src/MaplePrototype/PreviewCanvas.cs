using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MapleVisualPrototype
{
    internal enum CanvasMode
    {
        Realtime,
        MapCalibration
    }

    internal sealed class OverlayMarker
    {
        internal RectangleF Bounds { get; set; }
        internal string Label { get; set; }
        internal Color Color { get; set; }
    }

    internal sealed class PreviewCanvas : Panel
    {
        private Bitmap frame;
        private string frameReason = "等待客户区画面";
        private readonly List<OverlayMarker> markers = new List<OverlayMarker>();
        internal CanvasMode Mode { get; set; }

        internal PreviewCanvas()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(9, 16, 17);
            Mode = CanvasMode.Realtime;
            ResizeRedraw = true;
        }

        internal void SetFrame(Bitmap nextFrame, string reason)
        {
            if (frame != null) frame.Dispose();
            frame = nextFrame;
            frameReason = reason;
            markers.Clear();
            if (frame != null && reason == "OK")
            {
                markers.Add(new OverlayMarker { Bounds = new RectangleF(.47f, .56f, .07f, .18f), Label = "Self 0.94", Color = Theme.Green });
                markers.Add(new OverlayMarker { Bounds = new RectangleF(.33f, .51f, .06f, .13f), Label = "Monster 0.88", Color = Theme.Red });
                markers.Add(new OverlayMarker { Bounds = new RectangleF(.68f, .43f, .08f, .15f), Label = "Player 0.91", Color = Theme.Cyan });
            }
            Invalidate();
        }

        internal void SetMapMode(bool mapMode)
        {
            Mode = mapMode ? CanvasMode.MapCalibration : CanvasMode.Realtime;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && frame != null) frame.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Mode == CanvasMode.MapCalibration)
            {
                DrawMap(e.Graphics, ClientRectangle);
                return;
            }
            DrawRealtime(e.Graphics, ClientRectangle);
        }

        private void DrawRealtime(Graphics graphics, Rectangle bounds)
        {
            graphics.Clear(Color.FromArgb(9, 16, 17));
            if (frame == null)
            {
                using (var brush = new SolidBrush(Theme.Muted))
                using (var font = new Font("Microsoft YaHei UI", 11F))
                {
                    string text = "客户区预览\n" + frameReason + "\n\n原型安全模式：不会发送按键";
                    var layout = new RectangleF(0, bounds.Height / 2F - 48, bounds.Width, 120);
                    graphics.DrawString(text, font, brush, layout, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                }
                return;
            }

            Rectangle destination = FitRectangle(new Size(frame.Width, frame.Height), bounds);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(frame, destination);
            using (var border = new Pen(Theme.Border, 1F)) graphics.DrawRectangle(border, destination);
            foreach (OverlayMarker marker in markers)
            {
                var box = new RectangleF(destination.X + marker.Bounds.X * destination.Width, destination.Y + marker.Bounds.Y * destination.Height, marker.Bounds.Width * destination.Width, marker.Bounds.Height * destination.Height);
                using (var pen = new Pen(marker.Color, 2F)) graphics.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                using (var brush = new SolidBrush(Color.FromArgb(210, 8, 16, 17)))
                using (var textBrush = new SolidBrush(marker.Color))
                using (var font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
                {
                    SizeF size = graphics.MeasureString(marker.Label, font);
                    graphics.FillRectangle(brush, box.X, Math.Max(destination.Y, box.Y - size.Height), size.Width + 6, size.Height + 2);
                    graphics.DrawString(marker.Label, font, textBrush, box.X + 3, Math.Max(destination.Y, box.Y - size.Height + 1));
                }
            }
            using (var brush = new SolidBrush(Color.FromArgb(185, 0, 0, 0))) graphics.FillRectangle(brush, destination.X + 10, destination.Bottom - 32, 235, 22);
            using (var font = new Font("Microsoft YaHei UI", 8F)) using (var textBrush = new SolidBrush(Theme.Text)) graphics.DrawString("模拟识别叠加 · 不产生输入", font, textBrush, destination.X + 18, destination.Bottom - 27);
        }

        private static Rectangle FitRectangle(Size source, Rectangle bounds)
        {
            if (source.Width <= 0 || source.Height <= 0) return bounds;
            float scale = Math.Min((float)bounds.Width / source.Width, (float)bounds.Height / source.Height);
            int width = Math.Max(1, (int)(source.Width * scale));
            int height = Math.Max(1, (int)(source.Height * scale));
            return new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
        }

        private void DrawMap(Graphics graphics, Rectangle bounds)
        {
            graphics.Clear(Color.FromArgb(11, 21, 22));
            using (var gridPen = new Pen(Color.FromArgb(24, 62, 59)))
            {
                for (int x = 0; x < bounds.Width; x += 48) graphics.DrawLine(gridPen, x, 0, x, bounds.Height);
                for (int y = 0; y < bounds.Height; y += 48) graphics.DrawLine(gridPen, 0, y, bounds.Width, y);
            }
            DrawPlatform(graphics, new Point(70, bounds.Height - 110), 320, "P-01  y=0");
            DrawPlatform(graphics, new Point(410, bounds.Height - 235), 240, "P-02  y=-125");
            DrawPlatform(graphics, new Point(700, bounds.Height - 350), 330, "P-03  y=-240");
            using (var ladderPen = new Pen(Theme.Warning, 3F))
            {
                graphics.DrawLine(ladderPen, 445, bounds.Height - 110, 445, bounds.Height - 235);
                graphics.DrawLine(ladderPen, 820, bounds.Height - 235, 820, bounds.Height - 350);
                for (int y = bounds.Height - 100; y > bounds.Height - 350; y -= 16) graphics.DrawLine(ladderPen, 437, y, 453, y);
            }
            using (var font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold))
            using (var brush = new SolidBrush(Theme.Text))
            {
                graphics.DrawString("MapWorld · 视觉标定预览", font, brush, 22, 18);
                graphics.DrawString("覆盖率 68%   标定误差 2.4 px   状态：待继续扫描", font, new SolidBrush(Theme.Warning), 22, 44);
                graphics.DrawString("梯子 L-01", font, new SolidBrush(Theme.Warning), 455, bounds.Height - 178);
                graphics.DrawString("梯子 L-02", font, new SolidBrush(Theme.Warning), 830, bounds.Height - 300);
            }
            using (var selfBrush = new SolidBrush(Theme.Green)) graphics.FillEllipse(selfBrush, 215, bounds.Height - 145, 14, 14);
            using (var selfFont = new Font("Microsoft YaHei UI", 8F)) graphics.DrawString("Self (120,0)", selfFont, new SolidBrush(Theme.Green), 235, bounds.Height - 150);
        }

        private static void DrawPlatform(Graphics graphics, Point start, int width, string label)
        {
            using (var brush = new SolidBrush(Color.FromArgb(33, 106, 91))) graphics.FillRectangle(brush, start.X, start.Y, width, 12);
            using (var pen = new Pen(Theme.Accent, 2F)) graphics.DrawLine(pen, start.X, start.Y, start.X + width, start.Y);
            using (var font = new Font("Microsoft YaHei UI", 8F)) graphics.DrawString(label, font, new SolidBrush(Theme.Accent), start.X, start.Y - 20);
        }
    }
}
