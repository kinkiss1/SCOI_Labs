using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
        int normalizedWidth = NormalizeOddSize(Math.Clamp(windowWidth, 1, 255));
        int normalizedHeight = NormalizeOddSize(Math.Clamp(windowHeight, 1, 255));
        if (normalizedWidth == 1 && normalizedHeight == 1)
        {
            return sourceBytes.ToArray();
        }

        int[] xOffsets = BuildOffsets(normalizedWidth);
        int[] yOffsets = BuildOffsets(normalizedHeight);
        int[][] xLut = BuildMirrorLut(width, xOffsets);
        int[][] yLut = BuildMirrorLut(height, yOffsets);
        int medianIndex = (normalizedWidth * normalizedHeight) >> 1;
        byte[] result = new byte[sourceBytes.Length];

        Parallel.For(
            0,
            height,
            () => (new ushort[256], new ushort[256], new ushort[256]),
            (y, _, histograms) =>
            {
                ushort[] blueHistogram = histograms.Item1;
                ushort[] greenHistogram = histograms.Item2;
                ushort[] redHistogram = histograms.Item3;
                int destinationRowStart = y * stride;

                for (int x = 0; x < width; x++)
                {
                    Array.Clear(blueHistogram, 0, blueHistogram.Length);
                    Array.Clear(greenHistogram, 0, greenHistogram.Length);
                    Array.Clear(redHistogram, 0, redHistogram.Length);

                    for (int ky = 0; ky < normalizedHeight; ky++)
                    {
                        int sampleY = yLut[ky][y];
                        int sampleRowStart = sampleY * stride;

                        for (int kx = 0; kx < normalizedWidth; kx++)
                        {
                            int sampleX = xLut[kx][x];
                            int sourceIndex = sampleRowStart + (sampleX * 4);
                            blueHistogram[sourceBytes[sourceIndex]]++;
                            greenHistogram[sourceBytes[sourceIndex + 1]]++;
                            redHistogram[sourceBytes[sourceIndex + 2]]++;
                        }
                    }

                    int destinationIndex = destinationRowStart + (x * 4);
                    result[destinationIndex] = FindMedianFromHistogram(blueHistogram, medianIndex);
                    result[destinationIndex + 1] = FindMedianFromHistogram(greenHistogram, medianIndex);
                    result[destinationIndex + 2] = FindMedianFromHistogram(redHistogram, medianIndex);
                    result[destinationIndex + 3] = sourceBytes[destinationIndex + 3];
                }

                return histograms;
            },
            _ => { });

        return result;
    }

    private static byte FindMedianFromHistogram(ushort[] histogram, int medianIndex)
    {
        int count = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            count += histogram[i];
            if (count > medianIndex)
            {
                return (byte)i;
            }
        }

        return 0;
    }

    private static int[] BuildOffsets(int size)
    {
        int radius = size / 2;
        int[] offsets = new int[size];
        for (int i = 0; i < size; i++)
        {
            offsets[i] = i - radius;
        }

        return offsets;
    }

    private static int[][] BuildMirrorLut(int dimension, int[] offsets)
    {
        int[][] lut = new int[offsets.Length][];
        for (int i = 0; i < offsets.Length; i++)
        {
            int offset = offsets[i];
            int[] sourcePositions = new int[dimension];
            for (int position = 0; position < dimension; position++)
            {
                sourcePositions[position] = MirrorIndex(position + offset, dimension);
            }

            lut[i] = sourcePositions;
        }

        return lut;
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
