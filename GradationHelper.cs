using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SCOI_Lab_1;

public static class GradationHelper
{
    public static int[] CalculateHistogram(DrawingBitmap bitmap)
    {
        int[] histogram = new int[256];
        BitmapData data = bitmap.LockBits(
            new DrawingRectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        int bytes = Math.Abs(data.Stride) * bitmap.Height;
        byte[] rgbValues = new byte[bytes];
        Marshal.Copy(data.Scan0, rgbValues, 0, bytes);
        bitmap.UnlockBits(data);

        for (int i = 0; i < rgbValues.Length; i += 4)
        {
            byte blue = rgbValues[i];
            byte green = rgbValues[i + 1];
            byte red = rgbValues[i + 2];

            histogram[(red + green + blue) / 3]++;
        }

        return histogram;
    }

    public static DrawingBitmap ApplyLut(DrawingBitmap bitmap, byte[] lut)
    {
        DrawingBitmap result = new(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        BitmapData sourceData = bitmap.LockBits(
            new DrawingRectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        BitmapData destinationData = result.LockBits(
            new DrawingRectangle(0, 0, result.Width, result.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        int bytes = Math.Abs(sourceData.Stride) * bitmap.Height;
        byte[] sourceValues = new byte[bytes];
        byte[] destinationValues = new byte[bytes];
        Marshal.Copy(sourceData.Scan0, sourceValues, 0, bytes);

        for (int i = 0; i < bytes; i += 4)
        {
            destinationValues[i] = lut[sourceValues[i]];
            destinationValues[i + 1] = lut[sourceValues[i + 1]];
            destinationValues[i + 2] = lut[sourceValues[i + 2]];
            destinationValues[i + 3] = sourceValues[i + 3];
        }

        Marshal.Copy(destinationValues, 0, destinationData.Scan0, bytes);
        bitmap.UnlockBits(sourceData);
        result.UnlockBits(destinationData);

        return result;
    }

    public static DrawingBitmap DrawHistogram(int[] histogram, int width, int height)
    {
        DrawingBitmap bitmap = new(width, height);

        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.White);

        int maxValue = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            maxValue = Math.Max(maxValue, histogram[i]);
        }

        if (maxValue == 0)
        {
            return bitmap;
        }

        double scale = (double)(height - 1) / maxValue;
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(100, 100, 100));

        for (int i = 0; i < histogram.Length; i++)
        {
            int x = (int)(i * (width - 1) / 255.0);
            int y = (int)(height - 1 - (histogram[i] * scale));
            graphics.DrawLine(pen, x, height - 1, x, y);
        }

        return bitmap;
    }
}
