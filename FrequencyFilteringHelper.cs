using System.Numerics;
using System.Threading.Tasks;
using DrawingBitmap = System.Drawing.Bitmap;

namespace SCOI_Lab_1;

public enum FrequencyFilterMode
{
    LowPass,
    HighPass,
    BandReject,
    BandPass,
    NarrowBandPass,
    NarrowBandReject
}

public sealed class FrequencyFilterParameters
{
    public FrequencyFilterMode Mode { get; init; } = FrequencyFilterMode.LowPass;
    public double Radius { get; init; } = 24;
    public double InnerRadius { get; init; } = 18;
    public double OuterRadius { get; init; } = 48;
    public double NotchOffsetX { get; init; } = 64;
    public double NotchOffsetY { get; init; } = 0;
    public double NotchRadiusX { get; init; } = 10;
    public double NotchRadiusY { get; init; } = 10;
}

public sealed class FrequencyFilterPreview : IDisposable
{
    public required DrawingBitmap SpectrumImage { get; init; }
    public required DrawingBitmap FilterMaskImage { get; init; }
    public required DrawingBitmap FilteredImage { get; init; }
    public required int WorkingWidth { get; init; }
    public required int WorkingHeight { get; init; }
    public required bool UsedPadding { get; init; }

    public void Dispose()
    {
        SpectrumImage.Dispose();
        FilterMaskImage.Dispose();
        FilteredImage.Dispose();
    }
}

public static class FrequencyFilteringHelper
{
    public const int MaxRecommendedImageSize = 512;

    public static FrequencyFilterPreview BuildPreview(DrawingBitmap source, FrequencyFilterParameters parameters)
    {
        byte[] sourceBytes = BitmapUtilities.ReadPixelBytes(source, out int width, out int height, out int stride);
        ValidateSourceSize(width, height);

        int workingWidth = NextPowerOfTwo(width);
        int workingHeight = NextPowerOfTwo(height);
        bool usedPadding = workingWidth != width || workingHeight != height;

        double[,] filterMask = BuildFilterMask(parameters, workingWidth, workingHeight);
        double[,] spectrumValues = new double[workingHeight, workingWidth];
        byte[] resultBytes = new byte[sourceBytes.Length];

        for (int i = 0; i < sourceBytes.Length; i += 4)
        {
            resultBytes[i + 3] = sourceBytes[i + 3];
        }

        for (int channelOffset = 0; channelOffset < 3; channelOffset++)
        {
            Complex[,] spectrum = CreateSpectrumForChannel(sourceBytes, width, height, stride, workingWidth, workingHeight, channelOffset);
            Transform2D(spectrum, inverse: false);
            AccumulateSpectrumValues(spectrum, spectrumValues);
            ApplyMask(spectrum, filterMask);
            Transform2D(spectrum, inverse: true);
            WriteChannelBack(resultBytes, spectrum, width, height, stride, channelOffset);
        }

        DrawingBitmap spectrumBitmap = RenderSpectrumBitmap(spectrumValues);
        DrawingBitmap maskBitmap = RenderMaskBitmap(filterMask);
        DrawingBitmap filteredBitmap = BitmapUtilities.CreateBitmapFromBytes(resultBytes, width, height, stride);

        return new FrequencyFilterPreview
        {
            SpectrumImage = spectrumBitmap,
            FilterMaskImage = maskBitmap,
            FilteredImage = filteredBitmap,
            WorkingWidth = workingWidth,
            WorkingHeight = workingHeight,
            UsedPadding = usedPadding
        };
    }

