using System.Windows;
using System.Windows.Media;
using UsageTrackerWidget.App.Storage;

namespace UsageTrackerWidget.App.Views;

public sealed class HistorySparkline : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(IReadOnlyList<UsageHistoryPoint>),
        typeof(HistorySparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush),
        typeof(Brush),
        typeof(HistorySparkline),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ResetBrushProperty = DependencyProperty.Register(
        nameof(ResetBrush),
        typeof(Brush),
        typeof(HistorySparkline),
        new FrameworkPropertyMetadata(Brushes.Goldenrod, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GuideBrushProperty = DependencyProperty.Register(
        nameof(GuideBrush),
        typeof(Brush),
        typeof(HistorySparkline),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<UsageHistoryPoint>? Points
    {
        get => (IReadOnlyList<UsageHistoryPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush ResetBrush
    {
        get => (Brush)GetValue(ResetBrushProperty);
        set => SetValue(ResetBrushProperty, value);
    }

    public Brush GuideBrush
    {
        get => (Brush)GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 2 || height <= 2)
        {
            return;
        }

        var guidePen = CreatePen(GuideBrush, 1);
        drawingContext.DrawLine(
            guidePen,
            new Point(0, height - 1),
            new Point(width, height - 1));

        var points = Points;
        if (points is null || points.Count == 0)
        {
            return;
        }

        var ordered = points.OrderBy(point => point.ObservedAt).ToList();
        var minimumTime = ordered[0].ObservedAt;
        var maximumTime = ordered[^1].ObservedAt;
        var spanSeconds = Math.Max(1, (maximumTime - minimumTime).TotalSeconds);
        const double verticalPadding = 3;
        var plotHeight = Math.Max(1, height - verticalPadding * 2);

        var coordinates = ordered
            .Select(point =>
            {
                var x = width * ((point.ObservedAt - minimumTime).TotalSeconds / spanSeconds);
                var normalized = Math.Clamp(point.UsedPercent, 0, 100) / 100;
                var y = verticalPadding + plotHeight * (1 - normalized);
                return new Point(x, y);
            })
            .ToList();

        if (coordinates.Count == 1)
        {
            drawingContext.DrawEllipse(LineBrush, null, coordinates[0], 2.5, 2.5);
            return;
        }

        var area = new StreamGeometry();
        using (var areaContext = area.Open())
        {
            areaContext.BeginFigure(new Point(coordinates[0].X, height - 1), isFilled: true, isClosed: true);
            foreach (var point in coordinates)
            {
                areaContext.LineTo(point, isStroked: false, isSmoothJoin: false);
            }

            areaContext.LineTo(new Point(coordinates[^1].X, height - 1), isStroked: false, isSmoothJoin: false);
        }

        area.Freeze();
        var areaBrush = CreateAreaBrush(LineBrush);
        drawingContext.DrawGeometry(areaBrush, null, area);

        var line = new StreamGeometry();
        using (var lineContext = line.Open())
        {
            lineContext.BeginFigure(coordinates[0], isFilled: false, isClosed: false);
            foreach (var point in coordinates.Skip(1))
            {
                lineContext.LineTo(point, isStroked: true, isSmoothJoin: true);
            }
        }

        line.Freeze();
        drawingContext.DrawGeometry(null, CreatePen(LineBrush, 1.6), line);

        var resetPen = CreatePen(ResetBrush, 1);
        foreach (var point in ordered.Where(point => point.IsReset))
        {
            var x = width * ((point.ObservedAt - minimumTime).TotalSeconds / spanSeconds);
            drawingContext.DrawLine(resetPen, new Point(x, 1), new Point(x, height - 1));
        }

        drawingContext.DrawEllipse(LineBrush, null, coordinates[^1], 2.8, 2.8);
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static Brush CreateAreaBrush(Brush source)
    {
        if (source is not SolidColorBrush solid)
        {
            return new SolidColorBrush(Color.FromArgb(28, 91, 157, 255));
        }

        var color = solid.Color;
        var brush = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
