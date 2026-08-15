using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using PittaPos.App.Services;
using PittaPos.Core.Models;

namespace PittaPos.App.Views;

/// <summary>
/// Χάρτης διανομής — δείχνει τις παραγγελίες που παραδίδει δικός μας διανομέας (ΔΙΑΝΟΜΗ, και BOX — βλ.
/// OrderWizardViewModel.ShowCustomerForm· e-food/Wolt όχι, αυτές τις παραδίδει η ίδια η πλατφόρμα) που
/// είναι ακόμα σε αναμονή, δηλαδή δεν έχουν περαστεί ακόμα σε κανάλι — μόλις ο ταμίας τις περάσει στον
/// Διανομέα από τις Ζωντανές Παραγγελίες, βγαίνουν από τον χάρτη (έχουν πλέον αναληφθεί, δεν χρειάζεται
/// υπενθύμιση εδώ). Leaflet/OpenStreetMap (βλ. DeliveryRouteService). Ξεκίνημα χωρίς κλειδί/χρέωση
/// Google Maps — να αλλάξει αργότερα αν χρειαστεί πιο αξιόπιστη/γρήγορη υπηρεσία σε παραγωγική χρήση.
/// </summary>
public partial class DeliveryMapWindow : Window
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private bool _closed;
    private bool _syncInFlight;
    /// <summary>True αφού ολοκληρωθεί το EnsureCoreWebView2Async — πριν από αυτό, ένα SyncPinsAsync (π.χ.
    /// από αυτόματο συγχρονισμό που σηκώθηκε πολύ νωρίς, πριν προλάβει να φορτώσει το παράθυρο) θα
    /// πετούσε (CoreWebView2 not initialized). Το αρχικό LoadAsync θα διαβάσει ούτως ή άλλως την τρέχουσα
    /// κατάσταση παραγγελιών μόλις ετοιμαστεί, οπότε είναι ασφαλές να αγνοηθεί ένα πρόωρο auto-sync.</summary>
    private bool _shellReady;
    private readonly DispatcherTimer _autoSyncDebounce;
    /// <summary>Συντεταγμένες του καταστήματος από το τελευταίο επιτυχές συγχρονισμό — αφετηρία για τη
    /// διαδρομή προς μια συγκεκριμένη πινέζα όταν ο ταμίας την πατήσει (βλ. OnStopClicked).</summary>
    private (double Lat, double Lon)? _shopCoords;
    /// <summary>Σύνολο (#παραγγελία=διεύθυνση) του τελευταίου πραγματικού συγχρονισμού — βλ. SyncPinsAsync,
    /// ώστε ένα άσχετο OrderBoardService.Changed να μην ξανακάνει αίτημα διαδρομής χωρίς λόγο.</summary>
    private string? _lastSyncedSignature;

    /// <summary>Μήνυμα από τη σελίδα του χάρτη: <c>Kind</c> = "stop" (πατήθηκε πινέζα → διαδρομή) ή
    /// "pick" (πατήθηκε σημείο στον χάρτη ενώ διορθώνουμε τη θέση μιας διεύθυνσης).</summary>
    private sealed record ClickedStop(double Lat, double Lon, string? Kind);

    /// <summary>Η διεύθυνση της οποίας το σημείο διορθώνεται αυτή τη στιγμή — null όταν δεν διορθώνουμε.</summary>
    private string? _fixingAddress;

    public DeliveryMapWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);

        // Νέα παραγγελία διανομής (ή μία που πέρασε σε κανάλι/έφυγε από την αναμονή) σηκώνει το
        // OrderBoardService.Changed — συγχρονίζουμε αυτόματα τις πινέζες χωρίς να χρειάζεται ο ταμίας να
        // πατήσει «Ανανέωση». Debounce 800ms ώστε πολλές αλλαγές πολύ κοντά μεταξύ τους (π.χ. δύο
        // παραγγελίες στη σειρά) να γίνουν ένας συγχρονισμός, όχι πολλαπλά ταυτόχρονα αιτήματα.
        _autoSyncDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _autoSyncDebounce.Tick += async (_, _) =>
        {
            _autoSyncDebounce.Stop();
            await SyncPinsAsync(fitView: false);
        };
        OrderBoardService.Instance.Changed += OnOrdersChanged;

        Loaded += async (_, _) => await LoadAsync();
        // Η γεωκωδικοποίηση (βλ. SyncPinsAsync) μπορεί να πάρει αρκετά δευτερόλεπτα για πολλές παραγγελίες —
        // αν ο ταμίας κλείσει το παράθυρο πριν προλάβει να τελειώσει, το WebView2 έχει ήδη καταστραφεί
        // και ένα ExecuteScriptAsync μετά την επιστροφή θα πετούσε (βλ. crash-log, «CoreWebView2 not
        // initialized») χωρίς να φαίνεται τίποτα στην οθόνη — λευκή σελίδα, όχι σφάλμα ορατό στον χρήστη.
        Closed += (_, _) =>
        {
            _closed = true;
            _autoSyncDebounce.Stop();
            OrderBoardService.Instance.Changed -= OnOrdersChanged;
        };
    }

    private void OnOrdersChanged()
    {
        if (_closed)
            return;
        Dispatcher.Invoke(() =>
        {
            _autoSyncDebounce.Stop();
            _autoSyncDebounce.Start();
        });
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await SyncPinsAsync(fitView: true);

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private async void Retry_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    /// <summary>Τρέχει μία φορά όταν ανοίγει το παράθυρο — αρχικοποιεί το WebView2 και φορτώνει τον
    /// κενό χάρτη (μόνο πλακίδια), μετά καλεί το πρώτο SyncPinsAsync. Η «Ανανέωση» και ο αυτόματος
    /// συγχρονισμός (OnOrdersChanged) καλούν κατευθείαν το SyncPinsAsync — δεν ξαναφορτώνουν ποτέ όλη τη
    /// σελίδα, μόνο ενημερώνουν τις πινέζες πάνω στον ήδη φορτωμένο χάρτη (πολύ πιο γρήγορο).</summary>
    private async Task LoadAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingIcon.Text = "🗺";
        LoadingText.Text = "Φόρτωση χάρτη...";
        RetryButton.Visibility = Visibility.Collapsed;

        try
        {
            await Map.EnsureCoreWebView2Async();
            if (_closed)
                return;

            if (DeliveryRouteService.UsingGoogle)
            {
                // Google Directions/Geocoding δεν έχουν το τεχνητό όριο <=1 αίτημα/δευτ. του Nominatim — αρκετά
                // γρήγορο ώστε να μη χρειάζεται προοδευτική φόρτωση/αυτόματος συγχρονισμός, χτίζουμε τη σελίδα
                // μία φορά με τα πάντα μέσα σε κάθε «Ανανέωση» (βλ. Refresh_Click στο SyncPinsAsync παρακάτω).
                _shellReady = true;
                await SyncPinsAsync(fitView: true);
                LoadingOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            // Το marker.on('click', ...) της σελίδας στέλνει postMessage όταν ο ταμίας πατήσει μια πινέζα
            // παράδοσης — εδώ υπολογίζουμε τη διαδρομή κατάστημα→πινέζα και τη σχεδιάζουμε (OnStopClicked).
            Map.CoreWebView2.WebMessageReceived += OnStopClicked;

            var navDone = new TaskCompletionSource();
            void OnNavCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e) => navDone.TrySetResult();
            Map.CoreWebView2.NavigationCompleted += OnNavCompleted;
            Map.NavigateToString(BuildShellHtmlLeaflet());
            await navDone.Task;
            Map.CoreWebView2.NavigationCompleted -= OnNavCompleted;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            if (_closed)
                return;

            // Μόνο τώρα, αφού η σελίδα (με το window.addPins ήδη ορισμένο) έχει τελειώσει το φόρτωμα, είναι
            // ασφαλές να τρέξει ένα SyncPinsAsync — είτε από εδώ είτε από ένα auto-sync/Ανανέωση που έτυχε να
            // σηκωθεί νωρίτερα (βλ. _shellReady).
            _shellReady = true;
            // Δείξε αμέσως την πινέζα του καταστήματος (γεωκωδικοποιείται μόνη της, μία φορά, ίδιο cache
            // με BuildRouteAsync παρακάτω — άρα καθόλου επιπλέον καθυστέρηση) αντί να μείνει ο χάρτης κενός
            // όσο περιμένουμε τη γεωκωδικοποίηση/βελτιστοποίηση όλων των παραδόσεων.
            await ShowShopPinQuicklyAsync();
            await SyncPinsAsync(fitView: true);
        }
        catch (Exception ex)
        {
            // Π.χ. EnsureCoreWebView2Async πετάει UnauthorizedAccessException (0x80070005) όταν ο φάκελος
            // προφίλ του WebView2 είναι ακόμα κλειδωμένος από ένα προηγούμενο, μη τερματισμένο σωστά
            // instance της εφαρμογής — χωρίς αυτό το catch ο ταμίας έβλεπε «Φόρτωση χάρτη...» για πάντα,
            // χωρίς καμία ένδειξη ότι κάτι πήγε στραβά (το σφάλμα πήγαινε μόνο στο crash-log.txt).
            if (_closed)
                return;
            AppLog.Write("delivery-map", $"Αποτυχία φόρτωσης χάρτη: {ex}");
            LoadingIcon.Text = "⚠";
            LoadingText.Text = "Ο χάρτης δεν άνοιξε. Δοκίμασε ξανά — αν επιμένει, κλείσε και ξανάνοιξε την εφαρμογή.";
            RetryButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Γεωκωδικοποιεί μόνο το κατάστημα (βλ. DeliveryRouteService.GetShopCoordsAsync, cached) και
    /// δείχνει την πινέζα του κατευθείαν στον μόλις φορτωμένο άδειο χάρτη — καλείται μία φορά στο αρχικό
    /// άνοιγμα, πριν ξεκινήσει το (πιθανώς αργό, throttled) SyncPinsAsync για τις παραδόσεις. Μόνο για το
    /// OSM/Leaflet μονοπάτι· το Google φτιάχνει τη δική του σελίδα από την αρχή σε κάθε συγχρονισμό.</summary>
    private async Task ShowShopPinQuicklyAsync()
    {
        if (DeliveryRouteService.UsingGoogle)
            return;
        var shopAddr = SettingsStore.Instance.Settings.ShopAddress;
        if (string.IsNullOrWhiteSpace(shopAddr))
            return;
        var geo = await DeliveryRouteService.GetShopCoordsAsync();
        if (_closed || geo is not { } g)
            return;
        _shopCoords = (g.Lat, g.Lon);
        var shopJson = JsonSerializer.Serialize(new { lat = g.Lat, lon = g.Lon, label = "Κατάστημα" });
        try { await Map.ExecuteScriptAsync("showShopPin(" + Json(shopJson) + ")"); } catch { }
    }

    /// <summary>Ενημερώνει τις πινέζες πάνω στον ήδη φορτωμένο χάρτη ώστε να αντιστοιχούν στις τρέχουσες
    /// εκκρεμείς παραγγελίες διανομής — καλείται από το αρχικό άνοιγμα, το κουμπί «Ανανέωση», και
    /// αυτόματα σε κάθε αλλαγή στο OrderBoardService (νέα παραγγελία, ή μία που πέρασε σε κανάλι).
    /// <paramref name="fitView"/> = true (χειροκίνητο άνοιγμα/ανανέωση) κάνει zoom να χωρέσουν όλες οι
    /// πινέζες· false (αυτόματος συγχρονισμός στο παρασκήνιο) αφήνει το view όπως το έχει ο ταμίας, ώστε
    /// να μην «πηδάει» ο χάρτης ενώ τον κοιτάει.</summary>
    private async Task SyncPinsAsync(bool fitView)
    {
        if (_closed || _syncInFlight || !_shellReady)
            return;
        _syncInFlight = true;
        RefreshBtn.IsEnabled = false;

        try
        {
            // ΔΙΑΝΟΜΗ ή ΕΦΑΡΜΟΓΕΣ+BOX — και τις δύο τις παραδίδει δικός μας διανομέας (βλ. class doc πάνω
            // από το DeliveryMapWindow), άρα και οι δύο χρειάζονται πινέζα εδώ. e-food/Wolt όχι.
            var deliveries = OrderBoardService.Instance.Orders
                .Where(o => o.IsPending && !string.IsNullOrWhiteSpace(o.Address)
                    && (o.Type == OrderType.Delivery || (o.Type == OrderType.Apps && o.Channel == "BOX")))
                .Select(o => ("#" + o.OrderNumber + " · " + o.Name, CleanAddressForGeocoding(o.Address)))
                .ToList();

            // Ο αυτόματος συγχρονισμός στο παρασκήνιο (fitView=false) σηκώνεται σε ΚΑΘΕ αλλαγή στο
            // OrderBoardService.Changed -- π.χ. αποστολή μιας e-food παραγγελίας σε κανάλι, ακύρωση κ.λπ. --
            // όχι μόνο σε αλλαγές που αφορούν πραγματικά τη διανομή. Χωρίς αυτόν τον έλεγχο, κάθε τέτοιο
            // άσχετο event ξανάκανε πλήρες αίτημα βελτιστοποίησης διαδρομής (OSRM/Google Directions -- η
            // γεωκωδικοποίηση είναι ήδη cached, αυτό όχι) ακόμα κι όταν το σύνολο παραδόσεων ήταν ίδιο με
            // το προηγούμενο συγχρονισμένο -- αχρείαστη καθυστέρηση/δικτυακή κίνηση όσο ο χάρτης είναι
            // ανοιχτός σε ένα φουλ μαγαζί. Το χειροκίνητο «Ανανέωση» (fitView=true) πάντα ξανατρέχει.
            var signature = string.Join("|", deliveries.Select(d => d.Item1 + "=" + d.Item2));
            if (!fitView && signature == _lastSyncedSignature)
                return;
            _lastSyncedSignature = signature;

            if (DeliveryRouteService.UsingGoogle)
            {
                if (deliveries.Count == 0)
                {
                    Map.NavigateToString(BuildHtmlGoogle(null, [], []));
                    ShowFailedAddresses([]);
                }
                else
                {
                    var shopAddress = SettingsStore.Instance.Settings.ShopAddress;
                    var result = await DeliveryRouteService.BuildRouteAsync(shopAddress, deliveries);
                    if (_closed)
                        return;
                    Map.NavigateToString(BuildHtmlGoogle(result.Shop, result.Stops, result.Geometry));
                    ShowFailedAddresses(result.FailedAddresses);
                }
                return;
            }

            var shopAddr = SettingsStore.Instance.Settings.ShopAddress;

            if (deliveries.Count == 0)
            {
                // Καμία παραγγελία σε αναμονή δεν σημαίνει άδειος χάρτης — η πινέζα του καταστήματος
                // (βλ. ShowShopPinQuicklyAsync στο αρχικό άνοιγμα) πρέπει να παραμείνει ως σημείο αναφοράς.
                // Πριν, αυτό το κλαδί καθάριζε τα πάντα (addPins(null, [], ...)) — έτσι η στιγμιαία πινέζα
                // του μαγαζιού έσβηνε αμέσως μόλις έφτανε εδώ ένα SyncPinsAsync χωρίς παραδόσεις.
                var shopOnly = await DeliveryRouteService.GetShopCoordsAsync();
                if (_closed)
                    return;
                _shopCoords = shopOnly;
                var shopStop = shopOnly is { } sc ? new RouteStop("Κατάστημα", shopAddr, sc.Lat, sc.Lon) : null;
                await Map.ExecuteScriptAsync(BuildAddPinsScript(shopStop, [], fitView));
                ShowFailedAddresses([]);
                return;
            }

            PinsLoadingBadge.Visibility = Visibility.Visible;
            // Καθώς γεωκωδικοποιείται (throttled, μπορεί να πάρει δευτερόλεπτα με πολλές νέες παραδόσεις),
            // δείξε κάθε πινέζα παράδοσης αμέσως μόλις λυθεί, με τη σειρά προτεραιότητας (παλαιότερη
            // παραγγελία πρώτη — ίδια σειρά με το `deliveries` παραπάνω) αντί να μείνει ο χάρτης να δείχνει
            // μόνο το κατάστημα μέχρι να λυθούν ΟΛΕΣ. Οι πινέζες αυτές είναι προσωρινές· ο τελικός
            // ExecuteScriptAsync(BuildAddPinsScript...) παρακάτω σβήνει τα πάντα και τα ξαναφτιάχνει με τη
            // σωστή, βελτιστοποιημένη σειρά διαδρομής — οπότε ο αριθμός σε μια πινέζα μπορεί να αλλάξει
            // στο τέλος αν το OSRM βρήκε καλύτερη σειρά επίσκεψης απ' την προτεραιότητα καταχώρησης.
            var routeResult = await DeliveryRouteService.BuildRouteAsync(shopAddr, deliveries,
                onStopGeocoded: async (stop, index) =>
                {
                    if (_closed)
                        return;
                    var stopJson = JsonSerializer.Serialize(new { lat = stop.Lat, lon = stop.Lon, label = stop.Label, address = stop.Address });
                    try { await Map.ExecuteScriptAsync("addStopPinPreview(" + Json(stopJson) + "," + index + ")"); } catch { }
                });
            if (_closed)
                return;

            _shopCoords = routeResult.Shop is { } shop ? (shop.Lat, shop.Lon) : null;
            await Map.ExecuteScriptAsync(BuildAddPinsScript(routeResult.Shop, routeResult.Stops, fitView));
            ShowFailedAddresses(routeResult.FailedAddresses);
        }
        catch (Exception ex)
        {
            // Καλείται από async void (κουμπί Ανανέωση, αλλά και αυτόματα σε κάθε αλλαγή παραγγελιών) —
            // χωρίς αυτό το catch, οποιοδήποτε σφάλμα εδώ (πεσμένο δίκτυο, WebView2 που έκλεισε στο
            // ενδιάμεσο, απάντηση geocoder που δεν διαβάζεται) ΕΡΙΧΝΕ ΟΛΟ ΤΟ ΤΑΜΕΙΟ, στη μέση της βάρδιας.
            // Ο χάρτης απλά μένει όπως ήταν· το επόμενο άνοιγμα/ανανέωση ξαναδοκιμάζει.
            AppLog.Write("delivery-map", $"Αποτυχία ανανέωσης πινέζων: {ex}");
        }
        finally
        {
            PinsLoadingBadge.Visibility = Visibility.Collapsed;
            _syncInFlight = false;
            if (!_closed)
                RefreshBtn.IsEnabled = true;
        }
    }

    /// <summary>
    /// Δείχνει ποιες παραγγελίες δεν μπήκαν στον χάρτη επειδή δεν εντοπίστηκε η διεύθυνσή τους —
    /// αλλιώς απλά έλειπαν, χωρίς καμία εξήγηση, και ο ταμίας δεν είχε τρόπο να καταλάβει αν φταίει
    /// η διεύθυνση ή αν χάθηκε η παραγγελία. Ο αριθμός παραγγελίας είναι μέσα στο label (βλ.
    /// SyncPinsAsync), οπότε φαίνεται ακριβώς ποια πρέπει να ψαχτεί χειροκίνητα.
    /// </summary>
    private void ShowFailedAddresses(IReadOnlyList<string> failed)
    {
        if (_closed)
            return;

        // Κανένα σημείο για το μαγαζί σημαίνει χάρτης χωρίς το μαγαζί σου και καμία διαδρομή — και μέχρι
        // τώρα αυτό συνέβαινε σιωπηλά. Το λέμε καθαρά, μαζί με τον γρήγορο τρόπο να λυθεί.
        if (_shopCoords is null)
        {
            ShowShopAddressMissing();
            return;
        }

        if (failed.Count == 0)
        {
            FailedAddressesBadge.Visibility = Visibility.Collapsed;
            return;
        }

        FailedAddressesTitle.Text = failed.Count == 1
            ? "⚠ 1 παραγγελία δεν μπήκε στον χάρτη — δεν βρέθηκε η διεύθυνση"
            : $"⚠ {failed.Count} παραγγελίες δεν μπήκαν στον χάρτη — δεν βρέθηκαν οι διευθύνσεις";
        FailedAddressesList.Text = string.Join("\n", failed);
        FailedAddressesBadge.Visibility = Visibility.Visible;
    }

    /// <summary>Το ένα πράγμα που ρυθμίζεται μία φορά και χωρίς αυτό δεν δουλεύει τίποτα στον χάρτη:
    /// ούτε πινέζα μαγαζιού, ούτε διαδρομή προς καμία παράδοση (η διαδρομή ξεκινά από το μαγαζί).</summary>
    private void ShowShopAddressMissing()
    {
        if (_closed)
            return;
        FailedAddressesTitle.Text = "⚠ Δεν έχει οριστεί η διεύθυνση του μαγαζιού";
        FailedAddressesList.Text = "Ρυθμίσεις → Δίκτυο → «Διεύθυνση καταστήματος». Γράφεται μία φορά. " +
            "Χωρίς αυτήν δεν μπαίνει η πινέζα του μαγαζιού και δεν υπολογίζεται καμία διαδρομή, " +
            "γιατί κάθε διαδρομή ξεκινά από εκεί.";
        FailedAddressesBadge.Visibility = Visibility.Visible;
    }

    /// <summary>Ο ταμίας πάτησε μια πινέζα παράδοσης στον χάρτη (βλ. marker.on('click',...) στο
    /// <see cref="BuildShellHtmlLeaflet"/>) — υπολογίζει τη διαδρομή κατάστημα→αυτή τη διεύθυνση και τη
    /// σχεδιάζει πάνω στον ήδη ανοιχτό χάρτη μέσω <c>window.showStopRoute</c>. Χωρίς διεύθυνση
    /// καταστήματος δεν υπάρχει αφετηρία, οπότε απλά αγνοείται το κλικ.</summary>
    private async void OnStopClicked(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        ClickedStop? clicked;
        try
        {
            clicked = JsonSerializer.Deserialize<ClickedStop>(e.WebMessageAsJson, JsonOpts);
        }
        catch (JsonException)
        {
            return;
        }
        if (clicked is null)
            return;

        // Διορθώνουμε θέση διεύθυνσης: το κλικ ΔΕΝ είναι αίτημα διαδρομής, είναι η σωστή πόρτα.
        if (clicked.Kind == "pick")
        {
            await SavePickedPointAsync(clicked);
            return;
        }

        // Η διαδρομή ξεκινά ΠΑΝΤΑ από το μαγαζί. Χωρίς σημείο καταστήματος δεν υπάρχει αφετηρία, οπότε
        // το πάτημα της πινέζας δεν είχε τι να σχεδιάσει — και δεν γινόταν απολύτως τίποτα, χωρίς καμία
        // εξήγηση. Τώρα το λέει.
        if (_shopCoords is not { } shop)
        {
            ShowShopAddressMissing();
            return;
        }

        try
        {
            await DrawRouteToStopAsync(shop, clicked);
        }
        catch (Exception ex)
        {
            // async void handler — σφάλμα εδώ (δίκτυο, κλειστό WebView2) θα έριχνε όλο το ταμείο.
            AppLog.Write("delivery-map", $"Αποτυχία σχεδίασης διαδρομής: {ex}");
        }
    }

    // ---- διόρθωση σημείου διεύθυνσης πάνω στον χάρτη ----
    //
    // Η αναζήτηση διεύθυνσης (OSM/Google) βγάζει συχνά λάθος σημείο σε ελληνικές διευθύνσεις, και μέχρι
    // τώρα το ίδιο λάθος επαναλαμβανόταν σε κάθε παραγγελία στην ίδια διεύθυνση χωρίς να μπορεί να
    // διορθωθεί. Εδώ ο ταμίας δείχνει τη σωστή θέση μία φορά· από εκεί και πέρα η πινέζα έρχεται από τα
    // δικά μας σημεία (βλ. AddressPointsService) και δεν ρωτιέται ποτέ ξανά geocoder γι' αυτήν.

    /// <summary>Οι διευθύνσεις που παραδίδονται τώρα — υποψήφιες για διόρθωση. Πρώτο στη λίστα το ίδιο το
    /// κατάστημα: η δική του πινέζα λείπει ή πέφτει λάθος με την ίδια ευκολία, και χωρίς αυτήν δεν υπάρχει
    /// ούτε αφετηρία διαδρομής.</summary>
    private static List<(string Label, string Address)> FixablePoints()
    {
        // Η διεύθυνση του μαγαζιού γράφεται μία φορά στις Ρυθμίσεις. Μπαίνει κι αυτή στη λίστα, γιατί η
        // πινέζα της μπορεί να πέσει λάθος όπως κάθε άλλη — αλλά μόνο αφού έχει οριστεί.
        var shopAddress = SettingsStore.Instance.Settings.ShopAddress.Trim();
        var points = new List<(string Label, string Address)>();
        if (shopAddress.Length > 0)
            points.Add(("🏠 ΤΟ ΜΑΓΑΖΙ ΜΑΣ", shopAddress));

        points.AddRange(OrderBoardService.Instance.Orders
            .Where(o => o.IsPending && !string.IsNullOrWhiteSpace(o.Address)
                && (o.Type == OrderType.Delivery || (o.Type == OrderType.Apps && o.Channel == "BOX")))
            .Select(o => ("#" + o.OrderNumber + " · " + o.Name, CleanAddressForGeocoding(o.Address))));

        return points;
    }

    private void FixPin_Click(object sender, RoutedEventArgs e)
    {
        if (!_shellReady)
            return;

        FixPinList.ItemsSource = FixablePoints().Select(d => d.Label + " — " + d.Address).ToList();
        FixPinList.SelectedIndex = -1;
        FixPinPanel.Visibility = Visibility.Visible;
    }

    private void CancelFixPin_Click(object sender, RoutedEventArgs e) => StopPicking();

    private async void FixPinList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (FixPinList.SelectedIndex < 0)
            return;
        var points = FixablePoints();
        if (FixPinList.SelectedIndex >= points.Count)
            return;

        var chosen = points[FixPinList.SelectedIndex];
        _fixingAddress = chosen.Address;
        FixPinPanel.Visibility = Visibility.Collapsed;
        PickPointText.Text = "Πάτα στον χάρτη τη σωστή θέση για " + chosen.Label;
        PickPointBadge.Visibility = Visibility.Visible;
        try { await Map.ExecuteScriptAsync("setPickMode(true)"); } catch { }
    }

    private async Task SavePickedPointAsync(ClickedStop picked)
    {
        var address = _fixingAddress;
        StopPicking();
        if (address is null)
            return;

        AddressPointsService.Instance.Set(address, picked.Lat, picked.Lon, manual: true);
        AppLog.Write("delivery-map",
            $"Το σημείο της διεύθυνσης «{address}» ορίστηκε χειροκίνητα σε {picked.Lat:0.00000},{picked.Lon:0.00000}.");
        // Ξαναχτίζει τις πινέζες με το νέο σημείο· fitView=true γιατί είναι συνειδητή ενέργεια του ταμία.
        _lastSyncedSignature = null;
        await SyncPinsAsync(fitView: true);
    }

    private async void StopPicking()
    {
        _fixingAddress = null;
        FixPinPanel.Visibility = Visibility.Collapsed;
        PickPointBadge.Visibility = Visibility.Collapsed;
        if (_closed)
            return;
        try { await Map.ExecuteScriptAsync("setPickMode(false)"); } catch { }
    }

    private async Task DrawRouteToStopAsync((double Lat, double Lon) shop, ClickedStop clicked)
    {
        var geometry = await DeliveryRouteService.GetRouteGeometryAsync(shop, (clicked.Lat, clicked.Lon));
        if (_closed)
            return;

        // Το δωρεάν OSRM router (router.project-osrm.org) μπορεί να μην είναι προσβάσιμο από κάποια
        // δίκτυα (π.χ. αποτυχία TLS handshake, δοκιμασμένο) — χωρίς πραγματική διαδρομή, δείξε τουλάχιστον
        // μια ευθεία γραμμή κατάστημα→πινέζα (διακεκομμένη, ώστε να φαίνεται ότι δεν είναι πραγματική
        // διαδρομή οδήγησης) αντί να μη φανεί τίποτα.
        var isStraightLine = geometry.Count < 2;
        var lineGeometry = isStraightLine
            ? new List<(double Lat, double Lon)> { shop, (clicked.Lat, clicked.Lon) }
            : geometry;

        var geometryJson = JsonSerializer.Serialize(lineGeometry.Select(g => new { lat = g.Lat, lon = g.Lon }));
        await Map.ExecuteScriptAsync("showStopRoute(" + Json(geometryJson) + "," + (isStraightLine ? "true" : "false") + ")");
    }

    /// <summary>Το BoardOrder.Address είναι «οδός+αριθμός, περιοχή, Τ.Κ. · όροφος — σημειώσεις διανομής»
    /// (βλ. OrderWizardViewModel.ComposeDeliveryInfo, πάντα με αυτή τη σειρά). Ο όροφος και οι ελεύθερες
    /// σημειώσεις (π.χ. «ΑΝΑΠΑΝΤΗΤΗ ΘΑ ΚΑΤΕΒΕΙ») δεν έχουν νόημα σε geocoder — δοκιμασμένο με Nominatim,
    /// τα κάνουν να μην επιστρέφει τίποτα. Η περιοχή/Τ.Κ. όμως χρειάζονται: μόνο η οδός, χωρίς αυτά,
    /// είναι πιο επικίνδυνο από το να μη βρεθεί τίποτα — για κοινά ονόματα δρόμων (π.χ. «28ης Οκτωβρίου»
    /// υπάρχει σε πολλές περιοχές της Αττικής) επιστρέφει ΛΑΝΘΑΣΜΕΝΗ τοποθεσία σιωπηλά.</summary>
    private static string CleanAddressForGeocoding(string address)
    {
        var idx = address.IndexOf(" — ", StringComparison.Ordinal);
        var withoutNotes = idx >= 0 ? address[..idx] : address;
        var floorIdx = withoutNotes.IndexOf(" · ", StringComparison.Ordinal);
        var withoutFloor = floorIdx >= 0 ? withoutNotes[..floorIdx] : withoutNotes;
        return withoutFloor;
    }

    /// <summary>Άδειος χάρτης Leaflet/OpenStreetMap (μόνο πλακίδια, χωρίς πινέζες) — φορτώνει αμέσως,
    /// αφήνει το <c>window.addPins(...)</c> για να προστεθούν markers/διαδρομή αργότερα μέσω
    /// <see cref="BuildAddPinsScript"/> χωρίς πλήρες reload της σελίδας (βλ. LoadAsync).</summary>
    private static string BuildShellHtmlLeaflet() =>
        "<!doctype html><html><head><meta charset='utf-8'/>"
        + "<link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css'/>"
        + "<style>html,body,#map{height:100%;margin:0;padding:0;}"
        + ".stop-icon{background:#ec3013;color:#fff;border-radius:50%;width:26px;height:26px;display:flex;"
        + "align-items:center;justify-content:center;font-weight:bold;font-family:sans-serif;font-size:13px;"
        + "border:2px solid #fff;box-shadow:0 1px 4px rgba(0,0,0,.4);}"
        + ".shop-icon{background:#201e1d;color:#fff;border-radius:50%;width:30px;height:30px;display:flex;"
        + "align-items:center;justify-content:center;font-size:16px;border:2px solid #fff;"
        + "box-shadow:0 1px 4px rgba(0,0,0,.4);}</style></head><body>"
        + "<div id='map'></div>"
        + "<script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>"
        + "<script>"
        + "var map = L.map('map').setView([38.0,23.7], 12);"
        + "L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', "
        + "{maxZoom: 19, attribution: '&copy; OpenStreetMap &copy; CARTO'}).addTo(map);"
        + "function numberedIcon(n) { return L.divIcon({className:'', html:'<div class=\"stop-icon\">'+n+'</div>', iconSize:[26,26]}); }"
        + "var shopIcon = L.divIcon({className:'', html:'<div class=\"shop-icon\">🏠</div>', iconSize:[30,30]});"
        // Οι πινέζες ζουν σε ξεχωριστό layer group, όχι απευθείας πάνω στο map — έτσι το addPins είναι
        // ασφαλές να ξανακληθεί (χειροκίνητη «Ανανέωση» ή αυτόματος συγχρονισμός σε κάθε νέα παραγγελία,
        // βλ. SyncPinsAsync) χωρίς να διπλασιάζονται οι παλιές πινέζες πάνω στις καινούριες.
        + "var pinsLayer = L.layerGroup().addTo(map);"
        + "window.addPins = function(shopJson, stopsJson, fitView) {"
        + "  pinsLayer.clearLayers();"
        + "  var shop = JSON.parse(shopJson), stops = JSON.parse(stopsJson);"
        + "  var pts = [];"
        + "  if (shop) { L.marker([shop.lat, shop.lon], {icon: shopIcon}).addTo(pinsLayer).bindPopup(shop.label); pts.push([shop.lat, shop.lon]); }"
        + "  stops.forEach(function(s, i) {"
        + "    var m = L.marker([s.lat, s.lon], {icon: numberedIcon(i+1)}).addTo(pinsLayer).bindPopup((i+1) + '. ' + s.label + ' — ' + s.address);"
        // Πάτημα πινέζας -> στέλνει τις συντεταγμένες της στο C# (OnStopClicked), που υπολογίζει τη
        // διαδρομή κατάστημα→αυτή τη στάση και την επιστρέφει μέσω showStopRoute παρακάτω. Δεν σχεδιάζουμε
        // πια τη συνολική διαδρομή όλων των στάσεων (κόκκινη γραμμή) — μόνο η διαδρομή προς τη
        // συγκεκριμένη πινέζα που πατήθηκε (βλ. showStopRoute), όπως ζήτησε ο χρήστης.
        + "    m.on('click', function() { if (!window.__pickMode) window.chrome.webview.postMessage({kind:'stop', lat: s.lat, lon: s.lon}); });"
        + "    pts.push([s.lat, s.lon]);"
        + "  });"
        // fitView=false στον αυτόματο συγχρονισμό στο παρασκήνιο — δεν μετακινούμε τον χάρτη ενώ τον
        // κοιτάει ο ταμίας, μόνο στο αρχικό άνοιγμα/χειροκίνητη «Ανανέωση».
        + "  if (fitView && pts.length > 0) { map.fitBounds(pts, {padding:[30,30], maxZoom:15}); }"
        + "};"
        // Δείχνει μόνο την πινέζα του καταστήματος, κατευθείαν στο αρχικό άνοιγμα του χάρτη — πριν
        // προλάβουν να γεωκωδικοποιηθούν οι παραδόσεις (βλ. DeliveryMapWindow.ShowShopPinQuicklyAsync).
        // Κεντράρει τον χάρτη πάνω του αντί να αφήσει την default view [38.0,23.7] όλης της Αττικής.
        + "window.showShopPin = function(shopJson) {"
        + "  pinsLayer.clearLayers();"
        + "  var shop = JSON.parse(shopJson);"
        + "  L.marker([shop.lat, shop.lon], {icon: shopIcon}).addTo(pinsLayer).bindPopup(shop.label);"
        + "  map.setView([shop.lat, shop.lon], 15);"
        + "};"
        // Προσθέτει ΜΙΑ πινέζα παράδοσης πάνω στην ήδη υπάρχουσα πινέζα καταστήματος, χωρίς να καθαρίζει
        // τις άλλες — καλείται μία φορά ανά παράδοση καθώς γεωκωδικοποιείται (βλ. SyncPinsAsync), ώστε να
        // «γεμίζει» ο χάρτης προοδευτικά με σειρά προτεραιότητας αντί να δείχνει τίποτα μέχρι να λυθούν
        // όλες. Δεν μετακινεί τον χάρτη (δεν κάνει fitBounds) — θα ήταν ενοχλητικό να «πηδάει» η προβολή σε
        // κάθε νέα πινέζα· το τελικό addPins παραπάνω κάνει το οριστικό fitBounds όταν όλα είναι έτοιμα.
        + "window.addStopPinPreview = function(stopJson, index) {"
        + "  var s = JSON.parse(stopJson);"
        + "  L.marker([s.lat, s.lon], {icon: numberedIcon(index)}).addTo(pinsLayer).bindPopup(index + '. ' + s.label + ' — ' + s.address);"
        + "};"
        // Τονισμένη γραμμή (μπλε) για τη διαδρομή προς τη συγκεκριμένη πινέζα που πάτησε ο ταμίας —
        // αντικαθιστά την προηγούμενη τονισμένη γραμμή αν υπήρχε.
        // Κατάσταση «δείξε τη σωστή θέση»: όσο είναι ανοιχτή, ένα κλικ οπουδήποτε στον χάρτη στέλνει τις
        // συντεταγμένες στο C# (SavePickedPointAsync) αντί να ζητά διαδρομή. Ο δείκτης γίνεται σταυρός,
        // ώστε να είναι φανερό ότι ο χάρτης περιμένει κλικ και δεν είναι απλά «κολλημένος».
        + "window.__pickMode = false;"
        + "window.setPickMode = function(on) {"
        + "  window.__pickMode = !!on;"
        + "  document.getElementById('map').style.cursor = on ? 'crosshair' : '';"
        + "};"
        + "map.on('click', function(e) {"
        + "  if (window.__pickMode) window.chrome.webview.postMessage({kind:'pick', lat: e.latlng.lat, lon: e.latlng.lng});"
        + "});"
        + "var activeStopRoute = null;"
        + "window.showStopRoute = function(geometryJson, isStraightLine) {"
        + "  var geometry = JSON.parse(geometryJson);"
        + "  if (activeStopRoute) { map.removeLayer(activeStopRoute); activeStopRoute = null; }"
        + "  var line = geometry.map(function(g){ return [g.lat, g.lon]; });"
        + "  if (line.length > 1) {"
        + "    activeStopRoute = L.polyline(line, {color:'#1a73e8', weight:5, opacity:0.9,"
        + "      dashArray: isStraightLine ? '8,8' : null}).addTo(map);"
        + "    map.fitBounds(activeStopRoute.getBounds(), {padding:[40,40]});"
        + "  }"
        + "};"
        + "</script></body></html>";

    /// <summary>Κλήση JS που καλεί το <c>window.addPins</c> της ήδη φορτωμένης σελίδας του
    /// <see cref="BuildShellHtmlLeaflet"/> με τα markers/διαδρομή — μέσω <c>ExecuteScriptAsync</c>,
    /// όχι <c>NavigateToString</c>, ώστε ο χάρτης να μην ξαναφορτώνει τα πλακίδια από την αρχή.</summary>
    private static string BuildAddPinsScript(RouteStop? shop, List<RouteStop> stops, bool fitView)
    {
        var shopJson = shop is null ? "null" : JsonSerializer.Serialize(new { lat = shop.Lat, lon = shop.Lon, label = shop.Label });
        var stopsJson = JsonSerializer.Serialize(stops.Select(s => new { lat = s.Lat, lon = s.Lon, label = s.Label, address = s.Address }));
        return "addPins(" + Json(shopJson) + "," + Json(stopsJson) + "," + (fitView ? "true" : "false") + ")";
    }

    /// <summary>Χτίζει μια αυτόνομη σελίδα Google Maps JavaScript API με τα markers + τη γραμμή διαδρομής.</summary>
    private static string BuildHtmlGoogle(RouteStop? shop, List<RouteStop> stops, List<(double Lat, double Lon)> geometry)
    {
        var key = SettingsStore.Instance.Settings.GoogleMapsApiKey.Trim();
        var anyPoint = shop ?? stops.FirstOrDefault();
        var centerLat = anyPoint?.Lat ?? 38.0;
        var centerLon = anyPoint?.Lon ?? 23.7;

        var markersJs = new StringBuilder();
        if (shop is not null)
            markersJs.Append("new google.maps.Marker({position:{lat:" + Fmt(shop.Lat) + ",lng:" + Fmt(shop.Lon)
                + "},map:map,label:'🏠',title:" + Json(shop.Label) + "});");
        for (var i = 0; i < stops.Count; i++)
        {
            var s = stops[i];
            var title = (i + 1) + ". " + s.Label + " — " + s.Address;
            markersJs.Append("new google.maps.Marker({position:{lat:" + Fmt(s.Lat) + ",lng:" + Fmt(s.Lon)
                + "},map:map,label:'" + (i + 1) + "',title:" + Json(title) + "});");
        }

        var routeCoords = string.Join(",", geometry.Select(g => "{lat:" + Fmt(g.Lat) + ",lng:" + Fmt(g.Lon) + "}"));
        var boundsPts = new List<(double Lat, double Lon)>();
        if (shop is not null) boundsPts.Add((shop.Lat, shop.Lon));
        boundsPts.AddRange(stops.Select(s => (s.Lat, s.Lon)));
        var boundsJs = string.Concat(boundsPts.Select(p => "bounds.extend({lat:" + Fmt(p.Lat) + ",lng:" + Fmt(p.Lon) + "});"));

        return "<!doctype html><html><head><meta charset='utf-8'/>"
            + "<style>html,body,#map{height:100%;margin:0;padding:0;}</style></head><body>"
            + "<div id='map'></div>"
            + "<script>"
            + "function initMap() {"
            + "  var map = new google.maps.Map(document.getElementById('map'), {center:{lat:" + Fmt(centerLat)
            + ",lng:" + Fmt(centerLon) + "},zoom:13});"
            + markersJs
            + "  var routeLine = [" + routeCoords + "];"
            + "  if (routeLine.length > 1) {"
            + "    new google.maps.Polyline({path:routeLine,geodesic:true,strokeColor:'#ec3013',strokeWeight:4,strokeOpacity:0.85}).setMap(map);"
            + "  }"
            + "  var bounds = new google.maps.LatLngBounds();"
            + boundsJs
            + "  if (!bounds.isEmpty()) map.fitBounds(bounds, 30);"
            + "}"
            + "</script>"
            + "<script src='https://maps.googleapis.com/maps/api/js?key=" + Uri.EscapeDataString(key)
            + "&callback=initMap&language=el&region=GR' async defer></script>"
            + "</body></html>";
    }

    private static string Fmt(double d) => d.ToString(CultureInfo.InvariantCulture);
    private static string Json(string s) => JsonSerializer.Serialize(s);
}
