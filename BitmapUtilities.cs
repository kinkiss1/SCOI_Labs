using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;

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

    public static byte[] ReadPixelBytes(DrawingBitmap source, out int width, out int height, out int stride)
    {
        width = source.Width;
        height = source.Height;
        DrawingRectangle bounds = new(0, 0, width, height);
        BitmapData data = source.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            stride = Math.Abs(data.Stride);
            byte[] bytes = new byte[stride * height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            source.UnlockBits(data);
        }
    }

    public static DrawingBitmap CreateBitmapFromBytes(byte[] bytes, int width, int height, int sourceStride)
    {
        DrawingBitmap result = new(width, height, PixelFormat.Format32bppArgb);
        DrawingRectangle bounds = new(0, 0, width, height);
        BitmapData data = result.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            int targetStride = Math.Abs(data.Stride);
            if (targetStride == sourceStride)
            {
                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
                return result;
            }

            byte[] adjusted = new byte[targetStride * height];
            int rowBytes = Math.Min(sourceStride, targetStride);
            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(bytes, y * sourceStride, adjusted, y * targetStride, rowBytes);
            }

            Marshal.Copy(adjusted, 0, data.Scan0, adjusted.Length);
            return result;
        }
        finally
        {
            result.UnlockBits(data);
        }
    }
}
