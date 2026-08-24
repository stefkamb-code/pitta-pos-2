using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>Ένα «τικ» δικαιώματος μέσα στη γραμμή ενός ατόμου.</summary>
public partial class StaffRightToggle : ObservableObject
{
    public required string Key { get; init; }
    public required string Label { get; init; }

    [ObservableProperty] private bool _isOn;
}

/// <summary>Μία γραμμή ατόμου στην οθόνη κωδικών.</summary>
public partial class StaffPinRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _pin = "";

    /// <summary>Και τα έξι δικαιώματα, τικαρισμένα ή όχι (βλ. StaffRight.All).</summary>
    public ObservableCollection<StaffRightToggle> Rights { get; } = [];
}

/// <summary>
/// Οι κωδικοί του προσωπικού: <b>όσα άτομα θέλει το μαγαζί</b> (όνομα + 4ψήφιος + τι ανοίγει ο
/// καθένας) και ο <b>κωδικός σερβιτόρου</b> για το κινητό.
///
/// <para>Ανοίγει ΜΟΝΟ με τον κωδικό admin (βλ. SettingsWindow.ManageCancelStaff_Click): τα υπόλοιπα
/// τα ανοίγει ο καθένας με τον δικό του κωδικό, αλλά όχι αυτή την οθόνη — εδώ μέσα είναι οι κωδικοί
/// και τα δικαιώματα όλων.</para>
/// </summary>
public partial class CancelStaffWindow : Window
{
    /// <summary>Πόσα άτομα το πολύ. Δεν υπάρχει τεχνικός λόγος — απλώς μια λίστα 4ψήφιων που δεν
    /// τελειώνει ποτέ είναι πιο πιθανό να είναι λάθος παρά ανάγκη.</summary>
    private const int MaxRows = 10;

    private readonly ObservableCollection<StaffPinRow> _rows = [];

    public CancelStaffWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);

        foreach (var saved in SettingsStore.Instance.Settings.CancelStaffPins)
            _rows.Add(NewRow(saved));
        // Πάντα μία γραμμή στην αρχή: ένα άδειο πεδίο λέει «γράψε εδώ» χωρίς οδηγίες.
        if (_rows.Count == 0)
            _rows.Add(NewRow(null));
        RowsControl.ItemsSource = _rows;
        WaiterPinBox.Text = SettingsStore.Instance.Settings.WaiterPin;
    }

    /// <summary>Νέα γραμμή. Νέο άτομο ξεκινά με ΟΛΑ τικαρισμένα (και το ξετικάρεις όποιο δεν θέλεις)·
    /// αποθηκευμένο άτομο δείχνει ό,τι είχε — και τα παλιά, που γράφτηκαν πριν υπάρξουν δικαιώματα,
    /// έχουν <c>Rights == null</c> και σημαίνει «όλα» (βλ. StaffPin.Allows).</summary>
    private static StaffPinRow NewRow(StaffPin? saved)
    {
        var row = new StaffPinRow { Name = saved?.Name ?? "", Pin = saved?.Pin ?? "" };
        foreach (var (key, label) in StaffRight.All)
            row.Rights.Add(new StaffRightToggle
            {
                Key = key,
                Label = label,
                IsOn = saved is null || saved.Allows(key),
            });
        return row;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count >= MaxRows)
            return;
        _rows.Add(NewRow(null));
    }

    private void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StaffPinRow row })
            return;
        _rows.Remove(row);
        // Ποτέ εντελώς άδεια οθόνη: μένει μία γραμμή για να μπορεί να ξαναγράψει κάποιος.
        if (_rows.Count == 0)
            _rows.Add(NewRow(null));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var pins = new List<StaffPin>();
        foreach (var row in _rows)
        {
            var name = row.Name.Trim();
            var pin = row.Pin.Trim();
            if (name.Length == 0 && pin.Length == 0)
                continue;   // άδεια γραμμή = δεν υπάρχει άτομο
            if (!IsValidPin(pin))
            {
                ShowError("Ο κωδικός πρέπει να είναι 4 ψηφία.");
                return;
            }
            if (name.Length == 0)
            {
                ShowError("Βάλε όνομα σε κάθε άτομο που έχει κωδικό.");
                return;
            }
            var rights = row.Rights.Where(r => r.IsOn).Select(r => r.Key).ToList();
            if (rights.Count == 0)
            {
                ShowError($"Ο/Η {name} δεν έχει τικαρισμένο τίποτα — ο κωδικός δεν θα άνοιγε πουθενά.");
                return;
            }
            pins.Add(new StaffPin { Name = name, Pin = pin, Rights = rights });
        }

        var waiter = WaiterPinBox.Text.Trim();
        if (waiter.Length > 0 && !IsValidPin(waiter))
        {
            ShowError("Ο κωδικός σερβιτόρου πρέπει να είναι 4 ψηφία (ή άδειος).");
            return;
        }

        var used = pins.Select(p => p.Pin).Append(waiter).Where(p => p.Length > 0).ToList();
        if (used.Distinct().Count() != used.Count)
        {
            ShowError("Δύο κωδικοί δεν μπορούν να είναι ίδιοι.");
            return;
        }

        // ΟΧΙ ο κωδικός του καταστήματος και ΟΧΙ ο admin: αυτοί ελέγχονται ΠΡΩΤΟΙ παντού (βλ.
        // SettingsStore.VerifyOwnerPin), οπότε το άτομο θα άνοιγε τα πάντα ό,τι κι αν του τικάρεις —
        // και οι ακυρώσεις του θα γράφονταν στο όνομα του ιδιοκτήτη.
        if (used.Any(p => p == SettingsStore.Instance.Settings.Pin || p == SettingsStore.AdminPin))
        {
            ShowError("Αυτός ο κωδικός ανοίγει ολόκληρο το ταμείο — δώσε άλλον.");
            return;
        }

        SettingsStore.Instance.SetStaffPins(pins, waiter);
        Close();
    }

    private static bool IsValidPin(string pin) => pin.Length == 4 && pin.All(char.IsDigit);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
