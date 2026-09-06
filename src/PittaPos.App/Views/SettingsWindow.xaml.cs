using System.Printing;
using System.Windows;
using System.Windows.Controls;
using PittaPos.App.Services;
using PittaPos.Core.Models;

namespace PittaPos.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store = SettingsStore.Instance;
    private bool _loadingPrinters;

    private sealed record PrinterOption(string Display, string Value);

    public SettingsWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        LoadPrinters();
        RefreshUi();
    }

    /// <summary>Γεμίζει τη λίστα με τους εγκατεστημένους εκτυπωτές των Windows.</summary>
    private void LoadPrinters()
    {
        _loadingPrinters = true;
        try
        {
            var options = new List<PrinterOption> { new("(Χωρίς αυτόματη εκτύπωση)", "") };
            try
            {
                using var server = new LocalPrintServer();
                options.AddRange(server.GetPrintQueues().Select(q => new PrinterOption(q.FullName, q.FullName)));
                // Άδεια λίστα δεν είναι το ίδιο με σφάλμα, αλλά για τον ταμία είναι το ίδιο πράγμα
                // («δεν βλέπει εκτυπωτή») — γράφεται ώστε να ξεχωρίζουν εκ των υστέρων.
                if (options.Count == 1)
                    AppLog.Write("printer", "Η λίστα εκτυπωτών των Windows ήρθε ΑΔΕΙΑ (κανένας εγκατεστημένος;).");
            }
            catch (Exception ex)
            {
                // Πριν ήταν σιωπηλό: η λίστα έμενε άδεια, ο ταμίας έβλεπε «δεν διαβάζει εκτυπωτή» και δεν
                // υπήρχε πουθενά ίχνος για το γιατί — ούτε καν ότι έγινε προσπάθεια.
                AppLog.Write("printer", $"Δεν διαβάστηκαν οι εκτυπωτές των Windows: {ex.GetType().Name}: {ex.Message}");
            }
            // Ο αποθηκευμένος εκτυπωτής ΔΕΝ χάνεται επειδή δεν φαίνεται αυτή τη στιγμή στα Windows.
            //
            // Συμβαίνει κανονικότατα με κοινόχρηστο εκτυπωτή: ο δεύτερος υπολογιστής τυπώνει σε εκτυπωτή
            // δεμένο στον πρώτο, και όσο ο πρώτος είναι κλειστό ή δεν έχει σηκωθεί το δίκτυο, ο εκτυπωτής
            // λείπει από τη λίστα. Πριν, το ταμείο επέλεγε τότε σιωπηλά «(Χωρίς αυτόματη εκτύπωση)» — και
            // η ρύθμιση χανόταν για πάντα, χωρίς να το καταλάβει κανείς μέχρι να μη βγει απόδειξη.
            // Τώρα μένει στη λίστα, σημειωμένος, και συνεχίζει να είναι ο επιλεγμένος.
            var current = _store.Settings.PrinterName;
            var match = options.FirstOrDefault(o => o.Value == current);
            if (match is null && current.Length > 0)
            {
                match = new PrinterOption(current + "  (δεν είναι διαθέσιμος τώρα)", current);
                options.Insert(1, match);
                AppLog.Write("printer",
                    $"Ο αποθηκευμένος εκτυπωτής «{current}» δεν βρέθηκε στη λίστα των Windows — " +
                    "η ρύθμιση ΔΙΑΤΗΡΗΘΗΚΕ (π.χ. κοινόχρηστος εκτυπωτής με τον άλλο υπολογιστή κλειστό).");
            }

            PrinterCombo.ItemsSource = options;
            PrinterCombo.SelectedItem = match ?? options[0];
        }
        finally
        {
            _loadingPrinters = false;
        }
    }

    private void PrinterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPrinters)
            return;
        if (PrinterCombo.SelectedItem is PrinterOption option)
            _store.SetPrinter(option.Value);
    }

    private void OpenReceipt_Click(object sender, RoutedEventArgs e) =>
        new ReceiptSettingsWindow { Owner = this }.ShowDialog();

    private void OpenNetwork_Click(object sender, RoutedEventArgs e) =>
        new NetworkSettingsWindow { Owner = this }.ShowDialog();

    private void OpenEmail_Click(object sender, RoutedEventArgs e) =>
        new EmailSettingsWindow { Owner = this }.ShowDialog();

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void Light_Click(object sender, RoutedEventArgs e) { _store.SetTheme("light"); RefreshUi(); }
    private void Dark_Click(object sender, RoutedEventArgs e) { _store.SetTheme("dark"); RefreshUi(); }

    private void DecTableCount_Click(object sender, RoutedEventArgs e)
    {
        _store.SetTableCount(_store.Settings.TableCount - 1);
        RefreshUi();
    }

    private void IncTableCount_Click(object sender, RoutedEventArgs e)
    {
        _store.SetTableCount(_store.Settings.TableCount + 1);
        RefreshUi();
    }

    /// <summary>ΜΟΝΟ ο admin (4504). Οι υπεύθυνοι ανοίγουν τα πάντα με τον δικό τους κωδικό, αλλά όχι
    /// αυτό: εδώ μέσα είναι οι κωδικοί όλων, δικοί τους και του σερβιτόρου.</summary>
    private void ManageCancelStaff_Click(object sender, RoutedEventArgs e)
    {
        if (PinDialog.RequireAdmin(this))
            new CancelStaffWindow { Owner = this }.ShowDialog();
    }

    /// <summary>Τυπώνει την αναφορά τζίρου μέχρι τώρα — καμία επίδραση στο ταμείο, καμία απαίτηση
    /// επιβεβαίωσης αφού δεν είναι καταστροφική ενέργεια. Το πραγματικό κλείσιμο/μηδενισμός γίνεται μόνο
    /// αυτόματα στις 5πμ (βλ. SalesStatsService.CheckAutoClose/DayReportService.CloseDay).</summary>
    private void CloseDay_Click(object sender, RoutedEventArgs e) => DayReportService.PrintCurrentReport();

    /// <summary>
    /// Στέλνει την αναλυτική αναφορά της μέρας με email. <b>Ο ΜΟΝΟΣ τρόπος να φύγει email</b> — το
    /// αυτόματο κλείσιμο ημέρας δεν στέλνει τίποτα (ζητήθηκε ρητά, βλ. DayReportService.CloseDay):
    /// γίνεται στις 5 το πρωί ή στο επόμενο άνοιγμα, ώρες που δεν το περιμένει κανείς.
    ///
    /// <para><b>Δεν κλείνει και δεν μηδενίζει τίποτα</b>, επίτηδες: το πάτημα ενός κουμπιού που σβήνει
    /// τη μέρα είναι πολύ εύκολο να γίνει κατά λάθος στη μέση της βάρδιας. Ο μηδενισμός μένει
    /// αποκλειστικά στο αυτόματο κλείσιμο (βλ. SalesStatsService.CheckAutoClose).</para>
    /// </summary>
    /// <remarks>
    /// Ο έλεγχος για ανεξόφλητα γίνεται ΠΡΙΝ φύγει το email, όχι μετά: αν έχει μείνει τραπέζι χωρίς
    /// εξόφληση, η αναφορά που θα έστελνε είναι λάθος — και ένα email δεν ξαναγυρίζει πίσω για να
    /// διορθωθεί. Έτσι ο ταμίας προλαβαίνει να πάει να τα κλείσει και να ξαναπατήσει αποστολή, με τη
    /// σωστή αναφορά αυτή τη φορά (βλ. UnsettledBanner στο SettingsWindow.xaml).
    /// </remarks>
    private async void SendReport_Click(object sender, RoutedEventArgs e)
    {
        var (total, entries) = DayReportService.UnsettledToday();
        if (total != 0)
        {
            UnsettledAmount.Text = Order.FormatPrice(total);
            UnsettledList.ItemsSource = entries;
            UnsettledBanner.Visibility = Visibility.Visible;
            return;
        }

        await SendReportAsync();
    }

    /// <summary>Η αποστολή, χωρίς κανέναν έλεγχο — από το κουμπί όταν η μέρα είναι καθαρή, ή από το
    /// banner όταν ο ταμίας επιλέξει να σταλεί έτσι όπως είναι.</summary>
    private async Task SendReportAsync()
    {
        SendReportStatus.SetResourceReference(ForegroundProperty, "Neutral500");
        SendReportStatus.Text = "Στέλνω…";

        // Η αναφορά ΧΤΙΖΕΤΑΙ εδώ, στο UI thread: διαβάζει τις ζωντανές συλλογές του ταμείου. Μόνο η
        // αποστολή φεύγει στο παρασκήνιο — το SmtpClient.Send μπλοκάρει μέχρι και ~100 δευτερόλεπτα.
        var report = DayReportService.Build();
        var (ok, error) = await Task.Run(() => DayReportService.SendReportNow(report));

        SendReportStatus.SetResourceReference(ForegroundProperty, ok ? "Neutral500" : "Accent");
        SendReportStatus.Text = ok
            ? "✓ Η αναφορά στάλθηκε."
            : "✕ " + error + "  (μπήκε σε αναμονή — θα ξαναδοκιμάσει μόνη της)";
    }

    /// <summary>«ΠΙΣΩ ΝΑ ΤΑ ΚΛΕΙΣΩ»: κλείνει και τις Ρυθμίσεις, όχι μόνο το banner — τα τραπέζια
    /// εξοφλούνται στην κύρια οθόνη, που είναι ακριβώς από πίσω. Δεν στέλνεται τίποτα· ο ταμίας
    /// ξαναπατάει αποστολή όταν τα κλείσει.</summary>
    private void BackToTables_Click(object sender, RoutedEventArgs e)
    {
        UnsettledBanner.Visibility = Visibility.Collapsed;
        Close();
    }

    /// <summary>«ΣΤΕΙΛΕ ΤΗΝ ΕΤΣΙ»: υπάρχουν βράδια που το ανεξόφλητο είναι πραγματικό (κερασμένο
    /// τραπέζι, πελάτης που έφυγε) και η αναφορά πρέπει να φύγει όπως είναι.</summary>
    private async void SendAnyway_Click(object sender, RoutedEventArgs e)
    {
        UnsettledBanner.Visibility = Visibility.Collapsed;
        await SendReportAsync();
    }

    private void RefreshUi()
    {
        Highlight(LightBtn, _store.Settings.Theme == "light");
        Highlight(DarkBtn, _store.Settings.Theme == "dark");
        TableCountText.Text = _store.Settings.TableCount.ToString();
    }

    private static void Highlight(Button button, bool active)
    {
        button.SetResourceReference(BackgroundProperty, active ? "Accent100" : "Bg");
        button.SetResourceReference(BorderBrushProperty, active ? "Accent" : "Divider");
        button.BorderThickness = new Thickness(active ? 2 : 1);
    }
}
