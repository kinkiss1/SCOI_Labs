using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = System.Drawing.Bitmap;

namespace SCOI_Lab_1;

public partial class GradationWindow : Window
{
    private readonly DrawingBitmap _originalImage;
    private DrawingBitmap _previewImage;
    private readonly byte[] _lut = new byte[256];

    private readonly Image _originalImageView;
    private readonly Image _previewImageView;
    private readonly Image _originalHistogramView;
    private readonly Image _previewHistogramView;
    private readonly GradationCurveEditor _curveEditor;

    private AvaloniaBitmap? _originalImageBitmap;
    private AvaloniaBitmap? _previewImageBitmap;
    private AvaloniaBitmap? _originalHistogramBitmap;
    private AvaloniaBitmap? _previewHistogramBitmap;

    public GradationWindow()
        : this(new DrawingBitmap(1, 1))
    {
    }

    public GradationWindow(DrawingBitmap original)
    {
        InitializeComponent();

        _originalImage = BitmapUtilities.CreateWorkingCopy(original);
        _previewImage = BitmapUtilities.CreateWorkingCopy(original);

        _originalImageView = RequireControl<Image>("OriginalImage");
        _previewImageView = RequireControl<Image>("PreviewImage");
        _originalHistogramView = RequireControl<Image>("OriginalHistogramImage");
        _previewHistogramView = RequireControl<Image>("PreviewHistogramImage");
        _curveEditor = RequireControl<GradationCurveEditor>("CurveEditor");

        _curveEditor.SetControlPoints(CreateDefaultCurve());
        _curveEditor.PointsChanged += CurveEditor_PointsChanged;

        ReplaceImage(ref _originalImageBitmap, _originalImageView, _originalImage);
        ReplaceImage(ref _previewImageBitmap, _previewImageView, _previewImage);
        UpdateHistograms();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private T RequireControl<T>(string name) where T : Control
    {
        return this.FindControl<T>(name)
            ?? throw new InvalidOperationException($"Не найден контрол '{name}'.");
    }

    protected override void OnClosed(EventArgs e)
    {
        _curveEditor.PointsChanged -= CurveEditor_PointsChanged;

        _originalImageView.Source = null;
        _previewImageView.Source = null;
        _originalHistogramView.Source = null;
        _previewHistogramView.Source = null;

        _originalImageBitmap?.Dispose();
        _previewImageBitmap?.Dispose();
        _originalHistogramBitmap?.Dispose();
        _previewHistogramBitmap?.Dispose();

        _originalImage.Dispose();
        _previewImage.Dispose();

        base.OnClosed(e);
    }

    private void CurveEditor_PointsChanged(object? sender, EventArgs e)
    {
        UpdateLut();
    }

    private void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(BitmapUtilities.CreateWorkingCopy(_previewImage));
    }

    private void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        _curveEditor.SetControlPoints(CreateDefaultCurve());
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(default(DrawingBitmap));
    }

    private void UpdateLut()
    {
        Array.Fill(_lut, (byte)0);
        IReadOnlyList<Point> points = _curveEditor.GetControlPoints();

        for (int i = 0; i < points.Count - 1; i++)
        {
            int startX = (int)Math.Round(points[i].X);
            int endX = (int)Math.Round(points[i + 1].X);
            double startY = points[i].Y;
            double endY = points[i + 1].Y;

            for (int x = startX; x <= endX && x < 256; x++)
            {
                double t = (double)(x - startX) / Math.Max(1, endX - startX);
                int y = (int)Math.Round(startY + (t * (endY - startY)));
                _lut[x] = (byte)Math.Clamp(y, 0, 255);
            }
        }

        DrawingBitmap updatedPreview = GradationHelper.ApplyLut(_originalImage, _lut);
        _previewImage.Dispose();
        _previewImage = updatedPreview;

        ReplaceImage(ref _previewImageBitmap, _previewImageView, _previewImage);
        UpdateHistograms();
    }

    private void UpdateHistograms()
    {
        using DrawingBitmap originalHistogram = GradationHelper.DrawHistogram(
            GradationHelper.CalculateHistogram(_originalImage),
            256,
            100);
        using DrawingBitmap previewHistogram = GradationHelper.DrawHistogram(
            GradationHelper.CalculateHistogram(_previewImage),
            256,
            100);

        ReplaceImage(ref _originalHistogramBitmap, _originalHistogramView, originalHistogram);
        ReplaceImage(ref _previewHistogramBitmap, _previewHistogramView, previewHistogram);
    }

    private static IReadOnlyList<Point> CreateDefaultCurve()
    {
        return new[]
        {
            new Point(0, 0),
            new Point(255, 255)
        };
    }

    private static void ReplaceImage(ref AvaloniaBitmap? targetBitmap, Image targetControl, DrawingBitmap sourceBitmap)
    {
        targetControl.Source = null;
        targetBitmap?.Dispose();
        targetBitmap = BitmapUtilities.ToAvaloniaBitmap(sourceBitmap);
        targetControl.Source = targetBitmap;
    }
}
