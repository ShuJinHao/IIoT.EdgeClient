using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public enum EdgeChartSeriesKind
{
    Bar,
    Line
}

public enum EdgeChartAxis
{
    Primary,
    Secondary
}

public sealed class EdgeChartSeries
{
    public string Key { get; set; } = string.Empty;

    public object? Title { get; set; }

    public EdgeChartSeriesKind Kind { get; set; } = EdgeChartSeriesKind.Bar;

    public EdgeChartAxis Axis { get; set; } = EdgeChartAxis.Primary;

    public IBrush? Brush { get; set; }

    public string? TooltipValueFormat { get; set; }
}

public sealed class EdgeChartPoint
{
    public object? Label { get; set; }

    public object? TooltipLabel { get; set; }

    public IDictionary<string, double> Values { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);

    public double GetValue(string key)
    {
        return Values.TryGetValue(key, out var value) ? value : 0;
    }
}

public class EdgeBarLineChart : TemplatedControl
{
    private INotifyCollectionChanged? _itemsSourceNotifications;
    private INotifyCollectionChanged? _seriesNotifications;
    private int? _hoveredPointIndex;

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<EdgeBarLineChart, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<IEnumerable?> SeriesProperty =
        AvaloniaProperty.Register<EdgeBarLineChart, IEnumerable?>(nameof(Series));

    public static readonly StyledProperty<object?> EmptyContentProperty =
        AvaloniaProperty.Register<EdgeBarLineChart, object?>(nameof(EmptyContent));

    public static readonly StyledProperty<double> PrimaryAxisMaximumProperty =
        AvaloniaProperty.Register<EdgeBarLineChart, double>(nameof(PrimaryAxisMaximum));

    public static readonly StyledProperty<double> SecondaryAxisMaximumProperty =
        AvaloniaProperty.Register<EdgeBarLineChart, double>(nameof(SecondaryAxisMaximum));

    public static readonly StyledProperty<string?> PrimaryValueFormatProperty =
        AvaloniaProperty.Register<EdgeBarLineChart, string?>(nameof(PrimaryValueFormat));

    public static readonly StyledProperty<string?> SecondaryValueFormatProperty =
        AvaloniaProperty.Register<EdgeBarLineChart, string?>(nameof(SecondaryValueFormat));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<EdgeBarLineChart, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> GridLineBrushProperty =
        AvaloniaProperty.Register<EdgeBarLineChart, IBrush?>(nameof(GridLineBrush));

    public static readonly StyledProperty<IBrush?> AxisTextBrushProperty =
        AvaloniaProperty.Register<EdgeBarLineChart, IBrush?>(nameof(AxisTextBrush));

    public static readonly StyledProperty<IBrush?> LegendTextBrushProperty =
        AvaloniaProperty.Register<EdgeBarLineChart, IBrush?>(nameof(LegendTextBrush));

    public static readonly StyledProperty<IBrush?> DefaultBarBrushProperty =
        AvaloniaProperty.Register<EdgeBarLineChart, IBrush?>(nameof(DefaultBarBrush));

    public static readonly StyledProperty<IBrush?> DefaultLineBrushProperty =
        AvaloniaProperty.Register<EdgeBarLineChart, IBrush?>(nameof(DefaultLineBrush));

    static EdgeBarLineChart()
    {
        ItemsSourceProperty.Changed.AddClassHandler<EdgeBarLineChart>((chart, args) => chart.OnItemsSourceChanged(args.GetNewValue<IEnumerable?>()));
        SeriesProperty.Changed.AddClassHandler<EdgeBarLineChart>((chart, args) => chart.OnSeriesChanged(args.GetNewValue<IEnumerable?>()));
        PrimaryAxisMaximumProperty.Changed.AddClassHandler<EdgeBarLineChart>((chart, _) => chart.InvalidateVisual());
        SecondaryAxisMaximumProperty.Changed.AddClassHandler<EdgeBarLineChart>((chart, _) => chart.InvalidateVisual());
        PrimaryValueFormatProperty.Changed.AddClassHandler<EdgeBarLineChart>((chart, _) => chart.InvalidateVisual());
        SecondaryValueFormatProperty.Changed.AddClassHandler<EdgeBarLineChart>((chart, _) => chart.InvalidateVisual());
        TrackBrushProperty.Changed.AddClassHandler<EdgeBarLineChart>((chart, _) => chart.InvalidateVisual());
        GridLineBrushProperty.Changed.AddClassHandler<EdgeBarLineChart>((chart, _) => chart.InvalidateVisual());
        AxisTextBrushProperty.Changed.AddClassHandler<EdgeBarLineChart>((chart, _) => chart.InvalidateVisual());
        LegendTextBrushProperty.Changed.AddClassHandler<EdgeBarLineChart>((chart, _) => chart.InvalidateVisual());
        DefaultBarBrushProperty.Changed.AddClassHandler<EdgeBarLineChart>((chart, _) => chart.InvalidateVisual());
        DefaultLineBrushProperty.Changed.AddClassHandler<EdgeBarLineChart>((chart, _) => chart.InvalidateVisual());
    }

