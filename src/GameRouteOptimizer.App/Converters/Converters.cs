using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GameRouteOptimizer.App.Converters;

/// <summary>bool → Visibility (true = Visible).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>bool → Visibility invertido.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

/// <summary>bool → bool invertido.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

/// <summary>null o cadena vacía → Visibility.Collapsed.</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>double (ms) → texto legible, NaN → "—".</summary>
public sealed class PingTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d && !double.IsNaN(d) ? $"{d:0} ms" : "—";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>double (ratio 0-1) → porcentaje, NaN → "—".</summary>
public sealed class LossTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d && !double.IsNaN(d) ? $"{d:P1}" : "—";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Enum → texto traducido (para estados).</summary>
public sealed class StateTextConverter : IValueConverter
{
    private static readonly Dictionary<Core.Models.OptimizerState, string> Map = new()
    {
        [Core.Models.OptimizerState.Idle] = "Inactivo",
        [Core.Models.OptimizerState.DetectingGame] = "Detectando juego",
        [Core.Models.OptimizerState.ProbingDirect] = "Midiendo ruta directa",
        [Core.Models.OptimizerState.ProbingRelays] = "Midiendo relays",
        [Core.Models.OptimizerState.SelectingRoute] = "Seleccionando ruta",
        [Core.Models.OptimizerState.WaitingUser] = "Esperando decisión",
        [Core.Models.OptimizerState.Connecting] = "Conectando túnel",
        [Core.Models.OptimizerState.Active] = "Optimizando",
        [Core.Models.OptimizerState.Monitoring] = "Monitoreando",
        [Core.Models.OptimizerState.Degraded] = "Calidad degradada",
        [Core.Models.OptimizerState.Switching] = "Cambiando de ruta",
        [Core.Models.OptimizerState.FailingBack] = "Regresando a directa",
        [Core.Models.OptimizerState.Stopping] = "Deteniendo y restaurando",
        [Core.Models.OptimizerState.Error] = "Error"
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Core.Models.OptimizerState s && Map.TryGetValue(s, out var text))
            return text;
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
