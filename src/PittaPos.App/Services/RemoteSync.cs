using System.Net.Http;
using System.Net.Http.Json;
using System.Windows.Threading;

namespace PittaPos.App.Services;

/// <summary>
/// Υποδομή για δεύτερο ταμείο σε λειτουργία «client»: αντί να κρατάει τοπικά JSON αρχεία, το
/// ταμείο αυτό διαβάζει/γράφει πάνω στο κύριο ταμείο (host) μέσω του ενσωματωμένου API του
/// (βλ. WaiterApiService, θύρα 5190). Κάθε συγχρονιζόμενο store (τραπέζια, ζωντανές παραγγελίες,
/// πελάτες, στατιστικά, μενού) ελέγχει <see cref="IsClient"/> και είτε δουλεύει όπως σήμερα (host —
/// τοπικά JSON, καμία αλλαγή συμπεριφοράς) είτε προωθεί μέσω αυτής της κλάσης· βλ. docs/two-tills-el.md.
/// </summary>
public static class RemoteSync
{
    /// <summary>
    /// Λειτουργεί σαν δεύτερο ταμείο; ΚΑΙ ο έλεγχος ότι το «κύριο ταμείο» δεν είναι αυτός ο ίδιος
    /// υπολογιστής: με λάθος ρύθμιση (η δική του IP ή localhost στο πεδίο διεύθυνσης) κάθε καταχώρηση
    /// θα προωθούνταν στον εαυτό της — το endpoint θα καλούσε ξανά το ίδιο store, που θα έβλεπε πάλι
    /// «είμαι client» και θα ξαναπροωθούσε, ατέρμονα. Η παραγγελία δεν θα καταγραφόταν ΠΟΤΕ και το
    /// ταμείο θα φόρτωνε άσκοπα τον εαυτό του με αιτήματα. Εδώ επανέρχεται σε λειτουργία κύριου
    /// ταμείου (τοπικά αρχεία), που είναι πάντα ασφαλέστερο από το να χάνονται παραγγελίες.
    /// </summary>
    /// <para>Οι ξεχωριστές εφαρμογές (Ζωντανές Παραγγελίες, Στατιστικά — βλ. <see cref="AppMode"/>) περνούν ΠΑΝΤΑ από
    /// εδώ ως client, και μάλιστα με διεύθυνση 127.0.0.1 — δηλαδή ακριβώς αυτό που ο παραπάνω έλεγχος
    /// απαγορεύει. Δεν είναι αντίφαση: εκείνος υπάρχει για να μη μιλάει ένα ταμείο στον ΕΑΥΤΟ του,
    /// ενώ αυτές μιλάνε σε ΑΛΛΗ διεργασία, το ταμείο δίπλα τους. Βρόχος δεν γίνεται γιατί δεν σηκώνουν
    /// καν server (βλ. App.OnStartup): τίποτα δεν γυρίζει ποτέ πίσω σε αυτές.</para>
    public static bool IsClient =>
        AppMode.IsViewer
        || (SettingsStore.Instance.Settings.NetworkMode == "client" && !HostIsThisMachine());

    private static string? _checkedHost;
    private static bool _checkedResult;

    private static bool HostIsThisMachine()
    {
        var host = SettingsStore.Instance.Settings.HostAddress.Trim();
        if (_checkedHost == host)
            return _checkedResult;

        _checkedHost = host;
        _checkedResult = IsThisMachine(host);
        if (_checkedResult)
            AppLog.Write("remote-sync",
                $"Η διεύθυνση κύριου ταμείου «{host}» είναι αυτός ο υπολογιστής — αγνοείται η λειτουργία " +
                "δεύτερου ταμείου (θα δούλευε ατέρμονα στον εαυτό του και θα χάνονταν παραγγελίες).");
        return _checkedResult;
    }

