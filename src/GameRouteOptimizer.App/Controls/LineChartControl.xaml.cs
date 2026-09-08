using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GameRouteOptimizer.App.Controls;

/// <summary>
/// Gráfico de líneas ligero y sin dependencias externas: serie de valores (ms/%),
/// autoejecución vertical y etiquetas mínimas. Actualizable en caliente.
/// </summary>
public partial class LineChartControl : UserControl
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double?>), typeof(LineChartControl),
        new PropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(LineChartControl),
        new PropertyMetadata(Brushes.SteelBlue, OnVisualChanged));

    public static readonly DependencyProperty MaxPointsProperty = DependencyProperty.Register(
        nameof(MaxPoints), typeof(int), typeof(LineChartControl),
        new PropertyMetadata(120, OnVisualChanged));

    public static readonly DependencyProperty UnitLabelProperty = DependencyProperty.Register(
        nameof(UnitLabel), typeof(string), typeof(LineChartControl),
        new PropertyMetadata("ms", OnVisualChanged));

    private bool _invalidatePending;
    private INotifyCollectionChanged? _valuesCollection;

    public LineChartControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
        DataContextChanged += (_, _) => Redraw();
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LineChartControl)d;
        if (control._valuesCollection is { } previous)
        {
            previous.CollectionChanged -= control.OnValuesCollectionChanged;
        }

        control._valuesCollection = e.NewValue as INotifyCollectionChanged;
        if (control._valuesCollection is { } next)
        {
            next.CollectionChanged += control.OnValuesCollectionChanged;
        }

        control.ScheduleRedraw();
    }

    private void OnValuesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRedraw();

    public IReadOnlyList<double?>? Values
    {
        get => (IReadOnlyList<double?>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public int MaxPoints
    {
        get => (int)GetValue(MaxPointsProperty);
        set => SetValue(MaxPointsProperty, value);
    }

    public string UnitLabel
    {
        get => (string)GetValue(UnitLabelProperty);
        set => SetValue(UnitLabelProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LineChartControl)d).Redraw();

    private void ScheduleRedraw()
    {
        // Evita redibujar muchas veces en el mismo frame (series en vivo).
        if (_invalidatePending)
        {
            return;
        }

        _invalidatePending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _invalidatePending = false;
            Redraw();
        }));
    }

    private void Redraw()
    {
        var canvas = Plot;
        if (canvas is null || ActualWidth < 10 || ActualHeight < 10)
        {
            return;
        }

        canvas.Children.Clear();

        var values = Values?.Where(v => v.HasValue).Select(v => v!.Value).ToList() ?? new List<double>();
        var width = ActualWidth;
        var height = ActualHeight;

        // Rejilla horizontal (colores del tema cuando existen; valores oscuros por defecto).
        var gridPen = new Pen(ThemeBrush("BorderBrush2", Color.FromRgb(0x2C, 0x36, 0x44)), 1);
        var axisBrush = ThemeBrush("MutedTextBrush", Color.FromRgb(0x8F, 0xA0, 0xB0));
        const int gridLines = 4;
        for (var i = 0; i <= gridLines; i++)
        {
            var y = height - 6 - (height - 20) * i / gridLines;
            canvas.Children.Add(new Line { X1 = 0, X2 = width - 34, Y1 = y, Y2 = y, Stroke = gridPen.Brush, StrokeThickness = 0.6 });
            var label = new TextBlock
            {
                Text = FormatAxisValue(MaxValue * i / gridLines),
                Foreground = axisBrush,
                FontSize = 10,
            };
            Canvas.SetLeft(label, width - 32);
            Canvas.SetTop(label, y - 7);
            canvas.Children.Add(label);
        }

        if (values.Count < 2)
        {
            var empty = new TextBlock
            {
                Text = "acumulando muestras…",
                Foreground = axisBrush,
                FontSize = 11,
            };
            Canvas.SetLeft(empty, 8);
            Canvas.SetTop(empty, 8);
            canvas.Children.Add(empty);
            return;
        }

        // Escala.
        var plotWidth = width - 40;
        var plotHeight = height - 16;
        var max = MaxValue;
        if (max <= 0)
        {
            max = 1;
        }

        var effectiveMax = Math.Max(10, max * 1.15);
        var stepX = plotWidth / Math.Max(1, values.Count - 1);

        // Submuestreo: si hay más muestras que píxeles de ancho, se dibuja una por píxel
        // (la ventana se mueve igual: la serie completa se comprime con el mismo paso X).
        var stride = Math.Max(1, (int)Math.Ceiling(values.Count / Math.Max(1.0, plotWidth)));
        var pointCount = 1 + (values.Count - 1) / stride;
        var points = new List<Point>(pointCount);
        for (var i = 0; i < values.Count; i += stride)
        {
            var x = i * stepX;
            var y = plotHeight - 4 - (plotHeight - 14) * Math.Min(1, values[i] / effectiveMax);
            points.Add(new Point(x, y));
        }

        var poly = new Polyline
        {
            Points = new PointCollection(points),
            Stroke = Stroke,
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round,
        };
        canvas.Children.Add(poly);

        // Última muestra destacada.
        if (points.Count > 0)
        {
            var last = points[^1];
            canvas.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = Stroke,
                Margin = new Thickness(0),
            }.ApplyPosition(last.X - 3, last.Y - 3));
        }
    }

    private double MaxValue
    {
        get
        {
            var values = Values;
            if (values is null)
            {
                return 0;
            }

            var max = 0d;
            foreach (var v in values)
            {
                if (v.HasValue && v.Value > max)
                {
                    max = v.Value;
                }
            }

            return max;
        }
    }

    private string FormatAxisValue(double value) => value switch
    {
        >= 1000 => $"{value / 1000:F0}k",
        >= 100 => $"{value:F0}",
        >= 10 => $"{value:F0}",
        _ => $"{value:F1}",
    };

    /// <summary>
    /// Pincel del tema actual (recurso XAML) con respaldo oscuro si aún no hay tema cargado.
    /// Se consulta en cada redibujo para reflejar cambios de tema sin reiniciar.
    /// </summary>
    private Brush ThemeBrush(string resourceKey, Color fallback) =>
        TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallback);
}

internal static class ShapePositionExtensions
{
    public static T ApplyPosition<T>(this T shape, double x, double y) where T : FrameworkElement
    {
        Canvas.SetLeft(shape, x);
        Canvas.SetTop(shape, y);
        return shape;
    }
}
