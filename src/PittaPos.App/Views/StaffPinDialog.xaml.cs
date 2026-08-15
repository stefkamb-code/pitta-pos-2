using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>
/// Κωδικός ακύρωσης ονομαστικός — κάθε μέλος του προσωπικού έχει το δικό του 4ψήφιο, ώστε το
/// ιστορικό ακυρωμένων να δείχνει ποιος ακύρωσε (βλ. SettingsStore.CancelStaffPins).
/// </summary>
public partial class StaffPinDialog : Window
{
    private const int PinLength = 4;

    private string _entered = "";

    /// <summary>Το όνομα που αντιστοιχεί στον κωδικό που μπήκε — γεμίζει μόνο αν βρεθεί ταίριασμα.</summary>
    public string? MatchedName { get; private set; }

    public StaffPinDialog()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
    }

    /// <summary>Δείχνει το παράθυρο· επιστρέφει το όνομα του υπαλλήλου ή null αν ακυρώθηκε/απέτυχε.</summary>
    public static string? RequireName(Window owner)
    {
        var dialog = new StaffPinDialog { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.MatchedName : null;
    }

    private void Append(string digit)
    {
        if (_entered.Length >= PinLength)
            return;
        _entered += digit;
        Update();
        if (_entered.Length == PinLength)
            Check();
    }

    private void Check()
    {
        var name = SettingsStore.Instance.FindCancelStaffName(_entered);
        if (name is not null)
        {
            MatchedName = name;
            DialogResult = true;
            return;
        }
        _entered = "";
        Update();
        ErrorText.Visibility = Visibility.Visible;
    }

    private void Update()
    {
        Dots.Text = new string('●', _entered.Length);
        ErrorText.Visibility = Visibility.Hidden;
    }

    private void Digit_Click(object sender, RoutedEventArgs e) =>
        Append((string)((Button)sender).Content);

    private void Backspace_Click(object sender, RoutedEventArgs e)
    {
        if (_entered.Length > 0)
            _entered = _entered[..^1];
        Update();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Check();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is >= Key.D0 and <= Key.D9)
            Append(((char)('0' + (e.Key - Key.D0))).ToString());
        else if (e.Key is >= Key.NumPad0 and <= Key.NumPad9)
            Append(((char)('0' + (e.Key - Key.NumPad0))).ToString());
        else if (e.Key == Key.Back)
            Backspace_Click(sender, e);
        else if (e.Key == Key.Enter)
            Check();
        else if (e.Key == Key.Escape)
            Close();
    }
}
