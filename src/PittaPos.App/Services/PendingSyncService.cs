using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PittaPos.App.Services;

/// <summary>Μία καταχώρηση που περιμένει να σταλεί στο κύριο ταμείο.</summary>
public sealed record PendingSyncItem(string Path, string Json, string Label, DateTime CreatedAt);

/// <summary>
/// «Ουρά εξόδου» του δεύτερου ταμείου: ό,τι ΔΕΝ πρόλαβε να φτάσει στο κύριο ταμείο (σβηστό, πεσμένο
/// δίκτυο, WiFi εκτός εμβέλειας) γράφεται εδώ σε τοπικό αρχείο και ξαναστέλνεται μόλις επανέλθει η
/// σύνδεση. Χωρίς αυτό η παραγγελία χανόταν οριστικά: το δεύτερο ταμείο δεν κρατάει δικά του αρχεία
/// (όλα τα διαβάζει από το κύριο), οπότε μια αποτυχημένη αποστολή δεν άφηνε κανένα ίχνος πουθενά —
/// και το χαρτί τυπωνόταν κανονικά (ο εκτυπωτής είναι τοπικός), οπότε ο ταμίας νόμιζε ότι όλα πήγαν
/// καλά. Η παραγγελία έλειπε και από τον τζίρο της ημέρας.
///
/// ΠΡΟΣΟΧΗ — μπαίνουν εδώ ΜΟΝΟ προσθήκες (νέα παραγγελία, νέα εγγραφή στον πίνακα διανομής). Δεν
/// μπαίνουν ποτέ ενέργειες κατάστασης (κλείσιμο μέρας, διαγραφή, εξόφληση, κλείσιμο τραπεζιού):
/// αυτές έχουν νόημα μόνο τη στιγμή που γίνονται, και μια καθυστερημένη επανάληψή τους αργότερα θα
/// έκανε ζημιά (π.χ. «καθάρισε τη μέρα» που ξαναφτάνει μετά από ώρα θα έσβηνε καινούριες παραγγελίες).
/// Αυτές συνεχίζουν να αποτυγχάνουν σιωπηλά και να διορθώνονται με το επόμενο refresh από το host.
/// </summary>
public partial class PendingSyncService : ObservableObject
{
    public static PendingSyncService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>Όριο ασφαλείας — αν το δίκτυο λείπει για ώρες, να μη φουσκώσει ανεξέλεγκτα το αρχείο.
    /// Σε τόσες αστάλτες καταχωρήσεις το πρόβλημα είναι ήδη ορατό στην οθόνη εδώ και πολλή ώρα.</summary>
    private const int MaxItems = 500;

    private readonly string _path;
    private readonly List<PendingSyncItem> _items = [];
    private bool _flushing;

    /// <summary>Πόσες καταχωρήσεις περιμένουν — 0 = όλα συγχρονισμένα. Δένεται στην οθόνη (προειδοποίηση).</summary>
    [ObservableProperty]
    private int _count;

    /// <summary>Υπάρχει έστω μία αστάλτη καταχώρηση — ανάβει την προειδοποίηση στην οθόνη.</summary>
    [ObservableProperty]
    private bool _hasPending;

    /// <summary>Έτοιμο κείμενο για την προειδοποίηση (π.χ. «2 παραγγελίες σε αναμονή από 21:14»).</summary>
    [ObservableProperty]
    private string _warningText = "";

    /// <summary>Η ώρα της παλαιότερης ασταλτης καταχώρησης — για το μήνυμα στην οθόνη.</summary>
    [ObservableProperty]
    private string _oldestLabel = "";

    private PendingSyncService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "pending-sync.json");

        if (!RemoteSync.IsClient)
            return;

        Load();
        // Πιο αραιά από τα υπόλοιπα polls (2-3 δλ): εδώ δεν διαβάζουμε κατάσταση, ξαναστέλνουμε — αν
        // το κύριο ταμείο λείπει, δεν κερδίζει τίποτα να το χτυπάμε συνέχεια.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += async (_, _) => await FlushAsync();
        timer.Start();
        _ = FlushAsync();
    }

    /// <summary>Βάζει μια προσθήκη στην ουρά (μόνο αφού έχει ήδη αποτύχει η κανονική αποστολή).
    /// ΠΑΝΤΑ στο UI thread: μια παραγγελία από το κινητό του σερβιτόρου φτάνει εδώ από thread του
    /// Kestrel, ενώ το FlushAsync τρέχει από DispatcherTimer στο UI thread — ταυτόχρονη μεταβολή της
    /// ίδιας λίστας από τα δύο θα μπορούσε να «φάει» καταχώρηση ή να χαλάσει τη δομή, σιωπηλά.</summary>
    public void Enqueue(string path, object body, string label)
    {
        if (!RemoteSync.IsClient)
            return;

        OnUi(() =>
        {
            if (_items.Count >= MaxItems)
                return;
            _items.Add(new PendingSyncItem(path, JsonSerializer.Serialize(body), label, DateTime.Now));
            Save();
            UpdateCounters();
            AppLog.Write("pending-sync", $"Σε αναμονή αποστολής ({_items.Count}): {label}");
        });
    }

    /// <summary>Τρέχει τη δουλειά στο UI thread (ή αμέσως, αν είμαστε ήδη εκεί).</summary>
    private static void OnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    /// <summary>Ξαναστέλνει ό,τι περιμένει, με τη σειρά που δημιουργήθηκε. Σταματά στην πρώτη αποτυχία,
    /// ώστε να μη φτάσουν ποτέ οι παραγγελίες στο κύριο ταμείο ανακατεμένες.</summary>
    public async Task FlushAsync()
    {
        if (_flushing || _items.Count == 0)
            return;
        _flushing = true;
        try
        {
            while (_items.Count > 0)
            {
                var item = _items[0];
                if (!await RemoteSync.PostRawAsync(item.Path, item.Json))
                    return;
                _items.RemoveAt(0);
                Save();
                UpdateCounters();
                AppLog.Write("pending-sync", $"Στάλθηκε με καθυστέρηση: {item.Label}");
            }
        }
        finally
        {
            _flushing = false;
        }
    }

    private void UpdateCounters()
    {
        Count = _items.Count;
        HasPending = _items.Count > 0;
        OldestLabel = _items.Count > 0 ? _items[0].CreatedAt.ToString("HH:mm") : "";
        WarningText = _items.Count switch
        {
            0 => "",
            1 => $"1 παραγγελία δεν έχει σταλεί στο κύριο ταμείο (από {OldestLabel}) — έλεγξε το δίκτυο",
            _ => $"{_items.Count} παραγγελίες δεν έχουν σταλεί στο κύριο ταμείο (από {OldestLabel}) — έλεγξε το δίκτυο",
        };
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var items = JsonSerializer.Deserialize<List<PendingSyncItem>>(File.ReadAllText(_path)) ?? [];
            _items.AddRange(items);
            UpdateCounters();
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
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_items, JsonOpts));
        }
        catch (Exception)
        {
            // Αποτυχία εγγραφής δεν πρέπει να μπλοκάρει το ταμείο
        }
    }
}
