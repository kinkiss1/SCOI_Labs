using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = System.Drawing.Bitmap;

namespace SCOI_Lab_1;

public partial class BinarizationWindow : Window
{
    private readonly DrawingBitmap _originalImage;
    private DrawingBitmap _previewImage;

    private readonly Image _originalImageView;
    private readonly Image _previewImageView;
    private readonly ComboBox _methodComboBox;
    private readonly TextBox _windowSizeTextBox;
    private readonly TextBox _sensitivityTextBox;
    private readonly TextBlock _windowSizeLabel;
    private readonly TextBlock _sensitivityLabel;
    private readonly TextBlock _methodDescriptionText;
    private readonly TextBlock _statusText;
    private readonly Button _previewButton;
    private readonly Button _applyButton;

    private readonly List<MethodOption> _methodOptions =
    [
        new(BinarizationMethod.Gavrilov, "Гаврилов", "Глобальный порог равен среднему значению яркости изображения."),
        new(BinarizationMethod.Otsu, "Отсу", "Глобальный порог выбирается по максимуму межклассовой дисперсии."),
        new(BinarizationMethod.Niblack, "Ниблек", "Локальный порог: среднее по окну плюс k на стандартное отклонение."),
        new(BinarizationMethod.Sauvola, "Саувола", "Локальный порог для неравномерного освещения. Использует среднее, отклонение и параметр k."),
        new(BinarizationMethod.Wolf, "Вульф", "Локальный порог с учётом минимальной яркости и максимального отклонения по окнам."),
        new(BinarizationMethod.BradleyRoth, "Брэдли-Рот", "Быстрый локальный метод через интегральное изображение и чувствительность k.")
    ];

    private AvaloniaBitmap? _originalAvaloniaBitmap;
    private AvaloniaBitmap? _previewAvaloniaBitmap;
    private BinarizationParameters? _lastPreviewParameters;

    public BinarizationWindow()
        : this(new DrawingBitmap(1, 1))
    {
    }

    public BinarizationWindow(DrawingBitmap original)
    {
        InitializeComponent();

        _originalImage = BitmapUtilities.CreateWorkingCopy(original);
        _previewImage = BitmapUtilities.CreateWorkingCopy(original);

        _originalImageView = RequireControl<Image>("OriginalImage");
        _previewImageView = RequireControl<Image>("PreviewImage");
        _methodComboBox = RequireControl<ComboBox>("MethodComboBox");
        _windowSizeTextBox = RequireControl<TextBox>("WindowSizeTextBox");
        _sensitivityTextBox = RequireControl<TextBox>("SensitivityTextBox");
        _windowSizeLabel = RequireControl<TextBlock>("WindowSizeLabel");
        _sensitivityLabel = RequireControl<TextBlock>("SensitivityLabel");
        _methodDescriptionText = RequireControl<TextBlock>("MethodDescriptionText");
        _statusText = RequireControl<TextBlock>("StatusText");
        _previewButton = RequireControl<Button>("PreviewButton");
        _applyButton = RequireControl<Button>("ApplyButton");

        _methodComboBox.ItemsSource = _methodOptions;
        _methodComboBox.SelectedIndex = 0;

        ReplaceImage(ref _originalAvaloniaBitmap, _originalImageView, _originalImage);
        ReplaceImage(ref _previewAvaloniaBitmap, _previewImageView, _previewImage);

        Opened += BinarizationWindow_Opened;
        UpdateInputsForSelectedMethod();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        Opened -= BinarizationWindow_Opened;

        _originalImageView.Source = null;
        _previewImageView.Source = null;
        _originalAvaloniaBitmap?.Dispose();
        _previewAvaloniaBitmap?.Dispose();
        _originalImage.Dispose();
        _previewImage.Dispose();

        base.OnClosed(e);
    }

    private async void BinarizationWindow_Opened(object? sender, EventArgs e)
    {
        await RefreshPreviewAsync(showErrors: true);
    }

