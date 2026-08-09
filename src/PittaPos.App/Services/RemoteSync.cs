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
    public static bool IsClient =>
        SettingsStore.Instance.Settings.NetworkMode == "client" && !HostIsThisMachine();

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
        if (message == LastError)
            return;
        LastError = message;
        if (message is not null)
            AppLog.Write("remote-sync", $"Δεν φτάνει το κύριο ταμείο στο {BaseUrl}: {message}");
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
    /// </summary>
    public static void StartPolling(TimeSpan interval, Func<Task> refresh)
    {
        _ = refresh();
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += async (_, _) => await refresh();
        timer.Start();
    }
}
