using System.Windows;
using PittaPos.App.ViewModels;

namespace PittaPos.App.Views;

public partial class LiveOrdersWindow : Window
{
    private readonly LiveOrdersViewModel _vm = new();

    // Η βραδινή βάρδια ΔΕΝ αλλάζει πια τα χρώματα αυτού του παραθύρου. Υπήρχε σκούρα «παλέτα
    // νύχτας» που μαύριζε ολόκληρο τον πίνακα παραγγελιών· στο μαγαζί ήταν πιο δύσκολο να διαβαστεί
    // και δεν πρόσφερε τίποτα — η βάρδια συνεχίζει να μετράει κανονικά στις αναφορές και στα
    // στατιστικά, απλώς δεν φαίνεται στα χρώματα.
    public LiveOrdersWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Closed += (_, _) => _vm.Shutdown();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Ακύρωση (πραγματική διαγραφή) της επιλεγμένης παραγγελίας σε αναμονή — μόνο με ονομαστικό κωδικό.</summary>
    private void CancelPendingOrder_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedOrder is not { } order)
            return;
        var name = StaffPinDialog.RequireName(this);
        if (name is not null)
            _vm.CancelOrderCommand.Execute(new CancelBoardOrderRequest(order, name));
    }

    /// <summary>Ακύρωση (πραγματική διαγραφή) μιας ήδη περασμένης σε κανάλι παραγγελίας — μόνο με ονομαστικό κωδικό.</summary>
    private void CancelChannelOrder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChannelOrderViewModel row })
            return;
        var name = StaffPinDialog.RequireName(this);
        if (name is not null)
            _vm.CancelOrderCommand.Execute(new CancelBoardOrderRequest(row.Order, name));
    }
}
