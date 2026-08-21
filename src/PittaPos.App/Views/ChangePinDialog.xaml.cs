using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>Αλλαγή κωδικού σε 3 στάδια: τρέχων → νέος → επιβεβαίωση νέου.</summary>
public partial class ChangePinDialog : Window
{
    private const int PinLength = 4;

    private enum Stage { Current, New, Confirm }

    private Stage _stage = Stage.Current;
    private string _entered = "";
    private string _newPin = "";

    public ChangePinDialog()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
    }

    /// <summary>Δείχνει τον οδηγό αλλαγής κωδικού· true αν ο κωδικός άλλαξε.</summary>
    public static bool Run(Window owner) =>
        new ChangePinDialog { Owner = owner }.ShowDialog() == true;

    private void Append(string digit)
    {
        if (_entered.Length >= PinLength)
            return;
        _entered += digit;
        Update();
        if (_entered.Length == PinLength)
            Commit();
    }

    private void Commit()
    {
        switch (_stage)
        {
            case Stage.Current:
                if (!SettingsStore.Instance.VerifyPin(_entered))
                {
                    Reject("Λάθος κωδικός");
                    return;
                }
                GoTo(Stage.New, "Βάλε νέο κωδικό");
                break;

            case Stage.New:
                // Ο ίδιος έλεγχος από την ανάποδη (βλ. CancelStaffWindow): αν ο νέος κωδικός
                // καταστήματος είναι ήδη κωδικός ακύρωσης κάποιου υπαλλήλου, εκείνος αποκτά σιωπηλά
                // πρόσβαση σε Στατιστικά/Ιστορικό/Κατάλογο και οι ακυρώσεις του γράφονται στον ιδιοκτήτη.
                if (SettingsStore.Instance.Settings.CancelStaffPins.Any(p => p.Pin.Length > 0 && p.Pin == _entered))
                {
                    GoTo(Stage.New, "Βάλε νέο κωδικό");
                    ShowError("Τον έχει ήδη υπάλληλος για ακυρώσεις — διάλεξε άλλον.");
                    return;
                }
                _newPin = _entered;
                GoTo(Stage.Confirm, "Ξαναβάλε τον νέο κωδικό");
                break;

            case Stage.Confirm:
                if (_entered != _newPin)
                {
                    // Δεν ταιριάζουν — ξεκίνα ξανά τον νέο κωδικό
                    _newPin = "";
                    GoTo(Stage.New, "Δεν ταιριάζουν — βάλε νέο κωδικό");
                    ShowError("Οι κωδικοί δεν ταίριαξαν");
                    return;
                }
                SettingsStore.Instance.SetPin(_newPin);
                DialogResult = true;
                break;
        }
    }

    private void GoTo(Stage stage, string prompt)
    {
        _stage = stage;
        _entered = "";
        PromptText.Text = prompt;
        Update();
    }

    private void Reject(string message)
    {
        _entered = "";
        Update();
        ShowError(message);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
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

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_entered.Length == PinLength)
            Commit();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is >= Key.D0 and <= Key.D9)
            Append(((char)('0' + (e.Key - Key.D0))).ToString());
        else if (e.Key is >= Key.NumPad0 and <= Key.NumPad9)
            Append(((char)('0' + (e.Key - Key.NumPad0))).ToString());
        else if (e.Key == Key.Back)
            Backspace_Click(sender, e);
        else if (e.Key == Key.Enter)
            Ok_Click(sender, e);
        else if (e.Key == Key.Escape)
            Close();
    }
}
