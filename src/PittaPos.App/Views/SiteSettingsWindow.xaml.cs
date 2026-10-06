using System.Windows;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>Σύνδεση του ταμείου με το site διαχείρισης (βλ. SiteLinkService). Ανοίγει μόνο με τον κωδικό του
/// ιδιοκτήτη — το κλειδί στέλνει τις παραγγελίες του καταστήματος και φέρνει το κλειδί του παρόχου ΑΑΔΕ.</summary>
public partial class SiteSettingsWindow : Window
{
    private readonly SettingsStore _store = SettingsStore.Instance;

    public SiteSettingsWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        UrlBox.Text = _store.Settings.SiteUrl;
        KeyBox.Text = _store.Settings.SiteKey;
        ClientNote.Visibility = RemoteSync.IsClient ? Visibility.Visible : Visibility.Collapsed;
        ShowStatus();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Τι γίνεται ΤΩΡΑ με το site — τελευταία επικοινωνία, πάροχος, ή το γιατί δεν μιλάμε.</summary>
    private void ShowStatus()
    {
        var link = SiteLinkService.Instance;
        var fiscal = link.Fiscal;
        var provider = fiscal.Provider.Length > 0
            ? $" · πάροχος {fiscal.Provider} ({(fiscal.Production ? "κανονική" : "δοκιμαστική")})"
            : "";
        if (_store.Settings.SiteUrl.Length == 0 || _store.Settings.SiteKey.Length == 0)
            StatusText.Text = "Ανενεργό.";
        else if (link.LastError.Length > 0)
            StatusText.Text = "✕ " + link.LastError + provider;
        else if (link.LastContact is { } last)
            StatusText.Text = $"✓ Τελευταία επικοινωνία {last:HH:mm:ss} · στάλθηκαν {link.SentSinceStart} παραγγελίες από το άνοιγμα{provider}.";
        else
            StatusText.Text = "Περιμένω την πρώτη επικοινωνία…";
    }

    /// <summary>Αποθηκεύει και δοκιμάζει ΑΜΕΣΩΣ — ένα λάθος κλειδί πρέπει να φανεί εδώ, όχι όταν λείψουν νούμερα από το site.</summary>
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim().TrimEnd('/');
        var key = KeyBox.Text.Trim();
        if (url.Length > 0 && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
        {
            // Το κλειδί ταξιδεύει σε κάθε κλήση και φέρνει το κλειδί του παρόχου — ποτέ χωρίς κρυπτογράφηση.
            StatusText.SetResourceReference(ForegroundProperty, "Accent");
            StatusText.Text = "✕ Η διεύθυνση πρέπει να ξεκινά με https://";
            return;
        }

        _store.SetSiteLink(url, key);
        if (url.Length == 0 || key.Length == 0)
        {
            StatusText.SetResourceReference(ForegroundProperty, "Neutral500");
            StatusText.Text = "Αποθηκεύτηκε — ανενεργό.";
            return;
        }

        StatusText.SetResourceReference(ForegroundProperty, "Neutral500");
        StatusText.Text = "Δοκιμάζω…";
        var (ok, message) = await SiteLinkService.TestAsync(url, key);
        if (ok) await SiteLinkService.Instance.RefreshSettingsAsync(force: true);
        StatusText.SetResourceReference(ForegroundProperty, ok ? "Neutral500" : "Accent");
        StatusText.Text = (ok ? "✓ " : "✕ ") + message;
    }
}
