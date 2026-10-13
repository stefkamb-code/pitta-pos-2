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
    /// <summary>Το ποσό πάει στο τερματικό κάρτας (πληρωμή ή επιστροφή). Όχι στις διορθώσεις εκ των υστέρων.</summary>
    public bool UseTerminal { get; set; }
    /// <summary>Το «τραπέζι από τη στιγμή που άνοιξε ως τη στιγμή που έκλεισε» — όλα τα δελτία και οι αποδείξεις του.</summary>
    public string TableSession { get; set; } = "";
    public string TableName { get; set; } = "";
    /// <summary>Ποια παραστατικά του ημερολογίου συσχετίζει: τα δελτία που κλείνει μια απόδειξη, την απόδειξη που πιστώνει
    /// ένα πιστωτικό, το δελτίο που ακυρώνεται.</summary>
    public List<string> Correlates { get; set; } = [];
    public DateTime CreatedAt { get; set; }
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

    [JsonIgnore]
    public decimal Total => Items.Sum(i => i.Revenue);
    [JsonIgnore]
    public string Mark => Result?.Mark ?? "";
    [JsonIgnore]
    public bool IsLive => State is not FiscalEntryState.Dropped;
}

/// <summary>Πού ζει το ημερολόγιο — αρχείο στο ταμείο, μνήμη στις δοκιμές.</summary>
public interface IFiscalJournalStore
{
    List<FiscalEntry> Load();
    void Save(IReadOnlyList<FiscalEntry> entries);
}

public sealed class MemoryFiscalJournalStore : IFiscalJournalStore
{
    private string _json = "[]";
    public List<FiscalEntry> Load() => JsonSerializer.Deserialize<List<FiscalEntry>>(_json, FiscalJournal.JsonOptions) ?? [];
    public void Save(IReadOnlyList<FiscalEntry> entries) => _json = JsonSerializer.Serialize(entries, FiscalJournal.JsonOptions);
}

/// <summary>Αρχείο JSON, γραμμένο σε προσωρινό και μετονομασμένο: μια διακοπή ρεύματος δεν αφήνει μισό ημερολόγιο.</summary>
public sealed class FileFiscalJournalStore(string path) : IFiscalJournalStore
{
    public List<FiscalEntry> Load()
    {
        if (!File.Exists(path)) return [];
        return JsonSerializer.Deserialize<List<FiscalEntry>>(File.ReadAllText(path), FiscalJournal.JsonOptions) ?? [];
    }

    public void Save(IReadOnlyList<FiscalEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(entries, FiscalJournal.JsonOptions));
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
    private readonly object _gate = new();
    private readonly SemaphoreSlim _processing = new(1, 1);

    /// <summary>Το τερματικό κάρτας του μαγαζιού (Terminal ID από τις Ρυθμίσεις).</summary>
    public string TerminalId { get; set; } = "";

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
        _entries = store.Load();
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

    /// <summary>Ανοιχτά δελτία πάνω από <see cref="SlipWarning"/> — να φανούν πριν περάσει το 24ωρο.</summary>
    public IReadOnlyList<FiscalEntry> OverdueSlips => [.. OpenSlips.Where(e => _now() - e.CreatedAt > SlipWarning)];

    // ================================================================ τι έγινε στο ταμείο

