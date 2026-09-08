using System.Windows;

namespace GameRouteOptimizer.App;

/// <summary>
/// Cambio de tema (oscuro/claro) en caliente: intercambia el diccionario de recursos
/// fusionado de la aplicación. Todos los pinceles de las vistas se referencian con
/// DynamicResource, así que el cambio se refleja al instante sin reiniciar.
/// </summary>
public static class ThemeManager
{
    public const string DarkName = "Dark";
    public const string LightName = "Light";

    private static bool _isDark = true;

    /// <summary>Se dispara tras aplicar un tema (para ViewModels con pinceles propios).</summary>
    public static event EventHandler? ThemeChanged;

    public static bool IsDark => _isDark;

    public static string CurrentName => _isDark ? DarkName : LightName;

    /// <summary>Aplica el tema por nombre ("Dark" o "Light"); cualquier otro valor usa oscuro.</summary>
    public static void Apply(string themeName)
    {
        var dark = string.IsNullOrWhiteSpace(themeName) ||
                   !themeName.Equals(LightName, StringComparison.OrdinalIgnoreCase);
        if (dark == _isDark && Application.Current?.Resources.MergedDictionaries.Count > 0)
        {
            return;
        }

        var source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = source };

        if (Application.Current is { } app)
        {
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(dictionary);
        }

        _isDark = dark;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Pincel del tema por clave de recurso, con respaldo si el recurso no existe.</summary>
    public static Brush Brush(string resourceKey, Color fallback) =>
        Application.Current?.TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallback);
}
