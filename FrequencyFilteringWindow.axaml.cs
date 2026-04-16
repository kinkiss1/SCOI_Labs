using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = System.Drawing.Bitmap;

namespace SCOI_Lab_1;

public partial class FrequencyFilteringWindow : Window
{
    private readonly DrawingBitmap _originalImage;
    private DrawingBitmap _resultImage;
    private DrawingBitmap? _spectrumImage;
    private DrawingBitmap? _maskImage;

    private readonly Image _originalImageView;
    private readonly Image _spectrumImageView;
    private readonly Image _maskImageView;
    private readonly Image _resultImageView;
    private readonly ComboBox _filterTypeComboBox;
    private readonly TextBlock _filterDescriptionText;
    private readonly StackPanel _radiusPanel;
    private readonly TextBox _radiusTextBox;
    private readonly Border _bandPanel;
    private readonly TextBox _innerRadiusTextBox;
    private readonly TextBox _outerRadiusTextBox;
    private readonly Border _notchPanel;
    private readonly TextBox _notchOffsetXTextBox;
    private readonly TextBox _notchOffsetYTextBox;
    private readonly TextBox _notchRadiusXTextBox;
    private readonly TextBox _notchRadiusYTextBox;
    private readonly TextBlock _statusText;
    private readonly TextBlock _timingText;
    private readonly TextBlock _workingSizeText;
    private readonly Button _previewButton;
    private readonly Button _applyButton;

    private readonly List<FilterOption> _filterOptions =
    [
        new(FrequencyFilterMode.LowPass, "Низкочастотный", "Оставляет только частоты внутри окружности радиуса R."),
        new(FrequencyFilterMode.HighPass, "Высокочастотный", "Подавляет центральную область спектра и оставляет детали высокой частоты."),
        new(FrequencyFilterMode.BandReject, "Режекторный", "Подавляет кольцевую полосу частот между внутренним и внешним радиусами."),
        new(FrequencyFilterMode.BandPass, "Полосовой", "Пропускает только кольцевую полосу частот между внутренним и внешним радиусами."),
        new(FrequencyFilterMode.NarrowBandPass, "Узкополосный полосовой", "Пропускает только две симметричные узкие области спектра."),
        new(FrequencyFilterMode.NarrowBandReject, "Узкополосный режекторный", "Подавляет две симметричные узкие области спектра, что полезно для удаления периодического шума.")
    ];

    private AvaloniaBitmap? _originalAvaloniaBitmap;
    private AvaloniaBitmap? _spectrumAvaloniaBitmap;
    private AvaloniaBitmap? _maskAvaloniaBitmap;
    private AvaloniaBitmap? _resultAvaloniaBitmap;
    private FrequencyFilterParameters? _lastPreviewParameters;

    public FrequencyFilteringWindow()
        : this(new DrawingBitmap(1, 1))
    {
    }

    public FrequencyFilteringWindow(DrawingBitmap original)
    {
        InitializeComponent();

        _originalImage = BitmapUtilities.CreateWorkingCopy(original);
        _resultImage = BitmapUtilities.CreateWorkingCopy(original);

        _originalImageView = RequireControl<Image>("OriginalImage");
        _spectrumImageView = RequireControl<Image>("SpectrumImage");
        _maskImageView = RequireControl<Image>("MaskImage");
        _resultImageView = RequireControl<Image>("ResultImage");
        _filterTypeComboBox = RequireControl<ComboBox>("FilterTypeComboBox");
        _filterDescriptionText = RequireControl<TextBlock>("FilterDescriptionText");
        _radiusPanel = RequireControl<StackPanel>("RadiusPanel");
        _radiusTextBox = RequireControl<TextBox>("RadiusTextBox");
        _bandPanel = RequireControl<Border>("BandPanel");
        _innerRadiusTextBox = RequireControl<TextBox>("InnerRadiusTextBox");
        _outerRadiusTextBox = RequireControl<TextBox>("OuterRadiusTextBox");
        _notchPanel = RequireControl<Border>("NotchPanel");
        _notchOffsetXTextBox = RequireControl<TextBox>("NotchOffsetXTextBox");
        _notchOffsetYTextBox = RequireControl<TextBox>("NotchOffsetYTextBox");
        _notchRadiusXTextBox = RequireControl<TextBox>("NotchRadiusXTextBox");
        _notchRadiusYTextBox = RequireControl<TextBox>("NotchRadiusYTextBox");
        _statusText = RequireControl<TextBlock>("StatusText");
        _timingText = RequireControl<TextBlock>("TimingText");
        _workingSizeText = RequireControl<TextBlock>("WorkingSizeText");
        _previewButton = RequireControl<Button>("PreviewButton");
        _applyButton = RequireControl<Button>("ApplyButton");

        _filterTypeComboBox.ItemsSource = _filterOptions;
        _filterTypeComboBox.SelectedIndex = 0;

        ReplaceImage(ref _originalAvaloniaBitmap, _originalImageView, _originalImage);
        ReplaceImage(ref _resultAvaloniaBitmap, _resultImageView, _resultImage);

        Opened += FrequencyFilteringWindow_Opened;
        UpdateInputsForSelectedFilter();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        Opened -= FrequencyFilteringWindow_Opened;

        _originalImageView.Source = null;
        _spectrumImageView.Source = null;
        _maskImageView.Source = null;
        _resultImageView.Source = null;

        _originalAvaloniaBitmap?.Dispose();
        _spectrumAvaloniaBitmap?.Dispose();
        _maskAvaloniaBitmap?.Dispose();
        _resultAvaloniaBitmap?.Dispose();

        _originalImage.Dispose();
        _resultImage.Dispose();
        _spectrumImage?.Dispose();
        _maskImage?.Dispose();

        base.OnClosed(e);
    }

