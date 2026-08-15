using System.Globalization;
using System.Windows;
using System.Windows.Media;

using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>Offscreen-only παράθυρο για σιωπηλή εκτύπωση της αναφοράς ημέρας σε θερμικό εκτυπωτή
/// (βλ. ReceiptPrinter.PrintDayReport) — απλό μονόχωρο κείμενο, καμία σύνδεση σε ViewModel.</summary>
public partial class DayReportWindow : Window
{
    public DayReportWindow(string report)
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        ReportText.Text = report;
    }

    /// <summary>
    /// Μικραίνει τη γραμματοσειρά όσο χρειάζεται ώστε η ΠΙΟ ΜΑΚΡΙΑ γραμμή της αναφοράς να χωράει
    /// ολόκληρη στο πραγματικό πλάτος του χαρτιού. Η αναφορά είναι μονόχωρη με στοιχισμένες στήλες
    /// (βλ. DayReportService — π.χ. «Τζίρος      : 123,45» και γραμμές με 48 «=»): αν δεν χωρέσει,
    /// αναδιπλώνεται στη μέση και η στοίχιση καταστρέφεται εντελώς. Σε ρολό 80mm χωράνε περίπου 30
    /// χαρακτήρες στο αρχικό μέγεθος, ενώ η αναφορά θέλει ~48 — γι' αυτό πρέπει να προσαρμοστεί.
    /// Ποτέ δεν ΜΕΓΑΛΩΝΕΙ τη γραμματοσειρά: σε φαρδύ χαρτί μένει το μέγεθος σχεδίασης.
    /// </summary>
    public void FitTextToWidth(double availableWidth)
    {
        if (availableWidth <= 0 || ReportText.Text.Length == 0)
            return;

        var longest = "";
        foreach (var line in ReportText.Text.Split('\n'))
            if (line.Length > longest.Length)
                longest = line;
        if (longest.Length == 0)
            return;

        var typeface = new Typeface(ReportText.FontFamily, ReportText.FontStyle,
            ReportText.FontWeight, ReportText.FontStretch);
        var measured = new FormattedText(longest, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, ReportText.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        if (measured.Width <= availableWidth)
            return;

        // Κάτω όριο 5: κάτω από αυτό δεν διαβάζεται τίποτα — καλύτερα να αναδιπλωθεί παρά να βγει σκόνη.
        ReportText.FontSize = Math.Max(5, ReportText.FontSize * availableWidth / measured.Width);
    }
}
