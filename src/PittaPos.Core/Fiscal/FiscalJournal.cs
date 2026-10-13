using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PittaPos.Core.Fiscal;

/// <summary>
/// Ένα κομμάτι του λογαριασμού όπως το ξέρει το ταμείο: μια γραμμή παραγγελίας («παραγγελία:γραμμή») ή ένα τεμάχιό της
/// («παραγγελία:γραμμή:τεμάχιο»), με τον τζίρο του (με ΦΠΑ) και τον συντελεστή της κατηγορίας του.
/// </summary>
public sealed record FiscalItem(string Key, string Name, int Quantity, decimal Revenue, int VatRate)
{
    /// <summary>Ο αριθμός παραγγελίας (γύρου) από το κλειδί.</summary>
    [JsonIgnore]
    public int Order => int.Parse(Key.AsSpan(0, Key.IndexOf(':') is var i and >= 0 ? i : Key.Length));

    /// <summary>Ακυρωμένο: κρατιέται στο παραστατικό που κόπηκε, αλλά δεν ταιριάζει ποτέ ξανά με γραμμή του ταμείου.</summary>
    [JsonIgnore]
    public bool IsVoid => Key.Contains('~');

    /// <summary>«παραγγελία:γραμμή» — χωρίς το τεμάχιο.</summary>
    [JsonIgnore]
    public string LineKey => IsVoid ? Key : Key.Count(c => c == ':') >= 2 ? Key[..Key.LastIndexOf(':')] : Key;

    /// <summary>Η γραμμή (θέση στην παραγγελία) από το κλειδί.</summary>
    [JsonIgnore]
    public int Line => int.Parse(LineKey.AsSpan(LineKey.IndexOf(':') + 1));

    public static string KeyFor(int order, int line) => order + ":" + line;
    public static string KeyFor(int order, int line, int unit) => order + ":" + line + ":" + unit;
}

public enum FiscalEntryKind
{
    /// <summary>11.1 — πληρωμή (όρθιος, διανομή, εφαρμογή, ή ό,τι πληρώθηκε σε τραπέζι).</summary>
    Receipt,
    /// <summary>8.6 — γύρος τραπεζιού, τη στιγμή που γράφεται.</summary>
    Slip,
    /// <summary>11.4 — πιστωτικό για κάτι που είχε ήδη αποδειχθεί.</summary>
    Credit,
    /// <summary>Ακύρωση δελτίου 8.6 που δεν πληρώθηκε ποτέ.</summary>
    SlipCancel,
}

public enum FiscalEntryState
{
    /// <summary>Περιμένει να φύγει (ή να ξαναδοκιμαστεί).</summary>
    Waiting,
    /// <summary>Φεύγει αυτή τη στιγμή.</summary>
    Sending,
    /// <summary>Κόπηκε — έχει ΜΑΡΚ (ή ο πάροχος το κρατά για την ΑΑΔΕ με έγκυρο QR).</summary>
    Issued,
    /// <summary>Ακυρώθηκε πριν φύγει — δεν κόπηκε ποτέ, δεν χρειάζεται τίποτα.</summary>
    Dropped,
    /// <summary>Κάρτα που δεν πέρασε: δεν ξαναδοκιμάζεται μόνη της (ο πελάτης στέκεται μπροστά στο τερματικό) —
    /// ο ταμίας λέει «ξανά» ή «μετρητά».</summary>
    NeedsDecision,
    /// <summary>
    /// Αυτόματη έκδοση κλειστή (βλ. <see cref="ReceiptPolicy"/>): γραμμένη, αλλά δεν φεύγει ακόμα — περιμένει την αποστολή
    /// ή την πληρωμή. Διορθώνεται/ακυρώνεται χωρίς να κοπεί τίποτα. ΔΕΝ μένει ποτέ αδήλωτη: φεύγει μόλις τη ζητήσει το
    /// ταμείο, ή μόνη της στο <see cref="FiscalEntry.HeldUntil"/>, ή στο κλείσιμο της ημέρας.
    /// </summary>
    Held,
}

/// <summary>Ένα παραστατικό στο ημερολόγιο: τι πρέπει να κοπεί, και τι έγινε.</summary>
public sealed class FiscalEntry
{
    /// <summary>Μοναδικό και ΣΤΑΘΕΡΟ — πάει στον πάροχο ως external_id, οπότε μια επανάληψη δεν κόβει δεύτερο.</summary>
    public string Id { get; set; } = "";
    public FiscalEntryKind Kind { get; set; }
    public FiscalEntryState State { get; set; }
    public List<FiscalItem> Items { get; set; } = [];
    public FiscalPay Pay { get; set; }
    /// <summary>Το ποσό πάει στο τερματικό κάρτας ΤΩΡΑ (πληρωμή ή επιστροφή) — ο πελάτης είναι μπροστά του.</summary>
    public bool UseTerminal { get; set; }
    /// <summary>Διανομή με κάρτα: το ποσό προφορτώνεται σε ΑΥΤΟ το τερματικό (φορητό του διανομέα) και πληρώνεται στην πόρτα —
    /// χωρίς αναμονή εδώ, χωρίς ερώτηση στον ταμία· αν αποτύχει η αποστολή, ξαναδοκιμάζεται σαν όλα τα άλλα.</summary>
    public string PreloadTerminal { get; set; } = "";
    /// <summary>Το «τραπέζι από τη στιγμή που άνοιξε ως τη στιγμή που έκλεισε» — όλα τα δελτία και οι αποδείξεις του.</summary>
    public string TableSession { get; set; } = "";
    public string TableName { get; set; } = "";
    /// <summary>Ποια παραστατικά του ημερολογίου συσχετίζει: τα δελτία που κλείνει μια απόδειξη, την απόδειξη που πιστώνει
    /// ένα πιστωτικό, το δελτίο που ακυρώνεται.</summary>
    public List<string> Correlates { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    /// <summary>Σειρά και αύξων αριθμός από το ταμείο (βλ. <see cref="FiscalJournal.SeriesFor"/>).</summary>
    public string Series { get; set; } = "";
    public long Number { get; set; }
    public DateTime? NextTryAt { get; set; }
    /// <summary>Πληρωμή τραπεζιού που μαζεύει ακόμα τεμάχια του ίδιου πατήματος (βλ. <see cref="FiscalJournal.TablePayment"/>).</summary>
    public DateTime? GatherUntil { get; set; }
    public int Attempts { get; set; }
    public string LastError { get; set; } = "";
    /// <summary>Ακυρώθηκε/άλλαξε όσο έφευγε: μόλις κοπεί, γίνεται η αναίρεση (κλειδιά γραμμών· κενό = όλα).</summary>
    public List<string>? UndoLinesWhenIssued { get; set; }
    /// <summary>Άλλαξε τρόπος πληρωμής όσο έφευγε: ποια παραγγελία, και σε τι.</summary>
    public int? PayChangeOrder { get; set; }
    public FiscalPay? PayChangeWhenIssued { get; set; }
    public FiscalResult? Result { get; set; }
    /// <summary>Το χαρτί της απόδειξης (ΜΑΡΚ + QR) βγήκε — δεν ξανατυπώνεται μόνο του, ούτε μετά από κλείσιμο.</summary>
    public bool Printed { get; set; }
    /// <summary>Ζητήθηκε χαρτί πριν κοπεί (π.χ. «🧾 ΑΠΟΔΕΙΞΗ» σε απόδειξη που περίμενε): τυπώνεται μόλις κοπεί, μία φορά.</summary>
    public bool PrintWhenIssued { get; set; }
    /// <summary>Σε αναμονή (<see cref="FiscalEntryState.Held"/>) με όριο μέσα στη μέρα: ως πότε — μετά φεύγει μόνη της.
    /// null = χωρίς όριο (φεύγει με την πληρωμή/αποστολή, ή στο κλείσιμο της ημέρας).</summary>
    public DateTime? HeldUntil { get; set; }
    /// <summary>Γιατί περίμενε (από το <see cref="ReceiptPolicy"/>) — για το ημερολόγιο ελέγχου και την οθόνη.</summary>
    public string HoldReason { get; set; } = "";
    /// <summary>Πότε σταμάτησε να περιμένει: αυτή είναι η ημερομηνία έκδοσης της απόδειξης, όχι η ώρα της καταχώρησης.</summary>
    public DateTime? ReleasedAt { get; set; }

    [JsonIgnore]
    public decimal Total => Items.Sum(i => i.Revenue);
    [JsonIgnore]
    public string Mark => Result?.Mark ?? "";
    [JsonIgnore]
    public bool IsLive => State is not FiscalEntryState.Dropped;
}

/// <summary>Ό,τι κρατά το ημερολόγιο: τα παραστατικά, και τους μετρητές αρίθμησης ανά σειρά (που ΔΕΝ ξαναρχίζουν ποτέ —
/// ούτε όταν σβήνονται τα παλιά παραστατικά).</summary>
public sealed class FiscalJournalData
{
    public List<FiscalEntry> Entries { get; set; } = [];
    public Dictionary<string, long> Counters { get; set; } = [];
}

/// <summary>Πού ζει το ημερολόγιο — αρχείο στο ταμείο, μνήμη στις δοκιμές.</summary>
public interface IFiscalJournalStore
{
    FiscalJournalData Load();
    void Save(FiscalJournalData data);
}

public sealed class MemoryFiscalJournalStore : IFiscalJournalStore
{
    private string _json = "{}";
    public FiscalJournalData Load() => FiscalJournalData(_json);
    public void Save(FiscalJournalData data) => _json = JsonSerializer.Serialize(data, FiscalJournal.JsonOptions);

