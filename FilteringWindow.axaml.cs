using System.Globalization;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = System.Drawing.Bitmap;

namespace SCOI_Lab_1;

public partial class FilteringWindow : Window
{
    private readonly DrawingBitmap _originalImage;
    private DrawingBitmap _previewImage;

    private readonly Image _originalImageView;
    private readonly Image _previewImageView;
    private readonly ComboBox _filterTypeComboBox;
    private readonly TextBlock _filterDescriptionText;
    private readonly TextBlock _kernelLabel;
    private readonly TextBox _kernelTextBox;
    private readonly CheckBox _normalizeKernelCheckBox;
    private readonly Border _gaussianCard;
    private readonly TextBox _gaussianWidthTextBox;
    private readonly TextBox _gaussianHeightTextBox;
    private readonly TextBox _gaussianSigmaTextBox;
    private readonly Button _generateGaussianButton;
    private readonly Button _useLabPresetButton;
    private readonly TextBlock _medianWindowLabel;
    private readonly Grid _medianWindowPanel;
    private readonly TextBox _medianWidthTextBox;
    private readonly TextBox _medianHeightTextBox;
    private readonly TextBlock _statusText;
    private readonly TextBlock _timingText;
    private readonly Button _previewButton;
    private readonly Button _applyButton;

    private readonly List<FilterOption> _filterOptions =
    [
        new(SpatialFilterMode.Linear, "Линейная фильтрация", "Используется пользовательское ядро нечетного размера. Поддерживаются квадратные и прямоугольные ядра."),
        new(SpatialFilterMode.Median, "Медианная фильтрация", "Медиана рассчитывается алгоритмом Quickselect в настраиваемом нечетном окне.")
    ];

    private AvaloniaBitmap? _originalAvaloniaBitmap;
    private AvaloniaBitmap? _previewAvaloniaBitmap;
    private SpatialFilterParameters? _lastPreviewParameters;
    private double? _lastElapsedMilliseconds;

    public FilteringWindow()
        : this(new DrawingBitmap(1, 1))
    {
    }

    public FilteringWindow(DrawingBitmap original)
    {
        InitializeComponent();

        _originalImage = BitmapUtilities.CreateWorkingCopy(original);
        _previewImage = BitmapUtilities.CreateWorkingCopy(original);

        _originalImageView = RequireControl<Image>("OriginalImage");
        _previewImageView = RequireControl<Image>("PreviewImage");
        _filterTypeComboBox = RequireControl<ComboBox>("FilterTypeComboBox");
        _filterDescriptionText = RequireControl<TextBlock>("FilterDescriptionText");
        _kernelLabel = RequireControl<TextBlock>("KernelLabel");
        _kernelTextBox = RequireControl<TextBox>("KernelTextBox");
        _normalizeKernelCheckBox = RequireControl<CheckBox>("NormalizeKernelCheckBox");
        _gaussianCard = RequireControl<Border>("GaussianCard");
        _gaussianWidthTextBox = RequireControl<TextBox>("GaussianWidthTextBox");
        _gaussianHeightTextBox = RequireControl<TextBox>("GaussianHeightTextBox");
        _gaussianSigmaTextBox = RequireControl<TextBox>("GaussianSigmaTextBox");
        _generateGaussianButton = RequireControl<Button>("GenerateGaussianButton");
        _useLabPresetButton = RequireControl<Button>("UseLabPresetButton");
        _medianWindowLabel = RequireControl<TextBlock>("MedianWindowLabel");
        _medianWindowPanel = RequireControl<Grid>("MedianWindowPanel");
        _medianWidthTextBox = RequireControl<TextBox>("MedianWidthTextBox");
        _medianHeightTextBox = RequireControl<TextBox>("MedianHeightTextBox");
        _statusText = RequireControl<TextBlock>("StatusText");
        _timingText = RequireControl<TextBlock>("TimingText");
        _previewButton = RequireControl<Button>("PreviewButton");
        _applyButton = RequireControl<Button>("ApplyButton");

        _filterTypeComboBox.ItemsSource = _filterOptions;
        _filterTypeComboBox.SelectedIndex = 0;
        _kernelTextBox.Text = "0 0 0\n0 1 0\n0 0 0";

        ReplaceImage(ref _originalAvaloniaBitmap, _originalImageView, _originalImage);
        ReplaceImage(ref _previewAvaloniaBitmap, _previewImageView, _previewImage);

        Opened += FilteringWindow_Opened;
        UpdateInputsForSelectedFilter();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        Opened -= FilteringWindow_Opened;
        _originalImageView.Source = null;
        _previewImageView.Source = null;
        _originalAvaloniaBitmap?.Dispose();
        _previewAvaloniaBitmap?.Dispose();
        _originalImage.Dispose();
        _previewImage.Dispose();
        base.OnClosed(e);
    }