    private async void PreviewButton_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshPreviewAsync(showErrors: true);
    }

    private async void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryBuildParameters(out BinarizationParameters? parameters, out string? errorMessage))
        {
            await MessageDialog.ShowAsync(this, "Некорректные параметры", errorMessage ?? "Проверьте введённые значения.");
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

    private void MethodComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateInputsForSelectedMethod();
    }

    private async Task<bool> RefreshPreviewAsync(bool showErrors)
    {
        if (!TryBuildParameters(out BinarizationParameters? parameters, out string? errorMessage))
        {
            _statusText.Text = errorMessage ?? "Ошибка параметров.";
            if (showErrors)
            {
                await MessageDialog.ShowAsync(this, "Некорректные параметры", errorMessage ?? "Проверьте введённые значения.");
            }

            return false;
        }

        SetBusyState(true, "Вычисляю бинаризацию...");

        try
        {
            DrawingBitmap result = await Task.Run(() => BinarizationHelper.Apply(_originalImage, parameters));
            _previewImage.Dispose();
            _previewImage = result;
            _lastPreviewParameters = parameters;
            ReplaceImage(ref _previewAvaloniaBitmap, _previewImageView, _previewImage);
            _statusText.Text = "Предпросмотр обновлён.";
            return true;
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Ошибка: {ex.Message}";
            if (showErrors)
            {
                await MessageDialog.ShowAsync(this, "Ошибка бинаризации", ex.Message);
            }

            return false;
        }
        finally
        {
            SetBusyState(false, _statusText.Text ?? string.Empty);
        }
    }

    private bool TryBuildParameters(out BinarizationParameters parameters, out string? errorMessage)
    {
        parameters = null!;
        errorMessage = null;

        if (_methodComboBox.SelectedItem is not MethodOption option)
        {
            errorMessage = "Не выбран метод бинаризации.";
            return false;
        }

        int windowSize = 25;
        if (UsesWindowSize(option.Method))
        {
            if (!int.TryParse(_windowSizeTextBox.Text, out windowSize))
            {
                errorMessage = "Размер окна должен быть целым числом.";
                return false;
            }

            windowSize = Math.Max(3, windowSize);
            if (windowSize % 2 == 0)
            {
                windowSize++;
            }
        }

        double sensitivity = 0;
        if (UsesSensitivity(option.Method))
        {
            string rawText = (_sensitivityTextBox.Text ?? string.Empty).Replace(',', '.');
            if (!double.TryParse(rawText, NumberStyles.Float, CultureInfo.InvariantCulture, out sensitivity))
            {
                errorMessage = "Параметр чувствительности должен быть числом.";
                return false;
            }
        }

        parameters = new BinarizationParameters
        {
            Method = option.Method,
            WindowSize = windowSize,
            Sensitivity = sensitivity
        };

        return true;
    }

    private void UpdateInputsForSelectedMethod()
    {
        if (_methodComboBox.SelectedItem is not MethodOption option)
        {
            return;
        }

        _methodDescriptionText.Text = option.Description;
        bool useWindow = UsesWindowSize(option.Method);
        bool useSensitivity = UsesSensitivity(option.Method);

        _windowSizeLabel.IsVisible = useWindow;
        _windowSizeTextBox.IsVisible = useWindow;
        _windowSizeTextBox.IsEnabled = useWindow;

        _sensitivityLabel.IsVisible = useSensitivity;
        _sensitivityTextBox.IsVisible = useSensitivity;
        _sensitivityTextBox.IsEnabled = useSensitivity;

        switch (option.Method)
        {
            case BinarizationMethod.Gavrilov:
            case BinarizationMethod.Otsu:
                _windowSizeTextBox.Text = string.Empty;
                _sensitivityTextBox.Text = string.Empty;
                break;

            case BinarizationMethod.Niblack:
                _windowSizeTextBox.Text = "25";
                _sensitivityTextBox.Text = "-0.2";
                break;

            case BinarizationMethod.Sauvola:
                _windowSizeTextBox.Text = "25";
                _sensitivityTextBox.Text = "0.5";
                break;

            case BinarizationMethod.Wolf:
                _windowSizeTextBox.Text = "25";
                _sensitivityTextBox.Text = "0.5";
                break;

            case BinarizationMethod.BradleyRoth:
                _windowSizeTextBox.Text = "31";
                _sensitivityTextBox.Text = "0.15";
                break;
        }

        _lastPreviewParameters = null;
        _statusText.Text = "Параметры изменены. Постройте новый предпросмотр.";
    }

    private void SetBusyState(bool isBusy, string status)
    {
        _previewButton.IsEnabled = !isBusy;
        _applyButton.IsEnabled = !isBusy;
        _methodComboBox.IsEnabled = !isBusy;
        _windowSizeTextBox.IsEnabled = !isBusy && UsesWindowSize(GetSelectedMethod());
        _sensitivityTextBox.IsEnabled = !isBusy && UsesSensitivity(GetSelectedMethod());
        _statusText.Text = status;
    }

    private BinarizationMethod GetSelectedMethod()
    {
        return _methodComboBox.SelectedItem is MethodOption option
            ? option.Method
            : BinarizationMethod.Gavrilov;
    }

    private static bool UsesWindowSize(BinarizationMethod method)
    {
        return method is not BinarizationMethod.Gavrilov and not BinarizationMethod.Otsu;
    }

    private static bool UsesSensitivity(BinarizationMethod method)
    {
        return method is not BinarizationMethod.Gavrilov and not BinarizationMethod.Otsu;
    }

    private static bool AreSameParameters(BinarizationParameters? left, BinarizationParameters? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return left.Method == right.Method
            && left.WindowSize == right.WindowSize
            && Math.Abs(left.Sensitivity - right.Sensitivity) < 0.000001;
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

    private sealed record MethodOption(BinarizationMethod Method, string Label, string Description)
    {
        public override string ToString()
        {
            return Label;
        }
    }
}
