using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Threading;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

public partial class NetworkSettingsWindow : Window
{
    private readonly SettingsStore _store = SettingsStore.Instance;
    private readonly DispatcherTimer _clientStatusTimer;

    public NetworkSettingsWindow()
    {
        InitializeComponent();
        RefreshUi();

        // Δείχνει live αν το δεύτερο ταμείο φτάνει το κύριο, ενόσω είναι ανοιχτό αυτό το παράθυρο —
        // χωρίς αυτό, μια αποτυχημένη σύνδεση ήταν εντελώς αόρατη μέχρι να δοκιμάσει κανείς να ανοίξει τραπέζι.
        _clientStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _clientStatusTimer.Tick += (_, _) => RefreshClientStatus();
        _clientStatusTimer.Start();
        RefreshClientStatus();
        Closed += (_, _) => _clientStatusTimer.Stop();
    }

    private void RefreshClientStatus()
    {
        if (_store.Settings.NetworkMode != "client")
        {
            ClientStatusText.Text = "";
            return;
        }
        ClientStatusText.Text = RemoteSync.LastError is null
            ? "✓ Συνδεδεμένο με το κύριο ταμείο"
            : "✗ Δεν φτάνει το κύριο ταμείο: " + RemoteSync.LastError;
        ClientStatusText.Foreground = RemoteSync.LastError is null
            ? System.Windows.Media.Brushes.SeaGreen
            : System.Windows.Media.Brushes.Firebrick;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void ApplyShopAddress_Click(object sender, RoutedEventArgs e)
    {
        _store.SetShopAddress(ShopAddressBox.Text);
        MessageBox.Show("Αποθηκεύτηκε.", "Χάρτης διανομής", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ApplyGoogleMapsKey_Click(object sender, RoutedEventArgs e)
    {
        _store.SetGoogleMapsApiKey(GoogleMapsKeyBox.Text);
        MessageBox.Show("Αποθηκεύτηκε.", "Google Maps", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CopyWaiterAddress_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(WaiterAddressText.Text);
    }

    private void HostMode_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Αυτό το ταμείο θα γίνει το ΚΥΡΙΟ (κρατάει τα δεδομένα). Χρειάζεται επανεκκίνηση της εφαρμογής. Συνέχεια;",
            "Κύριο ταμείο", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;
        _store.SetNetworkMode("host", _store.Settings.HostAddress);
        RefreshUi();
    }

    private void ClientMode_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Αυτό το ταμείο θα γίνει το ΔΕΥΤΕΡΟ (διαβάζει/γράφει πάνω στο κύριο ταμείο). Χρειάζεται επανεκκίνηση της εφαρμογής. Συνέχεια;",
            "Δεύτερο ταμείο", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;
        _store.SetNetworkMode("client", _store.Settings.HostAddress);
        RefreshUi();
    }

    private void ApplyHostAddress_Click(object sender, RoutedEventArgs e)
    {
        // Η διεύθυνση του ΚΥΡΙΟΥ ταμείου δεν μπορεί να είναι αυτός ο ίδιος υπολογιστής: το ταμείο θα
        // προωθούσε κάθε παραγγελία στον εαυτό του, ατέρμονα, και δεν θα καταγραφόταν ποτέ καμία.
        // Η προστασία υπάρχει και στο RemoteSync, αλλά εκεί ενεργεί σιωπηλά — καλύτερα να το μάθει
        // εδώ, τη στιγμή που το γράφει, παρά να απορεί αργότερα γιατί «δεν συνδέεται».
        var host = HostAddressBox.Text.Trim();
        if (RemoteSync.IsThisMachine(host))
        {
            MessageBox.Show(
                "Αυτή είναι η διεύθυνση ΑΥΤΟΥ του υπολογιστή.\n\n" +
                "Στο πεδίο αυτό γράφεις τη διεύθυνση του ΑΛΛΟΥ ταμείου — εκείνου που κρατάει τα δεδομένα.",
                "Λάθος διεύθυνση", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _store.SetNetworkMode(_store.Settings.NetworkMode, host);
        MessageBox.Show(
            "Αποθηκεύτηκε. Κλείσε και ξανάνοιξε την εφαρμογή για να πιάσει η αλλαγή.",
            "Δεύτερο ταμείο", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ApplyAmiConfig_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(AmiPortBox.Text, out var port) || port < 1)
        {
            MessageBox.Show("Η θύρα AMI πρέπει να είναι αριθμός.", "Αναγνώριση κλήσεων",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _store.SetAmiConfig(UcmHostBox.Text, port, AmiUserBox.Text, AmiPasswordBox.Text);
        MessageBox.Show(
            "Αποθηκεύτηκε. Κλείσε και ξανάνοιξε την εφαρμογή για να πιάσει η αλλαγή.",
            "Αναγνώριση κλήσεων", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>IPv4 αυτού του υπολογιστή στο τοπικό δίκτυο — για να την αντιγράψει στο δεύτερο ταμείο.</summary>
    private static string? GetLocalIPv4()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName()).AddressList
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void RefreshUi()
    {
        WaiterAddressText.Text = WaiterApiService.GetLanAddress();

        ShopAddressBox.Text = _store.Settings.ShopAddress;
        GoogleMapsKeyBox.Text = _store.Settings.GoogleMapsApiKey;

        UcmHostBox.Text = _store.Settings.UcmHost;
        AmiPortBox.Text = _store.Settings.AmiPort.ToString();
        AmiUserBox.Text = _store.Settings.AmiUsername;
        AmiPasswordBox.Text = _store.Settings.AmiPassword;

        Highlight(HostModeBtn, _store.Settings.NetworkMode != "client");
        Highlight(ClientModeBtn, _store.Settings.NetworkMode == "client");
        HostAddressBox.Text = _store.Settings.HostAddress;
        var ip = GetLocalIPv4();
        ThisPcAddressText.Text = ip is null
            ? ""
            : "Η IP αυτού του υπολογιστή στο δίκτυο: " + ip
              + (_store.Settings.NetworkMode == "client" ? "" : " ← αυτή θα γράψεις στο δεύτερο ταμείο");
        RefreshClientStatus();
    }

    private static void Highlight(System.Windows.Controls.Button button, bool active)
    {
        button.SetResourceReference(BackgroundProperty, active ? "Accent100" : "Bg");
        button.SetResourceReference(BorderBrushProperty, active ? "Accent" : "Divider");
        button.BorderThickness = new Thickness(active ? 2 : 1);
    }
}
