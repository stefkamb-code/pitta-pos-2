using System.Windows;
using System.Windows.Media;

namespace PittaPos.App.Services;

/// <summary>
/// Εναλλαγή λευκού/μαύρου θέματος: αντικαθιστά τα brushes στο Application.Resources,
/// και επειδή όλα τα XAML τα δείχνουν με DynamicResource, η αλλαγή γίνεται ζωντανά.
/// </summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, (string Light, string Dark)> Palette = new()
    {
        ["Bg"] = ("#f3f2f2", "#1d1b1a"),
        ["Surface"] = ("#eae9e9", "#282625"),
        ["Ink"] = ("#201e1d", "#f3f2f2"),
        ["Divider"] = ("#9F9D9D", "#5a5754"),
        ["Accent"] = ("#ec3013", "#ec3013"),
        ["Accent100"] = ("#fff2ef", "#40201a"),
        ["Accent600"] = ("#dd2b0f", "#ff5a3c"),
        ["Accent700"] = ("#ae1800", "#ff7d64"),
        ["Accent800"] = ("#7c1405", "#ffa694"),
        ["Neutral300"] = ("#d7d3d3", "#403d3c"),
        ["Neutral400"] = ("#bab6b6", "#575452"),
        ["Neutral500"] = ("#9b9797", "#8c8885"),
        ["Neutral600"] = ("#7d7979", "#aaa6a2"),
        ["Neutral700"] = ("#605d5d", "#c7c3bf"),
    };

    /// <summary>Σηκώνεται μετά από κάθε αλλαγή θέματος — για στοιχεία που δεν διαβάζουν DynamicResource (π.χ. SkiaSharp διάγραμμα).</summary>
    public static event Action? Changed;

    public static void Apply(bool dark)
    {
        foreach (var (key, colors) in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(dark ? colors.Dark : colors.Light);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
        Changed?.Invoke();
    }
}
