using System.Globalization;
using System.Windows.Data;

namespace GameRouteOptimizer.App;

/// <summary>Lista de puertos (List&lt;int&gt;) ↔ texto separado por comas.</summary>
public sealed class PortsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable<int> ports)
        {
            return string.Join(", ", ports.Select(p => p.ToString(CultureInfo.InvariantCulture)));
        }

        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        var ports = new List<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) &&
                port is > 0 and <= 65535 && !ports.Contains(port))
            {
                ports.Add(port);
            }
        }

        return ports;
    }
}

/// <summary>bool → visibilidad (para advertencias).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is System.Windows.Visibility.Visible;
}

/// <summary>bool → visibilidad invertida (visible cuando false).</summary>
public sealed class InvertBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not System.Windows.Visibility.Visible;
}

/// <summary>RouteMode → índice de ComboBox (0=directa, 1=global, 2=solo juego).</summary>
public sealed class RouteModeToIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            GameRouteOptimizer.Core.Models.RouteMode.TunnelGlobal => 1,
            GameRouteOptimizer.Core.Models.RouteMode.TunnelGameDestinations => 2,
            _ => 0,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            1 => GameRouteOptimizer.Core.Models.RouteMode.TunnelGlobal,
            2 => GameRouteOptimizer.Core.Models.RouteMode.TunnelGameDestinations,
            _ => GameRouteOptimizer.Core.Models.RouteMode.Direct,
        };
}

/// <summary>ms (double?) → texto "12,3 ms" o "—".</summary>
public sealed class MsDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ms = value as double?;
        return ms is { } v && v > 0
            ? string.Format(CultureInfo.CurrentCulture, "{0:F1} ms", v)
            : "—";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>% (double?) → texto "5,0 %" o "—".</summary>
public sealed class PercentDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var pct = value as double?;
        return pct is { } v
            ? string.Format(CultureInfo.CurrentCulture, "{0:F1} %", v)
            : "—";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
