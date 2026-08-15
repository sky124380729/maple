using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Maple.InputProbe;

internal static class WindowScreenshot
{
    public static string Capture(TargetWindowInfo target, string directory, string fileName)
    {
        if (target.ClientWidth <= 0 || target.ClientHeight <= 0)
        {
            throw new InvalidOperationException("TARGET_CLIENT_EMPTY");
        }

        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        using var bitmap = new Bitmap(target.ClientWidth, target.ClientHeight, PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            target.ClientX,
            target.ClientY,
            0,
            0,
            new Size(target.ClientWidth, target.ClientHeight),
            CopyPixelOperation.SourceCopy);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}

