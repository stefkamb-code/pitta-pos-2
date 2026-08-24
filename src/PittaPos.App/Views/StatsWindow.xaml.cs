using System.Windows;
using System.Windows.Controls;
using PittaPos.App.ViewModels;

using PittaPos.App.Services;

namespace PittaPos.App.Views;

public partial class StatsWindow : Window
{
    private readonly StatsViewModel _vm = new();

    /// <summary>Ο κωδικός που άνοιξε ΑΥΤΟ το παράθυρο — βλ. OpenHistory_Click.</summary>
    private readonly string? _unlockedWith;

    public StatsWindow(string? unlockedWith = null)
    {
        _unlockedWith = unlockedWith;
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        DataContext = _vm;
        Closed += (_, _) => _vm.Detach();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Το ΙΣΤΟΡΙΚΟ ζει πλέον εδώ μέσα, δίπλα στο ΔΙΑΓΡΑΜΜΑ (έφυγε από την μπάρα του ταμείου).
    /// Κρατιέται ΕΝΑ παράθυρο: δεύτερο πάτημα φέρνει μπροστά αυτό που είναι ήδη ανοιχτό.
    ///
    /// <para>Κωδικό ζητάει ΜΟΝΟ αν χρειάζεται: όποιος άνοιξε τα Στατιστικά με κωδικό που έχει και το
    /// δικαίωμα ΙΣΤΟΡΙΚΟ μπαίνει κατευθείαν. Άτομο με τικαρισμένα μόνο τα Στατιστικά ρωτιέται εδώ.</para>
    /// </summary>
    private void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_history is null || !_history.IsLoaded)
        {
            if (!SettingsStore.Instance.PinOpens(_unlockedWith, StaffRight.History)
                && !PinDialog.Require(this, StaffRight.History))
                return;
            _history = new HistoryWindow();
            _history.Closed += (_, _) => _history = null;
            _history.Show();
        }
        else
        {
            _history.Activate();
        }
    }

    private HistoryWindow? _history;

    /// <summary>Κλείνει το popup μόλις διαλεχτεί μέρα, ώστε να μη μένει ανοιχτό.</summary>
    private void ChartCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e) => ChartDateToggle.IsChecked = false;

    private void CustomFromCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e) => CustomFromToggle.IsChecked = false;

    private void CustomToCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e) => CustomToToggle.IsChecked = false;
}
