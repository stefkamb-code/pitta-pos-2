using System.IO;
using System.Text.Json;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>
/// Ποιες γραμμές παραγγελιών τραπεζιού έχουν ήδη πληρωθεί ξεχωριστά, πριν κλείσει όλο το τραπέζι —
/// π.χ. όταν ένας από την παρέα πληρώνει μόνο το δικό του προϊόν. Οι γραμμές παραμένουν στο
/// ιστορικό/στατιστικά (SalesStatsService) — εδώ κρατάμε μόνο ποιες δεν οφείλονται πια στο τραπέζι.
/// JSON στο %AppData%\PittaPos — ώστε ένα ξαφνικό κλείσιμο/restart να μη χάνει την εξόφληση.
/// </summary>
public class TableSettlementService
{
    public static TableSettlementService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    /// <summary>τραπέζι → (κλειδί εξόφλησης → τρόπος πληρωμής). Παλιότερα ήταν σκέτο σύνολο κλειδιών,
    /// χωρίς τρόπο πληρωμής· τα παλιά αρχεία διαβάζονται κανονικά (βλ. Load) και θεωρούνται μετρητά.</summary>
    private readonly Dictionary<int, Dictionary<string, string>> _settled = [];

    private static string MethodKey(PaymentMethod m) => m == PaymentMethod.Card ? "card" : "cash";

    /// <summary>Σηκώνεται σε κάθε εξόφληση/ακύρωση — ώστε η κάτοψη τραπεζιών να δείχνει το σωστό οφειλόμενο.</summary>
    public event Action? Changed;