    public EdgeBarLineChart()
    {
        PointerMoved += OnPointerMoved;
        PointerExited += OnPointerExited;
        RefreshChart();
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IEnumerable? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public object? EmptyContent
    {
        get => GetValue(EmptyContentProperty);
        set => SetValue(EmptyContentProperty, value);
    }

    public double PrimaryAxisMaximum
    {
        get => GetValue(PrimaryAxisMaximumProperty);
        set => SetValue(PrimaryAxisMaximumProperty, value);
    }

    public double SecondaryAxisMaximum
    {
        get => GetValue(SecondaryAxisMaximumProperty);
        set => SetValue(SecondaryAxisMaximumProperty, value);
    }

    public string? PrimaryValueFormat
    {
        get => GetValue(PrimaryValueFormatProperty);
        set => SetValue(PrimaryValueFormatProperty, value);
    }

    public string? SecondaryValueFormat
    {
        get => GetValue(SecondaryValueFormatProperty);
        set => SetValue(SecondaryValueFormatProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? GridLineBrush
    {
        get => GetValue(GridLineBrushProperty);
        set => SetValue(GridLineBrushProperty, value);
    }

    public IBrush? AxisTextBrush
    {
        get => GetValue(AxisTextBrushProperty);
        set => SetValue(AxisTextBrushProperty, value);
    }

    public IBrush? LegendTextBrush
    {
        get => GetValue(LegendTextBrushProperty);
        set => SetValue(LegendTextBrushProperty, value);
    }

    public IBrush? DefaultBarBrush
    {
        get => GetValue(DefaultBarBrushProperty);
        set => SetValue(DefaultBarBrushProperty, value);
    }

    public IBrush? DefaultLineBrush
    {
        get => GetValue(DefaultLineBrushProperty);
        set => SetValue(DefaultLineBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var points = GetPoints();
        var series = GetSeries();
        if (points.Count == 0 || series.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var plot = CreatePlotRect();
        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        DrawGrid(context, plot);
        DrawHoverGuide(context, plot, points);
        DrawSeries(context, plot, points, series);
        DrawAxes(context, plot, points, series);
        DrawLegend(context, plot, series);
    }

    private void RefreshChart()
    {
        ClearHover();
        UpdatePseudoClasses();
        InvalidateVisual();
    }

    private void OnItemsSourceChanged(IEnumerable? itemsSource)
    {
        UpdateCollectionSubscription(ref _itemsSourceNotifications, itemsSource);
        RefreshChart();
    }

    private void OnSeriesChanged(IEnumerable? series)
    {
        UpdateCollectionSubscription(ref _seriesNotifications, series);
        RefreshChart();
    }

    private void UpdateCollectionSubscription(ref INotifyCollectionChanged? current, IEnumerable? value)
    {
        if (current is not null)
        {
            current.CollectionChanged -= OnCollectionChanged;
            current = null;
        }

        if (value is INotifyCollectionChanged next)
        {
            current = next;
            current.CollectionChanged += OnCollectionChanged;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshChart();

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var points = GetPoints();
        var series = GetSeries();
        if (points.Count == 0 || series.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            ClearHover();
            return;
        }

        var plot = CreatePlotRect();
        if (plot.Width <= 0 || plot.Height <= 0)
        {
            ClearHover();
            return;
        }

        var pointer = e.GetPosition(this);
        var hoverBounds = new Rect(plot.Left - 10, plot.Top - 18, plot.Width + 20, plot.Height + 36);
        if (!hoverBounds.Contains(pointer))
        {
            ClearHover();
            return;
        }

        var groupWidth = plot.Width / points.Count;
        var index = (int)Math.Clamp(
            Math.Floor((pointer.X - plot.Left) / groupWidth),
            0,
            points.Count - 1);

        if (_hoveredPointIndex == index)
        {
            return;
        }

        _hoveredPointIndex = index;
        ToolTip.SetTip(this, BuildTooltip(points[index], series));
        InvalidateVisual();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
        => ClearHover();

    private void ClearHover()
    {
        if (_hoveredPointIndex is null && ToolTip.GetTip(this) is null)
        {
            return;
        }

        _hoveredPointIndex = null;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    private void UpdatePseudoClasses()
    {
        SetPseudoClass(":empty", GetPoints().Count == 0 || GetSeries().Count == 0);
    }

    private Rect CreatePlotRect()
    {
        var legendHeight = 30d;
        return new Rect(48, 18, Math.Max(0, Bounds.Width - 96), Math.Max(0, Bounds.Height - 72 - legendHeight));
    }

    private void DrawGrid(DrawingContext context, Rect plot)
    {
        var gridBrush = GridLineBrush;
        var trackBrush = TrackBrush;
        if (trackBrush is not null)
        {
            context.DrawRectangle(trackBrush, null, plot, 10, 10);
        }

        if (gridBrush is null)
        {
            return;
        }

        var pen = new Pen(gridBrush, 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4;
            context.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
    }

    private void DrawSeries(
        DrawingContext context,
        Rect plot,
        IReadOnlyList<EdgeChartPoint> points,
        IReadOnlyList<EdgeChartSeries> series)
    {
        var barSeries = series.Where(item => item.Kind == EdgeChartSeriesKind.Bar).ToList();
        var lineSeries = series.Where(item => item.Kind == EdgeChartSeriesKind.Line).ToList();
        var groupWidth = plot.Width / points.Count;
        var barWidth = barSeries.Count == 0
            ? 0
            : Math.Clamp(groupWidth / Math.Max(1, barSeries.Count + 1) * 0.7, 6, 22);

        var primaryMax = ResolveAxisMaximum(points, series, EdgeChartAxis.Primary, PrimaryAxisMaximum);
        var secondaryMax = ResolveAxisMaximum(points, series, EdgeChartAxis.Secondary, SecondaryAxisMaximum);

        for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
        {
            var centerX = plot.Left + groupWidth * pointIndex + groupWidth / 2;
            for (var seriesIndex = 0; seriesIndex < barSeries.Count; seriesIndex++)
            {
                var item = barSeries[seriesIndex];
                var value = Math.Max(0, points[pointIndex].GetValue(item.Key));
                var axisMax = item.Axis == EdgeChartAxis.Primary ? primaryMax : secondaryMax;
                var height = axisMax <= 0 ? 0 : plot.Height * Math.Clamp(value / axisMax, 0, 1);
                var x = centerX - barWidth * barSeries.Count / 2 + barWidth * seriesIndex;
                var rect = new Rect(x, plot.Bottom - height, barWidth * 0.82, height);
                context.DrawRectangle(item.Brush ?? DefaultBarBrush, null, rect, 4, 4);
            }
        }

        foreach (var item in lineSeries)
        {
            DrawLineSeries(context, plot, points, item, groupWidth, primaryMax, secondaryMax);
        }
    }

    private void DrawHoverGuide(DrawingContext context, Rect plot, IReadOnlyList<EdgeChartPoint> points)
    {
        if (_hoveredPointIndex is not { } index || index < 0 || index >= points.Count)
        {
            return;
        }

        var guideBrush = GridLineBrush;
        if (guideBrush is null)
        {
            return;
        }

        var groupWidth = plot.Width / points.Count;
        var x = plot.Left + groupWidth * index + groupWidth / 2;
        var pen = new Pen(guideBrush, 1.4);
        context.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
    }

    private void DrawLineSeries(
        DrawingContext context,
        Rect plot,
        IReadOnlyList<EdgeChartPoint> points,
        EdgeChartSeries series,
        double groupWidth,
        double primaryMax,
        double secondaryMax)
    {
        var brush = series.Brush ?? DefaultLineBrush;
        if (brush is null)
        {
            return;
        }

        var pen = new Pen(brush, 2);
        Point? previous = null;
        for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
        {
            var value = Math.Max(0, points[pointIndex].GetValue(series.Key));
            var axisMax = series.Axis == EdgeChartAxis.Primary ? primaryMax : secondaryMax;
            var y = axisMax <= 0 ? plot.Bottom : plot.Bottom - plot.Height * Math.Clamp(value / axisMax, 0, 1);
            var point = new Point(plot.Left + groupWidth * pointIndex + groupWidth / 2, y);

            if (previous is not null)
            {
                context.DrawLine(pen, previous.Value, point);
            }

            context.DrawEllipse(brush, null, point, 3.5, 3.5);
            previous = point;
        }
    }

    private void DrawAxes(
        DrawingContext context,
        Rect plot,
        IReadOnlyList<EdgeChartPoint> points,
        IReadOnlyList<EdgeChartSeries> series)
    {
        var textBrush = AxisTextBrush;
        if (textBrush is null)
        {
            return;
        }

        var primaryMax = ResolveAxisMaximum(points, series, EdgeChartAxis.Primary, PrimaryAxisMaximum);
        var secondaryMax = ResolveAxisMaximum(points, series, EdgeChartAxis.Secondary, SecondaryAxisMaximum);
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4 - 7;
            DrawText(context, FormatValue(primaryMax * i / 4, PrimaryValueFormat), new Point(0, y), textBrush, 11);
            DrawText(context, FormatValue(secondaryMax * i / 4, SecondaryValueFormat), new Point(plot.Right + 10, y), textBrush, 11);
        }

        var groupWidth = plot.Width / points.Count;
        for (var i = 0; i < points.Count; i++)
        {
            var label = Convert.ToString(points[i].Label, CultureInfo.CurrentCulture);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            DrawText(context, label, new Point(plot.Left + groupWidth * i + 4, plot.Bottom + 10), textBrush, 11);
        }
    }

    private void DrawLegend(DrawingContext context, Rect plot, IReadOnlyList<EdgeChartSeries> series)
    {
        var textBrush = LegendTextBrush;
        if (textBrush is null)
        {
            return;
        }

        var x = plot.Left;
        var y = plot.Bottom + 38;
        foreach (var item in series)
        {
            var brush = item.Brush ?? (item.Kind == EdgeChartSeriesKind.Line ? DefaultLineBrush : DefaultBarBrush);
            if (brush is null)
            {
                continue;
            }

            var title = Convert.ToString(item.Title, CultureInfo.CurrentCulture);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = item.Key;
            }

            context.DrawRectangle(brush, null, new Rect(x, y + 3, 10, 10), 3, 3);
            DrawText(context, title, new Point(x + 16, y), textBrush, 12);
            x += Math.Max(76, title.Length * 9 + 30);
        }
    }

    private static double ResolveAxisMaximum(
        IReadOnlyList<EdgeChartPoint> points,
        IReadOnlyList<EdgeChartSeries> series,
        EdgeChartAxis axis,
        double configuredMaximum)
    {
        if (configuredMaximum > 0)
        {
            return configuredMaximum;
        }

        var maximum = 0d;
        foreach (var point in points)
        {
            foreach (var item in series.Where(item => item.Axis == axis))
            {
                maximum = Math.Max(maximum, point.GetValue(item.Key));
            }
        }

        return maximum <= 0 ? 1 : maximum * 1.12;
    }

    private IReadOnlyList<EdgeChartPoint> GetPoints()
    {
        return ItemsSource?.OfType<EdgeChartPoint>().ToList() ?? [];
    }

    private IReadOnlyList<EdgeChartSeries> GetSeries()
    {
        return Series?.OfType<EdgeChartSeries>().Where(item => !string.IsNullOrWhiteSpace(item.Key)).ToList() ?? [];
    }

    private static string FormatValue(double value, string? format)
    {
        return string.IsNullOrWhiteSpace(format)
            ? value.ToString("0.#", CultureInfo.CurrentCulture)
            : value.ToString(format, CultureInfo.CurrentCulture);
    }

    private static string BuildTooltip(EdgeChartPoint point, IReadOnlyList<EdgeChartSeries> series)
    {
        var title = Convert.ToString(point.TooltipLabel, CultureInfo.CurrentCulture);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Convert.ToString(point.Label, CultureInfo.CurrentCulture);
        }

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(title))
        {
            lines.Add(title);
        }

        foreach (var item in series)
        {
            var label = Convert.ToString(item.Title, CultureInfo.CurrentCulture);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = item.Key;
            }

            var value = FormatValue(point.GetValue(item.Key), item.TooltipValueFormat);
            lines.Add($"{label}: {value}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void DrawText(DrawingContext context, string text, Point origin, IBrush brush, double size)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush);

        context.DrawText(formatted, origin);
    }

    private void SetPseudoClass(string name, bool enabled)
    {
        if (enabled)
        {
            PseudoClasses.Add(name);
        }
        else
        {
            PseudoClasses.Remove(name);
        }
    }
}