    /// <summary>Παραγγελία εκτός τραπεζιού (όρθιος, διανομή, παραλαβή, εφαρμογή) → απόδειξη.</summary>
    public FiscalEntry? Sale(int orderNumber, DateTime businessDay, IEnumerable<FiscalItem> items, FiscalPay pay)
    {
        var list = Billable(items);
        if (list.Count == 0) return null;
        return Add(new FiscalEntry
        {
            Id = NewId(businessDay, orderNumber, "r"),
            Kind = FiscalEntryKind.Receipt,
            Items = list,
            Pay = pay,
            UseTerminal = pay == FiscalPay.Card,
        });
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
                        // Δεν έφυγε ποτέ: απλώς δεν φεύγει (ή φεύγει χωρίς αυτά).
                        e.Items.RemoveAll(i => hitKeys.Contains(i.Key));
                        if (e.Items.Count == 0) e.State = FiscalEntryState.Dropped;
                        break;
                    case FiscalEntryState.Sending:
                        // Σβήνονται ΤΩΡΑ από τη γραμμή του ταμείου (μπορεί αμέσως μετά να μετακινηθούν οι επόμενες γραμμές)·
                        // η αναίρεση γίνεται μόλις κοπεί.
                        (e.UndoLinesWhenIssued ??= []).AddRange(Void(e, hitKeys));
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

    /// <summary>Διόρθωση τρόπου πληρωμής μιας παραγγελίας εκ των υστέρων (π.χ. από το Ιστορικό).</summary>
    public void ChangePayment(int orderNumber, FiscalPay pay)
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
                        if (e.Items.All(i => i.Order == orderNumber))
                        {
                            e.Pay = pay;
                            // Κάρτα με πάροχο = πάντα μέσα από το τερματικό· απόδειξη κάρτας χωρίς συναλλαγή δεν υπάρχει.
                            e.UseTerminal = pay == FiscalPay.Card;
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
                        break;
                    case FiscalEntryState.Issued:
                        RepayIssued(e, orderNumber, pay);
                        break;
                }
            }
            Commit();
        }
    }

    /// <summary>Η κάρτα δεν πέρασε· ο ταμίας λέει «ξανά».</summary>
    public void RetryCard(string id) => Decide(id, e => { e.State = FiscalEntryState.Waiting; e.NextTryAt = null; });

    /// <summary>Η κάρτα δεν πέρασε· ο πελάτης πλήρωσε μετρητά.</summary>
    public void PayCardAsCash(string id) => Decide(id, e =>
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
                var doc = FiscalDocumentBuilder.OrderSlip(lines, e.TableName, e.Id, TableRef(e.TableSession));
                return () => provider.IssueAsync(doc, ct);
            }
            case FiscalEntryKind.Receipt:
            {
                var slips = e.Correlates.Select(Find).Where(s => s is { IsLive: true }).ToList();
                if (slips.Any(s => s!.State != FiscalEntryState.Issued)) return null;
                var doc = FiscalDocumentBuilder.Receipt(lines, e.Total, e.Pay, e.Id, terminal, e.TableName,
                    e.TableSession.Length > 0 ? TableRef(e.TableSession) : "", [.. slips.Select(s => s!.Mark).Where(m => m.Length > 0)]);
                return () => provider.IssueAsync(doc, ct);
            }
            case FiscalEntryKind.Credit:
            {
                var original = e.Correlates.Count > 0 ? Find(e.Correlates[0]) : null;
                if (original is null || original.State != FiscalEntryState.Issued) return null;
                var doc = FiscalDocumentBuilder.Credit(lines, e.Pay, original.Mark, e.Id, terminal);
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
            Add(new FiscalEntry
            {
                Id = NewId(DayOf(e), undone[0].Order, "c"),
                Kind = FiscalEntryKind.Credit,
                Items = [.. undone],
                Pay = e.Pay,
                UseTerminal = e.Pay == FiscalPay.Card && e.UseTerminal,
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
        Add(new FiscalEntry
        {
            Id = NewId(DayOf(e), orderNumber, "c"),
            Kind = FiscalEntryKind.Credit,
            Items = moved,
            Pay = e.Pay,
            UseTerminal = e.Pay == FiscalPay.Card && e.UseTerminal,
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
        UseTerminal = pay == FiscalPay.Card,
        TableSession = e.TableSession,
        TableName = e.TableName,
        Correlates = [.. e.Correlates],
    };

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

    private FiscalEntry Add(FiscalEntry e, bool commit = true)
    {
        lock (_gate)
        {
            e.CreatedAt = Later(_now());
            e.State = FiscalEntryState.Waiting;
            _entries.Add(e);
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

    private void Decide(string id, Action<FiscalEntry> change)
    {
        lock (_gate)
        {
            if (Find(id) is not { State: FiscalEntryState.NeedsDecision } e) return;
            change(e);
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
        _store.Save(_entries);
        Changed?.Invoke();
    }
}
