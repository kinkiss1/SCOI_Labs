using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace SCOI_Lab_1;

public sealed class GradationCurveEditor : Control
{
    private readonly List<Point> _controlPoints = new();
    private int _draggedPointIndex = -1;

    public event EventHandler? PointsChanged;

    public GradationCurveEditor()
    {
        Focusable = true;
    }

    public IReadOnlyList<Point> GetControlPoints()
    {
        return _controlPoints.ToArray();
    }

    public void SetControlPoints(IEnumerable<Point> points)
    {
        _controlPoints.Clear();
        _controlPoints.AddRange(points.OrderBy(point => point.X));
        RaisePointsChanged();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Math.Max(Bounds.Width - 1, 1);
        double height = Math.Max(Bounds.Height - 1, 1);
        Pen gridPen = new(new SolidColorBrush(Color.Parse("#D1D5DB")));
        Pen curvePen = new(Brushes.DodgerBlue, 2);

        for (int i = 0; i <= 4; i++)
        {
            double x = i * width / 4d;
            double y = i * height / 4d;
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, height));
            context.DrawLine(gridPen, new Point(0, y), new Point(width, y));
        }

        if (_controlPoints.Count < 2)
        {
            return;
        }

        List<Point> screenPoints = _controlPoints.Select(ToScreen).ToList();
        for (int i = 0; i < screenPoints.Count - 1; i++)
        {
            context.DrawLine(curvePen, screenPoints[i], screenPoints[i + 1]);
        }

        foreach (Point point in screenPoints)
        {
            context.DrawEllipse(Brushes.IndianRed, null, point, 5, 5);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        bool isLeftButton = properties.IsLeftButtonPressed;
        bool isRightButton = properties.IsRightButtonPressed;
        if (!isLeftButton && !isRightButton)
        {
            return;
        }

        Point position = e.GetPosition(this);
        Point worldPoint = ToWorld(position);

        for (int i = 0; i < _controlPoints.Count; i++)
        {
            Point screenPoint = ToScreen(_controlPoints[i]);
            if (Distance(screenPoint, position) >= 8)
            {
                continue;
            }

            if (isRightButton && i > 0 && i < _controlPoints.Count - 1)
            {
                _controlPoints.RemoveAt(i);
                RaisePointsChanged();
                return;
            }

            if (isLeftButton)
            {
                _draggedPointIndex = i;
                e.Pointer.Capture(this);
                return;
            }
        }

        if (!isLeftButton)
        {
            return;
        }

        _controlPoints.Add(worldPoint);
        _controlPoints.Sort((left, right) => left.X.CompareTo(right.X));
        _draggedPointIndex = _controlPoints.FindIndex(point => AreClose(point, worldPoint));
        e.Pointer.Capture(this);
        RaisePointsChanged();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggedPointIndex == -1)
        {
            return;
        }

        Point worldPoint = ToWorld(e.GetPosition(this));
        double minX = _draggedPointIndex > 0 ? _controlPoints[_draggedPointIndex - 1].X + 1 : 0;
        double maxX = _draggedPointIndex < _controlPoints.Count - 1
            ? _controlPoints[_draggedPointIndex + 1].X - 1
            : 255;

        if (_draggedPointIndex == 0)
        {
            minX = maxX = 0;
        }

        if (_draggedPointIndex == _controlPoints.Count - 1)
        {
            minX = maxX = 255;
        }

        _controlPoints[_draggedPointIndex] = new Point(
            Math.Clamp(worldPoint.X, minX, maxX),
            Math.Clamp(worldPoint.Y, 0, 255));

        RaisePointsChanged();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _draggedPointIndex = -1;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _draggedPointIndex = -1;
    }

    private void RaisePointsChanged()
    {
        InvalidateVisual();
        PointsChanged?.Invoke(this, EventArgs.Empty);
    }

    private Point ToScreen(Point worldPoint)
    {
        double width = Math.Max(Bounds.Width - 1, 1);
        double height = Math.Max(Bounds.Height - 1, 1);

        return new Point(
            worldPoint.X * width / 255d,
            height - (worldPoint.Y * height / 255d));
    }

    private Point ToWorld(Point screenPoint)
    {
        double width = Math.Max(Bounds.Width - 1, 1);
        double height = Math.Max(Bounds.Height - 1, 1);

        return new Point(
            Math.Clamp(screenPoint.X * 255d / width, 0, 255),
            Math.Clamp(255d - (screenPoint.Y * 255d / height), 0, 255));
    }

    private static double Distance(Point left, Point right)
    {
        double dx = left.X - right.X;
        double dy = left.Y - right.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static bool AreClose(Point left, Point right)
    {
        return Math.Abs(left.X - right.X) < 0.001 && Math.Abs(left.Y - right.Y) < 0.001;
    }
}
