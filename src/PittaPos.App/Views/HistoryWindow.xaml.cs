using System.Printing;
using System.Windows;
using System.Windows.Controls;
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
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true)
        {
            // Ίδιο πρόβλημα/διόρθωση με το ReceiptPrinter (αυτόματη εκτύπωση) — χωρίς αυτό ο εκτυπωτής
            // τροφοδοτεί/κόβει ολόκληρο μήκος σελίδας αντί για το πραγματικό ύψος της απόδειξης.
            var ticket = dialog.PrintTicket;
            var width = ticket.PageMediaSize?.Width ?? ReceiptCard.ActualWidth;
            ticket.PageMediaSize = new PageMediaSize(width, ReceiptCard.ActualHeight);
            dialog.PrintTicket = ticket;
            dialog.PrintVisual(ReceiptCard, "Απόδειξη #" + _vm.SelectedOrder.OrderNumber);
        }
    }

    /// <summary>Πραγματική διαγραφή παραγγελίας — βγαίνει από τον τζίρο, μόνο με ονομαστικό κωδικό.</summary>
    private void DeleteOrder_Click(object sender, RoutedEventArgs e)
    {
        var name = StaffPinDialog.RequireName(this);
        if (name is not null)
            _vm.DeleteOrderCommand.Execute(name);
    }

    /// <summary>Κλείνει το popup μόλις διαλεχτεί μέρα, ώστε να μη μένει ανοιχτό.</summary>
    private void FromDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e) => FromDateToggle.IsChecked = false;

    private void ToDateCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e) => ToDateToggle.IsChecked = false;
}
