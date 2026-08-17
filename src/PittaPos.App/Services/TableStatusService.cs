using System.IO;
using System.Text.Json;

namespace PittaPos.App.Services;

/// <summary>
/// Ποια τραπέζια είναι αυτή τη στιγμή «ανοιχτά» (έχουν ανοιχτό λογαριασμό) και από πότε —
/// ώστε αν ένα τραπέζι ξανανοίξει την ίδια μέρα, το τρέχον σύνολο να μη μαζεύει τις παλιές
/// παραγγελίες προηγούμενου πελάτη. Ένα τραπέζι ανοίγει αυτόματα με την πρώτη παραγγελία του
/// και κλείνει χειροκίνητα. JSON στο %AppData%\PittaPos — ώστε ένα ξαφνικό κλείσιμο/restart
/// να μη χάνει ποια ήταν ανοιχτά.
/// </summary>
public class TableStatusService
{
    public static TableStatusService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Dictionary<int, DateTime> _openSince = [];

    /// <summary>Σηκώνεται όταν ανοίγει/κλείνει τραπέζι.</summary>
    public event Action? Changed;

    public IReadOnlyDictionary<int, DateTime> OpenSince => _openSince;

    private TableStatusService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "table-status.json");
        if (RemoteSync.IsClient)
            RemoteSync.StartPolling(TimeSpan.FromSeconds(2), RefreshFromHostAsync);
        else
            Load();
    }

    /// <summary>Δεύτερο ταμείο (client) — αντικαθιστά την τοπική εικόνα με την κατάσταση του host.</summary>
    private async Task RefreshFromHostAsync()
    {
        var data = await RemoteSync.GetAsync<Dictionary<int, DateTime>>("/api/sync/table-status");
        if (data is null)
            return;
        _openSince.Clear();
        foreach (var (table, since) in data)
            _openSince[table] = since;
        Changed?.Invoke();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var tables = JsonSerializer.Deserialize<Dictionary<int, DateTime>>(File.ReadAllText(_path)) ?? [];
            foreach (var (table, since) in tables)
                _openSince[table] = since;
        }
        catch (Exception)
        {
            // Χαλασμένο αρχείο — ξεκίνα άδειο αντί να ρίξεις την εφαρμογή
        }
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_openSince, JsonOpts));
        }
        catch (Exception)
        {
            // Αποτυχία εγγραφής δεν πρέπει να μπλοκάρει το ταμείο
        }
    }

    /// <summary>Σημειώνει το τραπέζι ανοιχτό (αν δεν ήταν ήδη) — καλείται αυτόματα με κάθε παραγγελία τραπεζιού.
    /// <para><b>Δεύτερο ταμείο:</b> η ώρα ανοίγματος ορίζεται ΕΔΩ και κρατιέται ΑΜΕΣΩΣ τοπικά, πριν σταλεί
    /// στο κύριο. Ο καλών διαβάζει το <see cref="OpenSince"/> στην αμέσως επόμενη γραμμή για να σφραγίσει
    /// την παραγγελία (<c>CompletedOrder.TableOpenedAt</c>, βλ. OrderWizardViewModel και WaiterApiService):
    /// με σκέτη αποστολή στο κύριο ταμείο, η τοπική εικόνα ενημερωνόταν δευτερόλεπτα αργότερα και η
    /// <b>πρώτη παραγγελία κάθε τραπεζιού έβγαινε χωρίς άνοιγμα</b> — το Ιστορικό την έδειχνε σαν
    /// ξεχωριστό λογαριασμό, κομμένο από την υπόλοιπη παρέα.</para></summary>
    public void MarkOpen(int table)
    {
        if (_openSince.ContainsKey(table))
            return;

        var openedAt = DateTime.Now;
        if (RemoteSync.IsClient)
        {
            // Χωρίς Save: στο δεύτερο ταμείο η εικόνα έρχεται πάντα από το κύριο (βλ. RefreshFromHostAsync),
            // δεν κρατιέται τοπικό αρχείο κατάστασης.
            _openSince[table] = openedAt;
            Changed?.Invoke();
            _ = SyncThenRefreshAsync("/api/sync/table-status/open", new { Table = table, OpenedAt = openedAt });
            return;
        }
        _openSince[table] = openedAt;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Άνοιγμα τραπεζιού που ήρθε από το δεύτερο ταμείο, με την ώρα ΠΟΥ ΕΚΕΙΝΟ όρισε — έτσι η
    /// σφραγίδα πάνω στην πρώτη του παραγγελία ταιριάζει με το άνοιγμα που κρατά το κύριο ταμείο, και το
    /// Ιστορικό μαζεύει όλη την παρέα σε έναν λογαριασμό. Αν το τραπέζι είναι ήδη ανοιχτό, δεν αλλάζει
    /// τίποτα (πρώτο άνοιγμα κερδίζει, όπως και στο MarkOpen).</summary>
    public void MarkOpenAt(int table, DateTime openedAt)
    {
        if (_openSince.ContainsKey(table))
            return;
        _openSince[table] = openedAt;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Ελευθερώνει το τραπέζι — η επόμενη παραγγελία θα ξεκινήσει νέο, καθαρό σύνολο.</summary>
    public void MarkClosed(int table)
    {
        // ΠΡΙΝ τον έλεγχο για δεύτερο ταμείο: η ουρά εκτύπωσης του κινητού είναι ΤΟΠΙΚΗ σε κάθε ταμείο
        // (γεμίζει σε όποιο μιλάει το κινητό), οπότε καθαρίζει καθένα τη δική του — αλλιώς ένας γύρος
        // που εγκαταλείφθηκε θα κολλούσε στο δελτίο της επόμενης παρέας του τραπεζιού.
        WaiterApiService.DiscardPendingPrints(table);
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/table-status/close", new { Table = table });
            return;
        }
        TableSettlementService.Instance.ClearTable(table);
        // Και ο χωρισμός σε άτομα: ανήκει στην παρέα που μόλις έφυγε, όχι στο τραπέζι.
        TablePersonsService.Instance.ClearTable(table);
        if (!_openSince.Remove(table))
            return;
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Κλείνει όλα τα ανοιχτά τραπέζια — καλείται στο κλείσιμο μέρας, ώστε ένα τραπέζι που έμεινε
    /// ανοιχτό να μη μείνει «κολλημένο» σαν ανοιχτό με €0 την επόμενη μέρα (οι παραγγελίες του
    /// έχουν ήδη αρχειοθετηθεί και καθαρίσει από το SalesStatsService).
    /// </summary>
    public void CloseAll()
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/table-status/close-all", new { });
            return;
        }
        if (_openSince.Count == 0)
            return;
        foreach (var table in _openSince.Keys.ToList())
        {
            TableSettlementService.Instance.ClearTable(table);
            TablePersonsService.Instance.ClearTable(table);
            WaiterApiService.DiscardPendingPrints(table);
        }
        _openSince.Clear();
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Καθαρίζει «φαντάσματα»: τραπέζια σημειωμένα ανοιχτά που δεν έχουν καμία ζωντανή παραγγελία
    /// (π.χ. έκλεισε η μέρα ενώ ήταν ανοιχτά, πριν μπει το CloseAll). Καλείται μία φορά στο startup —
    /// μόνο στο κύριο ταμείο (host)· το δεύτερο ταμείο διαβάζει έτοιμη κατάσταση από εκεί.
    /// </summary>
    public void CloseIfNoLiveOrders(Func<int, DateTime, bool> hasLiveOrders)
    {
        if (RemoteSync.IsClient)
            return;
        var stale = _openSince.Where(kv => !hasLiveOrders(kv.Key, kv.Value)).Select(kv => kv.Key).ToList();
        if (stale.Count == 0)
            return;
        foreach (var table in stale)
        {
            TableSettlementService.Instance.ClearTable(table);
            TablePersonsService.Instance.ClearTable(table);
            _openSince.Remove(table);
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>Δεύτερο ταμείο (client) — στέλνει τη μεταβολή στο host, μετά ξαναδιαβάζει την αληθινή κατάσταση.</summary>
    private async Task SyncThenRefreshAsync(string path, object body)
    {
        await RemoteSync.PostAsync(path, body);
        await RefreshFromHostAsync();
    }
}