    private async void FrequencyFilteringWindow_Opened(object? sender, EventArgs e)
    {
        await RefreshPreviewAsync(showErrors: true);
    }

    private async void PreviewButton_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshPreviewAsync(showErrors: true);
    }

    private async void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryBuildParameters(out FrequencyFilterParameters? parameters, out string? errorMessage))
        {
            await MessageDialog.ShowAsync(this, "Некорректные параметры", errorMessage ?? "Проверьте введенные значения.");
            return;
        }

        if (!AreSameParameters(parameters, _lastPreviewParameters))
        {
            bool success = await RefreshPreviewAsync(showErrors: true);
            if (!success)
            {
                return;
            }
        }

        Close(BitmapUtilities.CreateWorkingCopy(_resultImage));
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(default(DrawingBitmap));
    }

    private void FilterTypeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateInputsForSelectedFilter();
    }

    private async Task<bool> RefreshPreviewAsync(bool showErrors)
    {
        if (!TryBuildParameters(out FrequencyFilterParameters? parameters, out string? errorMessage))
        {
            _statusText.Text = errorMessage ?? "Некорректные параметры.";
            if (showErrors)
            {
                await MessageDialog.ShowAsync(this, "Некорректные параметры", errorMessage ?? "Проверьте введенные значения.");
            }

            return false;
        }

        SetBusyState(true, "Выполняется частотная обработка...");
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            FrequencyFilterPreview preview = await Task.Run(() => FrequencyFilteringHelper.BuildPreview(_originalImage, parameters));
            stopwatch.Stop();

            ReplacePreviewData(preview);
            _lastPreviewParameters = CloneParameters(parameters);
            _timingText.Text = $"Время обработки: {stopwatch.Elapsed.TotalMilliseconds:0} ms";
            _workingSizeText.Text = preview.UsedPadding
                ? $"Рабочий размер FFT: {preview.WorkingWidth}x{preview.WorkingHeight} (исходное изображение было дополнено нулями)."
                : $"Рабочий размер FFT: {preview.WorkingWidth}x{preview.WorkingHeight}";
            _statusText.Text = "Предпросмотр обновлен.";
            return true;
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Ошибка: {ex.Message}";
            if (showErrors)
            {
                await MessageDialog.ShowAsync(this, "Ошибка частотной фильтрации", ex.Message);
            }

            return false;
        }
        finally
        {
            SetBusyState(false, _statusText.Text ?? string.Empty);
        }
    }

    private bool TryBuildParameters(out FrequencyFilterParameters parameters, out string? errorMessage)
    {
        parameters = null!;
        errorMessage = null;

        if (_filterTypeComboBox.SelectedItem is not FilterOption option)
        {
            errorMessage = "Не выбран тип фильтра.";
            return false;
        }

        if (!TryParseDouble(_radiusTextBox.Text, out double radius) || radius < 0)
        {
            errorMessage = "Радиус должен быть неотрицательным числом.";
            return false;
        }

        if (!TryParseDouble(_innerRadiusTextBox.Text, out double innerRadius) || innerRadius < 0)
        {
            errorMessage = "Внутренний радиус должен быть неотрицательным числом.";
            return false;
        }

        if (!TryParseDouble(_outerRadiusTextBox.Text, out double outerRadius) || outerRadius < 0)
        {
            errorMessage = "Внешний радиус должен быть неотрицательным числом.";
            return false;
        }

        if (outerRadius < innerRadius)
        {
            errorMessage = "Внешний радиус должен быть не меньше внутреннего.";
            return false;
        }

        if (!TryParseDouble(_notchOffsetXTextBox.Text, out double notchOffsetX))
        {
            errorMessage = "Смещение X должно быть числом.";
            return false;
        }

        if (!TryParseDouble(_notchOffsetYTextBox.Text, out double notchOffsetY))
        {
            errorMessage = "Смещение Y должно быть числом.";
            return false;
        }

        if (!TryParseDouble(_notchRadiusXTextBox.Text, out double notchRadiusX) || notchRadiusX <= 0)
        {
            errorMessage = "Радиус X узкополосного фильтра должен быть положительным числом.";
            return false;
        }

        if (!TryParseDouble(_notchRadiusYTextBox.Text, out double notchRadiusY) || notchRadiusY <= 0)
        {
            errorMessage = "Радиус Y узкополосного фильтра должен быть положительным числом.";
            return false;
        }

        parameters = new FrequencyFilterParameters
        {
            Mode = option.Mode,
            Radius = radius,
            InnerRadius = innerRadius,
            OuterRadius = outerRadius,
            NotchOffsetX = notchOffsetX,
            NotchOffsetY = notchOffsetY,
            NotchRadiusX = notchRadiusX,
            NotchRadiusY = notchRadiusY
        };

        return true;
    }

    private void ReplacePreviewData(FrequencyFilterPreview preview)
    {
        _resultImage.Dispose();
        _spectrumImage?.Dispose();
        _maskImage?.Dispose();

        _resultImage = preview.FilteredImage;
        _spectrumImage = preview.SpectrumImage;
        _maskImage = preview.FilterMaskImage;

        ReplaceImage(ref _resultAvaloniaBitmap, _resultImageView, _resultImage);
        ReplaceImage(ref _spectrumAvaloniaBitmap, _spectrumImageView, _spectrumImage);
        ReplaceImage(ref _maskAvaloniaBitmap, _maskImageView, _maskImage);
    }

    private void UpdateInputsForSelectedFilter()
    {
        if (_filterTypeComboBox.SelectedItem is not FilterOption option)
        {
            return;
        }

        _filterDescriptionText.Text = option.Description;

        bool usesRadius = option.Mode is FrequencyFilterMode.LowPass or FrequencyFilterMode.HighPass;
        bool usesBand = option.Mode is FrequencyFilterMode.BandReject or FrequencyFilterMode.BandPass;
        bool usesNotch = option.Mode is FrequencyFilterMode.NarrowBandPass or FrequencyFilterMode.NarrowBandReject;

        _radiusPanel.IsVisible = usesRadius;
        _bandPanel.IsVisible = usesBand;
        _notchPanel.IsVisible = usesNotch;
        _lastPreviewParameters = null;
    }

    private void SetBusyState(bool isBusy, string status)
    {
        _previewButton.IsEnabled = !isBusy;
        _applyButton.IsEnabled = !isBusy;
        _statusText.Text = status;
    }

    private static bool TryParseDouble(string? text, out double value)
    {
        string normalized = (text ?? string.Empty).Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static FrequencyFilterParameters CloneParameters(FrequencyFilterParameters parameters)
    {
        return new FrequencyFilterParameters
        {
            Mode = parameters.Mode,
            Radius = parameters.Radius,
            InnerRadius = parameters.InnerRadius,
            OuterRadius = parameters.OuterRadius,
            NotchOffsetX = parameters.NotchOffsetX,
            NotchOffsetY = parameters.NotchOffsetY,
            NotchRadiusX = parameters.NotchRadiusX,
            NotchRadiusY = parameters.NotchRadiusY
        };
    }

    private static bool AreSameParameters(FrequencyFilterParameters? left, FrequencyFilterParameters? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return left.Mode == right.Mode
            && AreClose(left.Radius, right.Radius)
            && AreClose(left.InnerRadius, right.InnerRadius)
            && AreClose(left.OuterRadius, right.OuterRadius)
            && AreClose(left.NotchOffsetX, right.NotchOffsetX)
            && AreClose(left.NotchOffsetY, right.NotchOffsetY)
            && AreClose(left.NotchRadiusX, right.NotchRadiusX)
            && AreClose(left.NotchRadiusY, right.NotchRadiusY);
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) <= 0.0001;
    }

    private void ReplaceImage(ref AvaloniaBitmap? currentBitmap, Image target, DrawingBitmap source)
    {
        target.Source = null;
        currentBitmap?.Dispose();
        currentBitmap = BitmapUtilities.ToAvaloniaBitmap(source);
        target.Source = currentBitmap;
    }

    private T RequireControl<T>(string name) where T : Control
    {
        return this.FindControl<T>(name)
            ?? throw new InvalidOperationException($"Control '{name}' was not found.");
    }

    private sealed record FilterOption(FrequencyFilterMode Mode, string Label, string Description)
    {
        public override string ToString()
        {
            return Label;
        }
    }
}
