using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using PittaPos.App.ViewModels;

namespace PittaPos.App.Views;

public partial class LiveOrdersWindow : Window
{
    private readonly LiveOrdersViewModel _vm = new();

    /// <summary>Παλέτα «νύχτας» — μόνο για αυτό το παράθυρο, όταν είναι ενεργή η βραδινή βάρδια.</summary>
    private static readonly Dictionary<string, string> NightPalette = new()
    {
        ["Bg"] = "#141a2e",
        ["Ink"] = "#e8eaf6",
        ["Divider"] = "#39406b",
        ["Neutral500"] = "#7b82ad",
        ["Neutral600"] = "#9fa5cc",
        ["Neutral700"] = "#c3c7e3",
        ["Accent100"] = "#26305a",
    };

    public LiveOrdersWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.PropertyChanged += Vm_PropertyChanged;
        ApplyShiftColors();
        Closed += (_, _) =>
        {
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.Shutdown();
        };
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveOrdersViewModel.IsEveningShift))
            ApplyShiftColors();
    }

    private void ApplyShiftColors()
    {
        if (_vm.IsEveningShift)
        {
            foreach (var (key, hex) in NightPalette)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                Resources[key] = brush;
            }
        }
        else
        {
            foreach (var key in NightPalette.Keys)
                Resources.Remove(key);
        }
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