    private TableSettlementService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "table-settlements.json");
        if (RemoteSync.IsClient)
            RemoteSync.StartPolling(TimeSpan.FromSeconds(2), RefreshFromHostAsync);
        else
            Load();
    }

    /// <summary>Δεύτερο ταμείο (client) — αντικαθιστά την τοπική εικόνα με την κατάσταση του host.</summary>
    private async Task RefreshFromHostAsync()
    {
        var data = await RemoteSync.GetAsync<Dictionary<int, Dictionary<string, string>>>("/api/sync/table-settlements");
        if (data is null)
            return;
        _settled.Clear();
        foreach (var (table, entries) in data)
            _settled[table] = new Dictionary<string, string>(entries);
        Changed?.Invoke();
    }

    /// <summary>Στιγμιότυπο για το endpoint συγχρονισμού του host.</summary>
    public Dictionary<int, Dictionary<string, string>> Snapshot() =>
        _settled.ToDictionary(kv => kv.Key, kv => new Dictionary<string, string>(kv.Value));

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var text = File.ReadAllText(_path);
            // Νέα μορφή: κλειδί → τρόπος πληρωμής.
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<int, Dictionary<string, string>>>(text);
                if (data is not null)
                {
                    foreach (var (table, entries) in data)
                        _settled[table] = new Dictionary<string, string>(entries);
                    return;
                }
            }
            catch (JsonException)
            {
                // παλιά μορφή παρακάτω
            }

            // Παλιά μορφή: σκέτη λίστα κλειδιών, χωρίς τρόπο πληρωμής — θεωρούνται μετρητά.
            var old = JsonSerializer.Deserialize<Dictionary<int, List<string>>>(text) ?? [];
            foreach (var (table, keys) in old)
                _settled[table] = keys.ToDictionary(k => k, _ => "cash");
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
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(Snapshot(), JsonOpts));
        }
        catch (Exception)
        {
            // Αποτυχία εγγραφής δεν πρέπει να μπλοκάρει το ταμείο
        }
    }

    /// <summary>Είναι ΟΛΗ η γραμμή εξοφλημένη; (παλιό κλειδί «παραγγελία:γραμμή»)</summary>
    public bool IsSettled(int table, int orderNumber, int lineIndex) =>
        _settled.TryGetValue(table, out var set) && set.ContainsKey(Key(orderNumber, lineIndex));

    /// <summary>
    /// Είναι εξοφλημένο ΤΟ ΣΥΓΚΕΚΡΙΜΕΝΟ τεμάχιο; Αν κάποιος πήρε 3 ίδιες πίττες και πληρώνει μόνο τη
    /// μία, πρέπει να μπορεί να σημειωθεί μόνο αυτή. Το παλιό κλειδί χωρίς τεμάχιο εξακολουθεί να
    /// σημαίνει «όλη η γραμμή», ώστε ό,τι είχε εξοφληθεί πριν να μετράει κανονικά.
    /// </summary>
    public bool IsSettled(int table, int orderNumber, int lineIndex, int unit) =>
        _settled.TryGetValue(table, out var set)
        && (set.ContainsKey(Key(orderNumber, lineIndex)) || set.ContainsKey(Key(orderNumber, lineIndex, unit)));

    /// <summary>Πώς πληρώθηκε το τεμάχιο — null αν δεν έχει εξοφληθεί ακόμα.</summary>
    public PaymentMethod? MethodFor(int table, int orderNumber, int lineIndex, int unit)
    {
        if (!_settled.TryGetValue(table, out var set))
            return null;
        if (!set.TryGetValue(Key(orderNumber, lineIndex, unit), out var m)
            && !set.TryGetValue(Key(orderNumber, lineIndex), out m))
            return null;
        return m == "card" ? PaymentMethod.Card : PaymentMethod.Cash;
    }

    /// <summary>Πόσα από τα <paramref name="quantity"/> τεμάχια της γραμμής έχουν εξοφληθεί.</summary>
    public int SettledUnits(int table, int orderNumber, int lineIndex, int quantity)
    {
        if (!_settled.TryGetValue(table, out var set))
            return 0;
        if (set.ContainsKey(Key(orderNumber, lineIndex)))
            return quantity; // παλιά εγγραφή: όλη η γραμμή
        var settled = 0;
        for (var u = 0; u < quantity; u++)
            if (set.ContainsKey(Key(orderNumber, lineIndex, u)))
                settled++;
        return settled;
    }

    /// <summary>Σημειώνει ΟΛΗ τη γραμμή πληρωμένη· true αν άλλαξε κάτι (false αν ήταν ήδη).</summary>
    public bool Settle(int table, int orderNumber, int lineIndex,
        PaymentMethod method = PaymentMethod.Cash, decimal amount = 0) =>
        SettleKey(table, Key(orderNumber, lineIndex), orderNumber, method, amount,
            new { Table = table, OrderNumber = orderNumber, LineIndex = lineIndex, Unit = -1,
                  Method = MethodKey(method), Amount = amount });

    /// <summary>Σημειώνει ΕΝΑ τεμάχιο της γραμμής πληρωμένο, με τον τρόπο πληρωμής και το ποσό του.</summary>
    public bool Settle(int table, int orderNumber, int lineIndex, int unit,
        PaymentMethod method = PaymentMethod.Cash, decimal amount = 0) =>
        SettleKey(table, Key(orderNumber, lineIndex, unit), orderNumber, method, amount,
            new { Table = table, OrderNumber = orderNumber, LineIndex = lineIndex, Unit = unit,
                  Method = MethodKey(method), Amount = amount });

    private bool SettleKey(int table, string key, int orderNumber, PaymentMethod method, decimal amount, object syncBody)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/table-settlements/settle", syncBody);
            return true;
        }
        if (!_settled.TryGetValue(table, out var set))
            _settled[table] = set = [];
        if (!set.TryAdd(key, MethodKey(method)))
            return false;
        // Το ποσό καταγράφεται ΞΕΧΩΡΙΣΤΑ (βλ. TablePaymentsService): οι εξοφλήσεις σβήνονται με το
        // κλείσιμο του τραπεζιού, ενώ ο διαχωρισμός μετρητά/κάρτα πρέπει να φτάσει στην αναφορά ημέρας.
        // Ο αριθμός παραγγελίας ταξιδεύει μαζί: με μία παραγγελία ανά άτομο, αυτός ΕΙΝΑΙ το άτομο —
        // χωρίς αυτόν δεν θα μπορούσε να διορθωθεί εκ των υστέρων μετρητά↔κάρτα ενός μόνο ατόμου.
        TablePaymentsService.Instance.Add(table, orderNumber, amount, method);
        // Ο τρόπος πληρωμής γράφεται ΚΑΙ πάνω στην παραγγελία, ώστε να επιβιώνει στο αρχείο του
        // ιστορικού: οι εισπράξεις καθαρίζονται με το κλείσιμο ημέρας, άρα χωρίς αυτό το «πώς πλήρωσε
        // ο Β» θα χανόταν το επόμενο πρωί. ΔΕΝ διπλομετράει: η αναφορά ημέρας αγνοεί ρητά το πεδίο
        // για τα τραπέζια και μετράει μόνο τις εισπράξεις (βλ. DayReportService).
        SalesStatsService.Instance.UpdatePaymentMethod(orderNumber, method);
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Καλείται όταν κλείνει όλο το τραπέζι — καθαρό ξεκίνημα την επόμενη φορά που θα ανοίξει.</summary>
    public void ClearTable(int table)
    {
        if (!_settled.Remove(table))
            return;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Καθαρίζει τα εξοφλημένα ενός ολόκληρου γύρου — π.χ. όταν ο γύρος ακυρώνεται εντελώς.</summary>
    public void ClearOrder(int table, int orderNumber)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/table-settlements/clear-order", new { Table = table, OrderNumber = orderNumber });
            return;
        }
        if (!_settled.TryGetValue(table, out var set))
            return;
        var toDrop = set.Keys.Where(k => k.StartsWith(orderNumber + ":", StringComparison.Ordinal)).ToList();
        if (toDrop.Count == 0)
            return;
        foreach (var k in toDrop)
            set.Remove(k);
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Μετά την πραγματική διαγραφή μιας γραμμής, οι επόμενες γραμμές του ίδιου γύρου μετακινούνται
    /// κατά μία θέση προς τα πίσω — προσαρμόζει ανάλογα ποιες ήταν ήδη εξοφλημένες, ώστε να μη
    /// «μπερδευτεί» η εξόφληση με λάθος προϊόν.
    /// </summary>
    public void ShiftAfterRemoval(int table, int orderNumber, int removedIndex)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/table-settlements/shift-after-removal",
                new { Table = table, OrderNumber = orderNumber, RemovedIndex = removedIndex });
            return;
        }
        if (!_settled.TryGetValue(table, out var set))
            return;

        var updated = new Dictionary<string, string>();
        foreach (var (key, method) in set)
        {
            // Τα κλειδιά είναι είτε «παραγγελία:γραμμή» (όλη η γραμμή) είτε «παραγγελία:γραμμή:τεμάχιο».
            var parts = key.Split(':');
            if (parts.Length is not (2 or 3) || !int.TryParse(parts[0], out var ord) || !int.TryParse(parts[1], out var idx))
                continue;
            if (ord != orderNumber)
            {
                updated[key] = method;
                continue;
            }
            if (idx == removedIndex)
                continue; // η ίδια η διαγραμμένη γραμμή φεύγει

            var shifted = idx > removedIndex ? idx - 1 : idx;
            updated[parts.Length == 3 && int.TryParse(parts[2], out var unit)
                ? Key(ord, shifted, unit)
                : Key(ord, shifted)] = method;
        }
        _settled[table] = updated;
        Save();
        Changed?.Invoke();
    }

    private static string Key(int orderNumber, int lineIndex) => orderNumber + ":" + lineIndex;

    /// <summary>Κλειδί ανά τεμάχιο — ώστε από 3 ίδιες πίττες να μπορεί να πληρωθεί μόνο η μία.</summary>
    private static string Key(int orderNumber, int lineIndex, int unit) => orderNumber + ":" + lineIndex + ":" + unit;

    /// <summary>Δεύτερο ταμείο (client) — στέλνει τη μεταβολή στο host, μετά ξαναδιαβάζει την αληθινή κατάσταση.</summary>
    private async Task SyncThenRefreshAsync(string path, object body)
    {
        await RemoteSync.PostAsync(path, body);
        await RefreshFromHostAsync();
    }
}
