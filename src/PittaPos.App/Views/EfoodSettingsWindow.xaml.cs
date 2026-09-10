using System.Windows;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>Σύνδεση του ταμείου με τη γέφυρα e-food (βλ. EfoodBridgeService). Ανοίγει μόνο με τον κωδικό
/// του ιδιοκτήτη — το κλειδί ανοίγει τις παραγγελίες του καταστήματος.</summary>
public partial class EfoodSettingsWindow : Window
{
    private readonly SettingsStore _store = SettingsStore.Instance;

    public EfoodSettingsWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        UrlBox.Text = _store.Settings.EfoodBridgeUrl;
        KeyBox.Text = _store.Settings.EfoodTillKey;
        AutoAcceptBox.Text = _store.Settings.EfoodAutoAcceptMinutes.ToString();
        ClientNote.Visibility = RemoteSync.IsClient ? Visibility.Visible : Visibility.Collapsed;
        ShowStatus();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Τι γίνεται ΤΩΡΑ με τη γέφυρα — τελευταία επικοινωνία ή το γιατί δεν μιλάμε.</summary>
    private void ShowStatus()
    {
        var bridge = EfoodBridgeService.Instance;
        if (_store.Settings.EfoodBridgeUrl.Length == 0 || _store.Settings.EfoodTillKey.Length == 0)
            StatusText.Text = "Ανενεργό.";
        else if (bridge.LastError.Length > 0)
            StatusText.Text = "✕ " + bridge.LastError;
        else if (bridge.LastContact is { } last)
            StatusText.Text = $"✓ Τελευταία επικοινωνία {last:HH:mm:ss} · μπήκαν {bridge.ImportedSinceStart} από το άνοιγμα.";
        else
            StatusText.Text = "Περιμένω την πρώτη επικοινωνία…";
    }

    /// <summary>Αποθηκεύει και δοκιμάζει ΑΜΕΣΩΣ — ένα λάθος κλειδί πρέπει να φανεί εδώ, όχι όταν λείψει
    /// η πρώτη παραγγελία.</summary>
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim().TrimEnd('/');
        var key = KeyBox.Text.Trim();
        _ = int.TryParse(AutoAcceptBox.Text.Trim(), out var autoAccept);
        if (url.Length > 0 && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
        {
            // Το κλειδί ταξιδεύει σε κάθε ερώτηση — ποτέ χωρίς κρυπτογράφηση πάνω από το internet.
            StatusText.SetResourceReference(ForegroundProperty, "Accent");
            StatusText.Text = "✕ Η διεύθυνση πρέπει να ξεκινά με https://";
            return;
        }

        _store.SetEfoodBridge(url, key, autoAccept);
        AutoAcceptBox.Text = _store.Settings.EfoodAutoAcceptMinutes.ToString();
        if (url.Length == 0 || key.Length == 0)
        {
            StatusText.SetResourceReference(ForegroundProperty, "Neutral500");
            StatusText.Text = "Αποθηκεύτηκε — ανενεργό.";
            return;
        }

        StatusText.SetResourceReference(ForegroundProperty, "Neutral500");
        StatusText.Text = "Δοκιμάζω…";
        var (ok, message) = await EfoodBridgeService.TestAsync(url, key);
        StatusText.SetResourceReference(ForegroundProperty, ok ? "Neutral500" : "Accent");
        StatusText.Text = (ok ? "✓ " : "✕ ") + message;
    }
}