    private static void ValidateSourceSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Image is empty.");
        }

        if (width > MaxRecommendedImageSize || height > MaxRecommendedImageSize)
        {
            throw new InvalidOperationException(
                $"Frequency filtering is limited to {MaxRecommendedImageSize}x{MaxRecommendedImageSize}. Resize the image before processing.");
        }
    }

    private static Complex[,] CreateSpectrumForChannel(
        byte[] sourceBytes,
        int width,
        int height,
        int stride,
        int workingWidth,
        int workingHeight,
        int channelOffset)
    {
        Complex[,] spectrum = new Complex[workingHeight, workingWidth];

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * stride;
            for (int x = 0; x < width; x++)
            {
                int sourceIndex = rowStart + (x * 4) + channelOffset;
                double sign = ((x + y) & 1) == 0 ? 1.0 : -1.0;
                spectrum[y, x] = new Complex(sourceBytes[sourceIndex] * sign, 0);
            }
        }

        return spectrum;
    }

    private static void AccumulateSpectrumValues(Complex[,] spectrum, double[,] accumulator)
    {
        int height = spectrum.GetLength(0);
        int width = spectrum.GetLength(1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                accumulator[y, x] += Math.Log(spectrum[y, x].Magnitude + 1.0);
            }
        }
    }

    private static void ApplyMask(Complex[,] spectrum, double[,] filterMask)
    {
        int height = spectrum.GetLength(0);
        int width = spectrum.GetLength(1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                spectrum[y, x] *= filterMask[y, x];
            }
        }
    }

    private static void WriteChannelBack(byte[] resultBytes, Complex[,] spatialDomain, int width, int height, int stride, int channelOffset)
    {
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * stride;
            for (int x = 0; x < width; x++)
            {
                double sign = ((x + y) & 1) == 0 ? 1.0 : -1.0;
                double value = spatialDomain[y, x].Real * sign;
                int index = rowStart + (x * 4) + channelOffset;
                resultBytes[index] = ClampToByte(value);
            }
        }
    }

    private static DrawingBitmap RenderSpectrumBitmap(double[,] spectrumValues)
    {
        int height = spectrumValues.GetLength(0);
        int width = spectrumValues.GetLength(1);
        double max = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                max = Math.Max(max, spectrumValues[y, x]);
            }
        }

        double scale = max > 0 ? 255.0 / max : 0.0;
        byte[] bytes = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                byte intensity = ClampToByte(spectrumValues[y, x] * scale);
                int index = rowStart + (x * 4);
                bytes[index] = intensity;
                bytes[index + 1] = intensity;
                bytes[index + 2] = intensity;
                bytes[index + 3] = 255;
            }
        }

        return BitmapUtilities.CreateBitmapFromBytes(bytes, width, height, width * 4);
    }

    private static DrawingBitmap RenderMaskBitmap(double[,] filterMask)
    {
        int height = filterMask.GetLength(0);
        int width = filterMask.GetLength(1);
        byte[] bytes = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                byte intensity = filterMask[y, x] >= 0.5 ? (byte)255 : (byte)0;
                int index = rowStart + (x * 4);
                bytes[index] = intensity;
                bytes[index + 1] = intensity;
                bytes[index + 2] = intensity;
                bytes[index + 3] = 255;
            }
        }

        return BitmapUtilities.CreateBitmapFromBytes(bytes, width, height, width * 4);
    }

    private static double[,] BuildFilterMask(FrequencyFilterParameters parameters, int width, int height)
    {
        double[,] mask = new double[height, width];
        double centerX = width / 2.0;
        double centerY = height / 2.0;
        double radius = Math.Max(0, parameters.Radius);
        double innerRadius = Math.Max(0, Math.Min(parameters.InnerRadius, parameters.OuterRadius));
        double outerRadius = Math.Max(innerRadius, parameters.OuterRadius);
        double notchRadiusX = Math.Max(1, parameters.NotchRadiusX);
        double notchRadiusY = Math.Max(1, parameters.NotchRadiusY);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double dx = x - centerX;
                double dy = y - centerY;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                bool inBand = distance >= innerRadius && distance <= outerRadius;
                bool inNotch = IsInsideEllipse(dx, dy, parameters.NotchOffsetX, parameters.NotchOffsetY, notchRadiusX, notchRadiusY)
                    || IsInsideEllipse(dx, dy, -parameters.NotchOffsetX, -parameters.NotchOffsetY, notchRadiusX, notchRadiusY);

                bool pass = parameters.Mode switch
                {
                    FrequencyFilterMode.LowPass => distance <= radius,
                    FrequencyFilterMode.HighPass => distance >= radius,
                    FrequencyFilterMode.BandReject => !inBand,
                    FrequencyFilterMode.BandPass => inBand,
                    FrequencyFilterMode.NarrowBandPass => inNotch,
                    FrequencyFilterMode.NarrowBandReject => !inNotch,
                    _ => true
                };

                mask[y, x] = pass ? 1.0 : 0.0;
            }
        }

        return mask;
    }

    private static bool IsInsideEllipse(double x, double y, double centerX, double centerY, double radiusX, double radiusY)
    {
        double normalizedX = (x - centerX) / radiusX;
        double normalizedY = (y - centerY) / radiusY;
        return (normalizedX * normalizedX) + (normalizedY * normalizedY) <= 1.0;
    }

    private static void Transform2D(Complex[,] data, bool inverse)
    {
        int height = data.GetLength(0);
        int width = data.GetLength(1);

        Parallel.For(0, height, y =>
        {
            Complex[] row = new Complex[width];
            for (int x = 0; x < width; x++)
            {
                row[x] = data[y, x];
            }

            Transform1D(row, inverse);

            for (int x = 0; x < width; x++)
            {
                data[y, x] = row[x];
            }
        });

        Parallel.For(0, width, x =>
        {
            Complex[] column = new Complex[height];
            for (int y = 0; y < height; y++)
            {
                column[y] = data[y, x];
            }

            Transform1D(column, inverse);

            for (int y = 0; y < height; y++)
            {
                data[y, x] = column[y];
            }
        });
    }

    private static void Transform1D(Complex[] buffer, bool inverse)
    {
        if (buffer.Length <= 1)
        {
            return;
        }

        if (IsPowerOfTwo(buffer.Length))
        {
            ApplyFft(buffer, inverse);
            return;
        }

        ApplyDft(buffer, inverse);
    }

    private static void ApplyFft(Complex[] buffer, bool inverse)
    {
        int n = buffer.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = (inverse ? 2.0 : -2.0) * Math.PI / length;
            Complex step = new(Math.Cos(angle), Math.Sin(angle));

            for (int start = 0; start < n; start += length)
            {
                Complex omega = Complex.One;
                int half = length / 2;
                for (int i = 0; i < half; i++)
                {
                    Complex even = buffer[start + i];
                    Complex odd = buffer[start + i + half] * omega;
                    buffer[start + i] = even + odd;
                    buffer[start + i + half] = even - odd;
                    omega *= step;
                }
            }
        }

        if (!inverse)
        {
            return;
        }

        for (int i = 0; i < n; i++)
        {
            buffer[i] /= n;
        }
    }

    private static void ApplyDft(Complex[] buffer, bool inverse)
    {
        int n = buffer.Length;
        Complex[] result = new Complex[n];
        double direction = inverse ? 1.0 : -1.0;

        for (int k = 0; k < n; k++)
        {
            Complex sum = Complex.Zero;
            for (int t = 0; t < n; t++)
            {
                double angle = direction * 2.0 * Math.PI * k * t / n;
                Complex factor = new(Math.Cos(angle), Math.Sin(angle));
                sum += buffer[t] * factor;
            }

            result[k] = inverse ? sum / n : sum;
        }

        Array.Copy(result, buffer, n);
    }

    private static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }

    private static byte ClampToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }
}
