using System.Drawing;

namespace SCOI_Lab_1;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

/// <summary>
/// Режимы наложения слоёв
/// </summary>
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

/// <summary>
/// Типы масок
/// </summary>
public enum MaskType
{
    None,
    Circle,
    Square,
    Rectangle
}

/// <summary>
/// Слой изображения
/// </summary>
public class ImageLayer : IDisposable
{
    public string Name { get; set; } = "";
    public Bitmap Image { get; set; } = null!;
    public BlendMode BlendMode { get; set; } = BlendMode.None;
    public double Opacity { get; set; } = 1.0;
    public MaskType MaskType { get; set; } = MaskType.None;

    public void Dispose()
    {
        Image?.Dispose();
    }

    public override string ToString() => $"{Name} [{BlendMode}, {Opacity:P0}]";
}

/// <summary>
/// Менеджер слоёв с поддержкой композитинга
/// </summary>
public class LayerManager : IDisposable
{
    public List<ImageLayer> Layers { get; } = new List<ImageLayer>();

    public event Action? LayersChanged;

    /// <summary>
    /// Добавить новый слой
    /// </summary>
    public void AddLayer(string filePath, BlendMode blendMode = BlendMode.None, double opacity = 1.0)
    {
        var layer = new ImageLayer
        {
            Name = Path.GetFileName(filePath),
            Image = new Bitmap(filePath),
            BlendMode = blendMode,
            Opacity = Math.Clamp(opacity, 0.0, 1.0)
        };
        Layers.Add(layer);
        LayersChanged?.Invoke();
    }

    /// <summary>
    /// Добавить слой из Bitmap
    /// </summary>
    public void AddLayer(Bitmap image, string name, BlendMode blendMode = BlendMode.None, double opacity = 1.0)
    {
        var layer = new ImageLayer
        {
            Name = name,
            Image = image,
            BlendMode = blendMode,
            Opacity = Math.Clamp(opacity, 0.0, 1.0)
        };
        Layers.Add(layer);
        LayersChanged?.Invoke();
    }

    /// <summary>
    /// Удалить слой по индексу
    /// </summary>
    public void RemoveLayer(int index)
    {
        if (index >= 0 && index < Layers.Count)
        {
            Layers[index].Dispose();
            Layers.RemoveAt(index);
            LayersChanged?.Invoke();
        }
    }

    /// <summary>
    /// Переместить слой вверх
    /// </summary>
    public void MoveUp(int index)
    {
        if (index > 0 && index < Layers.Count)
        {
            (Layers[index], Layers[index - 1]) = (Layers[index - 1], Layers[index]);
            LayersChanged?.Invoke();
        }
    }

    /// <summary>
    /// Переместить слой вниз
    /// </summary>
    public void MoveDown(int index)
    {
        if (index >= 0 && index < Layers.Count - 1)
        {
            (Layers[index], Layers[index + 1]) = (Layers[index + 1], Layers[index]);
            LayersChanged?.Invoke();
        }
    }

    /// <summary>
    /// Собрать все слои в одно изображение
    /// </summary>
    public Bitmap? CompositeLayers()
    {
        if (Layers.Count == 0)
            return null;

        // Находим максимальный размер
        int maxWidth = Layers.Max(l => l.Image.Width);
        int maxHeight = Layers.Max(l => l.Image.Height);

        // Создаём результирующее изображение
        Bitmap result = new Bitmap(maxWidth, maxHeight);

        // Заливаем белым цветом
        using (Graphics g = Graphics.FromImage(result))
        {
            g.Clear(Color.White);
        }

        // Накладываем слои снизу вверх
        foreach (var layer in Layers)
        {
            ApplyLayer(result, layer);
        }

        return result;
    }

