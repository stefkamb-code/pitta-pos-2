using System.Windows;
using PittaPos.App.ViewModels;

namespace PittaPos.App.Views;

public partial class TableDetailWindow : Window
{
    private readonly TableDetailViewModel _vm;

    public TableDetailWindow(int table)
    {
        InitializeComponent();
        _vm = new TableDetailViewModel(table);
        DataContext = _vm;
        Closed += (_, _) => _vm.Detach();
        _vm.NewRoundRequested += () =>
        {
            NewRoundRequested?.Invoke(_vm.TableNumber);
            Close();
        };
        _vm.TableClosed += Close;
    }

    /// <summary>Ζητά νέο γύρο για το τραπέζι — ο ακροατής (MainWindow) προωθεί στο βήμα προϊόντων.</summary>
    public event Action<int>? NewRoundRequested;

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

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
