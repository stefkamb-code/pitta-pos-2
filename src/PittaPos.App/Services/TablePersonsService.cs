using System.IO;
using System.Text.Json;

namespace PittaPos.App.Services;

/// <summary>
/// Πόσα άτομα έχει ένα ανοιχτό τραπέζι και ποιο τεμάχιο ανήκει σε ποιον.
///
/// Υπάρχει για ΕΝΑΝ λόγο: το μαγαζί κόβει τις αποδείξεις σε ξεχωριστή ταμειακή μηχανή και θέλει να
/// ξέρει <b>πόσες αποδείξεις θα κόψει και σε τι ποσά</b>. Ο κανόνας είναι απλός — <b>ένα άτομο = μία
/// απόδειξη</b>. Ο σερβιτόρος ρωτά «πόσα άτομα;» μόλις ανοίξει το τραπέζι (το ξέρει αμέσως) και μετά
/// γράφει την παραγγελία ανά άτομο· ο τρόπος πληρωμής ΔΕΝ ρωτιέται εκεί, μπαίνει στο ταμείο τη στιγμή
/// που κλείνει το κάθε άτομο.
///
/// <para>Ξεχωριστό αρχείο από τις παραγγελίες ΓΙΑ ΛΟΓΟ: το «ποιος πήρε τι» είναι πληροφορία του
/// τραπεζιού, όχι της πώλησης. Δεν πρέπει να μπει στο ιστορικό/στατιστικά ούτε στην απόδειξη —
/// σβήνεται μαζί με το τραπέζι, ακριβώς όπως οι εξοφλήσεις (βλ. <see cref="TableSettlementService"/>,
/// το οποίο και αντιγράφει σε δομή: ίδια κλειδιά «παραγγελία:γραμμή:τεμάχιο», ίδιος συγχρονισμός με
/// το δεύτερο ταμείο, ίδιο καθάρισμα στο κλείσιμο.)</para>
///
/// <para>Τραπέζι με 0 ή 1 άτομο σημαίνει «πληρώνουν μαζί» και συμπεριφέρεται ΑΚΡΙΒΩΣ όπως πριν
/// υπάρξει αυτό το αρχείο — το συνηθισμένο τραπέζι δεν επιβαρύνεται με τίποτα.</para>
/// </summary>
public class TablePersonsService
{
    public static TablePersonsService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>Τα ονόματα των ατόμων όπως τα βλέπει ο ταμίας και ο σερβιτόρος. Γράμματα και όχι
    /// νούμερα: «Α» δεν μπερδεύεται με ποσότητα, με αριθμό τραπεζιού ή με ποσό.</summary>
    private const string Letters = "ΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠΡΣΤΥΦΧΨΩ";

    /// <summary>Πάνω από τόσα άτομα δεν είναι τραπέζι, είναι λάθος πάτημα.</summary>
    public const int MaxPersons = 24;

    private readonly string _path;

    /// <summary>τραπέζι → πλήθος ατόμων.</summary>
    private readonly Dictionary<int, int> _counts = [];

    /// <summary>τραπέζι → (κλειδί τεμαχίου → άτομο, 0-based).</summary>
    private readonly Dictionary<int, Dictionary<string, int>> _assigned = [];

    /// <summary>Σηκώνεται σε κάθε αλλαγή — η οθόνη τραπεζιού ξαναχτίζει τα σύνολα ανά άτομο.</summary>
    public event Action? Changed;

    /// <summary>Μορφή αποθήκευσης — τα δύο μαζί, ώστε το αρχείο να διαβάζεται μονοκόμματα.</summary>
    private sealed class PersonsData
    {
        public Dictionary<int, int> Counts { get; set; } = [];
        public Dictionary<int, Dictionary<string, int>> Assigned { get; set; } = [];
    }

