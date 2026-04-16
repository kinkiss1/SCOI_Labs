using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SCOI_Lab_1;

public enum SpatialFilterMode
{
    Linear,
    Median
}

public sealed class SpatialFilterParameters
{
    public SpatialFilterMode Mode { get; init; } = SpatialFilterMode.Linear;
    public double[,] Kernel { get; init; } = new double[,] { { 0, 0, 0 }, { 0, 1, 0 }, { 0, 0, 0 } };
    public bool NormalizeKernel { get; init; } = true;
    public int MedianWindowWidth { get; init; } = 3;
    public int MedianWindowHeight { get; init; } = 3;
}

public static class SpatialFilteringHelper
{
    public static DrawingBitmap Apply(DrawingBitmap source, SpatialFilterParameters parameters)
    {
        byte[] sourceBytes = ReadPixelBytes(source, out int width, out int height, out int sourceStride);
        byte[] resultBytes = parameters.Mode switch
        {
            SpatialFilterMode.Linear => ApplyLinear(sourceBytes, width, height, sourceStride, parameters.Kernel, parameters.NormalizeKernel),
            SpatialFilterMode.Median => ApplyMedian(sourceBytes, width, height, sourceStride, parameters.MedianWindowWidth, parameters.MedianWindowHeight),
            _ => ApplyLinear(sourceBytes, width, height, sourceStride, parameters.Kernel, parameters.NormalizeKernel)
        };

        return CreateBitmapFromBytes(resultBytes, width, height, sourceStride);
    }

    public static double[,] GenerateGaussianKernel(int width, int height, double sigma)
    {
        int normalizedWidth = NormalizeOddSize(width);
        int normalizedHeight = NormalizeOddSize(height);
        double normalizedSigma = Math.Max(sigma, 0.0001);
        double sigmaSquared = normalizedSigma * normalizedSigma;
        double denominator = 2.0 * sigmaSquared;
        double factor = 1.0 / (2.0 * Math.PI * sigmaSquared);

        int radiusX = normalizedWidth / 2;
        int radiusY = normalizedHeight / 2;
        double[,] kernel = new double[normalizedHeight, normalizedWidth];

        for (int y = 0; y < normalizedHeight; y++)
        {
            int offsetY = y - radiusY;
            int squareY = offsetY * offsetY;

            for (int x = 0; x < normalizedWidth; x++)
            {
                int offsetX = x - radiusX;
                int distanceSquared = (offsetX * offsetX) + squareY;
                kernel[y, x] = factor * Math.Exp(-distanceSquared / denominator);
            }
        }

        return kernel;
    }

    private static byte[] ReadPixelBytes(DrawingBitmap source, out int width, out int height, out int stride)
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

    private static DrawingBitmap CreateBitmapFromBytes(byte[] bytes, int width, int height, int sourceStride)
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

    private static byte[] ApplyLinear(byte[] sourceBytes, int width, int height, int stride, double[,] kernel, bool normalizeKernel)
    {
        int kernelHeight = kernel.GetLength(0);
        int kernelWidth = kernel.GetLength(1);
        int radiusY = kernelHeight / 2;
        int radiusX = kernelWidth / 2;
        byte[] result = new byte[sourceBytes.Length];

        double kernelSum = 0;
        if (normalizeKernel)
        {
            for (int ky = 0; ky < kernelHeight; ky++)
            {
                for (int kx = 0; kx < kernelWidth; kx++)
                {
                    kernelSum += kernel[ky, kx];
                }
            }
        }

        double divisor = normalizeKernel && Math.Abs(kernelSum) > 0.0000001 ? kernelSum : 1.0;

        for (int y = 0; y < height; y++)
        {
            int destinationRowStart = y * stride;

            for (int x = 0; x < width; x++)
            {
                double blue = 0;
                double green = 0;
                double red = 0;

                for (int ky = 0; ky < kernelHeight; ky++)
                {
                    int sampleY = MirrorIndex(y + ky - radiusY, height);
                    int sampleRowStart = sampleY * stride;

                    for (int kx = 0; kx < kernelWidth; kx++)
                    {
                        int sampleX = MirrorIndex(x + kx - radiusX, width);
                        int sourceIndex = sampleRowStart + (sampleX * 4);
                        double weight = kernel[ky, kx];

                        blue += sourceBytes[sourceIndex] * weight;
                        green += sourceBytes[sourceIndex + 1] * weight;
                        red += sourceBytes[sourceIndex + 2] * weight;
                    }
                }

                int destinationIndex = destinationRowStart + (x * 4);
                int sourceCenterIndex = destinationIndex;
                result[destinationIndex] = ClampToByte(blue / divisor);
                result[destinationIndex + 1] = ClampToByte(green / divisor);
                result[destinationIndex + 2] = ClampToByte(red / divisor);
                result[destinationIndex + 3] = sourceBytes[sourceCenterIndex + 3];
            }
        }

        return result;
    }

