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
        DataContext = _vm;
        Closed += (_, _) => _vm.Detach();
        ApplyReceiptSettings();
    }

    /// <summary>Εφαρμόζει τις ρυθμίσεις «τι δείχνει η απόδειξη».</summary>
    private void ApplyReceiptSettings()
    {
        var s = Services.SettingsStore.Instance.Settings;
        TitleText.Text = s.ReceiptTitle;
        InfoText.Text = s.ReceiptInfo;
        InfoText.Visibility = s.ReceiptInfo.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        FooterText.Text = s.ReceiptFooter;
        FooterText.Visibility = s.ReceiptFooter.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DateTimeText.Visibility = s.ReceiptShowDateTime ? Visibility.Visible : Visibility.Collapsed;
        CustomerRow.Visibility = s.ReceiptShowCustomer ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>Κλείνει το popup μόλις διαλεχτεί μέρα, ώστε να μη μένει ανοιχτό.</summary>
    private void FromDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e) => FromDateToggle.IsChecked = false;

    private void ToDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e) => ToDateToggle.IsChecked = false;
}