    /// <summary>Νέα μορφή (αντικείμενο) ή η πρώτη (σκέτη λίστα παραστατικών, χωρίς μετρητές).</summary>
    internal static FiscalJournalData FiscalJournalData(string json) => json.TrimStart().StartsWith('[')
        ? new FiscalJournalData { Entries = JsonSerializer.Deserialize<List<FiscalEntry>>(json, FiscalJournal.JsonOptions) ?? [] }
        : JsonSerializer.Deserialize<FiscalJournalData>(json, FiscalJournal.JsonOptions) ?? new FiscalJournalData();
}

/// <summary>Αρχείο JSON, γραμμένο σε προσωρινό και μετονομασμένο: μια διακοπή ρεύματος δεν αφήνει μισό ημερολόγιο.</summary>
public sealed class FileFiscalJournalStore(string path) : IFiscalJournalStore
{
    public FiscalJournalData Load() =>
        File.Exists(path) ? MemoryFiscalJournalStore.FiscalJournalData(File.ReadAllText(path)) : new FiscalJournalData();

    public void Save(FiscalJournalData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, FiscalJournal.JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>
/// Ο «εγκέφαλος» των παραστατικών του ταμείου — ίδιος για ΚΑΘΕ πάροχο (μιλά μόνο με το <see cref="IFiscalProvider"/>).
/// Το ταμείο του λέει τι έγινε· εκείνος αποφασίζει τι κόβεται και πότε:
///
/// <list type="bullet">
/// <item><see cref="Sale"/>: παραγγελία εκτός τραπεζιού → απόδειξη 11.1.</item>
/// <item><see cref="TableRound"/>: γύρος τραπεζιού → δελτίο 8.6, αμέσως.</item>
/// <item><see cref="TablePayment"/>: «πληρώθηκαν αυτά, έτσι» → ΜΙΑ απόδειξη 11.1 που κλείνει τα δελτία τους.</item>
/// <item><see cref="Cancel"/>: ό,τι δεν έφυγε ακόμα δεν φεύγει ποτέ· δελτίο που κόπηκε ακυρώνεται· απόδειξη που κόπηκε
///   πιστώνεται (11.4) για ό,τι ακυρώθηκε.</item>
/// <item><see cref="ChangePayment"/>: διόρθωση μετρητά↔κάρτα → πιστωτικό της παλιάς και νέα απόδειξη.</item>
/// </list>
///
/// <para><b>Ούτε χαμένο, ούτε διπλό.</b> Κάθε παραστατικό γράφεται στο ημερολόγιο ΠΡΙΝ φύγει, με σταθερό αναγνωριστικό
/// (external_id). Χωρίς internet περιμένει και ξαναδοκιμάζει μόνο του· αν η απάντηση χαθεί, η επανάληψη με το ίδιο
/// αναγνωριστικό φέρνει το ίδιο παραστατικό, όχι δεύτερο. Οι αποδείξεις τραπεζιού περιμένουν τα ΜΑΡΚ των δελτίων τους.</para>
/// </summary>
public sealed class FiscalJournal
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Δελτίο ανοιχτό περισσότερο από αυτό = σήμα: πρέπει να κλείσει με απόδειξη μέσα σε 24 ώρες.</summary>
    public static readonly TimeSpan SlipWarning = TimeSpan.FromHours(20);

    private readonly IFiscalJournalStore _store;
    private readonly string _prefix;
    private readonly Func<DateTime> _now;
    private readonly List<FiscalEntry> _entries;
    private readonly Dictionary<string, long> _counters;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _processing = new(1, 1);

    /// <summary>
    /// Η σειρά κάθε είδους παραστατικού, για τους παρόχους που θέλουν σειρά και αύξοντα αριθμό από το ταμείο (Epsilon Digital,
    /// SBZ). Ο Wrapp αριθμεί μόνος του και τα αγνοεί. Ο αριθμός δίνεται τη στιγμή που γράφεται το παραστατικό και δεν αλλάζει
    /// ποτέ — μια επανάληψη στέλνει τον ίδιο.
    /// </summary>
    public static string SeriesFor(FiscalEntryKind kind) => kind switch
    {
        FiscalEntryKind.Receipt => "ΑΛΠ",
        FiscalEntryKind.Credit => "ΠΛΠ",
        _ => "ΔΠΕ",
    };

    /// <summary>Το τερματικό κάρτας του μαγαζιού (Terminal ID από τις Ρυθμίσεις).</summary>
    public string TerminalId { get; set; } = "";

    /// <summary>Το ημερολόγιο ελέγχου: κάθε βήμα κάθε παραστατικού (βλ. <see cref="FileFiscalAudit"/>).</summary>
    public IFiscalAudit Audit { get; set; } = NoFiscalAudit.Instance;

    /// <summary>Μία γραμμή στο ημερολόγιο ελέγχου — και για γεγονότα του ταμείου που δεν είναι παραστατικό (π.χ. ρύθμιση).</summary>
    public void Log(string ev, FiscalEntry? e, string detail = "") => Audit.Write(new FiscalAuditRecord(
        _now(), ev, e?.Id ?? "", e?.Kind.ToString() ?? "", e?.State.ToString() ?? "",
        e is null ? "" : string.Join(",", e.Items.Where(i => !i.IsVoid).Select(i => i.Order).Distinct()),
        e?.Total ?? 0, e?.Pay.ToString() ?? "", detail));

    /// <summary>
    /// Δουλεύει ΜΟΝΟ όταν το μαγαζί έχει διαλέξει πάροχο που το ταμείο ξέρει. Χωρίς πάροχο (όλα τα μαγαζιά σήμερα) το
    /// ταμείο δουλεύει ακριβώς όπως πριν: κανένα ημερολόγιο, καμία αποστολή, κανένα χαρτί παραπάνω.
    /// </summary>
    public static bool IsActive(FiscalSettings settings) =>
        settings.Provider.Length > 0 && FiscalProviders.Supported.Contains(settings.Provider);

    public event Action? Changed;

    /// <param name="prefix">Ξεχωρίζει το μαγαζί στα αναγνωριστικά — πολλά μαγαζιά μπορεί να κόβουν στον ίδιο λογαριασμό.</param>
    public FiscalJournal(IFiscalJournalStore store, string prefix, Func<DateTime>? clock = null)
    {
        _store = store;
        _prefix = new string(prefix.Where(char.IsAsciiLetterOrDigit).ToArray()) is { Length: > 0 } p ? p : "pos";
        _now = clock ?? (() => DateTime.Now);
        var data = store.Load();
        _entries = data.Entries;
        _counters = data.Counters;
        // Πρώτη μορφή του αρχείου (χωρίς μετρητές): ο μετρητής ξεκινά από τον μεγαλύτερο αριθμό που υπάρχει ήδη.
        foreach (var g in _entries.Where(e => e.Series.Length > 0).GroupBy(e => e.Series))
            _counters[g.Key] = Math.Max(_counters.GetValueOrDefault(g.Key), g.Max(e => e.Number));
        // Κόπηκε το ρεύμα την ώρα που έφευγε: ξαναπάει με το ίδιο αναγνωριστικό — αν είχε κοπεί, γυρίζει το ίδιο.
        foreach (var e in _entries.Where(e => e.State == FiscalEntryState.Sending))
            e.State = FiscalEntryState.Waiting;
    }

    public IReadOnlyList<FiscalEntry> Entries { get { lock (_gate) return [.. _entries]; } }

    /// <summary>Πόσα περιμένουν ακόμα να φύγουν.</summary>
    public int WaitingCount { get { lock (_gate) return _entries.Count(e => e.State is FiscalEntryState.Waiting or FiscalEntryState.Sending); } }

    /// <summary>Κάρτες που δεν πέρασαν και περιμένουν απόφαση από τον ταμία.</summary>
    public IReadOnlyList<FiscalEntry> NeedsDecision { get { lock (_gate) return [.. _entries.Where(e => e.State == FiscalEntryState.NeedsDecision)]; } }

    /// <summary>Δελτία που κόπηκαν και δεν έχει κλείσει ακόμα καμία απόδειξη ούτε ακύρωση.</summary>
    public IReadOnlyList<FiscalEntry> OpenSlips
    {
        get
        {
            lock (_gate)
                return [.. _entries.Where(e => e.Kind == FiscalEntryKind.Slip && e.State == FiscalEntryState.Issued && !IsClosed(e))];
        }
    }

    /// <summary>Αποδείξεις σε αναμονή (αυτόματη έκδοση κλειστή) — καμία δεν ξεχνιέται, βλ. <see cref="FiscalEntryState.Held"/>.</summary>
    public IReadOnlyList<FiscalEntry> Held { get { lock (_gate) return [.. _entries.Where(e => e.State == FiscalEntryState.Held)]; } }

    /// <summary>Αποδείξεις που ζητήθηκαν σε χαρτί πριν κοπούν και κόπηκαν τώρα — τυπώνονται μία φορά (χαρτί δεν βγαίνει
    /// αλλιώς μόνο του· όποτε το ζητήσει το μαγαζί).</summary>
    public IReadOnlyList<FiscalEntry> ToPrint
    {
        get
        {
            lock (_gate)
                return [.. _entries.Where(e => e.State == FiscalEntryState.Issued && e.PrintWhenIssued && !e.Printed
                                               && e.Kind == FiscalEntryKind.Receipt)];
        }
    }

    /// <summary>Όλα τα παραστατικά μιας παραγγελίας (για την οθόνη: σε αναμονή, φεύγει, κόπηκε με ΜΑΡΚ…).</summary>
    public IReadOnlyList<FiscalEntry> EntriesFor(int orderNumber)
    {
        lock (_gate)
            return [.. _entries.Where(e => e.Items.Any(i => !i.IsVoid && i.Order == orderNumber)).OrderBy(e => e.CreatedAt)];
    }

    /// <summary>Οι αποδείξεις που ισχύουν (κόπηκαν και δεν πιστώθηκαν ολόκληρες) για αυτές τις παραγγελίες — για το «τύπωσε
    /// απόδειξη» όποτε το ζητήσει το μαγαζί, από τον υπολογιστή ή το κινητό.</summary>
    public IReadOnlyList<FiscalEntry> ReceiptsFor(IEnumerable<int> orderNumbers)
    {
        var orders = orderNumbers.ToHashSet();
        lock (_gate)
            return [.. _entries.Where(e => e.Kind == FiscalEntryKind.Receipt && e.State == FiscalEntryState.Issued
                                           && e.Items.Any(i => !i.IsVoid && orders.Contains(i.Order)))
                .OrderBy(e => e.CreatedAt)];
    }

    public void MarkPrinted(string id)
    {
        lock (_gate)
        {
            if (Find(id) is not { } e || e.Printed) return;
            e.Printed = true;
            Log("τυπώθηκε", e, $"{e.Result?.Series} {e.Result?.Number} ΜΑΡΚ {e.Mark}".Trim());
            Commit();
        }
    }

    /// <summary>Ανοιχτά δελτία πάνω από <see cref="SlipWarning"/> — να φανούν πριν περάσει το 24ωρο.</summary>
    public IReadOnlyList<FiscalEntry> OverdueSlips => [.. OpenSlips.Where(e => _now() - e.CreatedAt > SlipWarning)];

    // ================================================================ τι έγινε στο ταμείο

    /// <summary>Παραγγελία εκτός τραπεζιού (όρθιος, διανομή, παραλαβή, εφαρμογή) → απόδειξη.</summary>
    /// <param name="preloadTerminal">Διανομή με κάρτα: το φορητό POS του διανομέα — το ποσό προφορτώνεται εκεί και πληρώνεται
    /// στην πόρτα. Κενό = η κάρτα πάει ΤΩΡΑ στο τερματικό του μαγαζιού (όρθιος).</param>
    /// <param name="hold">Αυτόματη έκδοση κλειστή (βλ. <see cref="ReceiptPolicy"/>): γράφεται τώρα αλλά περιμένει (βλ.
    /// <see cref="FiscalEntryState.Held"/>) — την πληρωμή/αποστολή, ή το κλείσιμο της ημέρας.</param>
    /// <param name="holdLimit">Προαιρετικό όριο μέσα στη μέρα· null = κανένα.</param>
    /// <param name="reason">Γιατί αποφασίστηκε έτσι — στο ημερολόγιο ελέγχου.</param>
    /// <param name="waitsFor">Για την οθόνη: τι περιμένει (π.χ. «κόβεται όταν φύγει με τον διανομέα»).</param>
    public FiscalEntry? Sale(int orderNumber, DateTime businessDay, IEnumerable<FiscalItem> items, FiscalPay pay, string preloadTerminal = "",
        bool hold = false, TimeSpan? holdLimit = null, string reason = "", string waitsFor = "")
    {
        var list = Billable(items);
        if (list.Count == 0) return null;
        var preload = pay == FiscalPay.Card && preloadTerminal.Length > 0;
        lock (_gate)
        {
            var e = new FiscalEntry
            {
                Id = NewId(businessDay, orderNumber, "r"),
                Kind = FiscalEntryKind.Receipt,
                Items = list,
                Pay = pay,
                UseTerminal = pay == FiscalPay.Card && !preload,
                PreloadTerminal = preload ? preloadTerminal : "",
            };
            Add(e, commit: false, reason);
            if (hold)
            {
                // Στην ίδια κλειδαριά με την προσθήκη: η αποστολή δεν προλαβαίνει να τη δει σαν «έτοιμη».
                e.State = FiscalEntryState.Held;
                e.HeldUntil = holdLimit is { } wait ? e.CreatedAt + wait : null;
                e.HoldReason = waitsFor.Length > 0 ? waitsFor : reason;
                Log("σε αναμονή", e, (e.HeldUntil is { } until ? $"ως {until:HH:mm} το αργότερο" : "ως την πληρωμή ή το κλείσιμο της ημέρας")
                    + $" — {reason}");
            }
            Commit();
            return e;
        }
    }

    /// <summary>Οι αποδείξεις σε αναμονή αυτών των παραγγελιών φεύγουν ΤΩΡΑ (πέρασε σε διανομέα, πλήρωσε, ζητήθηκε).</summary>
    /// <param name="print">Ζητήθηκε και χαρτί: τυπώνεται μόλις κοπεί (βλ. <see cref="ToPrint"/>).</param>
    public int Release(IEnumerable<int> orderNumbers, string why, bool print = false)
    {
        var orders = orderNumbers.ToHashSet();
        return ReleaseWhere(e => e.Items.Any(i => !i.IsVoid && orders.Contains(i.Order)), why, print);
    }

    /// <summary>Κλείσιμο ημέρας: ό,τι περιμένει από πριν από <paramref name="cutoff"/> φεύγει τώρα — καμία πώληση δεν περνά
    /// στην επόμενη μέρα αδήλωτη.</summary>
    public int ReleaseBefore(DateTime cutoff, string why) => ReleaseWhere(e => e.CreatedAt < cutoff, why, false);

    private int ReleaseWhere(Func<FiscalEntry, bool> which, string why, bool print)
    {
        lock (_gate)
        {
            var held = _entries.Where(e => e.State == FiscalEntryState.Held && which(e)).ToList();
            var now = _now();
            foreach (var e in held)
            {
                e.State = FiscalEntryState.Waiting;
                e.HeldUntil = null;
                e.NextTryAt = null;
                e.ReleasedAt = now;
                e.PrintWhenIssued |= print;
                Log("φεύγει", e, why);
            }
            if (held.Count > 0) Commit();
            return held.Count;
        }
    }

    /// <summary>Γύρος τραπεζιού → δελτίο παραγγελίας, τη στιγμή που γράφεται.</summary>
    public FiscalEntry? TableRound(int orderNumber, DateTime businessDay, string tableSession, string tableName, IEnumerable<FiscalItem> items)
    {
        var list = Billable(items);
        if (list.Count == 0) return null;
        return Add(new FiscalEntry
        {
            Id = NewId(businessDay, orderNumber, "s"),
            Kind = FiscalEntryKind.Slip,
            Items = list,
            TableSession = tableSession,
            TableName = tableName,
        });
    }

    /// <summary>
    /// Πληρώθηκαν αυτά τα προϊόντα του τραπεζιού, με αυτόν τον τρόπο → ΜΙΑ απόδειξη, που κλείνει τα δελτία στα οποία
    /// γράφτηκαν. Όπως κι αν διαλέγει η οθόνη τι πληρώνεται (άτομο, προϊόντα, όλο το τραπέζι), εδώ φτάνει μόνο αυτό.
    /// </summary>
    /// <param name="gather">Αν τα τεμάχια ενός πατήματος έρχονται ένα-ένα (δεύτερο ταμείο, κινητό), η απόδειξη μένει ανοιχτή
    /// τόσο για να τα μαζέψει — γραμμένη ήδη στο ημερολόγιο, ώστε ούτε κλείσιμο του ταμείου εκείνη τη στιγμή να μην τη χάνει.</param>
    public FiscalEntry? TablePayment(DateTime businessDay, string tableSession, string tableName, IEnumerable<FiscalItem> items, FiscalPay pay,
        TimeSpan? gather = null)
    {
        var list = Billable(items);
        if (list.Count == 0) return null;
        lock (_gate)
        {
            var lines = list.Select(i => i.LineKey).ToHashSet();
            var slips = _entries.Where(e => e.Kind == FiscalEntryKind.Slip && e.IsLive && e.TableSession == tableSession
                                            && !IsCancelledSlip(e) && e.Items.Any(i => lines.Contains(i.LineKey)))
                .Select(e => e.Id).ToList();
            var now = _now();
            if (gather is { } window
                && _entries.LastOrDefault(e => e.Kind == FiscalEntryKind.Receipt && e.State == FiscalEntryState.Waiting && e.Attempts == 0
                                               && e.TableSession == tableSession && e.Pay == pay && e.GatherUntil > now) is { } open)
            {
                open.Items.AddRange(list.Where(i => open.Items.All(o => o.Key != i.Key)));
                open.Correlates.AddRange(slips.Where(s => !open.Correlates.Contains(s)));
                open.GatherUntil = open.NextTryAt = now + window;
                Commit();
                return open;
            }
            return Add(new FiscalEntry
            {
                GatherUntil = gather is { } w ? now + w : null,
                NextTryAt = gather is { } w2 ? now + w2 : null,
                Id = NewId(businessDay, list[0].Order, "p"),
                Kind = FiscalEntryKind.Receipt,
                Items = list,
                Pay = pay,
                UseTerminal = pay == FiscalPay.Card,
                TableSession = tableSession,
                TableName = tableName,
                Correlates = slips,
            });
        }
    }

    /// <summary>Το πάτημα τελείωσε (όλα τα τεμάχια του έφτασαν μαζί): οι πληρωμές που μάζευαν, φεύγουν ΤΩΡΑ — σε κάρτα, το
    /// ποσό πάει αμέσως στο τερματικό.</summary>
    public void EndGather()
    {
        lock (_gate)
        {
            var any = false;
            foreach (var e in _entries.Where(e => e.GatherUntil is not null && e.State == FiscalEntryState.Waiting && e.Attempts == 0))
            {
                e.GatherUntil = null;
                e.NextTryAt = null;
                any = true;
            }
            if (any) Commit();
        }
    }

    /// <summary>
    /// Ακύρωση ολόκληρης παραγγελίας (<paramref name="lineKeys"/> null) ή μόνο κάποιων γραμμών της («παραγγελία:γραμμή»).
    /// </summary>
    public void Cancel(int orderNumber, IReadOnlyCollection<string>? lineKeys = null)
    {
        lock (_gate)
        {
            bool Hit(FiscalItem i) => !i.IsVoid && i.Order == orderNumber && (lineKeys is null || lineKeys.Contains(i.LineKey));
            foreach (var e in _entries.Where(e => e.IsLive && e.Kind is FiscalEntryKind.Receipt or FiscalEntryKind.Slip && e.Items.Any(Hit)).ToList())
            {
                var hitKeys = e.Items.Where(Hit).Select(i => i.Key).ToList();
                switch (e.State)
                {
                    case FiscalEntryState.Waiting:
                    case FiscalEntryState.NeedsDecision:
                    case FiscalEntryState.Held:
                        // Δεν έφυγε ποτέ: απλώς δεν φεύγει (ή φεύγει χωρίς αυτά) — καμία πώληση, κανένα πιστωτικό.
                        var removed = e.Items.Where(i => hitKeys.Contains(i.Key)).Sum(i => i.Revenue);
                        e.Items.RemoveAll(i => hitKeys.Contains(i.Key));
                        if (e.Items.Count == 0) e.State = FiscalEntryState.Dropped;
                        Log(e.State == FiscalEntryState.Dropped ? "ακυρώθηκε πριν κοπεί" : "αφαιρέθηκαν γραμμές πριν κοπεί", e,
                            $"#{orderNumber}: −{removed:0.00}");
                        break;
                    case FiscalEntryState.Sending:
                        // Σβήνονται ΤΩΡΑ από τη γραμμή του ταμείου (μπορεί αμέσως μετά να μετακινηθούν οι επόμενες γραμμές)·
                        // η αναίρεση γίνεται μόλις κοπεί.
                        (e.UndoLinesWhenIssued ??= []).AddRange(Void(e, hitKeys));
                        Log("ακύρωση όσο έφευγε", e, $"#{orderNumber}: η αναίρεση γίνεται μόλις κοπεί");
                        break;
                    case FiscalEntryState.Issued:
                        UndoIssued(e, hitKeys);
                        break;
                }
            }
            Commit();
        }
    }

    /// <summary>
    /// Το ταμείο έσβησε τη γραμμή <paramref name="lineIndex"/> της παραγγελίας και οι επόμενες ανέβηκαν μία θέση — το ίδιο
    /// εδώ, ώστε μια επόμενη ακύρωση/πληρωμή να βρίσκει το σωστό προϊόν. Καλείται ΜΕΤΑ το <see cref="Cancel"/> της γραμμής.
    /// </summary>
    public void LineRemoved(int orderNumber, int lineIndex)
    {
        lock (_gate)
        {
            foreach (var e in _entries)
                for (var k = 0; k < e.Items.Count; k++)
                {
                    var i = e.Items[k];
                    if (i.IsVoid || i.Order != orderNumber || i.Line <= lineIndex) continue;
                    var unit = i.Key.Count(c => c == ':') >= 2 ? i.Key[i.Key.LastIndexOf(':')..] : "";
                    e.Items[k] = i with { Key = FiscalItem.KeyFor(orderNumber, i.Line - 1) + unit };
                }
            Commit();
        }
    }

    /// <summary>Τα προϊόντα που ακυρώθηκαν μένουν στο παραστατικό που κόπηκε, αλλά δεν ταιριάζουν ποτέ ξανά με γραμμή του
    /// ταμείου (που μπορεί να πάρει αμέσως τη θέση τους). Επιστρέφει τα νέα τους κλειδιά.</summary>
    private static List<string> Void(FiscalEntry e, IReadOnlyCollection<string> keys)
    {
        var renamed = new List<string>();
        for (var k = 0; k < e.Items.Count; k++)
        {
            if (!keys.Contains(e.Items[k].Key) || e.Items[k].IsVoid) continue;
            var key = e.Items[k].Key + "~" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
            e.Items[k] = e.Items[k] with { Key = key };
            renamed.Add(key);
        }
        return renamed;
    }

    /// <summary>
    /// Διόρθωση τρόπου πληρωμής μιας παραγγελίας (Ιστορικό, Ζωντανές, κανάλι). Αν η απόδειξη δεν έχει κοπεί ακόμα (σε
    /// αναμονή ή στην ουρά), απλώς αλλάζει — ΜΙΑ απόδειξη με τον σωστό τρόπο, όχι απόδειξη + πιστωτικό + απόδειξη.
    /// </summary>
    /// <param name="preloadTerminal">Διανομή: σε κάρτα, το φορητό POS του διανομέα (προφόρτωση) αντί για του ταμείου.</param>
    /// <param name="releaseHeld">Μια απόδειξη σε αναμονή φεύγει ΤΩΡΑ (π.χ. όρθιος που γύρισε σε κάρτα: η χρέωση πάει στο
    /// τερματικό τώρα). false = συνεχίζει να περιμένει (διανομή που δεν έφυγε ακόμα).</param>
    public void ChangePayment(int orderNumber, FiscalPay pay, string preloadTerminal = "", bool releaseHeld = true)
    {
        lock (_gate)
        {
            foreach (var e in _entries.Where(e => e.IsLive && e.Kind == FiscalEntryKind.Receipt && e.Pay != pay
                                                  && e.Items.Any(i => !i.IsVoid && i.Order == orderNumber)).ToList())
            {
                switch (e.State)
                {
                    case FiscalEntryState.Waiting:
                    case FiscalEntryState.NeedsDecision:
                    case FiscalEntryState.Held:
                        if (e.Items.All(i => i.Order == orderNumber))
                        {
                            var before = e.Pay;
                            e.Pay = pay;
                            // Κάρτα με πάροχο = πάντα μέσα από τερματικό (τώρα, ή προφορτωμένη στον διανομέα)· απόδειξη κάρτας
                            // χωρίς συναλλαγή δεν υπάρχει.
                            e.PreloadTerminal = pay != FiscalPay.Card ? "" : preloadTerminal.Length > 0 ? preloadTerminal : e.PreloadTerminal;
                            e.UseTerminal = pay == FiscalPay.Card && e.PreloadTerminal.Length == 0;
                            Log("αλλαγή πληρωμής πριν κοπεί", e, $"#{orderNumber}: {before} → {pay}");
                            if (e.State == FiscalEntryState.Held && !releaseHeld)
                                break;
                            if (e.State == FiscalEntryState.Held)
                            {
                                e.HeldUntil = null;
                                e.ReleasedAt = _now();
                                Log("φεύγει", e, "άλλαξε ο τρόπος πληρωμής");
                            }
                            e.State = FiscalEntryState.Waiting;
                        }
                        else
                        {
                            // Μόνο ένα κομμάτι αλλάζει τρόπο: χωρίζει σε δύο αποδείξεις.
                            var moved = e.Items.Where(i => i.Order == orderNumber).ToList();
                            e.Items.RemoveAll(i => i.Order == orderNumber);
                            Add(Copy(e, moved, pay, "p"), commit: false);
                        }
                        break;
                    case FiscalEntryState.Sending:
                        e.PayChangeOrder = orderNumber;
                        e.PayChangeWhenIssued = pay;
                        Log("αλλαγή πληρωμής όσο έφευγε", e, $"#{orderNumber}: → {pay} μόλις κοπεί");
                        break;
                    case FiscalEntryState.Issued:
                        Log("αλλαγή πληρωμής μετά την έκδοση", e, $"#{orderNumber}: {e.Pay} → {pay} (πιστωτικό + νέα απόδειξη)");
                        RepayIssued(e, orderNumber, pay);
                        break;
                }
            }
            Commit();
        }
    }

    /// <summary>Η κάρτα δεν πέρασε· ο ταμίας λέει «ξανά».</summary>
    public void RetryCard(string id) => Decide(id, "κάρτα: ξανά στο τερματικό", e => { e.State = FiscalEntryState.Waiting; e.NextTryAt = null; });

    /// <summary>Η κάρτα δεν πέρασε· ο πελάτης πλήρωσε μετρητά.</summary>
    public void PayCardAsCash(string id) => Decide(id, "κάρτα: πλήρωσε μετρητά", e =>
    {
        e.Pay = FiscalPay.Cash;
        e.UseTerminal = false;
        e.State = FiscalEntryState.Waiting;
        e.NextTryAt = null;
    });

    // ================================================================ αποστολή

    /// <summary>
    /// Στέλνει ό,τι είναι έτοιμο να φύγει, ένα-ένα με τη σειρά που γράφτηκαν. Ό,τι αποτύχει ξαναδοκιμάζεται αργότερα (όχι
    /// οι κάρτες — βλ. <see cref="FiscalEntryState.NeedsDecision"/>). Επιστρέφει πόσα έφυγαν.
    /// </summary>
    public async Task<int> ProcessAsync(IFiscalProvider provider, CancellationToken ct = default)
    {
        if (!await _processing.WaitAsync(0, ct)) return 0;
        try
        {
            // Απόδειξη σε αναμονή που έφτασε στο όριό της: φεύγει με ό,τι τρόπο πληρωμής έχει — δεν μένει ποτέ αδήλωτη, ούτε
            // αν το ταμείο ήταν κλειστό τη στιγμή που έληγε (φεύγει στο πρώτο άνοιγμα).
            var now = _now();
            ReleaseWhere(e => e.HeldUntil <= now, "έφτασε το όριο αναμονής", false);
            var sent = 0;
            while (!ct.IsCancellationRequested)
            {
                FiscalEntry? next;
                Func<Task<FiscalResult>>? call;
                lock (_gate)
                {
                    (next, call) = NextDue(provider, ct);
                    if (next is null) break;
                    next.State = FiscalEntryState.Sending;
                    next.Attempts++;
                    Log("στέλνεται", next, $"απόπειρα {next.Attempts} → {provider.Name}" + (next.UseTerminal ? $", τερματικό {TerminalId}" : "")
                        + (next.PreloadTerminal.Length > 0 ? $", προφόρτωση {next.PreloadTerminal}" : ""));
                    Commit();
                }

                FiscalResult result;
                try { result = await call!(); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Κλείνει το ταμείο την ώρα που έφευγε: ξαναπάει την επόμενη φορά, με το ίδιο αναγνωριστικό.
                    lock (_gate)
                    {
                        next.State = FiscalEntryState.Waiting;
                        Commit();
                    }
                    throw;
                }
                catch (Exception ex)
                {
                    result = FiscalResult.Fail(ex.GetType().Name + ": " + ex.Message);
                }

                lock (_gate)
                {
                    if (result.Ok)
                    {
                        next.State = FiscalEntryState.Issued;
                        next.Result = result;
                        next.LastError = "";
                        next.NextTryAt = null;
                        sent++;
                        Log("κόπηκε", next, $"{result.Series} {result.Number} ΜΑΡΚ {result.Mark} UID {result.Uid}".Trim()
                            + (result.PosTxn.Length > 0 ? $", POS {result.PosTxn}" : ""));
                        if (next.UndoLinesWhenIssued is { } undo)
                        {
                            next.UndoLinesWhenIssued = null;
                            UndoIssued(next, undo);
                        }
                        if (next.PayChangeWhenIssued is { } pay && next.PayChangeOrder is { } order)
                        {
                            next.PayChangeWhenIssued = null;
                            next.PayChangeOrder = null;
                            if (next.Pay != pay && next.IsLive) RepayIssued(next, order, pay);
                        }
                    }
                    else
                    {
                        next.LastError = result.Error;
                        if (next.UseTerminal)
                            next.State = FiscalEntryState.NeedsDecision;
                        else
                        {
                            next.State = FiscalEntryState.Waiting;
                            next.NextTryAt = _now() + Backoff(next.Attempts);
                        }
                        Log("δεν κόπηκε", next, result.Error + (next.State == FiscalEntryState.Waiting ? $" — ξανά στις {next.NextTryAt:HH:mm:ss}" : " — ρωτά τον ταμία"));
                    }
                    Commit();
                }
            }
            return sent;
        }
        finally { _processing.Release(); }
    }

    /// <summary>Πόσο περιμένει πριν την επόμενη απόπειρα: γρήγορα στην αρχή, ως 5 λεπτά.</summary>
    public static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Max(0, attempts - 1))));

    private (FiscalEntry?, Func<Task<FiscalResult>>?) NextDue(IFiscalProvider provider, CancellationToken ct)
    {
        var now = _now();
        foreach (var e in _entries.Where(e => e.State == FiscalEntryState.Waiting && (e.NextTryAt is null || e.NextTryAt <= now))
                     .OrderBy(e => e.CreatedAt))
        {
            var call = Prepare(e, provider, ct);
            if (call is not null) return (e, call);
        }
        return (null, null);
    }

    /// <summary>Σειρά και αριθμός τη στιγμή της ΠΡΩΤΗΣ αποστολής — όχι νωρίτερα, ώστε ό,τι ακυρώνεται πριν φύγει να μην
    /// αφήνει κενό στην αρίθμηση. Από εκεί και πέρα ίδιος σε κάθε επανάληψη.</summary>
    private FiscalDocument Numbered(FiscalEntry e, FiscalDocument doc)
    {
        if (e.Number == 0)
        {
            e.Series = SeriesFor(e.Kind);
            e.Number = _counters[e.Series] = _counters.GetValueOrDefault(e.Series) + 1;
        }
        doc.Series = e.Series;
        doc.Number = e.Number;
        // Απόδειξη που περίμενε: εκδίδεται τη στιγμή που ελευθερώθηκε (αποστολή/πληρωμή), όχι της καταχώρησης.
        doc.IssueDate = e.ReleasedAt ?? e.CreatedAt;
        return doc;
    }

    /// <summary>Η απόδειξη αυτή αφήνει το τραπέζι χωρίς ανοιχτό δελτίο — για τους παρόχους που «κλείνουν» ρητά το
    /// τραπέζι (Epsilon Digital: closePointOfService).</summary>
    private bool ClosesTable(FiscalEntry receipt) =>
        receipt.TableSession.Length > 0
        && _entries.Where(s => s.Kind == FiscalEntryKind.Slip && s.IsLive && s.TableSession == receipt.TableSession && !IsCancelledSlip(s))
            .All(s => receipt.Correlates.Contains(s.Id)
                      || _entries.Any(r => r != receipt && r.IsLive && r.Kind == FiscalEntryKind.Receipt && r.Correlates.Contains(s.Id)));

    /// <summary>Το έγγραφο προς τον πάροχο — ή null αν περιμένει ακόμα κάτι άλλο να κοπεί πρώτα.</summary>
    private Func<Task<FiscalResult>>? Prepare(FiscalEntry e, IFiscalProvider provider, CancellationToken ct)
    {
        var lines = e.Items.Select(i => new TillLine(i.Name, i.Quantity, i.Revenue, i.VatRate)).ToList();
        var terminal = e.UseTerminal ? TerminalId : "";
        switch (e.Kind)
        {
            case FiscalEntryKind.Slip:
            {
                // Τα δελτία ενός τραπεζιού φεύγουν με τη σειρά: το πρώτο ανοίγει το τραπέζι στον πάροχο, τα επόμενα
                // πάνε στο ίδιο.
                if (_entries.Any(o => o.Kind == FiscalEntryKind.Slip && o.TableSession == e.TableSession && o.CreatedAt < e.CreatedAt
                                      && o.State is FiscalEntryState.Waiting or FiscalEntryState.Sending))
                    return null;
                var doc = Numbered(e, FiscalDocumentBuilder.OrderSlip(lines, e.TableName, e.Id, TableRef(e.TableSession)));
                return () => provider.IssueAsync(doc, ct);
            }
            case FiscalEntryKind.Receipt:
            {
                var slips = e.Correlates.Select(Find).Where(s => s is { IsLive: true }).ToList();
                if (slips.Any(s => s!.State != FiscalEntryState.Issued)) return null;
                var preload = e.Pay == FiscalPay.Card && e.PreloadTerminal.Length > 0;
                var doc = FiscalDocumentBuilder.Receipt(lines, e.Total, e.Pay, e.Id, preload ? e.PreloadTerminal : terminal, e.TableName,
                    e.TableSession.Length > 0 ? TableRef(e.TableSession) : "", [.. slips.Select(s => s!.Mark).Where(m => m.Length > 0)],
                    preload);
                doc.ClosesTable = ClosesTable(e);
                Numbered(e, doc);
                return () => provider.IssueAsync(doc, ct);
            }
            case FiscalEntryKind.Credit:
            {
                var original = e.Correlates.Count > 0 ? Find(e.Correlates[0]) : null;
                if (original is null || original.State != FiscalEntryState.Issued) return null;
                var doc = Numbered(e, FiscalDocumentBuilder.Credit(lines, e.Pay, original.Mark, e.Id, terminal));
                return () => provider.IssueAsync(doc, ct);
            }
            case FiscalEntryKind.SlipCancel:
            {
                var slip = e.Correlates.Count > 0 ? Find(e.Correlates[0]) : null;
                if (slip is null || slip.State != FiscalEntryState.Issued) return null;
                var mark = slip.Mark;
                return () => provider.CancelOrderSlipAsync(mark, ct);
            }
        }
        return null;
    }

    /// <summary>Ό,τι έδωσε ο πάροχος για το ανοιχτό τραπέζι (π.χ. catering_table_id) στο τελευταίο δελτίο του.</summary>
    private string TableRef(string session) =>
        _entries.Where(e => e.Kind == FiscalEntryKind.Slip && e.TableSession == session && e.State == FiscalEntryState.Issued
                            && (e.Result?.ProviderTableRef ?? "").Length > 0)
            .OrderByDescending(e => e.CreatedAt).Select(e => e.Result!.ProviderTableRef).FirstOrDefault() ?? "";

    // ================================================================ αναιρέσεις

    /// <summary>Κάτι που ΚΟΠΗΚΕ ακυρώνεται (όλο ή κάποιες γραμμές).</summary>
    private void UndoIssued(FiscalEntry e, IReadOnlyCollection<string> keys)
    {
        var undone = e.Items.Where(i => keys.Contains(i.Key)).ToList();
        if (undone.Count == 0) return;
        var undoneKeys = undone.Select(i => i.Key).ToList();
        if (e.Kind == FiscalEntryKind.Receipt)
        {
            // Πληρώθηκε: πιστωτικό για ό,τι ακυρώθηκε, με τον ίδιο τρόπο — σε κάρτα η επιστροφή πάει στο τερματικό.
            var creditPay = CreditPay(e);
            Add(new FiscalEntry
            {
                Id = NewId(DayOf(e), undone[0].Order, "c"),
                Kind = FiscalEntryKind.Credit,
                Items = [.. undone],
                Pay = creditPay,
                UseTerminal = creditPay == FiscalPay.Card && e.UseTerminal,
                Correlates = [e.Id],
                TableSession = e.TableSession,
                TableName = e.TableName,
            }, commit: false);
            Void(e, undoneKeys);
            return;
        }

        // Δελτίο. Αν το έκλεισε ήδη απόδειξη, δεν ακυρώνεται: ό,τι πληρώθηκε πιστώνεται από τη δική του απόδειξη (εκεί
        // είναι τα χρήματα — το κάνει το Cancel, που περνά και από τις αποδείξεις με τα ίδια προϊόντα), ό,τι δεν
        // πληρώθηκε απλώς δεν θα πληρωθεί ποτέ. Εδώ δεν κόβεται τίποτα — αλλιώς το πιστωτικό θα έβγαινε δύο φορές.
        if (IsClosed(e))
        {
            Void(e, undoneKeys);
            return;
        }

        // Δεν πληρώθηκε τίποτα: ακύρωση όλου του δελτίου, και νέο δελτίο με ό,τι έμεινε. Το παλιό δεν ταιριάζει πια με
        // τίποτα — αλλιώς μια επόμενη ακύρωση θα το «ξαναακύρωνε».
        Add(new FiscalEntry
        {
            Id = NewId(DayOf(e), undone[0].Order, "x"),
            Kind = FiscalEntryKind.SlipCancel,
            Correlates = [e.Id],
            TableSession = e.TableSession,
            TableName = e.TableName,
        }, commit: false);
        var remaining = e.Items.Where(i => !i.IsVoid && !undoneKeys.Contains(i.Key)).ToList();
        if (remaining.Count > 0)
            Add(new FiscalEntry
            {
                Id = NewId(DayOf(e), remaining[0].Order, "s"),
                Kind = FiscalEntryKind.Slip,
                Items = remaining,
                TableSession = e.TableSession,
                TableName = e.TableName,
            }, commit: false);
        Void(e, [.. e.Items.Where(i => !i.IsVoid).Select(i => i.Key)]);
    }

    /// <summary>
    /// Απόδειξη που κόπηκε με άλλον τρόπο πληρωμής: πιστωτικό για ΟΣΑ αλλάζουν (με τον παλιό τρόπο — σε κάρτα, επιστροφή
    /// στο τερματικό) και νέα απόδειξη γι' αυτά με τον νέο (σε κάρτα, χρέωση στο τερματικό). Ό,τι άλλο είχε μέσα η απόδειξη
    /// (άλλο άτομο του τραπεζιού) μένει όπως είναι.
    /// </summary>
    private void RepayIssued(FiscalEntry e, int orderNumber, FiscalPay pay)
    {
        var moved = e.Items.Where(i => !i.IsVoid && i.Order == orderNumber).ToList();
        if (moved.Count == 0) return;
        var creditPay = CreditPay(e);
        Add(new FiscalEntry
        {
            Id = NewId(DayOf(e), orderNumber, "c"),
            Kind = FiscalEntryKind.Credit,
            Items = moved,
            Pay = creditPay,
            UseTerminal = creditPay == FiscalPay.Card && e.UseTerminal,
            Correlates = [e.Id],
            TableSession = e.TableSession,
            TableName = e.TableName,
        }, commit: false);
        Add(Copy(e, [.. moved], pay, "p"), commit: false);
        // Από εδώ και πέρα αυτά ζουν στη νέα απόδειξη — η παλιά δεν ξαναπιστώνεται γι' αυτά.
        Void(e, [.. moved.Select(i => i.Key)]);
    }

    private FiscalEntry Copy(FiscalEntry e, List<FiscalItem> items, FiscalPay pay, string kind) => new()
    {
        Id = NewId(DayOf(e), items[0].Order, kind),
        Kind = FiscalEntryKind.Receipt,
        Items = items,
        Pay = pay,
        PreloadTerminal = pay == FiscalPay.Card ? e.PreloadTerminal : "",
        UseTerminal = pay == FiscalPay.Card && e.PreloadTerminal.Length == 0,
        TableSession = e.TableSession,
        TableName = e.TableName,
        Correlates = [.. e.Correlates],
    };

    /// <summary>Με ποιον τρόπο πιστώνεται μια απόδειξη: ίδιο με τον δικό της — εκτός από τη διανομή με κάρτα που δεν
    /// πληρώθηκε ποτέ στην πόρτα (προφόρτωση χωρίς συναλλαγή): εκείνη ήταν ακόμα «επί πιστώσει», έτσι πιστώνεται και
    /// καμία επιστροφή δεν πάει σε κάρτα που δεν χρεώθηκε.</summary>
    private static FiscalPay CreditPay(FiscalEntry receipt) =>
        receipt.Pay == FiscalPay.Card && receipt.PreloadTerminal.Length > 0 && (receipt.Result?.PosTxn ?? "").Length == 0
            ? FiscalPay.Online
            : receipt.Pay;

    private bool IsCancelledSlip(FiscalEntry slip) =>
        _entries.Any(o => o.IsLive && o.Kind == FiscalEntryKind.SlipCancel && o.Correlates.Contains(slip.Id));

    /// <summary>Το δελτίο το έχει «κλείσει» απόδειξη (ή ακυρώθηκε) — δεν μετράει πια στο 24ωρο.</summary>
    private bool IsClosed(FiscalEntry slip) =>
        _entries.Any(o => o.IsLive && o.Correlates.Contains(slip.Id) && o.Kind is FiscalEntryKind.Receipt or FiscalEntryKind.SlipCancel);

    // ================================================================ βοηθητικά

    private static List<FiscalItem> Billable(IEnumerable<FiscalItem> items) =>
        [.. items.Where(i => i.Revenue != 0 && i.Quantity != 0)];

    private FiscalEntry? Find(string id) => _entries.FirstOrDefault(e => e.Id == id);

    private static DateTime DayOf(FiscalEntry e) => e.CreatedAt;

    /// <summary>μαγαζί-ημέρα-παραγγελία-είδος+αύξων-τυχαίο. Το τυχαίο κομμάτι προστατεύει αν ποτέ χαθεί το ημερολόγιο:
    /// ένα παλιό αναγνωριστικό δεν ξαναδίνεται σε άλλο παραστατικό.</summary>
    private string NewId(DateTime day, int order, string kind)
    {
        lock (_gate)
        {
            var stem = $"{_prefix}-{day:yyyyMMdd}-{order}-{kind}";
            var n = _entries.Count(e => e.Id.StartsWith(stem, StringComparison.Ordinal)) + 1;
            return $"{stem}{n}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant()}";
        }
    }

    private FiscalEntry Add(FiscalEntry e, bool commit = true, string reason = "")
    {
        lock (_gate)
        {
            e.CreatedAt = Later(_now());
            e.State = FiscalEntryState.Waiting;
            _entries.Add(e);
            Log("νέο", e, string.Join(" — ", new[]
            {
                reason,
                e.Correlates.Count > 0 ? "συσχετίζει " + string.Join(",", e.Correlates) : "",
                e.TableName,
            }.Where(s => s.Length > 0)));
            if (commit) Commit();
            return e;
        }
    }

    /// <summary>Αυστηρά μετά από το τελευταίο: η σειρά αποστολής είναι η σειρά καταγραφής, ακόμα και μέσα στο ίδιο tick.</summary>
    private DateTime Later(DateTime t)
    {
        var last = _entries.Count > 0 ? _entries.Max(x => x.CreatedAt) : DateTime.MinValue;
        return t > last ? t : last.AddTicks(1);
    }

    private void Decide(string id, string what, Action<FiscalEntry> change)
    {
        lock (_gate)
        {
            if (Find(id) is not { State: FiscalEntryState.NeedsDecision } e) return;
            change(e);
            Log(what, e);
            Commit();
        }
    }

    /// <summary>Γράφεται ΠΡΙΝ φύγει οτιδήποτε. Όσα έκλεισαν (κόπηκαν/έπεσαν) πάνω από 10 μέρες σβήνονται· όσα
    /// περιμένουν, ποτέ.</summary>
    private void Commit()
    {
        var cutoff = _now().AddDays(-10);
        // Ό,τι χρειάζεται ακόμα κάποιο ανοιχτό (το ΜΑΡΚ του), μένει — υπολογίζεται ΠΡΙΝ το σβήσιμο, όχι μέσα του.
        var needed = _entries.Where(o => o.State is not (FiscalEntryState.Issued or FiscalEntryState.Dropped))
            .SelectMany(o => o.Correlates).ToHashSet();
        _entries.RemoveAll(e => e.State is FiscalEntryState.Issued or FiscalEntryState.Dropped && e.CreatedAt < cutoff && !needed.Contains(e.Id));
        _store.Save(new FiscalJournalData { Entries = _entries, Counters = _counters });
        Changed?.Invoke();
    }
}
