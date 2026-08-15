using System.Windows;
using PittaPos.App.ViewModels;

using PittaPos.App.Services;

namespace PittaPos.App.Views;

public partial class TableDetailWindow : Window
{
    private readonly TableDetailViewModel _vm;

    public TableDetailWindow(int table)
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        _vm = new TableDetailViewModel(table);
        DataContext = _vm;
        Closed += (_, _) => _vm.Detach();
        _vm.NewRoundRequested += person =>
        {
            NewRoundRequested?.Invoke(_vm.TableNumber, person);
            Close();
        };
        _vm.TableClosed += Close;
    }

    /// <summary>Ζητά νέο γύρο για το τραπέζι — ο ακροατής (MainWindow) προωθεί στο βήμα προϊόντων.
    /// Δεύτερη παράμετρος: σε ποιο άτομο γράφεται (-1 = στον πρώτο που δεν έχει παραγγείλει).</summary>
    public event Action<int, int>? NewRoundRequested;

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Ξανατυπώνει το δελτίο του τραπεζιού με ΟΛΑ όσα έχουν παραγγελθεί, ενωμένα σε ένα χαρτί — ακριβώς
    /// όπως βγαίνει όταν κλείνει η σειρά των ατόμων. Για όταν το χαρτί χάθηκε ή δεν βγήκε ποτέ (σβηστός
    /// εκτυπωτής τη στιγμή που ήρθε η παραγγελία από το κινητό).
    /// </summary>
    private void Reprint_Click(object sender, RoutedEventArgs e)
    {
        var ticket = _vm.BuildReprintTicket();
        if (ticket is null)
        {
            MessageBox.Show(this, "Δεν υπάρχει παραγγελία σε αυτό το τραπέζι για να τυπωθεί.",
                "Επανεκτύπωση", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ReceiptPrinter.PrintOrder(ticket);
    }

    /// <summary>Ακύρωση (πραγματική διαγραφή) επιλεγμένων προϊόντων — μόνο με ονομαστικό κωδικό.</summary>
    private void CancelSelected_Click(object sender, RoutedEventArgs e)
    {
        var name = StaffPinDialog.RequireName(this);
        if (name is not null)
            _vm.CancelSelectedCommand.Execute(name);
    }

    /// <summary>Ακύρωση (πραγματική διαγραφή) ολόκληρου γύρου — μόνο με ονομαστικό κωδικό.</summary>
    private void CancelRound_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TableRoundViewModel round })
            return;
        var name = StaffPinDialog.RequireName(this);
        if (name is not null)
            _vm.CancelRoundCommand.Execute(new CancelRoundRequest(round.OrderNumber, name));
    }
}