    private static byte[] ApplyMedian(byte[] sourceBytes, int width, int height, int stride, int windowWidth, int windowHeight)
    {
        int normalizedWidth = NormalizeOddSize(windowWidth);
        int normalizedHeight = NormalizeOddSize(windowHeight);
        int radiusX = normalizedWidth / 2;
        int radiusY = normalizedHeight / 2;
        int windowPixelCount = normalizedWidth * normalizedHeight;
        int medianIndex = windowPixelCount / 2;
        byte[] blueWindow = new byte[windowPixelCount];
        byte[] greenWindow = new byte[windowPixelCount];
        byte[] redWindow = new byte[windowPixelCount];
        byte[] result = new byte[sourceBytes.Length];

        for (int y = 0; y < height; y++)
        {
            int destinationRowStart = y * stride;

            for (int x = 0; x < width; x++)
            {
                int windowIndex = 0;

                for (int offsetY = -radiusY; offsetY <= radiusY; offsetY++)
                {
                    int sampleY = MirrorIndex(y + offsetY, height);
                    int sampleRowStart = sampleY * stride;

                    for (int offsetX = -radiusX; offsetX <= radiusX; offsetX++)
                    {
                        int sampleX = MirrorIndex(x + offsetX, width);
                        int sourceIndex = sampleRowStart + (sampleX * 4);

                        blueWindow[windowIndex] = sourceBytes[sourceIndex];
                        greenWindow[windowIndex] = sourceBytes[sourceIndex + 1];
                        redWindow[windowIndex] = sourceBytes[sourceIndex + 2];
                        windowIndex++;
                    }
                }

                int destinationIndex = destinationRowStart + (x * 4);
                int sourceCenterIndex = destinationIndex;
                result[destinationIndex] = QuickSelect(blueWindow, medianIndex, windowPixelCount);
                result[destinationIndex + 1] = QuickSelect(greenWindow, medianIndex, windowPixelCount);
                result[destinationIndex + 2] = QuickSelect(redWindow, medianIndex, windowPixelCount);
                result[destinationIndex + 3] = sourceBytes[sourceCenterIndex + 3];
            }
        }

        return result;
    }

    private static byte QuickSelect(byte[] values, int targetIndex, int length)
    {
        int left = 0;
        int right = length - 1;

        while (true)
        {
            if (left == right)
            {
                return values[left];
            }

            int pivotIndex = left + ((right - left) / 2);
            pivotIndex = Partition(values, left, right, pivotIndex);

            if (targetIndex == pivotIndex)
            {
                return values[targetIndex];
            }

            if (targetIndex < pivotIndex)
            {
                right = pivotIndex - 1;
            }
            else
            {
                left = pivotIndex + 1;
            }
        }
    }

    private static int Partition(byte[] values, int left, int right, int pivotIndex)
    {
        byte pivotValue = values[pivotIndex];
        Swap(values, pivotIndex, right);
        int storeIndex = left;

        for (int i = left; i < right; i++)
        {
            if (values[i] < pivotValue)
            {
                Swap(values, storeIndex, i);
                storeIndex++;
            }
        }

        Swap(values, right, storeIndex);
        return storeIndex;
    }

    private static void Swap(byte[] values, int left, int right)
    {
        if (left == right)
        {
            return;
        }

        byte temp = values[left];
        values[left] = values[right];
        values[right] = temp;
    }

    private static int MirrorIndex(int index, int size)
    {
        if (size <= 1)
        {
            return 0;
        }

        while (index < 0 || index >= size)
        {
            if (index < 0)
            {
                index = -index - 1;
            }
            else
            {
                index = (2 * size) - index - 1;
            }
        }

        return index;
    }

    private static int NormalizeOddSize(int value)
    {
        int normalized = Math.Max(1, value);
        return normalized % 2 == 0 ? normalized + 1 : normalized;
    }

    private static byte ClampToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }
}
