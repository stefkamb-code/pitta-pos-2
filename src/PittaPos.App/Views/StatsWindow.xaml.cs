using System.Windows;
using System.Windows.Controls;
using PittaPos.App.ViewModels;

using PittaPos.App.Services;

namespace PittaPos.App.Views;

public partial class StatsWindow : Window
{
    private readonly StatsViewModel _vm = new();

    public StatsWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        DataContext = _vm;
        Closed += (_, _) => _vm.Detach();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Κλείνει το popup μόλις διαλεχτεί μέρα, ώστε να μη μένει ανοιχτό.</summary>
    private void ChartCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e) => ChartDateToggle.IsChecked = false;

    private void CustomFromCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e) => CustomFromToggle.IsChecked = false;

    private void CustomToCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e) => CustomToToggle.IsChecked = false;
}
