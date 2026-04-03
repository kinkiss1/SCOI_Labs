using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SCOI_Lab_1;

public enum BlendMode
{
    None,
    Sum,
    Difference,
    Multiply,
    Screen,
    Average,
    Min,
    Max
}

public enum MaskType
{
    None,
    Circle,
    Square,
    Rectangle
}

public sealed class ImageLayer : IDisposable
{
    public string Name { get; set; } = string.Empty;
    public Bitmap Image { get; set; } = null!;
    public BlendMode BlendMode { get; set; }
    public double Opacity { get; set; } = 1.0;
    public MaskType MaskType { get; set; }

    public void Dispose()
    {
        Image.Dispose();
    }
}

public sealed class LayerManager : IDisposable
{
    public List<ImageLayer> Layers { get; } = new();
    private int _batchDepth;
    private bool _hasPendingChange;

    public event Action? LayersChanged;

    public IDisposable BeginBatchUpdate()
    {
        _batchDepth++;
        return new BatchUpdateScope(this);
    }

    public void AddLayer(string filePath, BlendMode blendMode = BlendMode.None, double opacity = 1.0)
    {
        using Bitmap source = new(filePath);
        AddLayerInternal(BitmapUtilities.CreateWorkingCopy(source), Path.GetFileName(filePath), blendMode, opacity);
    }

    public void RemoveLayer(int index)
    {
        if (index < 0 || index >= Layers.Count)
        {
            return;
        }

        Layers[index].Dispose();
        Layers.RemoveAt(index);
        NotifyLayersChanged();
    }

    public void MoveLayerTowardTop(int index)
    {
        if (index < 0 || index >= Layers.Count - 1)
        {
            return;
        }

        (Layers[index], Layers[index + 1]) = (Layers[index + 1], Layers[index]);
        NotifyLayersChanged();
    }

    public void MoveLayerTowardBottom(int index)
    {
        if (index <= 0 || index >= Layers.Count)
        {
            return;
        }

        (Layers[index], Layers[index - 1]) = (Layers[index - 1], Layers[index]);
        NotifyLayersChanged();
    }

    public Bitmap? CompositeLayers()
    {
        if (Layers.Count == 0)
        {
            return null;
        }

        int maxWidth = Layers.Max(layer => layer.Image.Width);
        int maxHeight = Layers.Max(layer => layer.Image.Height);
        Bitmap result = new(maxWidth, maxHeight, PixelFormat.Format32bppArgb);
        Rectangle bounds = new(0, 0, maxWidth, maxHeight);
        BitmapData resultData = result.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        byte[] resultBytes = new byte[Math.Abs(resultData.Stride) * maxHeight];
        Array.Fill(resultBytes, (byte)255);

        try
        {
            foreach (ImageLayer layer in Layers)
            {
                ApplyLayer(resultBytes, resultData.Stride, maxWidth, maxHeight, layer);
            }

            Marshal.Copy(resultBytes, 0, resultData.Scan0, resultBytes.Length);
        }
        finally
        {
            result.UnlockBits(resultData);
        }

        return result;
    }

    public void Dispose()
    {
        foreach (ImageLayer layer in Layers)
        {
            layer.Dispose();
        }

        Layers.Clear();
    }

