using System.Drawing.Imaging;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = System.Drawing.Bitmap;

namespace SCOI_Lab_1;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType OpenImageFileType = new("Изображения")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif" }
    };

    private static readonly FilePickerFileType PngFileType = new("PNG")
    {
        Patterns = new[] { "*.png" }
    };

    private static readonly FilePickerFileType JpegFileType = new("JPEG")
    {
        Patterns = new[] { "*.jpg", "*.jpeg" }
    };

    private static readonly FilePickerFileType BmpFileType = new("BMP")
    {
        Patterns = new[] { "*.bmp" }
    };

    private readonly LayerManager _layerManager = new();
    private readonly List<SelectionOption<BlendMode>> _blendModeOptions =
    [
        new(BlendMode.None, "Обычный"),
        new(BlendMode.Sum, "Сумма"),
        new(BlendMode.Difference, "Разность"),
        new(BlendMode.Multiply, "Умножение"),
        new(BlendMode.Screen, "Экран"),
        new(BlendMode.Average, "Среднее"),
        new(BlendMode.Min, "Минимум"),
        new(BlendMode.Max, "Максимум")
    ];
    private readonly List<SelectionOption<MaskType>> _maskTypeOptions =
    [
        new(MaskType.None, "Без маски"),
        new(MaskType.Circle, "Круг"),
        new(MaskType.Square, "Квадрат"),
        new(MaskType.Rectangle, "Прямоугольник")
    ];

    private readonly Image _previewImageControl;
    private readonly TextBlock _emptyStateText;
    private readonly ListBox _layersList;
    private readonly ComboBox _blendModeCombo;
    private readonly ComboBox _maskTypeCombo;
    private readonly Slider _opacitySlider;
    private readonly TextBlock _opacityText;
    private readonly Button _removeButton;
    private readonly Button _moveUpButton;
    private readonly Button _moveDownButton;
    private readonly Border _layerSettingsCard;
    private readonly Button _gradationButton;
    private readonly Button _binarizationButton;
    private readonly Button _filteringButton;
    private readonly Button _frequencyFilteringButton;
    private readonly Button _saveButton;

    private AvaloniaBitmap? _previewBitmap;
    private DrawingBitmap? _compositeResult;
    private bool _isSyncingControls;

    public MainWindow()
    {
        InitializeComponent();

        _previewImageControl = RequireControl<Image>("PreviewImage");
        _emptyStateText = RequireControl<TextBlock>("EmptyStateText");
        _layersList = RequireControl<ListBox>("LayersList");
        _blendModeCombo = RequireControl<ComboBox>("BlendModeCombo");
        _maskTypeCombo = RequireControl<ComboBox>("MaskTypeCombo");
        _opacitySlider = RequireControl<Slider>("OpacitySlider");
        _opacityText = RequireControl<TextBlock>("OpacityText");
        _removeButton = RequireControl<Button>("RemoveButton");
        _moveUpButton = RequireControl<Button>("MoveUpButton");
        _moveDownButton = RequireControl<Button>("MoveDownButton");
        _layerSettingsCard = RequireControl<Border>("LayerSettingsCard");
        _gradationButton = RequireControl<Button>("GradationButton");
        _binarizationButton = RequireControl<Button>("BinarizationButton");
        _filteringButton = RequireControl<Button>("FilteringButton");
        _frequencyFilteringButton = RequireControl<Button>("FrequencyFilteringButton");
        _saveButton = RequireControl<Button>("SaveButton");

        _blendModeCombo.ItemsSource = _blendModeOptions;
        _maskTypeCombo.ItemsSource = _maskTypeOptions;
        _blendModeCombo.SelectedIndex = 0;
        _maskTypeCombo.SelectedIndex = 0;

        _layerManager.LayersChanged += LayerManager_LayersChanged;

        RefreshLayersList();
        UpdatePreview();
        SyncLayerControls();
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
        _layerManager.LayersChanged -= LayerManager_LayersChanged;
        _previewImageControl.Source = null;

        _previewBitmap?.Dispose();
        _compositeResult?.Dispose();
        _layerManager.Dispose();

        base.OnClosed(e);
    }

    private async void AddButton_Click(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите изображения",
            AllowMultiple = true,
            FileTypeFilter = new[] { OpenImageFileType }
        });

        if (files.Count == 0)
        {
            return;
        }

        List<string> errors = new();
        using (_layerManager.BeginBatchUpdate())
        {
            foreach (IStorageFile file in files)
            {
                string? localPath = file.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(localPath))
                {
                    errors.Add($"{file.Name}: нельзя получить локальный путь.");
                    continue;
                }

                try
                {
                    _layerManager.AddLayer(localPath);
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(localPath)}: {ex.Message}");
                }
            }
        }

        int? selectedIndex = _layerManager.Layers.Count > 0 ? _layerManager.Layers.Count - 1 : null;
        RefreshLayersList(selectedIndex);

        if (errors.Count > 0)
        {
            await MessageDialog.ShowAsync(this, "Не удалось загрузить часть файлов", string.Join(Environment.NewLine, errors));
        }
    }

    private void RemoveButton_Click(object? sender, RoutedEventArgs e)
    {
        int? selectedIndex = GetSelectedModelIndex();
        if (selectedIndex is null)
        {
            return;
        }

        int nextSelection = Math.Min(selectedIndex.Value, _layerManager.Layers.Count - 2);
        _layerManager.RemoveLayer(selectedIndex.Value);
        RefreshLayersList(nextSelection >= 0 ? nextSelection : null);
    }

    private void MoveUpButton_Click(object? sender, RoutedEventArgs e)
    {
        int? selectedIndex = GetSelectedModelIndex();
        if (selectedIndex is null)
        {
            return;
        }

        _layerManager.MoveLayerTowardTop(selectedIndex.Value);
        RefreshLayersList(Math.Min(selectedIndex.Value + 1, _layerManager.Layers.Count - 1));
    }

    private void MoveDownButton_Click(object? sender, RoutedEventArgs e)
    {
        int? selectedIndex = GetSelectedModelIndex();
        if (selectedIndex is null)
        {
            return;
        }

        _layerManager.MoveLayerTowardBottom(selectedIndex.Value);
        RefreshLayersList(Math.Max(selectedIndex.Value - 1, 0));
    }

    private void LayersList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        SyncLayerControls();
    }

    private void BlendModeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingControls || _blendModeCombo.SelectedItem is not SelectionOption<BlendMode> selectedOption)
        {
            return;
        }

        ImageLayer? layer = GetSelectedLayer();
        if (layer is null)
        {
            return;
        }

        layer.BlendMode = selectedOption.Value;
        RefreshLayersList(GetSelectedModelIndex());
        UpdatePreview();
    }

    private void MaskTypeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingControls || _maskTypeCombo.SelectedItem is not SelectionOption<MaskType> selectedOption)
        {
            return;
        }

        ImageLayer? layer = GetSelectedLayer();
        if (layer is null)
        {
            return;
        }

        layer.MaskType = selectedOption.Value;
        UpdatePreview();
        UpdateControlState();
    }

    private void OpacitySlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        _opacityText.Text = $"Непрозрачность: {(int)Math.Round(_opacitySlider.Value)}%";

        if (_isSyncingControls)
        {
            return;
        }

        ImageLayer? layer = GetSelectedLayer();
        if (layer is null)
        {
            return;
        }

        layer.Opacity = _opacitySlider.Value / 100d;
        RefreshLayersList(GetSelectedModelIndex());
        UpdatePreview();
    }

    private async void GradationButton_Click(object? sender, RoutedEventArgs e)
    {
        ImageLayer? layer = GetSelectedLayer();
        if (layer is null)
        {
            return;
        }

        int? selectedIndex = GetSelectedModelIndex();
        GradationWindow dialog = new(layer.Image);
        DrawingBitmap? updatedImage = await dialog.ShowDialog<DrawingBitmap?>(this);
        if (updatedImage is null)
        {
            return;
        }

        DrawingBitmap previousImage = layer.Image;
        layer.Image = updatedImage;
        previousImage.Dispose();

        RefreshLayersList(selectedIndex);
        UpdatePreview();
    }

    private async void BinarizationButton_Click(object? sender, RoutedEventArgs e)
    {
        ImageLayer? layer = GetSelectedLayer();
        if (layer is null)
        {
            return;
        }

        int? selectedIndex = GetSelectedModelIndex();
        BinarizationWindow dialog = new(layer.Image);
        DrawingBitmap? updatedImage = await dialog.ShowDialog<DrawingBitmap?>(this);
        if (updatedImage is null)
        {
            return;
        }

        DrawingBitmap previousImage = layer.Image;
        layer.Image = updatedImage;
        previousImage.Dispose();

        RefreshLayersList(selectedIndex);
        UpdatePreview();
    }

    private async void FilteringButton_Click(object? sender, RoutedEventArgs e)
    {
        ImageLayer? layer = GetSelectedLayer();
        if (layer is null)
        {
            return;
        }

        int? selectedIndex = GetSelectedModelIndex();
        FilteringWindow dialog = new(layer.Image);
        DrawingBitmap? updatedImage = await dialog.ShowDialog<DrawingBitmap?>(this);
        if (updatedImage is null)
        {
            return;
        }

        DrawingBitmap previousImage = layer.Image;
        layer.Image = updatedImage;
        previousImage.Dispose();

        RefreshLayersList(selectedIndex);
        UpdatePreview();
    }

    private async void FrequencyFilteringButton_Click(object? sender, RoutedEventArgs e)
    {
        ImageLayer? layer = GetSelectedLayer();
        if (layer is null)
        {
            return;
        }

        int? selectedIndex = GetSelectedModelIndex();
        FrequencyFilteringWindow dialog = new(layer.Image);
        DrawingBitmap? updatedImage = await dialog.ShowDialog<DrawingBitmap?>(this);
        if (updatedImage is null)
        {
            return;
        }

        DrawingBitmap previousImage = layer.Image;
        layer.Image = updatedImage;
        previousImage.Dispose();

        RefreshLayersList(selectedIndex);
        UpdatePreview();
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_compositeResult is null)
        {
            await MessageDialog.ShowAsync(this, "Нечего сохранять", "Добавьте хотя бы один слой, чтобы собрать итоговое изображение.");
            return;
        }

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить результат",
            SuggestedFileName = "composite.png",
            DefaultExtension = "png",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[] { PngFileType, JpegFileType, BmpFileType }
        });

        if (file is null)
        {
            return;
        }

        ImageFormat format = GetImageFormat(file.Name);
        string? localPath = file.TryGetLocalPath();

        try
        {
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                _compositeResult.Save(localPath, format);
            }
            else
            {
                await using Stream output = await file.OpenWriteAsync();
                using MemoryStream buffer = new();
                _compositeResult.Save(buffer, format);
                buffer.Position = 0;
                await buffer.CopyToAsync(output);
                await output.FlushAsync();
            }

            await MessageDialog.ShowAsync(this, "Сохранено", "Итоговое изображение успешно сохранено.");
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, "Ошибка сохранения", ex.Message);
        }
    }

    private void LayerManager_LayersChanged()
    {
        UpdatePreview();
    }

    private void RefreshLayersList(int? preferredSelectedModelIndex = null)
    {
        int? selection = preferredSelectedModelIndex ?? GetSelectedModelIndex();
        List<LayerListItem> items = new();

        for (int i = _layerManager.Layers.Count - 1; i >= 0; i--)
        {
            ImageLayer layer = _layerManager.Layers[i];
            items.Add(new LayerListItem(i, $"{layer.Name} [{GetBlendModeLabel(layer.BlendMode)}, {layer.Opacity:P0}]"));
        }

        _layersList.ItemsSource = items;
        _layersList.SelectedItem = selection is null
            ? null
            : items.FirstOrDefault(item => item.ModelIndex == selection.Value);

        UpdateControlState();
    }

    private void SyncLayerControls()
    {
        ImageLayer? layer = GetSelectedLayer();

        _isSyncingControls = true;
        try
        {
            if (layer is null)
            {
                _blendModeCombo.SelectedIndex = 0;
                _maskTypeCombo.SelectedIndex = 0;
                _opacitySlider.Value = 100;
                _opacityText.Text = "Непрозрачность: 100%";
            }
            else
            {
                _blendModeCombo.SelectedItem = _blendModeOptions.First(option => option.Value == layer.BlendMode);
                _maskTypeCombo.SelectedItem = _maskTypeOptions.First(option => option.Value == layer.MaskType);
                _opacitySlider.Value = layer.Opacity * 100d;
                _opacityText.Text = $"Непрозрачность: {(int)Math.Round(layer.Opacity * 100d)}%";
            }
        }
        finally
        {
            _isSyncingControls = false;
        }

        UpdateControlState();
    }

    private void UpdatePreview()
    {
        DrawingBitmap? previousComposite = _compositeResult;
        _compositeResult = _layerManager.CompositeLayers();
        previousComposite?.Dispose();

        _previewImageControl.Source = null;
        _previewBitmap?.Dispose();
        _previewBitmap = _compositeResult is null ? null : BitmapUtilities.ToAvaloniaBitmap(_compositeResult);
        _previewImageControl.Source = _previewBitmap;
        _emptyStateText.IsVisible = _previewBitmap is null;

        UpdateControlState();
    }

    private void UpdateControlState()
    {
        int? selectedIndex = GetSelectedModelIndex();
        bool hasSelection = selectedIndex is not null;

        _layerSettingsCard.IsEnabled = hasSelection;
        _removeButton.IsEnabled = hasSelection;
        _moveUpButton.IsEnabled = hasSelection && selectedIndex < _layerManager.Layers.Count - 1;
        _moveDownButton.IsEnabled = hasSelection && selectedIndex > 0;
        _gradationButton.IsEnabled = hasSelection;
        _binarizationButton.IsEnabled = hasSelection;
        _filteringButton.IsEnabled = hasSelection;
        _frequencyFilteringButton.IsEnabled = hasSelection;
        _saveButton.IsEnabled = _compositeResult is not null;
    }

    private ImageLayer? GetSelectedLayer()
    {
        int? selectedIndex = GetSelectedModelIndex();
        if (selectedIndex is null || selectedIndex < 0 || selectedIndex >= _layerManager.Layers.Count)
        {
            return null;
        }

        return _layerManager.Layers[selectedIndex.Value];
    }

    private int? GetSelectedModelIndex()
    {
        return _layersList.SelectedItem is LayerListItem item ? item.ModelIndex : null;
    }

    private static ImageFormat GetImageFormat(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".bmp" => ImageFormat.Bmp,
            _ => ImageFormat.Png
        };
    }

    private static string GetBlendModeLabel(BlendMode blendMode)
    {
        return blendMode switch
        {
            BlendMode.None => "Обычный",
            BlendMode.Sum => "Сумма",
            BlendMode.Difference => "Разность",
            BlendMode.Multiply => "Умножение",
            BlendMode.Screen => "Экран",
            BlendMode.Average => "Среднее",
            BlendMode.Min => "Минимум",
            BlendMode.Max => "Максимум",
            _ => blendMode.ToString()
        };
    }

    private sealed record SelectionOption<T>(T Value, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private sealed record LayerListItem(int ModelIndex, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }
}
