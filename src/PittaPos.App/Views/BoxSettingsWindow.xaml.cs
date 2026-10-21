using System.Windows;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>Σύνδεση του ταμείου με τη γέφυρα Box (βλ. BoxBridgeService) — ίδια με τη σελίδα E-FOOD, χωρίς αυτόματη
/// αποδοχή: την παραγγελία Box την αποδέχεται το μαγαζί στο tablet της Box. Ανοίγει μόνο με τον κωδικό του
/// ιδιοκτήτη — το κλειδί ανοίγει τις παραγγελίες του καταστήματος.</summary>
public partial class BoxSettingsWindow : Window
{
    private readonly SettingsStore _store = SettingsStore.Instance;

    public BoxSettingsWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        UrlBox.Text = _store.Settings.BoxBridgeUrl;
        KeyBox.Text = _store.Settings.BoxTillKey;
        ClientNote.Visibility = RemoteSync.IsClient ? Visibility.Visible : Visibility.Collapsed;
        // Η αντιστοίχιση ζει στο κύριο ταμείο, εκεί που μπαίνουν οι παραγγελίες.
        MatchPanel.Visibility = RemoteSync.IsClient ? Visibility.Collapsed : Visibility.Visible;
        ShowStatus();
        if (!RemoteSync.IsClient)
            ShowMatching();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Τι γίνεται ΤΩΡΑ με τη γέφυρα — τελευταία επικοινωνία ή το γιατί δεν μιλάμε.</summary>
    private void ShowStatus()
    {
        var bridge = BoxBridgeService.Instance;
        if (_store.Settings.BoxBridgeUrl.Length == 0 || _store.Settings.BoxTillKey.Length == 0)
            StatusText.Text = "Ανενεργό.";
        else if (bridge.LastError.Length > 0)
            StatusText.Text = "✕ " + bridge.LastError;
        else if (bridge.LastContact is { } last)
            StatusText.Text = $"✓ Τελευταία επικοινωνία {last:HH:mm:ss} · μπήκαν {bridge.ImportedSinceStart} από το άνοιγμα"
                + (bridge.TestsSinceStart > 0 ? $" · {bridge.TestsSinceStart} δοκιμαστικές (μόνο χαρτί)." : ".");
        else
            StatusText.Text = "Περιμένω την πρώτη επικοινωνία…";
    }

    /// <summary>Πόσα προϊόντα της Box θέλουν ακόμα ματιά.</summary>
    private void ShowMatching()
    {
        var matching = EfoodMatchStore.Box;
        var pending = matching.PendingCount;
        MatchText.Text = matching.Catalog.Count == 0
            ? "Ο κατάλογος της Box δεν έχει έρθει ακόμα — έρχεται μόλις ανοίξει η αντιστοίχιση."
            : pending > 0
                ? $"{pending} προς επιβεβαίωση — μέχρι τότε αυτά τυπώνονται με το όνομα της Box."
                : $"✓ Και τα {matching.Catalog.Count} προϊόντα της Box έχουν αντιστοιχιστεί.";
    }

    private void Match_Click(object sender, RoutedEventArgs e)
    {
        new EfoodMatchWindow(EfoodMatchStore.Box, EfoodMenu.Live) { Owner = this }.ShowDialog();
        ShowMatching();
    }

    /// <summary>Αποθηκεύει και δοκιμάζει ΑΜΕΣΩΣ — ένα λάθος κλειδί πρέπει να φανεί εδώ, όχι όταν λείψει
    /// η πρώτη παραγγελία.</summary>
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim().TrimEnd('/');
        var key = KeyBox.Text.Trim();
        if (url.Length > 0 && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
        {
            // Το κλειδί ταξιδεύει σε κάθε ερώτηση — ποτέ χωρίς κρυπτογράφηση πάνω από το internet.
            StatusText.SetResourceReference(ForegroundProperty, "Accent");
            StatusText.Text = "✕ Η διεύθυνση πρέπει να ξεκινά με https://";
            return;
        }

        _store.SetBoxBridge(url, key);
        if (url.Length == 0 || key.Length == 0)
        {
            StatusText.SetResourceReference(ForegroundProperty, "Neutral500");
            StatusText.Text = "Αποθηκεύτηκε — ανενεργό.";
            return;
        }

        StatusText.SetResourceReference(ForegroundProperty, "Neutral500");
        StatusText.Text = "Δοκιμάζω…";
        var (ok, message) = await BoxBridgeService.TestAsync(url, key);
        StatusText.SetResourceReference(ForegroundProperty, ok ? "Neutral500" : "Accent");
        StatusText.Text = (ok ? "✓ " : "✕ ") + message;
    }
}
