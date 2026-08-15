using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Maple.Contracts;

namespace Maple.Preview
{
    public sealed class NativePreviewSurface : Control
    {
        private readonly FrameSlot<Bitmap> frames = new FrameSlot<Bitmap>(frame => frame.Dispose());
        private readonly object overlaySync = new object();
        private readonly Queue<long> paintTimestamps = new Queue<long>();
        private OverlaySnapshot overlay;
        private PreviewTelemetrySnapshot telemetry;
        private long nowMonoMs;

        public NativePreviewSurface()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(9, 16, 17);
            ResizeRedraw = true;
        }

        public long DroppedFrames { get { return frames.DroppedFrames; } }

        public void PublishFrame(Bitmap frame, long capturedAtMonoMs)
        {
            frames.Publish(frame, capturedAtMonoMs);
            nowMonoMs = Math.Max(nowMonoMs, capturedAtMonoMs);
            Invalidate();
        }

        public void PublishOverlay(OverlaySnapshot snapshot, long currentMonoMs)
        {
            lock (overlaySync) overlay = snapshot;
            nowMonoMs = currentMonoMs;
            Invalidate();
        }

        public void PublishTelemetry(PreviewTelemetrySnapshot snapshot)
        {
            lock (overlaySync) telemetry = snapshot;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            FrameRead<Bitmap> read;
            if (!frames.TryRead(nowMonoMs, out read) || read.Frame == null)
            {
                DrawEmpty(e.Graphics);
                return;
            }
            Rectangle destination = Fit(read.Frame.Size, ClientRectangle);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            e.Graphics.DrawImage(read.Frame, destination);
            OverlaySnapshot snapshot;
            PreviewTelemetrySnapshot telemetrySnapshot;
            lock (overlaySync)
            {
                snapshot = overlay;
                telemetrySnapshot = telemetry;
            }
            double renderFps = TrackRenderFps(Environment.TickCount64);
            if (telemetrySnapshot != null) telemetrySnapshot = telemetrySnapshot with { RenderFps = renderFps };
            PreviewRenderModel model = PreviewRenderModel.Build(snapshot, telemetrySnapshot, nowMonoMs);
            DrawMarkers(e.Graphics, destination, model.Markers);
            DrawHud(e.Graphics, destination, model);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) frames.Dispose();
            base.Dispose(disposing);
        }

        private double TrackRenderFps(long timestamp)
        {
            paintTimestamps.Enqueue(timestamp);
            while (paintTimestamps.Count > 0 && timestamp - paintTimestamps.Peek() >= 1000) paintTimestamps.Dequeue();
            return paintTimestamps.Count;
        }

        private static void DrawMarkers(Graphics graphics, Rectangle destination, IReadOnlyList<PreviewRenderMarker> markers)
        {
            foreach (PreviewRenderMarker marker in markers) DrawMarker(graphics, destination, marker);
        }

        private static void DrawMarker(Graphics graphics, Rectangle destination, PreviewRenderMarker marker)
        {
            string colorHex = marker.Kind == "self" ? OverlayColors.Self : marker.Kind == "player" ? OverlayColors.Player : OverlayColors.Monster;
            Color color = ColorTranslator.FromHtml(colorHex);
            double[] box = marker.Box;
            var rectangle = new RectangleF((float)(destination.X + box[0] * destination.Width), (float)(destination.Y + box[1] * destination.Height), (float)(box[2] * destination.Width), (float)(box[3] * destination.Height));
            using (var pen = new Pen(color, marker.Selected ? 3F : 2F))
            {
                graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                if (marker.Selected) DrawSelectionCorners(graphics, pen, rectangle);
            }
            using (var font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
            using (var textBrush = new SolidBrush(color))
            using (var background = new SolidBrush(Color.FromArgb(210, 8, 16, 17)))
            {
                SizeF size = graphics.MeasureString(marker.Label, font);
                float y = Math.Max(destination.Y, rectangle.Y - size.Height - 2);
                float width = Math.Min(size.Width + 6, Math.Max(36, destination.Right - rectangle.X));
                graphics.FillRectangle(background, rectangle.X, y, width, size.Height + 2);
                using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                graphics.DrawString(marker.Label, font, textBrush, new RectangleF(rectangle.X + 3, y + 1, Math.Max(1, width - 6), size.Height + 1), format);
            }
        }

        private static void DrawSelectionCorners(Graphics graphics, Pen pen, RectangleF rectangle)
        {
            float length = Math.Min(11F, Math.Min(rectangle.Width, rectangle.Height) / 3F);
            graphics.DrawLines(pen, [new PointF(rectangle.Left, rectangle.Top + length), new PointF(rectangle.Left, rectangle.Top), new PointF(rectangle.Left + length, rectangle.Top)]);
            graphics.DrawLines(pen, [new PointF(rectangle.Right - length, rectangle.Top), new PointF(rectangle.Right, rectangle.Top), new PointF(rectangle.Right, rectangle.Top + length)]);
            graphics.DrawLines(pen, [new PointF(rectangle.Left, rectangle.Bottom - length), new PointF(rectangle.Left, rectangle.Bottom), new PointF(rectangle.Left + length, rectangle.Bottom)]);
            graphics.DrawLines(pen, [new PointF(rectangle.Right - length, rectangle.Bottom), new PointF(rectangle.Right, rectangle.Bottom), new PointF(rectangle.Right, rectangle.Bottom - length)]);
        }

        private static void DrawHud(Graphics graphics, Rectangle destination, PreviewRenderModel model)
        {
            if (model.HudBands.Count == 0 || destination.Width < 180 || destination.Height < 140) return;
            const int gap = 4;
            const int bandHeight = 21;
            int width = Math.Min(330, destination.Width - 16);
            int totalHeight = model.HudBands.Count * bandHeight + (model.HudBands.Count - 1) * gap;
            bool right = model.HudCorner is PreviewHudCorner.TopRight or PreviewHudCorner.BottomRight;
            bool bottom = model.HudCorner is PreviewHudCorner.BottomLeft or PreviewHudCorner.BottomRight;
            int x = right ? destination.Right - width - 8 : destination.Left + 8;
            int y = bottom ? destination.Bottom - totalHeight - 8 : destination.Top + 8;

            using var font = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Regular);
            using var labelFont = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Bold);
            for (int index = 0; index < model.HudBands.Count; index++)
            {
                PreviewHudBand band = model.HudBands[index];
                Rectangle row = new Rectangle(x, y + index * (bandHeight + gap), width, bandHeight);
                Color accent = SeverityColor(band.Severity);
                using var background = new SolidBrush(Color.FromArgb(218, 8, 16, 23));
                using var border = new Pen(Color.FromArgb(145, accent), 1F);
                using var labelBrush = new SolidBrush(accent);
                using var valueBrush = new SolidBrush(Color.FromArgb(224, 226, 235, 242));
                graphics.FillRectangle(background, row);
                graphics.DrawRectangle(border, row);
                graphics.DrawString(band.Label, labelFont, labelBrush, row.X + 6, row.Y + 4);
                using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                graphics.DrawString(band.Value, font, valueBrush, new RectangleF(row.X + 60, row.Y + 3, row.Width - 66, row.Height - 4), format);
            }
        }

        private static Color SeverityColor(PreviewHudSeverity severity) => severity switch
        {
            PreviewHudSeverity.Warning => Color.FromArgb(232, 189, 101),
            PreviewHudSeverity.Critical => Color.FromArgb(255, 100, 116),
            _ => Color.FromArgb(85, 199, 247),
        };

        private void DrawEmpty(Graphics graphics)
        {
            graphics.Clear(BackColor);
            using (var font = new Font("Microsoft YaHei UI", 10F))
            using (var brush = new SolidBrush(Color.FromArgb(145, 158, 166)))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("等待原生画面", font, brush, ClientRectangle, format);
            }
        }

        private static Rectangle Fit(Size source, Rectangle bounds)
        {
            if (source.Width <= 0 || source.Height <= 0) return bounds;
            double scale = Math.Min((double)bounds.Width / source.Width, (double)bounds.Height / source.Height);
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            return new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
        }
    }
}
