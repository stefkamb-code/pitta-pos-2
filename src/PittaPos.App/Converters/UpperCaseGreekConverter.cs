using System.Globalization;
using System.Windows.Data;
using PittaPos.Core.Data;

namespace PittaPos.App.Converters;

/// <summary>Κεφαλαία (ελληνικά, χωρίς τόνους) μόνο για εμφάνιση — π.χ. το καλάθι παραγγελίας επί οθόνης,
/// ώστε ο υπάλληλος να διαβάζει προϊόντα/σχόλια/έξτρα πιο εύκολα. Δεν αλλάζει τα υποκείμενα δεδομένα.</summary>
public sealed class UpperCaseGreekConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? MenuSeed.ToUpperGreek(s) : value ?? "";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
