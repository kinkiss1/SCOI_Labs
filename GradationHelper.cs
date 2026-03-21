using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SCOI_Lab_1;

public static class GradationHelper
{
    public static int[] CalculateHistogram(Bitmap bmp)
    {
        int[] hist = new int[256];
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int bytes = Math.Abs(data.Stride) * bmp.Height;
        byte[] rgbValues = new byte[bytes];
        Marshal.Copy(data.Scan0, rgbValues, 0, bytes);
        bmp.UnlockBits(data);

        for (int i = 0; i < rgbValues.Length; i += 4)
        {
            byte b = rgbValues[i];
            byte g = rgbValues[i + 1];
            byte r = rgbValues[i + 2];
            
            int c = (r + g + b) / 3;
            hist[c]++;
        }
        return hist;
    }

    public static Bitmap ApplyLUT(Bitmap bmp, byte[] lut)
    {
        Bitmap res = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
        BitmapData srcData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData dstData = res.LockBits(new Rectangle(0, 0, res.Width, res.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        int bytes = Math.Abs(srcData.Stride) * bmp.Height;
        byte[] srcValues = new byte[bytes];
        byte[] dstValues = new byte[bytes];
        Marshal.Copy(srcData.Scan0, srcValues, 0, bytes);

        for (int i = 0; i < bytes; i += 4)
        {
            dstValues[i] = lut[srcValues[i]];
            dstValues[i + 1] = lut[srcValues[i + 1]];
            dstValues[i + 2] = lut[srcValues[i + 2]];
            dstValues[i + 3] = srcValues[i + 3];
        }

        Marshal.Copy(dstValues, 0, dstData.Scan0, bytes);
        bmp.UnlockBits(srcData);
        res.UnlockBits(dstData);

        return res;
    }

    public static Bitmap DrawHistogram(int[] hist, int width, int height)
    {
        Bitmap bmp = new Bitmap(width, height);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.White);

        int max = 0;
        for (int i = 0; i < 256; i++) if (hist[i] > max) max = hist[i];

        if (max == 0) return bmp;

        double k = (double)(height - 1) / max;
        using Pen pen = new Pen(Color.FromArgb(100, 100, 100));

        for (int i = 0; i < 256; i++)
        {
            int x = (int)(i * (width - 1) / 255.0);
            int y = (int)(height - 1 - hist[i] * k);
            g.DrawLine(pen, x, height - 1, x, y);
        }

        return bmp;
    }
}