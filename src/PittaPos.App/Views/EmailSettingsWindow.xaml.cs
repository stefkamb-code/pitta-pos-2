using System.Windows;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>Πού και από ποιον λογαριασμό φεύγει η αναφορά ημέρας — βλ. DayReportService.TrySendEmail.</summary>
public partial class EmailSettingsWindow : Window
{
    private readonly SettingsStore _store = SettingsStore.Instance;

    public EmailSettingsWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        SmtpUserBox.Text = _store.Settings.SmtpUser;
        SmtpPasswordBox.Text = _store.Settings.SmtpPassword;
        ReportEmailBox.Text = _store.Settings.ReportEmail;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Αποθηκεύει και <b>στέλνει αμέσως δοκιμαστική αναφορά</b>. Η δοκιμή γίνεται εδώ και όχι σιωπηλά
    /// στο κλείσιμο επίτηδες: αλλιώς ένα λάθος (κωδικός, κλειστή επαλήθευση δύο βημάτων) θα φαινόταν
    /// πρώτη φορά στις 5 το πρωί, όταν δεν κοιτάει κανείς.
    /// </summary>
    private async void SaveEmail_Click(object sender, RoutedEventArgs e)
    {
        var user = SmtpUserBox.Text.Trim();
        // Ο κωδικός εφαρμογής της Google δίνεται με κενά ανά τέσσερα («abcd efgh ijkl mnop») και τα
        // κενά ΔΕΝ είναι μέρος του — αν μείνουν, η σύνδεση αποτυγχάνει με «λάθος κωδικός».
        var password = SmtpPasswordBox.Text.Replace(" ", "").Trim();
        var to = ReportEmailBox.Text.Trim();

        // Το gmail θέλει πάντα smtp.gmail.com:587· κρατιούνται όπως είναι για όποιον βάλει άλλον πάροχο.
        _store.SetEmail(_store.Settings.SmtpHost, _store.Settings.SmtpPort, user, password, to);

        EmailStatus.SetResourceReference(ForegroundProperty, "Neutral500");
        EmailStatus.Text = "Στέλνω δοκιμαστικό…";

        var (ok, error) = await Task.Run(() =>
            DayReportService.TrySendEmail(DayReportService.BuildPrintSummary()));

        EmailStatus.SetResourceReference(ForegroundProperty, ok ? "Neutral500" : "Accent");
        EmailStatus.Text = ok
            ? "✓ Στάλθηκε δοκιμαστικό στο " + (to.Length > 0 ? to : user) + " — δες το εισερχόμενο."
            : "✕ " + error;
    }
}
