using System.Drawing;
using System.Drawing.Imaging;

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

    public event Action? LayersChanged;

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
        LayersChanged?.Invoke();
    }

    public void MoveLayerTowardTop(int index)
    {
        if (index < 0 || index >= Layers.Count - 1)
        {
            return;
        }

        (Layers[index], Layers[index + 1]) = (Layers[index + 1], Layers[index]);
        LayersChanged?.Invoke();
    }

    public void MoveLayerTowardBottom(int index)
    {
        if (index <= 0 || index >= Layers.Count)
        {
            return;
        }

        (Layers[index], Layers[index - 1]) = (Layers[index - 1], Layers[index]);
        LayersChanged?.Invoke();
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

        using (Graphics graphics = Graphics.FromImage(result))
        {
            graphics.Clear(Color.White);
        }

        foreach (ImageLayer layer in Layers)
        {
            ApplyLayer(result, layer);
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

            LayersChanged?.Invoke();
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static void ApplyLayer(Bitmap result, ImageLayer layer)
    {
        int width = result.Width;
        int height = result.Height;
        Bitmap? mask = layer.MaskType == MaskType.None ? null : GenerateMask(width, height, layer.MaskType);
        int layerWidth = layer.Image.Width;
        int layerHeight = layer.Image.Height;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color baseColor = result.GetPixel(x, y);
                int layerX = x * layerWidth / width;
                int layerY = y * layerHeight / height;
                Color layerColor = layer.Image.GetPixel(layerX, layerY);

                double effectiveOpacity = layer.Opacity;
                if (mask is not null)
                {
                    double maskAlpha = mask.GetPixel(x, y).R / 255.0;
                    effectiveOpacity *= maskAlpha;
                    if (effectiveOpacity < 0.001)
                    {
                        continue;
                    }
                }

                Color blended = BlendPixels(baseColor, layerColor, layer.BlendMode);
                result.SetPixel(x, y, ApplyOpacity(baseColor, blended, effectiveOpacity));
            }
        }

        mask?.Dispose();
    }

    private static Bitmap GenerateMask(int width, int height, MaskType maskType)
    {
        Bitmap mask = new(width, height, PixelFormat.Format32bppArgb);

        using Graphics graphics = Graphics.FromImage(mask);
        using Brush brush = new SolidBrush(Color.White);
        graphics.Clear(Color.Black);

        switch (maskType)
        {
            case MaskType.Circle:
                int diameter = Math.Min(width, height);
                graphics.FillEllipse(brush, (width - diameter) / 2, (height - diameter) / 2, diameter, diameter);
                break;

            case MaskType.Square:
                int size = Math.Min(width, height);
                graphics.FillRectangle(brush, (width - size) / 2, (height - size) / 2, size, size);
                break;

            case MaskType.Rectangle:
                int marginX = width / 10;
                int marginY = height / 10;
                graphics.FillRectangle(brush, marginX, marginY, width - (marginX * 2), height - (marginY * 2));
                break;
        }

        return mask;
    }

    private static Color BlendPixels(Color baseColor, Color topColor, BlendMode mode)
    {
        return mode switch
        {
            BlendMode.None => topColor,
            BlendMode.Sum => Color.FromArgb(
                Math.Min(baseColor.R + topColor.R, 255),
                Math.Min(baseColor.G + topColor.G, 255),
                Math.Min(baseColor.B + topColor.B, 255)),
            BlendMode.Difference => Color.FromArgb(
                Math.Abs(baseColor.R - topColor.R),
                Math.Abs(baseColor.G - topColor.G),
                Math.Abs(baseColor.B - topColor.B)),
            BlendMode.Multiply => Color.FromArgb(
                (baseColor.R * topColor.R) / 255,
                (baseColor.G * topColor.G) / 255,
                (baseColor.B * topColor.B) / 255),
            BlendMode.Screen => Color.FromArgb(
                255 - (((255 - baseColor.R) * (255 - topColor.R)) / 255),
                255 - (((255 - baseColor.G) * (255 - topColor.G)) / 255),
                255 - (((255 - baseColor.B) * (255 - topColor.B)) / 255)),
            BlendMode.Average => Color.FromArgb(
                (baseColor.R + topColor.R) / 2,
                (baseColor.G + topColor.G) / 2,
                (baseColor.B + topColor.B) / 2),
            BlendMode.Min => Color.FromArgb(
                Math.Min(baseColor.R, topColor.R),
                Math.Min(baseColor.G, topColor.G),
                Math.Min(baseColor.B, topColor.B)),
            BlendMode.Max => Color.FromArgb(
                Math.Max(baseColor.R, topColor.R),
                Math.Max(baseColor.G, topColor.G),
                Math.Max(baseColor.B, topColor.B)),
            _ => topColor
        };
    }

    private static Color ApplyOpacity(Color baseColor, Color blendedColor, double opacity)
    {
        int red = (int)(baseColor.R * (1 - opacity) + blendedColor.R * opacity);
        int green = (int)(baseColor.G * (1 - opacity) + blendedColor.G * opacity);
        int blue = (int)(baseColor.B * (1 - opacity) + blendedColor.B * opacity);

        return Color.FromArgb(
            Math.Clamp(red, 0, 255),
            Math.Clamp(green, 0, 255),
            Math.Clamp(blue, 0, 255));
    }
}
