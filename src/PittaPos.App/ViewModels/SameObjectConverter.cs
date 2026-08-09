using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PittaPos.App.ViewModels;

/// <summary>Visible μόνο όταν όλα τα bindings είναι true — για συνθήκες ορατότητας.</summary>
public class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public static readonly AllTrueToVisibilityConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True όταν τα δύο bindings δείχνουν το ίδιο αντικείμενο — για highlight επιλεγμένης γραμμής.</summary>
public class SameObjectConverter : IMultiValueConverter
{
    public static readonly SameObjectConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is not null && ReferenceEquals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
