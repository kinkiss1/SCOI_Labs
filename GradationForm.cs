using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SCOI_Lab_1;

public class GradationForm : Form
{
    private Bitmap _originalImage;
    private Bitmap _previewImage;
    
    private PictureBox _pbOriginal;
    private PictureBox _pbPreview;
    private PictureBox _pbHistOriginal;
    private PictureBox _pbHistPreview;
    
    // Panel for the interactive chart:
    private PictureBox _pbGraph;
    
    private List<PointF> _controlPoints = new();
    private int _draggedPointIndex = -1;
    
    private byte[] _lut = new byte[256];

    public Bitmap ResultImage => _previewImage;

    public GradationForm(Bitmap original)
    {
        _originalImage = (Bitmap)original.Clone();
        _previewImage = (Bitmap)original.Clone();

        Text = "Градационные преобразования";
        Size = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterParent;

        InitializeComponents();
        InitGraphPoints();
        UpdateLUT();
    }

    private void InitializeComponents()
    {
        TableLayoutPanel pnl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
        };
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        pnl.RowStyles.Add(new RowStyle(SizeType.Percent, 60F)); // Images
        pnl.RowStyles.Add(new RowStyle(SizeType.Percent, 20F)); // Histograms
        pnl.RowStyles.Add(new RowStyle(SizeType.Percent, 20F)); // Graph & Buttons

        Controls.Add(pnl);

