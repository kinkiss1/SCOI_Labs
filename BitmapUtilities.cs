using System.Drawing;
using System.Drawing.Imaging;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = System.Drawing.Bitmap;

namespace SCOI_Lab_1;

internal static class BitmapUtilities
{
    public static DrawingBitmap CreateWorkingCopy(DrawingBitmap source)
    {
        DrawingBitmap copy = new(source.Width, source.Height, PixelFormat.Format32bppArgb);

        using Graphics graphics = Graphics.FromImage(copy);
        graphics.DrawImage(source, 0, 0, source.Width, source.Height);

        return copy;
    }

    public static AvaloniaBitmap ToAvaloniaBitmap(DrawingBitmap source)
    {
        using MemoryStream stream = new();
        source.Save(stream, ImageFormat.Bmp);
        stream.Position = 0;

        return new AvaloniaBitmap(stream);
    }
}
