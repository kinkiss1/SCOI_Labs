using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SCOI_Lab_1;

public enum BinarizationMethod
{
    Gavrilov,
    Otsu,
    Niblack,
    Sauvola,
    Wolf,
    BradleyRoth
}

public sealed class BinarizationParameters
{
    public BinarizationMethod Method { get; init; }
    public int WindowSize { get; init; } = 25;
    public double Sensitivity { get; init; }
    public double DynamicRange { get; init; } = 128.0;
}

public static class BinarizationHelper
{
    public static DrawingBitmap Apply(DrawingBitmap source, BinarizationParameters parameters)
    {
        byte[] grayscale = ReadGrayscale(source, out int width, out int height);
        byte[] binary = parameters.Method switch
        {
            BinarizationMethod.Gavrilov => ApplyGlobalThreshold(grayscale, ComputeAverageThreshold(grayscale)),
            BinarizationMethod.Otsu => ApplyGlobalThreshold(grayscale, ComputeOtsuThreshold(grayscale)),
            BinarizationMethod.Niblack => ApplyLocalThreshold(grayscale, width, height, parameters, LocalThresholdMode.Niblack),
            BinarizationMethod.Sauvola => ApplyLocalThreshold(grayscale, width, height, parameters, LocalThresholdMode.Sauvola),
            BinarizationMethod.Wolf => ApplyLocalThreshold(grayscale, width, height, parameters, LocalThresholdMode.Wolf),
            BinarizationMethod.BradleyRoth => ApplyBradleyRoth(grayscale, width, height, parameters),
            _ => ApplyGlobalThreshold(grayscale, ComputeAverageThreshold(grayscale))
        };

        return CreateBitmapFromGray(binary, width, height);
    }

    private static byte[] ReadGrayscale(DrawingBitmap source, out int width, out int height)
    {
        width = source.Width;
        height = source.Height;
        DrawingRectangle bounds = new(0, 0, width, height);
        BitmapData sourceData = source.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            int stride = Math.Abs(sourceData.Stride);
            byte[] pixelBytes = new byte[stride * height];
            byte[] grayscale = new byte[width * height];
            Marshal.Copy(sourceData.Scan0, pixelBytes, 0, pixelBytes.Length);

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * stride;
                int grayRowStart = y * width;

                for (int x = 0; x < width; x++)
                {
                    int index = rowStart + (x * 4);
                    byte blue = pixelBytes[index];
                    byte green = pixelBytes[index + 1];
                    byte red = pixelBytes[index + 2];
                    int intensity = (int)Math.Round((0.0721 * blue) + (0.7154 * green) + (0.2125 * red));
                    grayscale[grayRowStart + x] = (byte)Math.Clamp(intensity, 0, 255);
                }
            }

