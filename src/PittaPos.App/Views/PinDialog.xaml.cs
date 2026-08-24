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

    /// <summary>Δέχεται ΜΟΝΟ τον κωδικό admin — βλ. RequireAdmin.</summary>
    private bool _adminOnly;

    public PinDialog()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        // Το κείμενο ορίζεται στο Loaded και όχι στον constructor: το _adminOnly μπαίνει με object
        // initializer (βλ. RequireAdmin), δηλαδή ΜΕΤΑ τον constructor.
        Loaded += (_, _) => { if (_adminOnly) PromptText.Text = "Βάλε τον κωδικό admin"; };
    }

    /// <summary>Ποιο σημείο ξεκλειδώνει αυτό το παράθυρο (βλ. StaffRight) — καθορίζει ΠΟΙΟΙ κωδικοί
    /// προσωπικού γίνονται δεκτοί.</summary>
    private string _right = StaffRight.Stats;

    /// <summary>Ο κωδικός που μπήκε και δέχτηκε — τον κρατά ο καλών ώστε να μην ξαναρωτήσει για κάτι
    /// που ανοίγει ο ίδιος κωδικός (βλ. MainWindow: Στατιστικά -> Ιστορικό).</summary>
    public string? AcceptedPin { get; private set; }

    /// <summary>Δείχνει το παράθυρο κωδικού· true μόνο αν μπήκε κωδικός που ανοίγει ΑΥΤΟ το σημείο.</summary>
    public static bool Require(Window owner, string right) => RequirePin(owner, right) is not null;

    /// <summary>
    /// Χωρίς παράθυρο-γονιό — για την εφαρμογή ΖΩΝΤΑΝΕΣ ΠΑΡΑΓΓΕΛΙΕΣ, που ζητάει κωδικό ΠΡΙΝ ανοίξει
    /// οτιδήποτε άλλο (βλ. App.StartLiveBoard): δεν υπάρχει ακόμα παράθυρο να το κεντράρει.
    /// </summary>
    public static bool RequireStandalone(string right)
    {
        var dialog = new PinDialog
        {
            _right = right,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
        };
        return dialog.ShowDialog() == true;
    }

    /// <summary>Ίδιο με το Require, αλλά επιστρέφει ΤΟΝ ΚΩΔΙΚΟ που δέχτηκε (null = ακυρώθηκε).</summary>
    public static string? RequirePin(Window owner, string right)
    {
        var dialog = new PinDialog { Owner = owner, _right = right };
        return dialog.ShowDialog() == true ? dialog.AcceptedPin : null;
    }

    /// <summary>Το ίδιο παράθυρο, αλλά δέχεται ΜΟΝΟ τον κωδικό admin — για την οθόνη των κωδικών
    /// προσωπικού, που δεν πρέπει να την ανοίγει υπεύθυνος (βλ. SettingsStore.VerifyAdminPin).</summary>
    public static bool RequireAdmin(Window owner) =>
        new PinDialog { Owner = owner, _adminOnly = true }.ShowDialog() == true;

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
        var ok = _adminOnly
            ? SettingsStore.Instance.VerifyAdminPin(_entered)
            : SettingsStore.Instance.VerifyPin(_entered, _right);
        if (ok)
        {
            AcceptedPin = _entered;
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
