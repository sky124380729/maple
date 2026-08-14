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
        private readonly FrameSlot<Bitmap> frames = new FrameSlot<Bitmap>();
        private readonly object overlaySync = new object();
        private OverlaySnapshot overlay;
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
            lock (overlaySync) snapshot = overlay;
            if (snapshot != null)
            {
                DrawSelf(e.Graphics, destination, snapshot.Self);
                DrawPlayers(e.Graphics, destination, snapshot.Players);
                DrawMonsters(e.Graphics, destination, snapshot.Monsters);
            }
        }

        private void DrawSelf(Graphics graphics, Rectangle destination, SelfObservation self)
        {
            if (self == null || self.FreshUntilMonoMs < nowMonoMs) return;
            DrawMarker(graphics, destination, self.Box, OverlayColors.Self, "Self " + self.Confidence.ToString("0.00"));
        }

        private void DrawPlayers(Graphics graphics, Rectangle destination, List<PlayerObservation> players)
        {
            if (players == null) return;
            foreach (PlayerObservation player in players)
            {
                if (player != null && player.FreshUntilMonoMs >= nowMonoMs) DrawMarker(graphics, destination, player.Box, OverlayColors.Player, "Player " + player.Confidence.ToString("0.00") + " #" + player.TrackId);
            }
        }

        private void DrawMonsters(Graphics graphics, Rectangle destination, List<MonsterObservation> monsters)
        {
            if (monsters == null) return;
            foreach (MonsterObservation monster in monsters)
            {
                if (monster != null && monster.FreshUntilMonoMs >= nowMonoMs) DrawMarker(graphics, destination, monster.Box, OverlayColors.Monster, monster.Class + " " + monster.Confidence.ToString("0.00") + " #" + monster.TargetId);
            }
        }

        private static void DrawMarker(Graphics graphics, Rectangle destination, double[] box, string colorHex, string label)
        {
            if (box == null || box.Length != 4) return;
            Color color = ColorTranslator.FromHtml(colorHex);
            var rectangle = new RectangleF((float)(destination.X + box[0] * destination.Width), (float)(destination.Y + box[1] * destination.Height), (float)(box[2] * destination.Width), (float)(box[3] * destination.Height));
            using (var pen = new Pen(color, 2F)) graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            using (var font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
            using (var textBrush = new SolidBrush(color))
            using (var background = new SolidBrush(Color.FromArgb(210, 8, 16, 17)))
            {
                SizeF size = graphics.MeasureString(label, font);
                float y = Math.Max(destination.Y, rectangle.Y - size.Height - 2);
                graphics.FillRectangle(background, rectangle.X, y, size.Width + 6, size.Height + 2);
                graphics.DrawString(label, font, textBrush, rectangle.X + 3, y + 1);
            }
        }

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