    /// <summary>Η δοσμένη διεύθυνση δείχνει σε ΑΥΤΟΝ τον υπολογιστή; Χρησιμοποιείται και από την οθόνη
    /// Ρυθμίσεων Δικτύου, ώστε να προειδοποιήσει τον χρήστη τη στιγμή που τη γράφει — αλλιώς η
    /// προστασία παρακάτω θα ενεργούσε σιωπηλά και το ταμείο θα φαινόταν «δεύτερο» ενώ δεν είναι.</summary>
    public static bool IsThisMachine(string host)
    {
        if (host.Length == 0)
            return false; // κενό = απλώς αποτυγχάνει η σύνδεση, δεν κάνει βρόχο
        if (host is "localhost" or "127.0.0.1" or "::1")
            return true;
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Any(a => a.Address.ToString() == host);
        }
        catch (Exception)
        {
            return false; // αδυναμία ελέγχου δεν πρέπει να αλλάζει συμπεριφορά
        }
    }

    private static string BaseUrl => "http://" + SettingsStore.Instance.Settings.HostAddress.Trim() + ":" + WaiterApiService.Port;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    /// <summary>Το τελευταίο σφάλμα σύνδεσης με το κύριο ταμείο (κενό = τελευταία επικοινωνία ΟΚ).
    /// Δείχνεται στην οθόνη Ρυθμίσεων ώστε να φαίνεται στο μαγαζί ΓΙΑΤΙ δεν συνδέεται το δεύτερο
    /// ταμείο, αντί το πρόβλημα να μένει εντελώς σιωπηλό (κάθε GET/POST αποτυγχάνει ξεχωριστά και
    /// σιωπηλά, βλ. παρακάτω — χωρίς αυτό δεν έχει κανείς κανένα ίχνος για να ξεκινήσει τη διάγνωση).</summary>
    public static string? LastError { get; private set; }

    private static void ReportOutcome(Exception? ex)
    {
        var message = ex is null ? null : DescribeError(ex);
        if (message is not null)
            _ = TryRediscoverHostAsync();
        if (message == LastError)
            return;
        LastError = message;
        if (message is not null)
            AppLog.Write("remote-sync", $"Δεν φτάνει το κύριο ταμείο στο {BaseUrl}: {message}");
    }

    // ---- αυτόματη εύρεση του κύριου ταμείου στο τοπικό δίκτυο ----
    //
    // Η γραμμένη διεύθυνση του κύριου ταμείου παύει να ισχύει μόνη της: το router μοιράζει IP με DHCP,
    // οπότε ένα restart (ρεύμα, αναβάθμιση των Windows, αλλαγή router) μπορεί να δώσει στο κύριο ταμείο
    // ΑΛΛΗ IP από αυτήν που είναι γραμμένη εδώ. Από εκείνη τη στιγμή το δεύτερο ταμείο δεν βρίσκει
    // τίποτα — ούτε τραπέζια ούτε εκτύπωση — χωρίς να έχει αλλάξει κανείς τίποτα. Αντί να ξαναγράφεται
    // η IP στο χέρι κάθε φορά, το ταμείο τη βρίσκει μόνο του: σαρώνει το τοπικό δίκτυο για ένα ταμείο
    // που δηλώνει «είμαι το κύριο» (βλ. /api/whoami) και αποθηκεύει τη νέα διεύθυνση.

    private static DateTime _lastDiscovery = DateTime.MinValue;
    private static bool _discovering;

    /// <summary>Πόσο συχνά το πολύ ξαναψάχνει — η σάρωση είναι φθηνή αλλά όχι δωρεάν, και οι αποτυχίες
    /// έρχονται κατά δεκάδες (κάθε store κάνει το δικό του poll κάθε 2-3 δευτερόλεπτα).</summary>
    private static readonly TimeSpan DiscoveryCooldown = TimeSpan.FromSeconds(45);

    private static async Task TryRediscoverHostAsync()
    {
        if (DateTime.Now - _lastDiscovery < DiscoveryCooldown)
            return;
        await FindAndSaveHostAsync();
    }

    /// <summary>
    /// Ζει το κύριο ταμείο στη διεύθυνση που ξέρουμε; Ελαφρύ, χωρίς παρενέργειες — δεν περνάει από το
    /// ReportOutcome, ώστε ο περιοδικός έλεγχος (βλ. HostWatchdog) να μη σκανδαλίζει μόνος του σάρωση.
    ///
    /// «Ζει» σημαίνει ΑΠΑΝΤΑΕΙ, ό,τι κι αν απαντήσει — ακόμα και 404. ΔΕΝ απαιτείται το /api/whoami:
    /// εκείνο υπάρχει μόνο από αυτή την έκδοση και μετά, και το κύριο ταμείο μπορεί κάλλιστα να τρέχει
    /// ακόμα την προηγούμενη (π.χ. μπήκε το setup μόνο στο δεύτερο μηχάνημα). Αν το ζητούσαμε, ο έλεγχος
    /// θα αποτύγχανε ΠΑΝΤΑ ενώ η σύνδεση δουλεύει μια χαρά, και το ταμείο θα σάρωνε ολόκληρο το δίκτυο
    /// κάθε μισό λεπτό, ατέρμονα, χωρίς κανένα όφελος.
    /// </summary>
    public static async Task<bool> PingHostAsync()
    {
        var host = SettingsStore.Instance.Settings.HostAddress.Trim();
        if (host.Length == 0)
            return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"http://{host}:{WaiterApiService.Port}/api/whoami");
            using var response = await Http.SendAsync(request);
            return true;
        }
        catch (Exception)
        {
            // Μόνο πραγματική αποτυχία επικοινωνίας (κλειστό μηχάνημα, λάθος IP, πεσμένο δίκτυο).
            return false;
        }
    }

    /// <summary>Ψάχνει το κύριο ταμείο στο δίκτυο και, αν το βρει σε άλλη διεύθυνση, την αποθηκεύει.
    /// Επιστρέφει τη διεύθυνση που ισχύει τώρα, ή null αν δεν βρέθηκε τίποτα.</summary>
    public static async Task<string?> FindAndSaveHostAsync()
    {
        // Οι ξεχωριστές εφαρμογές μιλάνε εξ ορισμού στο ταμείο του ΙΔΙΟΥ υπολογιστή (βλ. AppMode) —
        // δεν έχουν IP να χάσουν και δεν έχουν τίποτα να ψάξουν. Χωρίς αυτό, με το ταμείο κλειστό θα
        // σάρωναν το δίκτυο και θα «κόλλαγαν» σε ταμείο άλλου μηχανήματος, δείχνοντας νούμερα και
        // παραγγελίες που δεν είναι αυτού του πάγκου.
        if (AppMode.IsViewer || _discovering || !IsClient)
            return null;
        _discovering = true;
        _lastDiscovery = DateTime.Now;
        try
        {
            var found = await DiscoverHostAsync();
            if (found is null)
                return null;
            if (found == SettingsStore.Instance.Settings.HostAddress.Trim())
                return found;

            AppLog.Write("remote-sync",
                $"Το κύριο ταμείο βρέθηκε σε νέα διεύθυνση: {found} (ήταν {SettingsStore.Instance.Settings.HostAddress}). " +
                "Η ρύθμιση ενημερώθηκε αυτόματα.");
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                SettingsStore.Instance.SetNetworkMode("client", found);
            else
                dispatcher.Invoke(() => SettingsStore.Instance.SetNetworkMode("client", found));
            return found;
        }
        catch (Exception ex)
        {
            AppLog.Write("remote-sync", $"Απέτυχε η αυτόματη εύρεση κύριου ταμείου: {ex.Message}");
            return null;
        }
        finally
        {
            _discovering = false;
        }
    }

    /// <summary>
    /// Σαρώνει το τοπικό δίκτυο (το /24 της κάθε κάρτας δικτύου) για ένα ταμείο που απαντά στη θύρα μας
    /// ΚΑΙ δηλώνει ότι είναι το κύριο. Επιστρέφει την IP του, ή null αν δεν βρέθηκε.
    /// Χρησιμοποιείται και από το κουμπί «Εύρεση κύριου ταμείου» στις Ρυθμίσεις Δικτύου.
    /// </summary>
    public static async Task<string?> DiscoverHostAsync()
    {
        var mine = LocalIPv4Addresses();
        if (mine.Count == 0)
            return null;

        // Το ίδιο μας το ταμείο ακούει επίσης σε αυτή τη θύρα — χωρίς αυτόν τον αποκλεισμό θα «έβρισκε»
        // τον εαυτό του και θα προωθούσε τις παραγγελίες στον εαυτό του (βλ. HostIsThisMachine).
        var skip = mine.ToHashSet(StringComparer.Ordinal);
        var candidates = mine
            .Select(ip => ip[..(ip.LastIndexOf('.') + 1)])
            .Distinct(StringComparer.Ordinal)
            .SelectMany(prefix => Enumerable.Range(1, 254).Select(last => prefix + last))
            .Where(ip => !skip.Contains(ip))
            .ToList();

        // Παράλληλα, αλλά με φρένο: 64 ταυτόχρονες συνδέσεις σαρώνουν ένα /24 σε ~2 δευτερόλεπτα χωρίς
        // να πνίγουν το δίκτυο του μαγαζιού την ώρα που δουλεύει.
        using var gate = new SemaphoreSlim(64);
        using var found = new CancellationTokenSource();
        string? result = null;

        var probes = candidates.Select(async ip =>
        {
            await gate.WaitAsync();
            try
            {
                if (found.IsCancellationRequested || !await IsHostAtAsync(ip, found.Token))
                    return;
                result = ip;
                found.Cancel();
            }
            catch (Exception)
            {
                // Μια IP που δεν απαντά είναι το φυσιολογικό εδώ, όχι σφάλμα
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(probes);
        return result;
    }

    /// <summary>Απαντά σε αυτή την IP ταμείο σε λειτουργία ΚΥΡΙΟΥ; Πρώτα σκέτο TCP (γρήγορο «όχι» για τις
    /// 250 IP που δεν τρέχουν τίποτα), και μόνο μετά η ερώτηση ταυτότητας.</summary>
    private static async Task<bool> IsHostAtAsync(string ip, CancellationToken token)
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        connectTimeout.CancelAfter(TimeSpan.FromMilliseconds(600));
        try
        {
            await socket.ConnectAsync(System.Net.IPAddress.Parse(ip), WaiterApiService.Port, connectTimeout.Token);
        }
        catch (Exception)
        {
            return false;
        }

        // Κοινός HttpClient, όχι καινούριος ανά διεύθυνση: η σάρωση αγγίζει δεκάδες μηχανήματα και ένας
        // client ανά αίτημα αφήνει πίσω του sockets σε αναμονή κλεισίματος για λεπτά.
        //
        // Εδώ το /api/whoami είναι ΑΠΑΡΑΙΤΗΤΟ (σε αντίθεση με το PingHostAsync): ψάχνουμε ποιο από τα
        // μηχανήματα του δικτύου είναι το κύριο ταμείο ΑΥΤΟΥ του καταστήματος — χωρίς την ταυτότητα θα
        // κολλούσαμε στο πρώτο που τυχαίνει να ακούει στη θύρα. Κύριο ταμείο σε παλιότερη έκδοση δεν
        // εντοπίζεται αυτόματα· εκεί η IP γράφεται στο χέρι, όπως πάντα.
        var who = await Http.GetFromJsonAsync<WhoAmIDto>(
            $"http://{ip}:{WaiterApiService.Port}/api/whoami", token);
        return who is not null && who.Mode == "host" && who.Store == AppIdentity.StoreName;
    }

    /// <summary>Ταυτότητα ταμείου, όπως την επιστρέφει το /api/whoami — βλ. WaiterApiService.</summary>
    public sealed record WhoAmIDto(string Mode, string Store);

    private static List<string> LocalIPv4Addresses()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !System.Net.IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string DescribeError(Exception ex) => ex switch
    {
        TaskCanceledException => "λήξη χρόνου αναμονής (timeout) — δεν απάντησε το κύριο ταμείο",
        HttpRequestException hre => hre.Message,
        _ => ex.Message,
    };

    /// <summary>GET από το κύριο ταμείο. Επιστρέφει default(T) αν δεν είναι διαθέσιμο αυτή τη στιγμή —
    /// το επόμενο poll θα ξαναδοκιμάσει.</summary>
    public static async Task<T?> GetAsync<T>(string path)
    {
        try
        {
            var result = await Http.GetFromJsonAsync<T>(BaseUrl + path);
            ReportOutcome(null);
            return result;
        }
        catch (Exception ex)
        {
            ReportOutcome(ex);
            return default;
        }
    }

    /// <summary>POST προς το κύριο ταμείο. Επιστρέφει αν έφτασε — οι ενέργειες κατάστασης το αγνοούν
    /// (τις διορθώνει το επόμενο refresh), αλλά οι προσθήκες (νέα παραγγελία) το ελέγχουν και σε
    /// αποτυχία μπαίνουν σε ουρά για επανάληψη (βλ. PendingSyncService).</summary>
    public static async Task<bool> PostAsync(string path, object body)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(BaseUrl + path, body);
            ReportOutcome(null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            ReportOutcome(ex);
            return false;
        }
    }

    /// <summary>Ίδιο με PostAsync, αλλά με έτοιμο JSON — για την ουρά αναμονής, που κρατάει το σώμα του
    /// αιτήματος αποθηκευμένο ως κείμενο και δεν έχει πια το αρχικό αντικείμενο.</summary>
    public static async Task<bool> PostRawAsync(string path, string json)
    {
        try
        {
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await Http.PostAsync(BaseUrl + path, content);
            ReportOutcome(null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            ReportOutcome(ex);
            return false;
        }
    }

    /// <summary>
    /// Ίδιο με PostAsync, αλλά περιμένει και την απάντηση του host — για όπου ο καλών χρειάζεται μια
    /// τιμή που αποφασίστηκε εκεί (π.χ. τελικός αριθμός παραγγελίας μετά από έλεγχο σύγκρουσης).
    ///
    /// Το <c>Ok</c> λέει αν το αίτημα ΕΦΤΑΣΕ, ξεχωριστά από το αν διαβάστηκε τιμή — και τα δύο μαζί θα
    /// ήταν επικίνδυνα: αν το host δεχόταν κανονικά την παραγγελία αλλά δεν επέστρεφε αναγνώσιμο σώμα
    /// (π.χ. τρέχει παλιότερη έκδοση, όπου το endpoint γύριζε σκέτο 200), ο καλών θα το εκλάμβανε ως
    /// αποτυχία, θα την έβαζε στην ουρά και θα την ξανάστελνε — ΔΙΠΛΗ παραγγελία στον τζίρο.
    /// </summary>
    public static async Task<(bool Ok, T? Value)> PostForResultAsync<T>(string path, object body)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(BaseUrl + path, body);
            ReportOutcome(null);
            if (!response.IsSuccessStatusCode)
                return (false, default);
            try
            {
                return (true, await response.Content.ReadFromJsonAsync<T>());
            }
            catch (Exception)
            {
                // Έφτασε και έγινε δεκτό — απλώς δεν πήραμε τιμή. ΔΕΝ είναι αποτυχία αποστολής.
                return (true, default);
            }
        }
        catch (Exception ex)
        {
            ReportOutcome(ex);
            return (false, default);
        }
    }

    /// <summary>
    /// Ξεκινά περιοδικό refresh από το host — αμέσως μία φορά, μετά κάθε <paramref name="interval"/>.
    /// Ο κάθε store καλεί αυτό μία φορά στον constructor του όταν IsClient.
    /// <para>Ο timer στήνεται ΠΑΝΤΑ πάνω στο UI thread: ο DispatcherTimer δένεται στο νήμα που τον
    /// δημιουργεί, και τα stores γεννιούνται «τεμπέλικα» — η πρώτη χρήση ενός store μπορεί κάλλιστα να
    /// είναι μέσα σε αίτημα του κινητού ή σε γεωκωδικοποίηση, δηλαδή σε νήμα παρασκηνίου, όπου ο timer
    /// δεν θα χτυπούσε ΠΟΤΕ και το δεύτερο ταμείο δεν θα ενημερωνόταν ποτέ.</para>
    /// </summary>
    public static void StartPolling(TimeSpan interval, Func<Task> refresh)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => StartPolling(interval, refresh));
            return;
        }

        // Το πρώτο poll μπαίνει στην ουρά του UI αντί να τρέξει ΤΩΡΑ. Ο κάθε store το ζητά μέσα από
        // τον constructor του, δηλαδή τη στιγμή που το `Instance` του δεν έχει ακόμα ανατεθεί: ένα
        // refresh εκείνη τη στιγμή έσκαγε με NullReferenceException μόλις κάποιος διάβαζε ρυθμίσεις
        // (βλ. BaseUrl -> SettingsStore.Instance) και το πρώτο συγχρονισμό τον έτρωγε το crash-log.
        if (dispatcher is null)
            _ = refresh();
        else
            dispatcher.BeginInvoke(refresh);
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += async (_, _) => await refresh();
        timer.Start();
    }
}
