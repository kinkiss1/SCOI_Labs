using System.Drawing;

namespace SCOI_Lab_1;

public class MainForm : Form
{
    private readonly LayerManager _layerManager = new();
    private Bitmap? _compositeResult;

    //управл
    private PictureBox _previewBox = null!;
    private ListBox _layersList = null!;
    private ComboBox _blendModeCombo = null!;
    private ComboBox _maskTypeCombo = null!;
    private TrackBar _opacityTracker = null!;
    private Label _opacityLabel = null!;
    private Button _addButton = null!;
    private Button _removeButton = null!;
    private Button _moveUpButton = null!;
    private Button _moveDownButton = null!;
    private Button _saveButton = null!;

    public MainForm()
    {
        // из-за скейла монитора
        AutoScaleMode = AutoScaleMode.Dpi;

        InitializeComponents();
        _layerManager.LayersChanged += UpdatePreview;
    }

    private void InitializeComponents()
    {
        //форма
        Text = "Редактор слоёв изображений";
        Size = new Size(1200, 800);
        MinimumSize = new Size(900, 700);
        StartPosition = FormStartPosition.CenterScreen;

        //левая панель
        _previewBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.LightGray,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(10)
        };

        //правая часть
        var rightPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 350,
            Padding = new Padding(15),
            AutoScroll = true
        };

        var titleLabel = new Label
        {
            Text = "Слои",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Location = new Point(15, 15),
            Size = new Size(320, 35),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        // слои
        _layersList = new ListBox
        {
            Location = new Point(15, 60),
            Size = new Size(320, 300),
            Font = new Font("Segoe UI", 10),
            IntegralHeight = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _layersList.SelectedIndexChanged += LayersList_SelectedIndexChanged;

        //кнопки
        _addButton = new Button 
        { 
            Text = "+", 
            Location = new Point(15, 370),
            Size = new Size(105, 35),
            Font = new Font("Segoe UI", 9),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _addButton.Click += AddButton_Click;

        _removeButton = new Button 
        { 
            Text = "-", 
            Location = new Point(125, 370),
            Size = new Size(95, 35),
            Font = new Font("Segoe UI", 9),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _removeButton.Click += RemoveButton_Click;

        _moveUpButton = new Button 
        { 
            Text = "Вверх", 
            Location = new Point(225, 370),
            Size = new Size(50, 35),
            Font = new Font("Segoe UI", 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _moveUpButton.Click += MoveUpButton_Click;

        _moveDownButton = new Button 
        { 
            Text = "Вниз", 
            Location = new Point(280, 370),
            Size = new Size(50, 35),
            Font = new Font("Segoe UI", 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _moveDownButton.Click += MoveDownButton_Click;

        var settingsGroup = new GroupBox
        {
            Text = "Настройки слоя",
            Location = new Point(15, 415),
            Size = new Size(320, 220),
            Font = new Font("Segoe UI", 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var blendLabel = new Label
        {
            Text = "Режим наложения:",
            Location = new Point(15, 30),
            AutoSize = true,
            Font = new Font("Segoe UI", 9)
        };

        _blendModeCombo = new ComboBox
        {
            Location = new Point(15, 50),
            Size = new Size(290, 30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9)
        };
        _blendModeCombo.Items.AddRange(Enum.GetNames(typeof(BlendMode)));
        _blendModeCombo.SelectedIndex = 0;
        _blendModeCombo.SelectedIndexChanged += BlendModeCombo_SelectedIndexChanged;

        var maskLabel = new Label
        {
            Text = "Маска:",
            Location = new Point(15, 85),
            AutoSize = true,
            Font = new Font("Segoe UI", 9)
        };

        _maskTypeCombo = new ComboBox
        {
            Location = new Point(15, 105),
            Size = new Size(290, 30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9)
        };
        _maskTypeCombo.Items.AddRange(Enum.GetNames(typeof(MaskType)));
        _maskTypeCombo.SelectedIndex = 0;
        _maskTypeCombo.SelectedIndexChanged += MaskTypeCombo_SelectedIndexChanged;

        _opacityLabel = new Label
        {
            Text = "Видимость: 100%",
            Location = new Point(15, 145),
            AutoSize = true,
            Font = new Font("Segoe UI", 9)
        };

        _opacityTracker = new TrackBar
        {
            Location = new Point(15, 165),
            Size = new Size(290, 35),
            Minimum = 0,
            Maximum = 100,
            Value = 100,
            TickFrequency = 10
        };
        _opacityTracker.ValueChanged += OpacityTracker_ValueChanged;

        settingsGroup.Controls.Add(blendLabel);
        settingsGroup.Controls.Add(_blendModeCombo);
        settingsGroup.Controls.Add(maskLabel);
        settingsGroup.Controls.Add(_maskTypeCombo);
        settingsGroup.Controls.Add(_opacityLabel);
        settingsGroup.Controls.Add(_opacityTracker);

        //сейв
        _saveButton = new Button
        {
            Text = "Сохранить",
            Location = new Point(15, 645),
            Size = new Size(320, 45),
            Font = new Font("Segoe UI", 11),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _saveButton.Click += SaveButton_Click;

        //добавляем элементы на правую панель
        rightPanel.Controls.Add(titleLabel);
        rightPanel.Controls.Add(_layersList);
        rightPanel.Controls.Add(_addButton);
        rightPanel.Controls.Add(_removeButton);
        rightPanel.Controls.Add(_moveUpButton);
        rightPanel.Controls.Add(_moveDownButton);
        rightPanel.Controls.Add(settingsGroup);
        rightPanel.Controls.Add(_saveButton);

        //добавляем панели на форму
        Controls.Add(_previewBox);
        Controls.Add(rightPanel);
    }

    private void AddButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Выберите изображение",
            Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Все файлы|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            foreach (var file in dialog.FileNames)
            {
                try
                {
                    _layerManager.AddLayer(file);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки {file}: {ex.Message}", "Ошибка", 
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            RefreshLayersList();
        }
    }

    private void RemoveButton_Click(object? sender, EventArgs e)
    {
        if (_layersList.SelectedIndex >= 0)
        {
            _layerManager.RemoveLayer(_layersList.SelectedIndex);
            RefreshLayersList();
        }
    }

    private void MoveUpButton_Click(object? sender, EventArgs e)
    {
        int index = _layersList.SelectedIndex;
        if (index > 0)
        {
            _layerManager.MoveUp(index);
            RefreshLayersList();
            _layersList.SelectedIndex = index - 1;
        }
    }

    private void MoveDownButton_Click(object? sender, EventArgs e)
    {
        int index = _layersList.SelectedIndex;
        if (index >= 0 && index < _layerManager.Layers.Count - 1)
        {
            _layerManager.MoveDown(index);
            RefreshLayersList();
            _layersList.SelectedIndex = index + 1;
        }
    }

    private void LayersList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_layersList.SelectedIndex >= 0 && _layersList.SelectedIndex < _layerManager.Layers.Count)
        {
            var layer = _layerManager.Layers[_layersList.SelectedIndex];
            _blendModeCombo.SelectedIndex = (int)layer.BlendMode;
            _maskTypeCombo.SelectedIndex = (int)layer.MaskType;
            _opacityTracker.Value = (int)(layer.Opacity * 100);
        }
    }

    private void BlendModeCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_layersList.SelectedIndex >= 0 && _layersList.SelectedIndex < _layerManager.Layers.Count)
        {
            var layer = _layerManager.Layers[_layersList.SelectedIndex];
            layer.BlendMode = (BlendMode)_blendModeCombo.SelectedIndex;
            RefreshLayersList();
            UpdatePreview();
        }
    }

    private void MaskTypeCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_layersList.SelectedIndex >= 0 && _layersList.SelectedIndex < _layerManager.Layers.Count)
        {
            var layer = _layerManager.Layers[_layersList.SelectedIndex];
            layer.MaskType = (MaskType)_maskTypeCombo.SelectedIndex;
            UpdatePreview();
        }
    }

    private void OpacityTracker_ValueChanged(object? sender, EventArgs e)
    {
        _opacityLabel.Text = $"Видимость: {_opacityTracker.Value}%";

        if (_layersList.SelectedIndex >= 0 && _layersList.SelectedIndex < _layerManager.Layers.Count)
        {
            var layer = _layerManager.Layers[_layersList.SelectedIndex];
            layer.Opacity = _opacityTracker.Value / 100.0;
            UpdatePreview();
        }
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        if (_compositeResult == null)
        {
            MessageBox.Show("Нет изображения для сохранения!", "Ошибка", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Сохранить результат",
            Filter = "JPEG|*.jpg|PNG|*.png|BMP|*.bmp",
            DefaultExt = "jpg"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            try
            {
                _compositeResult.Save(dialog.FileName);
                MessageBox.Show("Изображение сохранено!", "Успех", 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void RefreshLayersList()
    {
        int selectedIndex = _layersList.SelectedIndex;
        _layersList.Items.Clear();

        // Показываем слои сверху вниз (последний добавленный - сверху)
        for (int i = _layerManager.Layers.Count - 1; i >= 0; i--)
        {
            _layersList.Items.Add(_layerManager.Layers[i].ToString());
        }

        if (selectedIndex >= 0 && selectedIndex < _layersList.Items.Count)
        {
            _layersList.SelectedIndex = selectedIndex;
        }
    }

    private void UpdatePreview()
    {
        _compositeResult?.Dispose();
        _compositeResult = _layerManager.CompositeLayers();
        _previewBox.Image = _compositeResult;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _layerManager.Dispose();
        _compositeResult?.Dispose();
        base.OnFormClosed(e);
    }
}