    private void AddLayerInternal(Bitmap image, string name, BlendMode blendMode, double opacity)
    {
        try
        {
            Layers.Add(new ImageLayer
            {
                Name = name,
                Image = image,
                BlendMode = blendMode,
                Opacity = Math.Clamp(opacity, 0.0, 1.0),
                MaskType = MaskType.None
            });

            NotifyLayersChanged();
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private void NotifyLayersChanged()
    {
        if (_batchDepth > 0)
        {
            _hasPendingChange = true;
            return;
        }

        LayersChanged?.Invoke();
    }

    private void EndBatchUpdate()
    {
        if (_batchDepth == 0)
        {
            return;
        }

        _batchDepth--;
        if (_batchDepth == 0 && _hasPendingChange)
        {
            _hasPendingChange = false;
            LayersChanged?.Invoke();
        }
    }

    private static void ApplyLayer(byte[] resultBytes, int resultStride, int width, int height, ImageLayer layer)
    {
        Rectangle sourceBounds = new(0, 0, layer.Image.Width, layer.Image.Height);
        BitmapData sourceData = layer.Image.LockBits(sourceBounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            int sourceStride = Math.Abs(sourceData.Stride);
            byte[] sourceBytes = new byte[sourceStride * layer.Image.Height];
            Marshal.Copy(sourceData.Scan0, sourceBytes, 0, sourceBytes.Length);

            int[] xMap = CreateCoordinateMap(width, layer.Image.Width);
            int[] yMap = CreateCoordinateMap(height, layer.Image.Height);
            byte[]? maskAlpha = GenerateMaskAlpha(width, height, layer.MaskType);
            int opacity = (int)Math.Round(Math.Clamp(layer.Opacity, 0.0, 1.0) * 255.0);

            for (int y = 0; y < height; y++)
            {
                int resultRow = y * resultStride;
                int sourceRow = yMap[y] * sourceStride;
                int maskRow = y * width;

                for (int x = 0; x < width; x++)
                {
                    int effectiveOpacity = opacity;
                    if (maskAlpha is not null)
                    {
                        effectiveOpacity = (effectiveOpacity * maskAlpha[maskRow + x] + 127) / 255;
                        if (effectiveOpacity == 0)
                        {
                            continue;
                        }
                    }

                    int resultIndex = resultRow + (x * 4);
                    int sourceIndex = sourceRow + (xMap[x] * 4);

                    int baseBlue = resultBytes[resultIndex];
                    int baseGreen = resultBytes[resultIndex + 1];
                    int baseRed = resultBytes[resultIndex + 2];
                    int topBlue = sourceBytes[sourceIndex];
                    int topGreen = sourceBytes[sourceIndex + 1];
                    int topRed = sourceBytes[sourceIndex + 2];

                    BlendPixel(baseBlue, baseGreen, baseRed, topBlue, topGreen, topRed, layer.BlendMode,
                        out int blendedBlue, out int blendedGreen, out int blendedRed);

                    resultBytes[resultIndex] = ApplyOpacity(baseBlue, blendedBlue, effectiveOpacity);
                    resultBytes[resultIndex + 1] = ApplyOpacity(baseGreen, blendedGreen, effectiveOpacity);
                    resultBytes[resultIndex + 2] = ApplyOpacity(baseRed, blendedRed, effectiveOpacity);
                    resultBytes[resultIndex + 3] = 255;
                }
            }
        }
        finally
        {
            layer.Image.UnlockBits(sourceData);
        }
    }

    private static int[] CreateCoordinateMap(int targetSize, int sourceSize)
    {
        int[] map = new int[targetSize];
        for (int i = 0; i < targetSize; i++)
        {
            map[i] = i * sourceSize / targetSize;
        }

        return map;
    }

    private static byte[]? GenerateMaskAlpha(int width, int height, MaskType maskType)
    {
        if (maskType == MaskType.None)
        {
            return null;
        }

        byte[] mask = new byte[width * height];

        switch (maskType)
        {
            case MaskType.Circle:
                FillCircleMask(mask, width, height);
                break;

            case MaskType.Square:
                FillSquareMask(mask, width, height);
                break;

            case MaskType.Rectangle:
                FillRectangleMask(mask, width, height);
                break;
        }

        return mask;
    }

    private static void FillCircleMask(byte[] mask, int width, int height)
    {
        double diameter = Math.Min(width, height);
        double radius = diameter / 2d;
        double centerX = width / 2d;
        double centerY = height / 2d;
        double radiusSquared = radius * radius;

        for (int y = 0; y < height; y++)
        {
            double dy = (y + 0.5d) - centerY;
            int row = y * width;

            for (int x = 0; x < width; x++)
            {
                double dx = (x + 0.5d) - centerX;
                if ((dx * dx) + (dy * dy) <= radiusSquared)
                {
                    mask[row + x] = 255;
                }
            }
        }
    }

    private static void FillSquareMask(byte[] mask, int width, int height)
    {
        int size = Math.Min(width, height);
        int startX = (width - size) / 2;
        int startY = (height - size) / 2;
        int endX = startX + size;
        int endY = startY + size;

        for (int y = startY; y < endY; y++)
        {
            int row = y * width;
            for (int x = startX; x < endX; x++)
            {
                mask[row + x] = 255;
            }
        }
    }

    private static void FillRectangleMask(byte[] mask, int width, int height)
    {
        int marginX = width / 10;
        int marginY = height / 10;

        for (int y = marginY; y < height - marginY; y++)
        {
            int row = y * width;
            for (int x = marginX; x < width - marginX; x++)
            {
                mask[row + x] = 255;
            }
        }
    }

    private static void BlendPixel(
        int baseBlue,
        int baseGreen,
        int baseRed,
        int topBlue,
        int topGreen,
        int topRed,
        BlendMode mode,
        out int blendedBlue,
        out int blendedGreen,
        out int blendedRed)
    {
        switch (mode)
        {
            case BlendMode.None:
                blendedBlue = topBlue;
                blendedGreen = topGreen;
                blendedRed = topRed;
                break;

            case BlendMode.Sum:
                blendedBlue = Math.Min(baseBlue + topBlue, 255);
                blendedGreen = Math.Min(baseGreen + topGreen, 255);
                blendedRed = Math.Min(baseRed + topRed, 255);
                break;

            case BlendMode.Difference:
                blendedBlue = Math.Abs(baseBlue - topBlue);
                blendedGreen = Math.Abs(baseGreen - topGreen);
                blendedRed = Math.Abs(baseRed - topRed);
                break;

            case BlendMode.Multiply:
                blendedBlue = (baseBlue * topBlue) / 255;
                blendedGreen = (baseGreen * topGreen) / 255;
                blendedRed = (baseRed * topRed) / 255;
                break;

            case BlendMode.Screen:
                blendedBlue = 255 - (((255 - baseBlue) * (255 - topBlue)) / 255);
                blendedGreen = 255 - (((255 - baseGreen) * (255 - topGreen)) / 255);
                blendedRed = 255 - (((255 - baseRed) * (255 - topRed)) / 255);
                break;

            case BlendMode.Average:
                blendedBlue = (baseBlue + topBlue) / 2;
                blendedGreen = (baseGreen + topGreen) / 2;
                blendedRed = (baseRed + topRed) / 2;
                break;

            case BlendMode.Min:
                blendedBlue = Math.Min(baseBlue, topBlue);
                blendedGreen = Math.Min(baseGreen, topGreen);
                blendedRed = Math.Min(baseRed, topRed);
                break;

            case BlendMode.Max:
                blendedBlue = Math.Max(baseBlue, topBlue);
                blendedGreen = Math.Max(baseGreen, topGreen);
                blendedRed = Math.Max(baseRed, topRed);
                break;

            default:
                blendedBlue = topBlue;
                blendedGreen = topGreen;
                blendedRed = topRed;
                break;
        }
    }

    private static byte ApplyOpacity(int baseChannel, int blendedChannel, int opacity)
    {
        return (byte)(((baseChannel * (255 - opacity)) + (blendedChannel * opacity) + 127) / 255);
    }

    private sealed class BatchUpdateScope : IDisposable
    {
        private LayerManager? _owner;

        public BatchUpdateScope(LayerManager owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            _owner?.EndBatchUpdate();
            _owner = null;
        }
    }
}
