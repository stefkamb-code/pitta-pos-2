using System.Windows;
using System.Windows.Controls;
using PittaPos.App.Services;
using PittaPos.App.ViewModels;

namespace PittaPos.App.Views;

public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel _vm = new();

    public HistoryWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        DataContext = _vm;
        // Αλλαγή πληρωμής/καναλιού = προσωπικός κωδικός, όπως η ακύρωση· το όνομα πάει στην αναφορά ημέρας.
        _vm.AskStaffName = () => StaffPinDialog.RequireName(this, "ΠΟΙΟΣ ΑΛΛΑΖΕΙ",
            "Βάλε τον προσωπικό σου κωδικό για την αλλαγή");
        Closed += (_, _) => _vm.Detach();
        ApplyReceiptSettings();
        FiscalReceiptButton.Visibility = FiscalService.Instance.ProviderEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (FiscalService.Instance.ProviderEnabled)
        {
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(HistoryViewModel.SelectedOrder)) RefreshFiscalStatus();
            };
            // Κόπηκε/ελευθερώθηκε στο μεταξύ (κύριο ταμείο): η γραμμή ενημερώνεται μόνη της.
            void OnFiscalChanged() => Dispatcher.BeginInvoke(RefreshFiscalStatus);
            FiscalService.Instance.Changed += OnFiscalChanged;
            Closed += (_, _) => FiscalService.Instance.Changed -= OnFiscalChanged;
            RefreshFiscalStatus();
        }
    }

    /// <summary>Η απόδειξη του παρόχου ΑΑΔΕ (ΜΑΡΚ + QR) της επιλεγμένης παραγγελίας — μόνο όταν τη ζητήσει το μαγαζί. Αν
    /// περιμένει ακόμα (αυτόματη έκδοση κλειστή), το πάτημα είναι η πληρωμή: κόβεται τώρα και τυπώνεται μόλις έρθει το ΜΑΡΚ.</summary>
    private async void FiscalReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedOrder is not { } order)
            return;
        FiscalService.Instance.PrintReceipts([order.OrderNumber]);
        // Από το δεύτερο ταμείο η αλλαγή γίνεται στο κύριο — μια ματιά λίγο μετά, για να φανεί ότι έφυγε.
        await Task.Delay(1500);
        RefreshFiscalStatus();
    }

    private int _statusFor = -1;

    private async void RefreshFiscalStatus()
    {
        if (_vm.SelectedOrder is not { } order)
        {
            FiscalStatusText.Visibility = Visibility.Collapsed;
            return;
        }
        var number = _statusFor = order.OrderNumber;
        var status = await FiscalService.Instance.StatusOfAsync(number);
        if (number != _statusFor)
            return; // άλλαξε η επιλογή όσο ρωτούσε το κύριο ταμείο
        FiscalStatusText.Text = status.Text;
        FiscalStatusText.Visibility = status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Εφαρμόζει τις ρυθμίσεις «τι δείχνει η απόδειξη».</summary>
    private void ApplyReceiptSettings()
    {
        var s = Services.SettingsStore.Instance.Settings;
        InfoText.Text = s.ReceiptInfo;
        InfoText.Visibility = s.ReceiptInfo.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        FooterText.Text = s.ReceiptFooter;
        FooterText.Visibility = s.ReceiptFooter.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DateTimeText.Visibility = s.ReceiptShowDateTime ? Visibility.Visible : Visibility.Collapsed;
        // Ο πελάτης ΔΕΝ ακολουθεί τη ρύθμιση της απόδειξης: αυτή η κάρτα είναι προεπισκόπηση, δεν
        // τυπώνεται (η επανεκτύπωση περνά από το ReceiptPrinter). Εδώ ψάχνει ο ταμίας ποιανού είναι η
        // παραγγελία — μαζί με τηλέφωνο/διεύθυνση από κάτω — και το «μη δείχνεις πελάτη στο χαρτί» τα
        // έκρυβε όλα.
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void Reprint_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedOrder is null)
            return;

        // ΤΥΠΩΝΕΙ ΑΠΟ ΤΟ ΙΔΙΟ ΜΟΝΟΠΑΤΙ ΜΕ ΤΗΝ ΑΥΤΟΜΑΤΗ ΕΚΤΥΠΩΣΗ, επίτηδες. Πριν τυπωνόταν το ίδιο το
        // ReceiptCard της οθόνης, που παίρνει τα χρώματα του ΘΕΜΑΤΟΣ (Background="{DynamicResource Bg}")
        // — με σκούρο θέμα έβγαινε κατάμαυρο χαρτί, με μαύρο μελάνι πάνω σε μαύρο φόντο. Το
        // ReceiptPrinter φτιάχνει καθαρό παράθυρο απόδειξης και ρυθμίζει και το ύψος σελίδας σωστά,
        // οπότε η επανεκτύπωση βγαίνει ολόιδια με την αρχική.
        ReceiptPrinter.PrintOrder(_vm.SelectedOrder);
    }

    /// <summary>Πραγματική διαγραφή παραγγελίας — βγαίνει από τον τζίρο, μόνο με ονομαστικό κωδικό.</summary>
    private void DeleteOrder_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedOrder is null)
            return;

        // Λέει ΡΗΤΑ τι σβήνει: σε τραπέζι με άτομα φεύγει μόνο η απόδειξη ΕΝΟΣ ατόμου, όχι η παρέα.
        var answer = MessageBox.Show(
            $"Θα διαγραφεί {_vm.DeleteTargetLabel}.\n\n" +
            "Αφαιρείται από τον τζίρο και καταγράφεται στα ακυρωμένα. Οι υπόλοιπες αποδείξεις του " +
            "τραπεζιού δεν επηρεάζονται.",
            "Διαγραφή", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        var name = StaffPinDialog.RequireName(this);
        if (name is not null)
            _vm.DeleteOrderCommand.Execute(name);
    }

    /// <summary>Διόρθωση καναλιού σε πλατφόρμα: ρωτά πρώτα τον κωδικό της παραγγελίας. Κενός κωδικός
    /// δεν μπλοκάρει τη διόρθωση — η παραγγελία απλώς κρατά τον αριθμό που είχε.</summary>
    private void SetAppChannel_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedOrder is null)
            return;
        var platform = (string)((System.Windows.Controls.Button)sender).Tag;
        var reference = AppRefDialog.Ask(this, platform, _vm.SelectedOrder.AppOrderRef ?? "");
        _vm.SetAppChannel(platform, reference);
    }

    /// <summary>Κλείνει το popup μόλις διαλεχτεί μέρα, ώστε να μη μένει ανοιχτό.</summary>
    private void FromDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e) => FromDateToggle.IsChecked = false;

    private void ToDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e) => ToDateToggle.IsChecked = false;
}