    private void ApplyLayer(Bitmap result, ImageLayer layer)
    {
        int width = result.Width;
        int height = result.Height;

        // Генерируем маску, если нужно
        Bitmap? mask = layer.MaskType != MaskType.None 
            ? GenerateMask(width, height, layer.MaskType) 
            : null;

        int lWidth = layer.Image.Width;
        int lHeight = layer.Image.Height;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color baseColor = result.GetPixel(x, y);

                int lx = x * lWidth / width;
                int ly = y * lHeight / height;
                Color layerColor = layer.Image.GetPixel(lx, ly);

                double effectiveOpacity = layer.Opacity;
                if (mask != null)
                {
                    Color maskColor = mask.GetPixel(x, y);
                    double maskAlpha = maskColor.R / 255.0;
                    effectiveOpacity *= maskAlpha;

                    if (effectiveOpacity < 0.001)
                    {
                        continue;
                    }
                }

                Color blended = BlendPixels(baseColor, layerColor, layer.BlendMode);
                Color final = ApplyOpacity(baseColor, blended, effectiveOpacity);

                result.SetPixel(x, y, final);
            }
        }

        mask?.Dispose();
    }

    /// <summary>
    /// Генерация маски заданной формы
    /// </summary>
    private Bitmap GenerateMask(int width, int height, MaskType maskType)
    {
        Bitmap mask = new Bitmap(width, height);
        using Graphics g = Graphics.FromImage(mask);
        g.Clear(Color.Black); // Черный = прозрачный

        using Brush whiteBrush = new SolidBrush(Color.White);

        switch (maskType)
        {
            case MaskType.Circle:
                // Рисуем круг в центре
                int diameter = Math.Min(width, height);
                int x = (width - diameter) / 2;
                int y = (height - diameter) / 2;
                g.FillEllipse(whiteBrush, x, y, diameter, diameter);
                break;

            case MaskType.Square:
                // Рисуем квадрат в центре
                int size = Math.Min(width, height);
                int sx = (width - size) / 2;
                int sy = (height - size) / 2;
                g.FillRectangle(whiteBrush, sx, sy, size, size);
                break;

            case MaskType.Rectangle:
                // Прямоугольник с отступами 10%
                int marginX = width / 10;
                int marginY = height / 10;
                g.FillRectangle(whiteBrush, marginX, marginY, 
                    width - marginX * 2, height - marginY * 2);
                break;
        }

        return mask;
    }

    private Color BlendPixels(Color baseC, Color topC, BlendMode mode)
    {
        int r, g, b;

        switch (mode)
        {
            case BlendMode.None:
                return topC;

            case BlendMode.Sum:
                r = Math.Min(baseC.R + topC.R, 255);
                g = Math.Min(baseC.G + topC.G, 255);
                b = Math.Min(baseC.B + topC.B, 255);
                break;

            case BlendMode.Difference:
                r = Math.Abs(baseC.R - topC.R);
                g = Math.Abs(baseC.G - topC.G);
                b = Math.Abs(baseC.B - topC.B);
                break;

            case BlendMode.Multiply:
                r = (baseC.R * topC.R) / 255;
                g = (baseC.G * topC.G) / 255;
                b = (baseC.B * topC.B) / 255;
                break;

            case BlendMode.Screen:
                r = 255 - ((255 - baseC.R) * (255 - topC.R)) / 255;
                g = 255 - ((255 - baseC.G) * (255 - topC.G)) / 255;
                b = 255 - ((255 - baseC.B) * (255 - topC.B)) / 255;
                break;

            case BlendMode.Average:
                r = (baseC.R + topC.R) / 2;
                g = (baseC.G + topC.G) / 2;
                b = (baseC.B + topC.B) / 2;
                break;

            case BlendMode.Min:
                r = Math.Min(baseC.R, topC.R);
                g = Math.Min(baseC.G, topC.G);
                b = Math.Min(baseC.B, topC.B);
                break;

            case BlendMode.Max:
                r = Math.Max(baseC.R, topC.R);
                g = Math.Max(baseC.G, topC.G);
                b = Math.Max(baseC.B, topC.B);
                break;

            default:
                return topC;
        }

        return Color.FromArgb(r, g, b);
    }

    private Color ApplyOpacity(Color baseC, Color blendedC, double opacity)
    {
        int r = (int)(baseC.R * (1 - opacity) + blendedC.R * opacity);
        int g = (int)(baseC.G * (1 - opacity) + blendedC.G * opacity);
        int b = (int)(baseC.B * (1 - opacity) + blendedC.B * opacity);

        return Color.FromArgb(
            Math.Clamp(r, 0, 255),
            Math.Clamp(g, 0, 255),
            Math.Clamp(b, 0, 255)
        );
    }

    public void Dispose()
    {
        foreach (var layer in Layers)
        {
            layer.Dispose();
        }
        Layers.Clear();
    }
}