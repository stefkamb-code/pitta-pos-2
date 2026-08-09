using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>Μία γραμμή επεξεργασίας στο παράθυρο διαχείρισης κωδικών ακύρωσης.</summary>
public partial class StaffPinRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _pin = "";
}

/// <summary>
/// Διαχείριση των 4 ονομαστικών κωδικών ακύρωσης — μόνο ο admin φτάνει εδώ (βλ. SettingsWindow,
/// πίσω από τον γενικό κωδικό καταστήματος).
/// </summary>
public partial class CancelStaffWindow : Window
{
    private readonly ObservableCollection<StaffPinRow> _rows = [];

    public CancelStaffWindow()
    {
        InitializeComponent();
        var saved = SettingsStore.Instance.Settings.CancelStaffPins;
        for (var i = 0; i < 4; i++)
        {
            var existing = i < saved.Count ? saved[i] : new StaffPin();
            _rows.Add(new StaffPinRow { Name = existing.Name, Pin = existing.Pin });
        }
        RowsControl.ItemsSource = _rows;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var pins = new List<StaffPin>();
        foreach (var row in _rows)
        {
            var name = row.Name.Trim();
            var pin = row.Pin.Trim();

            if (pin.Length == 0 && name.Length == 0)
            {
                pins.Add(new StaffPin());
                continue;
            }
            if (pin.Length > 0 && (pin.Length != 4 || !pin.All(char.IsDigit)))
            {
                ShowError("Ο κωδικός πρέπει να είναι 4 ψηφία (ή άδειος).");
                return;
            }
            if (pin.Length > 0 && name.Length == 0)
            {
                ShowError("Βάλε όνομα για κάθε κωδικό.");
                return;
            }
            pins.Add(new StaffPin { Name = name, Pin = pin });
        }

        var usedPins = pins.Where(p => p.Pin.Length > 0).Select(p => p.Pin).ToList();
        if (usedPins.Distinct().Count() != usedPins.Count)
        {
            ShowError("Δύο ονόματα δεν μπορούν να έχουν τον ίδιο κωδικό.");
            return;
        }

        SettingsStore.Instance.SetCancelStaffPins(pins);
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
