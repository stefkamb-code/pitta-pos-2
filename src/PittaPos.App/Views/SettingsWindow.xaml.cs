using System.Printing;
using System.Windows;
using System.Windows.Controls;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store = SettingsStore.Instance;
    private bool _loadingPrinters;

    private sealed record PrinterOption(string Display, string Value);

    public SettingsWindow()
    {
        InitializeComponent();
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
            }
            catch (Exception)
            {
                // Δεν υπάρχει πρόσβαση σε print server — μένει μόνο η επιλογή απενεργοποίησης
            }
            PrinterCombo.ItemsSource = options;
            var current = _store.Settings.PrinterName;
            PrinterCombo.SelectedItem = options.FirstOrDefault(o => o.Value == current) ?? options[0];
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

    private void ChangePin_Click(object sender, RoutedEventArgs e)
    {
        if (ChangePinDialog.Run(this))
            MessageBox.Show("Ο κωδικός άλλαξε.", "Αλλαγή κωδικού",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Μόνο ο admin — θέλει τον γενικό κωδικό καταστήματος πριν ανοίξει.</summary>
    private void ManageCancelStaff_Click(object sender, RoutedEventArgs e)
    {
        if (PinDialog.Require(this))
            new CancelStaffWindow { Owner = this }.ShowDialog();
    }

    /// <summary>Τυπώνει την αναφορά τζίρου μέχρι τώρα — καμία επίδραση στο ταμείο, καμία απαίτηση
    /// επιβεβαίωσης αφού δεν είναι καταστροφική ενέργεια. Το πραγματικό κλείσιμο/μηδενισμός γίνεται μόνο
    /// αυτόματα στις 5πμ (βλ. SalesStatsService.CheckAutoClose/DayReportService.CloseDay).</summary>
    private void CloseDay_Click(object sender, RoutedEventArgs e) => DayReportService.PrintCurrentReport();

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