        _pbOriginal = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = _originalImage };
        _pbPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = _previewImage };

        _pbHistOriginal = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.StretchImage };
        _pbHistPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.StretchImage };

        UpdateHistograms();

        _pbGraph = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle };
        _pbGraph.Paint += PbGraph_Paint;
        _pbGraph.MouseDown += PbGraph_MouseDown;
        _pbGraph.MouseMove += PbGraph_MouseMove;
        _pbGraph.MouseUp += PbGraph_MouseUp;

        Panel pnlActions = new Panel { Dock = DockStyle.Fill };
        Button btnApply = new Button { Text = "Применить", Location = new Point(10, 10), Size = new Size(150, 40) };
        btnApply.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        
        Button btnReset = new Button { Text = "Сброс", Location = new Point(170, 10), Size = new Size(150, 40) };
        btnReset.Click += (s, e) => { InitGraphPoints(); UpdateLUT(); };
        
        pnlActions.Controls.Add(btnApply);
        pnlActions.Controls.Add(btnReset);

        pnl.Controls.Add(_pbOriginal, 0, 0);
        pnl.Controls.Add(_pbPreview, 1, 0);
        pnl.Controls.Add(_pbHistOriginal, 0, 1);
        pnl.Controls.Add(_pbHistPreview, 1, 1);
        pnl.Controls.Add(_pbGraph, 0, 2);
        pnl.Controls.Add(pnlActions, 1, 2);
    }
    
    private void InitGraphPoints()
    {
        _controlPoints.Clear();
        _controlPoints.Add(new PointF(0, 0));
        _controlPoints.Add(new PointF(255, 255));
    }

    private void PbGraph_Paint(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Draw grid
        using Pen gridPen = new Pen(Color.LightGray);
        for(int i=0; i<=4; i++)
        {
            float x = i * (_pbGraph.Width - 1) / 4f;
            float y = i * (_pbGraph.Height - 1) / 4f;
            g.DrawLine(gridPen, x, 0, x, _pbGraph.Height);
            g.DrawLine(gridPen, 0, y, _pbGraph.Width, y);
        }

        // Draw curve
        using Pen curvePen = new Pen(Color.Blue, 2f);
        List<PointF> screenPts = _controlPoints.Select(p => ToScreen(p)).ToList();
        for (int i = 0; i < screenPts.Count - 1; i++)
        {
            g.DrawLine(curvePen, screenPts[i], screenPts[i + 1]);
        }

        // Draw control points
        foreach (var pt in screenPts)
        {
            g.FillEllipse(Brushes.Red, pt.X - 4, pt.Y - 4, 8, 8);
        }
    }

    private PointF ToScreen(PointF p) => new PointF(
        p.X * (_pbGraph.Width - 1) / 255f, 
        (_pbGraph.Height - 1) - p.Y * (_pbGraph.Height - 1) / 255f);

    private PointF ToWorld(PointF p) => new PointF(
        Math.Clamp(p.X * 255f / (_pbGraph.Width - 1), 0, 255),
        Math.Clamp(255f - (p.Y * 255f / (_pbGraph.Height - 1)), 0, 255));

    private void PbGraph_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;

        PointF worldPt = ToWorld(e.Location);
        
        // Find if we clicked on an existing point
        for (int i = 0; i < _controlPoints.Count; i++)
        {
            PointF scr = ToScreen(_controlPoints[i]);
            if (Math.Abs(scr.X - e.X) < 8 && Math.Abs(scr.Y - e.Y) < 8)
            {
                if (e.Button == MouseButtons.Right && i > 0 && i < _controlPoints.Count - 1)
                {
                    _controlPoints.RemoveAt(i);
                    UpdateLUT();
                }
                else if (e.Button == MouseButtons.Left)
                {
                    _draggedPointIndex = i;
                }
                return;
            }
        }

        if (e.Button == MouseButtons.Left)
        {
            _controlPoints.Add(worldPt);
            _controlPoints = _controlPoints.OrderBy(p => p.X).ToList();
            _draggedPointIndex = _controlPoints.IndexOf(worldPt);
            UpdateLUT();
        }
    }

    private void PbGraph_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedPointIndex == -1) return;

        PointF worldPt = ToWorld(e.Location);
        
        float minX = _draggedPointIndex > 0 ? _controlPoints[_draggedPointIndex - 1].X + 1 : 0;
        float maxX = _draggedPointIndex < _controlPoints.Count - 1 ? _controlPoints[_draggedPointIndex + 1].X - 1 : 255;
        
        if (_draggedPointIndex == 0) minX = maxX = 0;
        if (_draggedPointIndex == _controlPoints.Count - 1) minX = maxX = 255;

        worldPt.X = Math.Clamp(worldPt.X, minX, maxX);
        _controlPoints[_draggedPointIndex] = worldPt;
        
        UpdateLUT();
    }

    private void PbGraph_MouseUp(object sender, MouseEventArgs e)
    {
        _draggedPointIndex = -1;
    }

    private void UpdateLUT()
    {
        _pbGraph.Invalidate();
        for (int i = 0; i < _controlPoints.Count - 1; i++)
        {
            int startX = (int)Math.Round(_controlPoints[i].X);
            int endX = (int)Math.Round(_controlPoints[i + 1].X);
            float startY = _controlPoints[i].Y;
            float endY = _controlPoints[i + 1].Y;

            for (int x = startX; x <= endX && x < 256; x++)
            {
                float t = (float)(x - startX) / Math.Max(1, endX - startX);
                int y = (int)Math.Round(startY + t * (endY - startY));
                _lut[x] = (byte)Math.Clamp(y, 0, 255);
            }
        }

        var oldImg = _pbPreview.Image;
        _pbPreview.Image = GradationHelper.ApplyLUT(_originalImage, _lut);
        _previewImage = (Bitmap)_pbPreview.Image;
        if (oldImg != null && oldImg != _originalImage) oldImg.Dispose();
        
        UpdateHistograms();
    }

    private void UpdateHistograms()
    {
        var oldHo = _pbHistOriginal.Image;
        var oldHp = _pbHistPreview.Image;

        int[] hOriginal = GradationHelper.CalculateHistogram(_originalImage);
        int[] hPreview = GradationHelper.CalculateHistogram(_previewImage);

        _pbHistOriginal.Image = GradationHelper.DrawHistogram(hOriginal, 256, 100);
        _pbHistPreview.Image  = GradationHelper.DrawHistogram(hPreview, 256, 100);

        oldHo?.Dispose();
        oldHp?.Dispose();
    }
}