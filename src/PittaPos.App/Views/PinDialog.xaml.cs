using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>Κλείδωμα με κωδικό για Στατιστικά, Ιστορικό και Κατάλογο.</summary>
public partial class PinDialog : Window
{
    private const int PinLength = 4;

    private string _entered = "";

    public PinDialog()
    {
        InitializeComponent();
    }

    /// <summary>Δείχνει το παράθυρο κωδικού· true μόνο αν μπήκε ο σωστός.</summary>
    public static bool Require(Window owner) =>
        new PinDialog { Owner = owner }.ShowDialog() == true;

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
        if (SettingsStore.Instance.VerifyPin(_entered))
        {
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