    private async void FilteringWindow_Opened(object? sender, EventArgs e)
    {
        await RefreshPreviewAsync(showErrors: true);
    }

    private async void PreviewButton_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshPreviewAsync(showErrors: true);
    }

    private async void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryBuildParameters(out SpatialFilterParameters? parameters, out string? errorMessage))
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

        Close(BitmapUtilities.CreateWorkingCopy(_previewImage));
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(default(DrawingBitmap));
    }

    private void FilterTypeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateInputsForSelectedFilter();
    }

    private void GenerateGaussianButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryParseOddSize(_gaussianWidthTextBox.Text, out int width)
            || !TryParseOddSize(_gaussianHeightTextBox.Text, out int height))
        {
            _statusText.Text = "Ширина и высота ядра Гаусса должны быть положительными нечетными числами.";
            return;
        }

        if (!TryParseDouble(_gaussianSigmaTextBox.Text, out double sigma) || sigma <= 0)
        {
            _statusText.Text = "Sigma должна быть положительным числом.";
            return;
        }

        double[,] kernel = SpatialFilteringHelper.GenerateGaussianKernel(width, height, sigma);
        _kernelTextBox.Text = KernelToText(kernel, 5);
        _normalizeKernelCheckBox.IsChecked = true;
        _statusText.Text = $"Ядро Гаусса создано: {width}x{height}, sigma={sigma.ToString("0.###", CultureInfo.InvariantCulture)}.";
        _lastPreviewParameters = null;
    }

    private void UseLabPresetButton_Click(object? sender, RoutedEventArgs e)
    {
        _gaussianWidthTextBox.Text = "13";
        _gaussianHeightTextBox.Text = "13";
        _gaussianSigmaTextBox.Text = "3";
        GenerateGaussianButton_Click(sender, e);
    }

    private async Task<bool> RefreshPreviewAsync(bool showErrors)
    {
        if (!TryBuildParameters(out SpatialFilterParameters? parameters, out string? errorMessage))
        {
            _statusText.Text = errorMessage ?? "Некорректные параметры.";
            if (showErrors)
            {
                await MessageDialog.ShowAsync(this, "Некорректные параметры", errorMessage ?? "Проверьте введенные значения.");
            }

            return false;
        }

        SetBusyState(true, "Выполняется обработка...");
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            DrawingBitmap result = await Task.Run(() => SpatialFilteringHelper.Apply(_originalImage, parameters));
            stopwatch.Stop();
            UpdateTimingText(stopwatch.Elapsed.TotalMilliseconds);

            _previewImage.Dispose();
            _previewImage = result;
            _lastPreviewParameters = CloneParameters(parameters);
            ReplaceImage(ref _previewAvaloniaBitmap, _previewImageView, _previewImage);
            _statusText.Text = "Предпросмотр обновлен.";
            return true;
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Ошибка: {ex.Message}";
            if (showErrors)
            {
                await MessageDialog.ShowAsync(this, "Ошибка фильтрации", ex.Message);
            }

            return false;
        }
        finally
        {
            SetBusyState(false, _statusText.Text ?? string.Empty);
        }
    }

    private bool TryBuildParameters(out SpatialFilterParameters parameters, out string? errorMessage)
    {
        parameters = null!;
        errorMessage = null;

        if (_filterTypeComboBox.SelectedItem is not FilterOption option)
        {
            errorMessage = "Не выбран тип фильтра.";
            return false;
        }

        if (option.Mode == SpatialFilterMode.Linear)
        {
            if (!TryParseKernel(_kernelTextBox.Text, out double[,]? kernel, out string? kernelError))
            {
                errorMessage = kernelError ?? "Некорректное ядро.";
                return false;
            }

            parameters = new SpatialFilterParameters
            {
                Mode = SpatialFilterMode.Linear,
                Kernel = kernel,
                NormalizeKernel = _normalizeKernelCheckBox.IsChecked == true
            };

            return true;
        }

        if (!TryParseOddSize(_medianWidthTextBox.Text, out int medianWidth)
            || !TryParseOddSize(_medianHeightTextBox.Text, out int medianHeight))
        {
            errorMessage = "Ширина и высота окна медианы должны быть положительными нечетными числами.";
            return false;
        }

        parameters = new SpatialFilterParameters
        {
            Mode = SpatialFilterMode.Median,
            MedianWindowWidth = medianWidth,
            MedianWindowHeight = medianHeight
        };
        return true;
    }

    private void UpdateInputsForSelectedFilter()
    {
        if (_filterTypeComboBox.SelectedItem is not FilterOption option)
        {
            return;
        }

        bool isLinear = option.Mode == SpatialFilterMode.Linear;
        _filterDescriptionText.Text = option.Description;

        _kernelLabel.IsVisible = isLinear;
        _kernelTextBox.IsVisible = isLinear;
        _kernelTextBox.IsEnabled = isLinear;
        _normalizeKernelCheckBox.IsVisible = isLinear;
        _normalizeKernelCheckBox.IsEnabled = isLinear;
        _gaussianCard.IsVisible = isLinear;
        _generateGaussianButton.IsEnabled = isLinear;
        _useLabPresetButton.IsEnabled = isLinear;

        _medianWindowLabel.IsVisible = !isLinear;
        _medianWindowPanel.IsVisible = !isLinear;
        _medianWidthTextBox.IsEnabled = !isLinear;
        _medianHeightTextBox.IsEnabled = !isLinear;

        _lastPreviewParameters = null;
        _statusText.Text = "Параметры изменены. Постройте новый предпросмотр.";
    }

    private void SetBusyState(bool isBusy, string status)
    {
        _previewButton.IsEnabled = !isBusy;
        _applyButton.IsEnabled = !isBusy;
        _filterTypeComboBox.IsEnabled = !isBusy;

        bool linearMode = GetSelectedMode() == SpatialFilterMode.Linear;
        _kernelTextBox.IsEnabled = !isBusy && linearMode;
        _normalizeKernelCheckBox.IsEnabled = !isBusy && linearMode;
        _generateGaussianButton.IsEnabled = !isBusy && linearMode;
        _useLabPresetButton.IsEnabled = !isBusy && linearMode;
        _gaussianWidthTextBox.IsEnabled = !isBusy && linearMode;
        _gaussianHeightTextBox.IsEnabled = !isBusy && linearMode;
        _gaussianSigmaTextBox.IsEnabled = !isBusy && linearMode;

        _medianWidthTextBox.IsEnabled = !isBusy && !linearMode;
        _medianHeightTextBox.IsEnabled = !isBusy && !linearMode;
        _statusText.Text = status;
    }

    private SpatialFilterMode GetSelectedMode()
    {
        return _filterTypeComboBox.SelectedItem is FilterOption option
            ? option.Mode
            : SpatialFilterMode.Linear;
    }

    private static bool TryParseKernel(string? rawText, out double[,] kernel, out string? errorMessage)
    {
        kernel = new double[1, 1];
        errorMessage = null;
        string[] rows = (rawText ?? string.Empty)
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (rows.Length == 0)
        {
            errorMessage = "Ядро не задано.";
            return false;
        }

        List<double[]> parsedRows = new(rows.Length);
        int expectedColumns = -1;

        foreach (string row in rows)
        {
            string[] tokens = row.Split([' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
            {
                continue;
            }

            if (expectedColumns < 0)
            {
                expectedColumns = tokens.Length;
            }
            else if (expectedColumns != tokens.Length)
            {
                errorMessage = "Во всех строках ядра должно быть одинаковое число столбцов.";
                return false;
            }

            double[] parsed = new double[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!TryParseDouble(tokens[i], out double value))
                {
                    errorMessage = $"Не удалось прочитать '{tokens[i]}' как число.";
                    return false;
                }

                parsed[i] = value;
            }

            parsedRows.Add(parsed);
        }

        if (parsedRows.Count == 0 || expectedColumns <= 0)
        {
            errorMessage = "Ядро не задано.";
            return false;
        }

        if (parsedRows.Count % 2 == 0 || expectedColumns % 2 == 0)
        {
            errorMessage = "Размер ядра по обеим сторонам должен быть нечетным.";
            return false;
        }

        kernel = new double[parsedRows.Count, expectedColumns];
        for (int y = 0; y < parsedRows.Count; y++)
        {
            for (int x = 0; x < expectedColumns; x++)
            {
                kernel[y, x] = parsedRows[y][x];
            }
        }

        return true;
    }

    private static bool TryParseOddSize(string? text, out int value)
    {
        if (!int.TryParse(text, out value))
        {
            return false;
        }

        return value > 0 && value % 2 == 1;
    }

    private static bool TryParseDouble(string? text, out double value)
    {
        string normalized = (text ?? string.Empty).Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string KernelToText(double[,] kernel, int decimals)
    {
        int rows = kernel.GetLength(0);
        int columns = kernel.GetLength(1);
        string[] lines = new string[rows];
        string format = "0." + new string('0', Math.Max(0, decimals));

        for (int y = 0; y < rows; y++)
        {
            string[] values = new string[columns];
            for (int x = 0; x < columns; x++)
            {
                values[x] = kernel[y, x].ToString(format, CultureInfo.InvariantCulture);
            }

            lines[y] = string.Join(' ', values);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool AreSameParameters(SpatialFilterParameters? left, SpatialFilterParameters? right)
    {
        if (left is null || right is null || left.Mode != right.Mode)
        {
            return false;
        }

        if (left.Mode == SpatialFilterMode.Median)
        {
            return left.MedianWindowWidth == right.MedianWindowWidth
                && left.MedianWindowHeight == right.MedianWindowHeight;
        }

        if (left.NormalizeKernel != right.NormalizeKernel)
        {
            return false;
        }

        int rows = left.Kernel.GetLength(0);
        int columns = left.Kernel.GetLength(1);
        if (rows != right.Kernel.GetLength(0) || columns != right.Kernel.GetLength(1))
        {
            return false;
        }

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                if (Math.Abs(left.Kernel[y, x] - right.Kernel[y, x]) > 0.0000001)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static SpatialFilterParameters CloneParameters(SpatialFilterParameters source)
    {
        int rows = source.Kernel.GetLength(0);
        int columns = source.Kernel.GetLength(1);
        double[,] kernelCopy = new double[rows, columns];
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                kernelCopy[y, x] = source.Kernel[y, x];
            }
        }

        return new SpatialFilterParameters
        {
            Mode = source.Mode,
            Kernel = kernelCopy,
            NormalizeKernel = source.NormalizeKernel,
            MedianWindowWidth = source.MedianWindowWidth,
            MedianWindowHeight = source.MedianWindowHeight
        };
    }

    private T RequireControl<T>(string name) where T : Control
    {
        return this.FindControl<T>(name)
            ?? throw new InvalidOperationException($"Не найден контрол '{name}'.");
    }

    private static void ReplaceImage(ref AvaloniaBitmap? targetBitmap, Image targetControl, DrawingBitmap sourceBitmap)
    {
        targetControl.Source = null;
        targetBitmap?.Dispose();
        targetBitmap = BitmapUtilities.ToAvaloniaBitmap(sourceBitmap);
        targetControl.Source = targetBitmap;
    }

    private sealed record FilterOption(SpatialFilterMode Mode, string Label, string Description)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private void UpdateTimingText(double elapsedMilliseconds)
    {
        string current = elapsedMilliseconds.ToString("0.##", CultureInfo.InvariantCulture);
        if (_lastElapsedMilliseconds is null)
        {
            _timingText.Text = $"Время обработки: {current} мс";
            _lastElapsedMilliseconds = elapsedMilliseconds;
            return;
        }

        double previous = _lastElapsedMilliseconds.Value;
        double deltaPercent = previous > 0.000001
            ? ((elapsedMilliseconds - previous) / previous) * 100.0
            : 0;
        string sign = deltaPercent >= 0 ? "+" : string.Empty;
        _timingText.Text = $"Время обработки: {current} мс ({sign}{deltaPercent:0.##}% к предыдущему запуску)";
        _lastElapsedMilliseconds = elapsedMilliseconds;
    }
}