    private TablePersonsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "table-persons.json");
        if (RemoteSync.IsClient)
            RemoteSync.StartPolling(TimeSpan.FromSeconds(2), RefreshFromHostAsync);
        else
            Load();
    }

    /// <summary>Το γράμμα του ατόμου: 0 → «Α», 1 → «Β». Πάνω από το αλφάβητο γυρνά σε νούμερο, ώστε να
    /// μη σκάσει ποτέ σε τραπέζι-εκδήλωση.</summary>
    public static string Label(int person) =>
        person >= 0 && person < Letters.Length ? Letters[person].ToString() : "#" + (person + 1);

    /// <summary>Πόσα άτομα δηλώθηκαν. 0 = δεν ρωτήθηκε ποτέ, δηλαδή «πληρώνουν μαζί».</summary>
    public int CountFor(int table) => _counts.GetValueOrDefault(table);

    /// <summary>Έχει το τραπέζι χωρισμό σε άτομα; Με ένα άτομο δεν έχει νόημα να δείχνουμε τίποτα.</summary>
    public bool IsSplit(int table) => CountFor(table) > 1;

    /// <summary>Ποια άτομα έχουν ήδη παραγγείλει κάτι — ώστε ένας νέος γύρος να ξεκινά από τον πρώτο
    /// που δεν έχει, αντί να ξαναρωτά όποιον έχει ήδη τελειώσει.</summary>
    public HashSet<int> PersonsWithItems(int table) =>
        _assigned.TryGetValue(table, out var map) ? [.. map.Values] : [];

    /// <summary>Σε ποιο άτομο ανήκει το τεμάχιο — null αν δεν έχει χρεωθεί σε κανέναν ακόμα.</summary>
    public int? PersonFor(int table, int orderNumber, int lineIndex, int unit) =>
        _assigned.TryGetValue(table, out var map) && map.TryGetValue(Key(orderNumber, lineIndex, unit), out var p)
            ? p
            : null;

    /// <summary>«Πόσα άτομα;» — η ερώτηση στο άνοιγμα του τραπεζιού. Μικρότερο πλήθος από πριν αφήνει
    /// ορφανά τεμάχια χρεωμένα σε άτομο που δεν υπάρχει πια· αυτά αποδεσμεύονται αντί να κρυφτούν.</summary>
    public void SetCount(int table, int count)
    {
        count = Math.Clamp(count, 0, MaxPersons);
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/table-persons/count", new { Table = table, Count = count });
            return;
        }
        if (CountFor(table) == count)
            return;

        _counts[table] = count;
        if (_assigned.TryGetValue(table, out var map))
            foreach (var key in map.Where(kv => kv.Value >= count).Select(kv => kv.Key).ToList())
                map.Remove(key);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Χρεώνει το τεμάχιο σε άτομο. Αρνητικό άτομο = αποδέσμευση («κοινό», μπαίνει στο τέλος
    /// χειροκίνητα σε όποιον το πληρώσει).</summary>
    public void Assign(int table, int orderNumber, int lineIndex, int unit, int person)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/table-persons/assign",
                new { Table = table, OrderNumber = orderNumber, LineIndex = lineIndex, Unit = unit, Person = person });
            return;
        }
        if (!_assigned.TryGetValue(table, out var map))
            _assigned[table] = map = [];

        var key = Key(orderNumber, lineIndex, unit);
        if (person < 0)
        {
            if (!map.Remove(key))
                return;
        }
        else
        {
            if (map.TryGetValue(key, out var existing) && existing == person)
                return;
            map[key] = person;
            // Το τραπέζι μπορεί να μη ρωτήθηκε ποτέ (παραγγελία από ταμείο): αν έρθει χρέωση σε άτομο
            // Δ, τότε έχει τουλάχιστον 4 άτομα — αλλιώς το άτομο θα ήταν χρεωμένο και αόρατο.
            if (CountFor(table) < person + 1)
                _counts[table] = person + 1;
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Δηλώθηκαν 4 άτομα αλλά ο Δ τελικά δεν πήρε τίποτα → το τραπέζι έχει 3, όχι 4. Καλείται μόλις
    /// καταχωρηθεί ολόκληρος ο γύρος (ταμείο και κινητό), ώστε να μη μένει κενό άτομο στην οθόνη.
    /// <para>Κόβει μέχρι τον ΜΕΓΑΛΥΤΕΡΟ δείκτη που έχει προϊόντα, όχι μέχρι το πλήθος τους: αν πήραν ο
    /// Α και ο Γ, το τραπέζι έχει 3 θέσεις (ο Β απλώς δεν πήρε) — αλλιώς ο Γ θα εξαφανιζόταν μαζί με
    /// την παραγγελία του.</para>
    /// <para>Αν δεν έχει προϊόντα ΚΑΝΕΙΣ, το τραπέζι καθαρίζει μόνο εφόσον δεν είναι καν ανοιχτό:
    /// ανοιχτό τραπέζι χωρίς χρεώσεις σε άτομα σημαίνει παραγγελίες που ήρθαν χωρίς άτομα (π.χ. παλιό
    /// APK) — δεν του σβήνουμε τον χωρισμό από κάτω.</para>
    /// </summary>
    public void ShrinkToWhoOrdered(int table)
    {
        var withItems = PersonsWithItems(table);
        if (withItems.Count > 0)
            SetCount(table, withItems.Max() + 1);
        else if (!TableStatusService.Instance.OpenSince.ContainsKey(table))
            ClearTable(table);
    }

    /// <summary>Καλείται όταν κλείνει το τραπέζι — καθαρό ξεκίνημα για τον επόμενο πελάτη.
    /// <para>Χρειάζεται συγχρονισμό όπως τα αδέλφια του: φτάνει εδώ και από το ShrinkToWhoOrdered, που
    /// τρέχει και στο δεύτερο ταμείο (βλ. OrderWizardViewModel) — χωρίς αυτό ο χωρισμός σε άτομα
    /// καθαριζόταν μόνο τοπικά και ξαναγύριζε με το επόμενο poll από το κύριο ταμείο.</para></summary>
    public void ClearTable(int table)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/table-persons/clear-table", new { Table = table });
            return;
        }
        var removed = _counts.Remove(table);
        removed |= _assigned.Remove(table);
        if (!removed)
            return;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Ακυρώθηκε ολόκληρος γύρος — φεύγουν και οι χρεώσεις των τεμαχίων του.</summary>
    public void ClearOrder(int table, int orderNumber)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/table-persons/clear-order", new { Table = table, OrderNumber = orderNumber });
            return;
        }
        if (!_assigned.TryGetValue(table, out var map))
            return;
        var toDrop = map.Keys.Where(k => k.StartsWith(orderNumber + ":", StringComparison.Ordinal)).ToList();
        if (toDrop.Count == 0)
            return;
        foreach (var k in toDrop)
            map.Remove(k);
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Διαγράφηκε γραμμή του γύρου — οι επόμενες μετακινούνται μία θέση πίσω. Ίδια λογική με το
    /// <see cref="TableSettlementService.ShiftAfterRemoval"/>: χωρίς αυτό, η χρέωση «του Β» θα
    /// κολλούσε σιωπηλά σε άλλο προϊόν.
    /// </summary>
    public void ShiftAfterRemoval(int table, int orderNumber, int removedIndex)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/table-persons/shift-after-removal",
                new { Table = table, OrderNumber = orderNumber, RemovedIndex = removedIndex });
            return;
        }
        if (!_assigned.TryGetValue(table, out var map))
            return;

        var updated = new Dictionary<string, int>();
        foreach (var (key, person) in map)
        {
            var parts = key.Split(':');
            if (parts.Length != 3
                || !int.TryParse(parts[0], out var ord)
                || !int.TryParse(parts[1], out var idx)
                || !int.TryParse(parts[2], out var unit))
                continue;
            if (ord != orderNumber)
            {
                updated[key] = person;
                continue;
            }
            if (idx == removedIndex)
                continue; // η ίδια η διαγραμμένη γραμμή φεύγει
            updated[Key(ord, idx > removedIndex ? idx - 1 : idx, unit)] = person;
        }
        _assigned[table] = updated;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Στιγμιότυπο για το endpoint συγχρονισμού του host.</summary>
    public PersonsSnapshot Snapshot() =>
        new(new Dictionary<int, int>(_counts),
            _assigned.ToDictionary(kv => kv.Key, kv => new Dictionary<string, int>(kv.Value)));

    /// <summary>Ό,τι χρειάζεται το δεύτερο ταμείο για να δείχνει την ίδια εικόνα.</summary>
    public sealed record PersonsSnapshot(Dictionary<int, int> Counts, Dictionary<int, Dictionary<string, int>> Assigned);

    /// <summary>Δεύτερο ταμείο (client) — αντικαθιστά την τοπική εικόνα με την κατάσταση του host.</summary>
    private async Task RefreshFromHostAsync()
    {
        var data = await RemoteSync.GetAsync<PersonsSnapshot>("/api/sync/table-persons");
        if (data is null)
            return;
        _counts.Clear();
        foreach (var (table, count) in data.Counts)
            _counts[table] = count;
        _assigned.Clear();
        foreach (var (table, entries) in data.Assigned)
            _assigned[table] = new Dictionary<string, int>(entries);
        Changed?.Invoke();
    }

    private async Task SyncThenRefreshAsync(string path, object body)
    {
        await RemoteSync.PostAsync(path, body);
        await RefreshFromHostAsync();
    }

    private static string Key(int orderNumber, int lineIndex, int unit) => orderNumber + ":" + lineIndex + ":" + unit;

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var data = JsonSerializer.Deserialize<PersonsData>(File.ReadAllText(_path));
            if (data is null)
                return;
            foreach (var (table, count) in data.Counts)
                _counts[table] = count;
            foreach (var (table, entries) in data.Assigned)
                _assigned[table] = new Dictionary<string, int>(entries);
        }
        catch (Exception)
        {
            // Χαλασμένο αρχείο — ξεκίνα άδειο αντί να ρίξεις το ταμείο. Χάνεται μόνο ο χωρισμός σε
            // άτομα των ανοιχτών τραπεζιών, όχι παραγγελία ή τζίρος.
        }
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_path,
                JsonSerializer.Serialize(new PersonsData { Counts = _counts, Assigned = _assigned }, JsonOpts));
        }
        catch (Exception ex)
        {
            // Δεν μπλοκάρει το ταμείο — αλλά ΓΡΑΦΕΤΑΙ. Μια αποτυχία εγγραφής (γεμάτος δίσκος,
            // κλείδωμα από antivirus, χαλασμένος δίσκος) σήμαινε ότι τα δεδομένα ζούσαν πια μόνο
            // στη μνήμη και θα χάνονταν στο επόμενο κλείσιμο — χωρίς κανένα ίχνος πουθενά.
            AppLog.Write("save", $"Δεν γράφτηκε το «{Path.GetFileName(_path)}»: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
