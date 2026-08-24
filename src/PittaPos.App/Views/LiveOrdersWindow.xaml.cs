using System.Windows;
using PittaPos.App.ViewModels;

using PittaPos.App.Services;

namespace PittaPos.App.Views;

public partial class LiveOrdersWindow : Window
{
    private readonly LiveOrdersViewModel _vm = new();

    // Η βραδινή βάρδια ΔΕΝ αλλάζει πια τα χρώματα αυτού του παραθύρου. Υπήρχε σκούρα «παλέτα
    // νύχτας» που μαύριζε ολόκληρο τον πίνακα παραγγελιών· στο μαγαζί ήταν πιο δύσκολο να διαβαστεί
    // και δεν πρόσφερε τίποτα — η βάρδια συνεχίζει να μετράει κανονικά στις αναφορές και στα
    // στατιστικά, απλώς δεν φαίνεται στα χρώματα.
    /// <summary>Μόνο όταν τρέχει ως ξεχωριστή εφαρμογή — ακούει το «έλα μπροστά» (βλ. AppMode).</summary>
    private readonly BoardActivationListener? _activation;

    public LiveOrdersWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        DataContext = _vm;
        Closed += (_, _) => _vm.Shutdown();

        if (!AppMode.IsBoard)
            return;

        // Ξεχωριστή εφαρμογή: ανοίγει σε ΚΑΝΟΝΙΚΟ παράθυρο, στο μέγεθος που είχε πάντα — ζητήθηκε
        // ρητά να μη μεγιστοποιείται μόνο του («θα το κάνω αν θέλω εγώ»). Το «←» δεν γυρίζει πουθενά
        // πλέον: κλείνει την ίδια την εφαρμογή.
        Title = "ΖΩΝΤΑΝΕΣ ΠΑΡΑΓΓΕΛΙΕΣ · " + AppIdentity.StoreName;
        _activation = new BoardActivationListener(this);
        Closed += (_, _) => _activation.Dispose();
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
