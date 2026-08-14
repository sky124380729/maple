using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace MapleVisualPrototype
{
    internal sealed class TargetWindowInfo
    {
        internal IntPtr Handle { get; set; }
        internal string Title { get; set; }
        internal uint ProcessId { get; set; }
        internal Rectangle ClientScreenBounds { get; set; }
        internal int Dpi { get; set; }
        internal bool IsMinimized { get; set; }
        internal bool IsForeground { get; set; }
    }

    internal static class WindowCapture
    {
        private const string TitleFragment = "冒险岛怀旧服";

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            internal int X;
            internal int Y;
        }

        private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr handle, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr handle, ref POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PrintWindow(IntPtr handle, IntPtr deviceContext, uint flags);

        private const uint PrintWindowClientOnly = 0x00000001;
        private const uint PrintWindowRenderFullContent = 0x00000002;

        internal static string TargetTitleFragment { get { return TitleFragment; } }

        internal static bool TryFindTarget(out TargetWindowInfo info)
        {
            TargetWindowInfo found = null;
            EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                if (!IsWindowVisible(handle)) return true;
                string title = GetTitle(handle);
                if (title.IndexOf(TitleFragment, StringComparison.OrdinalIgnoreCase) < 0) return true;
                TargetWindowInfo candidate;
                if (!TryReadInfo(handle, title, out candidate)) return true;
                found = candidate;
                return false;
            }, IntPtr.Zero);
            info = found;
            return info != null;
        }

        internal static bool TryReadInfo(IntPtr handle, string title, out TargetWindowInfo info)
        {
            info = null;
            RECT client;
            if (!GetClientRect(handle, out client)) return false;
            POINT origin = new POINT { X = client.Left, Y = client.Top };
            if (!ClientToScreen(handle, ref origin)) return false;
            int width = Math.Max(0, client.Right - client.Left);
            int height = Math.Max(0, client.Bottom - client.Top);
            uint pid;
            GetWindowThreadProcessId(handle, out pid);
            int dpi = 96;
            try
            {
                uint actual = GetDpiForWindow(handle);
                if (actual > 0) dpi = (int)actual;
            }
            catch (EntryPointNotFoundException) { }
            info = new TargetWindowInfo
            {
                Handle = handle,
                Title = title,
                ProcessId = pid,
                ClientScreenBounds = new Rectangle(origin.X, origin.Y, width, height),
                Dpi = dpi,
                IsMinimized = IsIconic(handle),
                IsForeground = GetForegroundWindow() == handle
            };
            return true;
        }

        internal static Bitmap Capture(TargetWindowInfo target, out string reason)
        {
            reason = null;
            if (target == null) { reason = "未找到目标窗口"; return null; }
            TargetWindowInfo current;
            if (!TryReadInfo(target.Handle, target.Title, out current)) { reason = "目标窗口已关闭"; return null; }
            if (current.IsMinimized) { reason = "目标窗口已最小化"; return null; }
            if (current.ClientScreenBounds.Width < 100 || current.ClientScreenBounds.Height < 100) { reason = "客户区尺寸无效"; return null; }
            if (!current.IsForeground) { reason = "目标窗口未在前台，预览暂停"; return null; }
            try
            {
                var bitmap = new Bitmap(current.ClientScreenBounds.Width, current.ClientScreenBounds.Height, PixelFormat.Format32bppArgb);
                bool rendered;
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr deviceContext = graphics.GetHdc();
                    try
                    {
                        rendered = PrintWindow(current.Handle, deviceContext, PrintWindowClientOnly | PrintWindowRenderFullContent);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(deviceContext);
                    }
                }

                if (rendered && !LooksBlank(bitmap))
                {
                    reason = "OK（PrintWindow 客户区）";
                    return bitmap;
                }

                bitmap.Dispose();
                var foregroundBitmap = new Bitmap(current.ClientScreenBounds.Width, current.ClientScreenBounds.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(foregroundBitmap))
                {
                    graphics.CopyFromScreen(current.ClientScreenBounds.Location, Point.Empty, current.ClientScreenBounds.Size);
                }
                reason = "OK（前台客户区）";
                return foregroundBitmap;
            }
            catch (Exception exception)
            {
                reason = "截图失败：" + exception.GetType().Name;
                return null;
            }
        }

        private static bool LooksBlank(Bitmap bitmap)
        {
            int samples = 0;
            int visible = 0;
            int stepX = Math.Max(1, bitmap.Width / 12);
            int stepY = Math.Max(1, bitmap.Height / 12);
            for (int y = stepY / 2; y < bitmap.Height; y += stepY)
            {
                for (int x = stepX / 2; x < bitmap.Width; x += stepX)
                {
                    Color color = bitmap.GetPixel(x, y);
                    samples++;
                    if (color.R + color.G + color.B > 36) visible++;
                }
            }
            return samples == 0 || visible < Math.Max(2, samples / 20);
        }

        private static string GetTitle(IntPtr handle)
        {
            var builder = new StringBuilder(512);
            GetWindowText(handle, builder, builder.Capacity);
            return builder.ToString();
        }
    }
}
