using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GameRouteOptimizer.App.Controls;

/// <summary>
/// Gráfico de líneas en tiempo real sin dependencias externas: dibuja una serie de
/// muestras (ms o %) con rejilla, escala automática y etiqueta del último valor.
/// Redibuja al cambiar la colección o al cambiar el tamaño.
/// </summary>
public sealed class LatencyChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<double>), typeof(LatencyChart),
        new PropertyMetadata(null, OnSeriesChanged));

    public static readonly DependencyProperty SeriesNameProperty = DependencyProperty.Register(
        nameof(SeriesName), typeof(string), typeof(LatencyChart),
        new PropertyMetadata("Latencia"));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(LatencyChart),
        new PropertyMetadata("ms"));

    private static readonly Brush BackBrush = new SolidColorBrush(Color.FromRgb(0x11, 0x15, 0x20));
    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x25, 0x36));
    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x20, 0x30));
    private static readonly Brush FillBrush = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0x1E, 0x27));
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x95, 0xA5));
    private static readonly Brush AlertBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x17, 0x44));
    private static readonly Brush DotBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

    private readonly Typeface _typeface = new("Segoe UI");

    static LatencyChart()
    {
        BackBrush.Freeze();
        GridBrush.Freeze();
        LineBrush.Freeze();
        FillBrush.Freeze();
        TextBrush.Freeze();
        AlertBrush.Freeze();
        DotBrush.Freeze();
    }

    public IReadOnlyList<double>? Series
    {
        get => (IReadOnlyList<double>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public string SeriesName
    {
        get => (string)GetValue(SeriesNameProperty);
        set => SetValue(SeriesNameProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public LatencyChart()
    {
        SnapsToDevicePixels = true;
    }

    private static void OnSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (LatencyChart)d;
        if (e.OldValue is INotifyCollectionChanged old) old.CollectionChanged -= chart.OnDataChanged;
        if (e.NewValue is INotifyCollectionChanged neu) neu.CollectionChanged += chart.OnDataChanged;
        chart.InvalidateVisual();
    }

    private void OnDataChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.Invoke(InvalidateVisual);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 10 || h < 10) return;

        dc.DrawRoundedRectangle(BackBrush, new Pen(GridBrush, 1), new Rect(0, 0, w, h), 6, 6);

        const double padLeft = 46;
        const double padTop = 24;
        const double padRight = 10;
        const double padBottom = 8;
        var plotW = Math.Max(1, w - padLeft - padRight);
        var plotH = Math.Max(1, h - padTop - padBottom);

        var data = Series;
        if (data is null || data.Count == 0)
        {
            DrawText(dc, SeriesName + " — sin datos aún", padLeft + 8, padTop + 8, TextBrush, 12);
            return;
        }

        var max = Math.Max(1, data.Max());
        var min = Math.Min(0, data.Min());
        var range = Math.Max(1, max - min);

        // Rejilla horizontal con etiquetas.
        for (var i = 0; i <= 4; i++)
        {
            var y = padTop + plotH * i / 4.0;
            dc.DrawLine(new Pen(GridBrush, 1), new Point(padLeft, y), new Point(w - padRight, y));
            var value = max - range * i / 4.0;
            DrawText(dc, $"{value:0}{Unit}", 4, y - 7, TextBrush, 10);
        }

        // Mapear puntos.
        var count = data.Count;
        var stepX = count > 1 ? plotW / (count - 1) : plotW;
        var points = new Point[count];
        for (var i = 0; i < count; i++)
        {
            var x = padLeft + stepX * i;
            var y = padTop + plotH - ((data[i] - min) / range) * plotH;
            points[i] = new Point(x, Math.Clamp(y, padTop, padTop + plotH));
        }

        var baseline = padTop + plotH;

        // Relleno bajo la curva.
        var fill = new StreamGeometry();
        using (var ctx = fill.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, baseline), true, true);
            foreach (var p in points) ctx.LineTo(p, true, false);
            ctx.LineTo(new Point(points[count - 1].X, baseline), true, false);
            ctx.LineTo(new Point(points[0].X, baseline), true, false);
        }
        dc.DrawGeometry(FillBrush, null, fill);

        // Línea principal.
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            for (var i = 1; i < count; i++) ctx.LineTo(points[i], true, false);
        }
        dc.DrawGeometry(null, new Pen(LineBrush, 1.8), line);

        // Punto final + etiqueta del último valor.
        var last = points[count - 1];
        var lastValue = data[count - 1];
        dc.DrawEllipse(LineBrush, new Pen(DotBrush, 1), last, 3.2, 3.2);
        var labelBrush = lastValue > max * 0.85 ? AlertBrush : LineBrush;
        DrawText(dc, $"{lastValue:0}{Unit}", Math.Min(last.X + 6, w - 50), Math.Max(4, last.Y - 18), labelBrush, 11);

        // Título.
        DrawText(dc, $"{SeriesName} (últimos {count})", padLeft + 6, 5, TextBrush, 11);
    }

    private void DrawText(DrawingContext dc, string text, double x, double y, Brush brush, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            _typeface, size, brush, 1.25);
        dc.DrawText(formatted, new Point(x, y));
    }
}