            return grayscale;
        }
        finally
        {
            source.UnlockBits(sourceData);
        }
    }

    private static DrawingBitmap CreateBitmapFromGray(byte[] grayscale, int width, int height)
    {
        DrawingBitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        DrawingRectangle bounds = new(0, 0, width, height);
        BitmapData data = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] outputBytes = new byte[stride * height];

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * stride;
                int grayRowStart = y * width;

                for (int x = 0; x < width; x++)
                {
                    byte value = grayscale[grayRowStart + x];
                    int index = rowStart + (x * 4);
                    outputBytes[index] = value;
                    outputBytes[index + 1] = value;
                    outputBytes[index + 2] = value;
                    outputBytes[index + 3] = 255;
                }
            }

            Marshal.Copy(outputBytes, 0, data.Scan0, outputBytes.Length);
            return bitmap;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static int ComputeAverageThreshold(byte[] grayscale)
    {
        long sum = 0;
        for (int i = 0; i < grayscale.Length; i++)
        {
            sum += grayscale[i];
        }

        return (int)Math.Round((double)sum / grayscale.Length);
    }

    private static int ComputeOtsuThreshold(byte[] grayscale)
    {
        int[] histogram = new int[256];
        for (int i = 0; i < grayscale.Length; i++)
        {
            histogram[grayscale[i]]++;
        }

        int total = grayscale.Length;
        double totalSum = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            totalSum += i * histogram[i];
        }

        int backgroundWeight = 0;
        double backgroundSum = 0;
        double maxVariance = double.MinValue;
        int bestThreshold = 0;

        for (int threshold = 0; threshold < histogram.Length; threshold++)
        {
            backgroundWeight += histogram[threshold];
            if (backgroundWeight == 0)
            {
                continue;
            }

            int foregroundWeight = total - backgroundWeight;
            if (foregroundWeight == 0)
            {
                break;
            }

            backgroundSum += threshold * histogram[threshold];
            double backgroundMean = backgroundSum / backgroundWeight;
            double foregroundMean = (totalSum - backgroundSum) / foregroundWeight;
            double variance = backgroundWeight * foregroundWeight * Math.Pow(backgroundMean - foregroundMean, 2);

            if (variance > maxVariance)
            {
                maxVariance = variance;
                bestThreshold = threshold;
            }
        }

        return bestThreshold;
    }

    private static byte[] ApplyGlobalThreshold(byte[] grayscale, int threshold)
    {
        byte[] result = new byte[grayscale.Length];

        for (int i = 0; i < grayscale.Length; i++)
        {
            result[i] = grayscale[i] > threshold ? (byte)255 : (byte)0;
        }

        return result;
    }

    private static byte[] ApplyLocalThreshold(
        byte[] grayscale,
        int width,
        int height,
        BinarizationParameters parameters,
        LocalThresholdMode mode)
    {
        int windowSize = NormalizeWindowSize(parameters.WindowSize);
        int radius = windowSize / 2;
        long[] integral = BuildIntegralImage(grayscale, width, height);
        long[] squaredIntegral = BuildSquaredIntegralImage(grayscale, width, height);
        byte[] result = new byte[grayscale.Length];
        byte minIntensity = 255;
        double maxDeviation = 0;

        if (mode == LocalThresholdMode.Wolf)
        {
            for (int i = 0; i < grayscale.Length; i++)
            {
                minIntensity = Math.Min(minIntensity, grayscale[i]);
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    GetLocalStatistics(integral, squaredIntegral, width, height, x, y, radius, out _, out double deviation);
                    maxDeviation = Math.Max(maxDeviation, deviation);
                }
            }
        }

        double dynamicRange = Math.Max(parameters.DynamicRange, 1.0);

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width;
            for (int x = 0; x < width; x++)
            {
                GetLocalStatistics(integral, squaredIntegral, width, height, x, y, radius, out double mean, out double deviation);
                double threshold = mode switch
                {
                    LocalThresholdMode.Niblack => mean + (parameters.Sensitivity * deviation),
                    LocalThresholdMode.Sauvola => mean * (1 + (parameters.Sensitivity * ((deviation / dynamicRange) - 1))),
                    LocalThresholdMode.Wolf => maxDeviation <= 0.0001
                        ? mean
                        : mean + (parameters.Sensitivity * ((deviation / maxDeviation) - 1) * (mean - minIntensity)),
                    _ => mean
                };

                result[rowStart + x] = grayscale[rowStart + x] > threshold ? (byte)255 : (byte)0;
            }
        }

        return result;
    }

    private static byte[] ApplyBradleyRoth(byte[] grayscale, int width, int height, BinarizationParameters parameters)
    {
        int windowSize = NormalizeWindowSize(parameters.WindowSize);
        int radius = windowSize / 2;
        double sensitivity = Math.Clamp(parameters.Sensitivity, 0, 1);
        long[] integral = BuildIntegralImage(grayscale, width, height);
        byte[] result = new byte[grayscale.Length];

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width;

            for (int x = 0; x < width; x++)
            {
                int left = Math.Max(0, x - radius);
                int top = Math.Max(0, y - radius);
                int right = Math.Min(width - 1, x + radius);
                int bottom = Math.Min(height - 1, y + radius);
                int pixelCount = (right - left + 1) * (bottom - top + 1);
                long sum = GetIntegralSum(integral, width, left, top, right, bottom);
                double mean = (double)sum / pixelCount;
                double threshold = mean * (1.0 - sensitivity);
                result[rowStart + x] = grayscale[rowStart + x] <= threshold ? (byte)0 : (byte)255;
            }
        }

        return result;
    }

    private static long[] BuildIntegralImage(byte[] grayscale, int width, int height)
    {
        long[] integral = new long[(width + 1) * (height + 1)];

        for (int y = 1; y <= height; y++)
        {
            long rowSum = 0;
            int grayRowStart = (y - 1) * width;
            int integralRowStart = y * (width + 1);
            int integralPreviousRowStart = (y - 1) * (width + 1);

            for (int x = 1; x <= width; x++)
            {
                rowSum += grayscale[grayRowStart + x - 1];
                integral[integralRowStart + x] = integral[integralPreviousRowStart + x] + rowSum;
            }
        }

        return integral;
    }

    private static long[] BuildSquaredIntegralImage(byte[] grayscale, int width, int height)
    {
        long[] integral = new long[(width + 1) * (height + 1)];

        for (int y = 1; y <= height; y++)
        {
            long rowSum = 0;
            int grayRowStart = (y - 1) * width;
            int integralRowStart = y * (width + 1);
            int integralPreviousRowStart = (y - 1) * (width + 1);

            for (int x = 1; x <= width; x++)
            {
                int value = grayscale[grayRowStart + x - 1];
                rowSum += value * value;
                integral[integralRowStart + x] = integral[integralPreviousRowStart + x] + rowSum;
            }
        }

        return integral;
    }

    private static void GetLocalStatistics(
        long[] integral,
        long[] squaredIntegral,
        int width,
        int height,
        int x,
        int y,
        int radius,
        out double mean,
        out double deviation)
    {
        int left = Math.Max(0, x - radius);
        int top = Math.Max(0, y - radius);
        int right = Math.Min(width - 1, x + radius);
        int bottom = Math.Min(height - 1, y + radius);
        int pixelCount = (right - left + 1) * (bottom - top + 1);
        long sum = GetIntegralSum(integral, width, left, top, right, bottom);
        long squaredSum = GetIntegralSum(squaredIntegral, width, left, top, right, bottom);
        mean = (double)sum / pixelCount;
        double squareMean = (double)squaredSum / pixelCount;
        double variance = Math.Max(0, squareMean - (mean * mean));
        deviation = Math.Sqrt(variance);
    }

    private static long GetIntegralSum(long[] integral, int width, int left, int top, int right, int bottom)
    {
        int stride = width + 1;
        int x1 = left;
        int y1 = top;
        int x2 = right + 1;
        int y2 = bottom + 1;

        return integral[(y2 * stride) + x2]
            - integral[(y1 * stride) + x2]
            - integral[(y2 * stride) + x1]
            + integral[(y1 * stride) + x1];
    }

    private static int NormalizeWindowSize(int windowSize)
    {
        int normalized = Math.Max(3, windowSize);
        if (normalized % 2 == 0)
        {
            normalized++;
        }

        return normalized;
    }

    private enum LocalThresholdMode
    {
        Niblack,
        Sauvola,
        Wolf
    }
}
