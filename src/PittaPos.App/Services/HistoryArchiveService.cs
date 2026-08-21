using System.Globalization;
using System.IO;
using System.Text.Json;

namespace PittaPos.App.Services;

/// <summary>Μια αρχειοθετημένη μέρα και πότε γράφτηκε τελευταία — το <paramref name="Stamp"/> λέει στο
/// δεύτερο ταμείο ποιες μέρες του λείπουν και ποιες ξαναγράφτηκαν στο κύριο (το ArchiveDay συγχωνεύει,
/// άρα μια μέρα μπορεί να αλλάξει και μετά την πρώτη αρχειοθέτησή της).</summary>
public sealed record ArchivedDay(string Day, long Stamp);

/// <summary>Το περιεχόμενο μιας αρχειοθετημένης μέρας, όπως ταξιδεύει προς το δεύτερο ταμείο.</summary>
public sealed record ArchivedDayContent(List<CompletedOrder> Orders, List<CancelledLine> Cancellations);

/// <summary>
/// Αρχείο ιστορικού ανά ημέρα-επιχείρησης — ένα JSON ανά μέρα (παραγγελίες + ακυρωμένα), γραμμένο
/// στο κλείσιμο κάθε μέρας (βλ. DayReportService.CloseDay) ΠΡΙΝ καθαρίσουν τα τρέχοντα στατιστικά.
/// Επιτρέπει στο Ιστορικό να δείχνει παλιότερες μέρες, όχι μόνο τη σημερινή.
///
/// <para><b>Δεύτερο ταμείο:</b> το κλείσιμο ημέρας το κάνει ΜΟΝΟ το κύριο ταμείο, οπότε στο δεύτερο δεν
/// γραφόταν ποτέ αρχείο — Ιστορικό και Στατιστικά έδειχναν άδειο σε κάθε παλιότερη ημερομηνία. Τώρα το
/// δεύτερο κρατά τοπικό ΑΝΤΙΓΡΑΦΟ του αρχείου του κυρίου (βλ. StartClientMirror): κατεβάζει όποια μέρα
/// του λείπει ή άλλαξε, και από κει και πέρα όλα διαβάζουν τοπικά αρχεία όπως πάντα.</para>
/// </summary>
public static class HistoryArchiveService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>Σηκώνεται όταν το δεύτερο ταμείο κατεβάσει νέα/αλλαγμένη μέρα — ώστε ένα ανοιχτό
    /// Ιστορικό ή Στατιστικά να δείξουν αμέσως ό,τι ήρθε, χωρίς να χρειάζεται να ξανανοίξουν.</summary>
    public static event Action? Changed;

    private static string ArchiveDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder, "archive");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string OrdersPath(DateTime businessDay) => Path.Combine(ArchiveDir, "orders-" + businessDay.ToString("yyyy-MM-dd") + ".json");
    private static string CancellationsPath(DateTime businessDay) => Path.Combine(ArchiveDir, "cancellations-" + businessDay.ToString("yyyy-MM-dd") + ".json");

    /// <summary>
    /// Γράφει στο αρχείο μια ολοκληρωμένη ημέρα-επιχείρησης — καλείται πριν καθαρίσουν τα τρέχοντα
    /// στατιστικά. ΣΥΓΧΩΝΕΥΕΙ με ό,τι υπάρχει ήδη για την ίδια μέρα, δεν αντικαθιστά: μια ημέρα
    /// μπορεί να αρχειοθετηθεί ΔΕΥΤΕΡΗ φορά αν φτάσει καθυστερημένα έστω μία παραγγελία της (π.χ. από
    /// την ουρά αναμονής του δεύτερου ταμείου, βλ. PendingSyncService, ή αν το κλείσιμο βρήκε
    /// μπερδεμένες μέρες). Με σκέτη αντικατάσταση, εκείνη η μία παραγγελία θα έσβηνε ΟΛΟ το ιστορικό
    /// της μέρας — δεκάδες παραγγελίες, χωρίς κανένα ίχνος.
    /// </summary>
    public static void ArchiveDay(DateTime businessDay, IReadOnlyCollection<CompletedOrder> orders, IReadOnlyList<CancelledLine> cancellations)
    {
        try
        {
            if (orders.Count > 0)
            {
                // Κλειδί ο αριθμός παραγγελίας· η νεότερη εκδοχή κερδίζει (μπορεί να διορθώθηκε
                // τρόπος πληρωμής/κανάλι από το Ιστορικό πριν κλείσει η μέρα).
                var merged = ReadOrders(businessDay).ToDictionary(o => o.OrderNumber);
                foreach (var o in orders)
                    merged[o.OrderNumber] = o;
                AtomicFile.WriteAllText(OrdersPath(businessDay),
                    JsonSerializer.Serialize(merged.Values.OrderBy(o => o.PlacedAt).ToList(), JsonOpts));
            }

            if (cancellations.Count > 0)
            {
                var existing = ReadCancellations(businessDay);
                // Οι ακυρώσεις δεν έχουν δικό τους μοναδικό κωδικό — ταυτίζονται από το σύνολο των
                // πεδίων τους (record: η ισότητα είναι κατά τιμή), ώστε να μη διπλογραφτούν.
                var merged = existing.Concat(cancellations).Distinct().OrderBy(c => c.CancelledAt).ToList();
                AtomicFile.WriteAllText(CancellationsPath(businessDay), JsonSerializer.Serialize(merged, JsonOpts));
            }
        }
        catch (Exception ex)
        {
            // Αποτυχία αρχειοθέτησης δεν πρέπει να μπλοκάρει το κλείσιμο μέρας — αλλά ΠΡΕΠΕΙ να αφήσει
            // ίχνος: εδώ χάνεται ιστορικό ημέρας, δεν είναι κάτι που θέλουμε να περάσει σιωπηλά.
            AppLog.Write("archive", $"Αποτυχία αρχειοθέτησης {businessDay:yyyy-MM-dd}: {ex}");
        }
        // Και στην αποτυχία: η σύνοψη ξανακοιτάζει τον φάκελο αντί να εμπιστευτεί ό,τι θυμόταν.
        InvalidateSummaries();
    }

    // ================= ΣΥΝΟΨΗ ΑΝΑ ΜΕΡΑ =================
    //
    // Το διάγραμμα των Στατιστικών ήθελε τον τζίρο κάθε μέρας ενός μήνα, ενός χρόνου ή μιας ΔΕΚΑΕΤΙΑΣ.
    // Τον έβγαζε ανοίγοντας και αποκωδικοποιώντας ΟΛΑ τα αρχεία της περιόδου — κάθε φορά που ζωγράφιζε,
    // δηλαδή και σε κάθε νέα παραγγελία όσο το παράθυρο ήταν ανοιχτό. Με δύο εβδομάδες αρχείου δεν
    // φαίνεται· με τρία χρόνια είναι χίλια αρχεία και εκατοντάδες MB JSON ανά παραγγελία.
    //
    // ΜΙΑ ΜΕΡΑ ΠΟΥ ΕΚΛΕΙΣΕ ΔΕΝ ΑΛΛΑΖΕΙ. Οπότε ο τζίρος της υπολογίζεται ΜΙΑ φορά στη ζωή της και
    // γράφεται σε ένα μικρό αρχείο σύνοψης δίπλα στο αρχείο — ένας αριθμός ανά μέρα, δηλαδή μερικές
    // δεκάδες KB για μια δεκαετία. Από κει και πέρα το διάγραμμα δεν ανοίγει ΚΑΝΕΝΑ αρχείο ημέρας.

    /// <summary>Τι κρατάμε για κάθε αρχειοθετημένη μέρα χωρίς να την ανοίξουμε.</summary>
    /// <param name="Stamp">Μέγεθος + ώρα εγγραφής του αρχείου. Μια μέρα μπορεί να ξαναγραφτεί (βλ.
    /// ArchiveDay: συγχωνεύει καθυστερημένες παραγγελίες) — έτσι η σύνοψη ξαναϋπολογίζεται μόνο τότε.</param>
    public sealed record DaySummary(decimal Revenue, int Orders, long Stamp);

    private static readonly object SummaryGate = new();
    private static Dictionary<string, DaySummary>? _summaries;
    private static Dictionary<DateTime, DaySummary>? _byDay;
    /// <summary>Χρειάζεται ξανασάρωμα του φακέλου; Μόνο όταν όντως γράφτηκε/κατέβηκε μέρα — αλλιώς
    /// σερβίρεται ό,τι έχουμε ήδη, χωρίς να αγγιχτεί ο δίσκος.</summary>
    private static bool _summariesStale = true;

    private static string SummaryPath => Path.Combine(ArchiveDir, "day-summary.json");

    /// <summary>Τζίρος και πλήθος παραγγελιών κάθε αρχειοθετημένης μέρας. Δεν διαβάζει αρχεία ημερών
    /// παρά μόνο για μέρες που δεν έχουν ξαναϋπολογιστεί (ή ξαναγράφτηκαν στο μεταξύ).</summary>
    public static IReadOnlyDictionary<DateTime, DaySummary> DailySummaries()
    {
        lock (SummaryGate)
        {
            if (!_summariesStale && _byDay is not null)
                return _byDay;

            _summaries ??= ReadFileAs<Dictionary<string, DaySummary>>(SummaryPath) ?? [];

            var changed = false;
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (day, path) in ArchiveFiles("orders"))
            {
                var key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                present.Add(key);
                long stamp;
                try
                {
                    var info = new FileInfo(path);
                    stamp = info.Length ^ info.LastWriteTimeUtc.Ticks;
                }
                catch (Exception)
                {
                    continue;
                }
                if (_summaries.TryGetValue(key, out var known) && known.Stamp == stamp)
                    continue;

                var orders = ReadFile<CompletedOrder>(path);
                _summaries[key] = new DaySummary(orders.Sum(o => o.Total), orders.Count, stamp);
                changed = true;
            }

            // Μέρα που σβήστηκε με το χέρι δεν πρέπει να μείνει στη σύνοψη και να φαντάζει στο διάγραμμα.
            foreach (var gone in _summaries.Keys.Where(k => !present.Contains(k)).ToList())
            {
                _summaries.Remove(gone);
                changed = true;
            }

            if (changed)
            {
                try
                {
                    AtomicFile.WriteAllText(SummaryPath, JsonSerializer.Serialize(_summaries, JsonOpts));
                }
                catch (Exception ex)
                {
                    // Χωρίς το αρχείο απλώς θα ξαναϋπολογιστεί στο επόμενο άνοιγμα — τίποτα δεν χάνεται.
                    AppLog.Write("archive", $"Δεν γράφτηκε η σύνοψη ημερών: {ex.Message}");
                }
            }

            _byDay = _summaries.ToDictionary(
                kv => DateTime.ParseExact(kv.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                kv => kv.Value);
            _summariesStale = false;
            return _byDay;
        }
    }

    /// <summary>Κάτι γράφτηκε/κατέβηκε στο αρχείο — η σύνοψη ξανακοιτάζει τον φάκελο την επόμενη φορά.</summary>
    private static void InvalidateSummaries()
    {
        lock (SummaryGate)
            _summariesStale = true;
    }

    private static T? ReadFileAs<T>(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default;
        }
        catch (Exception)
        {
            return default;
        }
    }

    private static List<CompletedOrder> ReadOrders(DateTime businessDay) => ReadFile<CompletedOrder>(OrdersPath(businessDay));

    private static List<CancelledLine> ReadCancellations(DateTime businessDay) => ReadFile<CancelledLine>(CancellationsPath(businessDay));

    /// <summary>Πόσα αρχεία ημέρας κρατιούνται διαβασμένα στη μνήμη. Το Ιστορικό ξαναχτίζεται σε κάθε
    /// νέα παραγγελία όσο είναι ανοιχτό, και ξαναδιάβαζε από τον δίσκο τα ΙΔΙΑ αρχεία κάθε φορά —
    /// με μεγάλο εύρος ημερομηνιών αυτό μεγαλώνει για πάντα. Σαράντα μέρες είναι πολύ περισσότερο
    /// απ' όσο κοιτάει κανείς μαζί, και πιάνουν λίγα MB.</summary>
    private const int MaxCachedFiles = 40;

    private static readonly object FileCacheGate = new();
    private static readonly Dictionary<string, (long Stamp, object Items, long Used)> FileCache = new(StringComparer.OrdinalIgnoreCase);
    private static long _useCounter;

    /// <summary>Διαβάζει ένα αρχείο ημέρας — από τη μνήμη αν δεν έχει αλλάξει από την τελευταία φορά.
    /// Η σφραγίδα (μέγεθος + ώρα εγγραφής) φροντίζει ώστε μια μέρα που ξαναγράφτηκε να ξαναδιαβαστεί.</summary>
    private static List<T> ReadFile<T>(string path)
    {
        try
        {
            if (!File.Exists(path))
                return [];

            var info = new FileInfo(path);
            var stamp = info.Length ^ info.LastWriteTimeUtc.Ticks;

            lock (FileCacheGate)
            {
                if (FileCache.TryGetValue(path, out var hit) && hit.Stamp == stamp && hit.Items is List<T> cached)
                {
                    FileCache[path] = (stamp, hit.Items, ++_useCounter);
                    // Αντίγραφο της λίστας: ο καλών δεν πρέπει να μπορεί να πειράξει ό,τι κρατάμε.
                    return [.. cached];
                }
            }

            var items = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path)) ?? [];
            lock (FileCacheGate)
            {
                FileCache[path] = (stamp, items, ++_useCounter);
                if (FileCache.Count > MaxCachedFiles)
                    foreach (var oldest in FileCache.OrderBy(kv => kv.Value.Used)
                                 .Take(FileCache.Count - MaxCachedFiles)
                                 .Select(kv => kv.Key)
                                 .ToList())
                        FileCache.Remove(oldest);
            }
            return [.. items];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Παραγγελίες αρχειοθετημένων ημερών μέσα στο εύρος (χωρίς τη σημερινή — αυτή έρχεται ζωντανή από το SalesStatsService).</summary>
    public static List<CompletedOrder> LoadOrders(DateTime from, DateTime to) =>
        LoadRange<CompletedOrder>("orders", from, to);

    /// <summary>
    /// Παραγγελίες αρχειοθετημένων ημερών, <b>τεμπέλικα και από την πιο πρόσφατη μέρα προς τα πίσω</b>.
    ///
    /// <para>Ο καλών σταματά όποτε έχει δει όσα χρειάζεται, και τα υπόλοιπα αρχεία <b>δεν ανοίγονται
    /// καν</b>. Χωρίς αυτό, ένα εύρος «όλες οι ημερομηνίες» στο Ιστορικό φόρτωνε ΟΛΟ το αρχείο του
    /// μαγαζιού στη μνήμη — μετά από χρόνια λειτουργίας, εκατοντάδες χιλιάδες παραγγελίες μονομιάς.</para>
    /// </summary>
    public static IEnumerable<CompletedOrder> OrdersDescending(DateTime from, DateTime to)
    {
        var days = ArchiveFiles("orders")
            .Where(f => f.Day >= from.Date && f.Day <= to.Date)
            .OrderByDescending(f => f.Day)
            .ToList();
        foreach (var (_, path) in days)
            foreach (var order in ReadFile<CompletedOrder>(path).OrderByDescending(o => o.PlacedAt))
                yield return order;
    }

    /// <summary>Ακυρωμένα αρχειοθετημένων ημερών μέσα στο εύρος.</summary>
    public static List<CancelledLine> LoadCancellations(DateTime from, DateTime to) =>
        LoadRange<CancelledLine>("cancellations", from, to);

    /// <summary>
    /// Διαβάζει όσα αρχεία ΥΠΑΡΧΟΥΝ και πέφτουν μέσα στο εύρος — δεν διατρέχει ημερομηνίες.
    /// Πριν, ο βρόχος πήγαινε μέρα-μέρα από το «από» ως το «έως»: αν ο χρήστης καθάριζε το πεδίο
    /// ημερομηνίας στο Ιστορικό, το εύρος γινόταν 01/01/0001 – 31/12/9999, δηλαδή ~3,6 εκατομμύρια
    /// επαναλήψεις με έλεγχο αρχείου η καθεμία — το ταμείο πάγωνε τελείως. Έτσι το κόστος εξαρτάται
    /// μόνο από το πόσες μέρες έχουν πραγματικά αρχειοθετηθεί.
    /// </summary>
    private static List<T> LoadRange<T>(string prefix, DateTime from, DateTime to)
    {
        var result = new List<T>();
        foreach (var (day, path) in ArchiveFiles(prefix))
            if (day >= from.Date && day <= to.Date)
                result.AddRange(ReadFile<T>(path));
        return result;
    }

    /// <summary>Τα αρχεία ενός είδους («orders»/«cancellations») με τη μέρα τους — μόνο όσα υπάρχουν,
    /// χωρίς να διατρέχονται ημερομηνίες (βλ. LoadRange για το γιατί).</summary>
    private static IEnumerable<(DateTime Day, string Path)> ArchiveFiles(string prefix)
    {
        string[] paths;
        try
        {
            paths = Directory.GetFiles(ArchiveDir, prefix + "-*.json");
        }
        catch (Exception)
        {
            // Πρόβλημα προσπέλασης φακέλου — καλύτερα άδειο ιστορικό παρά σφάλμα στην οθόνη
            yield break;
        }

        foreach (var path in paths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length <= prefix.Length + 1)
                continue;
            if (DateTime.TryParseExact(name[(prefix.Length + 1)..], "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                yield return (day, path);
        }
    }

    // ---- Συγχρονισμός με το δεύτερο ταμείο ----

    /// <summary>Ποιες μέρες υπάρχουν στο αρχείο και πότε γράφτηκαν τελευταία — το κύριο ταμείο το
    /// σερβίρει, το δεύτερο το συγκρίνει με ό,τι έχει ήδη κατεβάσει.</summary>
    public static List<ArchivedDay> ArchivedDays()
    {
        var stamps = new Dictionary<string, long>();
        foreach (var prefix in new[] { "orders", "cancellations" })
            foreach (var (day, path) in ArchiveFiles(prefix))
            {
                long ticks;
                try
                {
                    ticks = File.GetLastWriteTimeUtc(path).Ticks;
                }
                catch (Exception)
                {
                    continue;
                }
                var key = day.ToString("yyyy-MM-dd");
                stamps[key] = stamps.TryGetValue(key, out var have) ? Math.Max(have, ticks) : ticks;
            }
        return stamps.Select(kv => new ArchivedDay(kv.Key, kv.Value)).OrderBy(d => d.Day, StringComparer.Ordinal).ToList();
    }

    /// <summary>Ολόκληρη μια αρχειοθετημένη μέρα — για το endpoint που διαβάζει το δεύτερο ταμείο.</summary>
    public static ArchivedDayContent ContentFor(DateTime businessDay) =>
        new(ReadOrders(businessDay), ReadCancellations(businessDay));

    /// <summary>Δεύτερο ταμείο — κατεβάζει όποια μέρα λείπει ή άλλαξε στο κύριο. Κάθε 2 λεπτά: το αρχείο
    /// αλλάζει το πολύ μία φορά τη μέρα (στο κλείσιμο), δεν χρειάζεται πιο συχνά.</summary>
    public static void StartClientMirror()
    {
        if (!RemoteSync.IsClient)
            return;
        RemoteSync.StartPolling(TimeSpan.FromMinutes(2), MirrorFromHostAsync);
    }

    /// <summary>Ημέρα → σήμανση της εκδοχής που έχει ήδη κατεβάσει αυτό το ταμείο. Δεν ταιριάζει με το
    /// μοτίβο ονομάτων orders-*/cancellations-*, οπότε δεν μπερδεύεται με τα ίδια τα αρχεία ημέρας.</summary>
    private static string MirrorStampsPath => Path.Combine(ArchiveDir, "mirror-stamps.json");

    private static Dictionary<string, long> ReadMirrorStamps()
    {
        try
        {
            return File.Exists(MirrorStampsPath)
                ? JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(MirrorStampsPath)) ?? []
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Τρέχει ήδη κατέβασμα; Σε αντίθεση με τα υπόλοιπα polls (ένα σκέτο GET), εδώ γίνεται ένα
    /// αίτημα ΑΝΑ ΜΕΡΑ που λείπει — με πολλές μέρες και αργό δίκτυο το πέρασμα μπορεί να ξεπεράσει το
    /// διάστημα του timer και να ξεκινήσει δεύτερο από πάνω του, κατεβάζοντας τα ίδια δύο φορές.</summary>
    private static bool _mirroring;

    private static async Task MirrorFromHostAsync()
    {
        if (_mirroring)
            return;
        _mirroring = true;
        try
        {
            await MirrorOnceAsync();
        }
        finally
        {
            _mirroring = false;
        }
    }

    private static async Task MirrorOnceAsync()
    {
        var days = await RemoteSync.GetAsync<List<ArchivedDay>>("/api/sync/archive/days");
        if (days is null)
            return;

        var have = ReadMirrorStamps();
        var fetched = false;
        foreach (var d in days)
        {
            if (have.TryGetValue(d.Day, out var stamp) && stamp == d.Stamp)
                continue;
            if (!DateTime.TryParseExact(d.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                continue;
            var content = await RemoteSync.GetAsync<ArchivedDayContent>("/api/sync/archive/day?day=" + d.Day);
            if (content is null)
                continue; // έπεσε το δίκτυο — το επόμενο πέρασμα ξαναδοκιμάζει
            if (!WriteMirroredDay(day, content))
                continue;
            have[d.Day] = d.Stamp;
            fetched = true;
        }

        if (!fetched)
            return;
        // Το «τι έχω κατεβάσει» γράφεται ΜΟΝΟ αφού γραφτούν τα ίδια τα αρχεία — αλλιώς μια αποτυχία
        // εγγραφής θα άφηνε τη μέρα σημειωμένη ως κατεβασμένη και δεν θα ξαναδοκίμαζε ποτέ.
        try
        {
            AtomicFile.WriteAllText(MirrorStampsPath, JsonSerializer.Serialize(have, JsonOpts));
        }
        catch (Exception)
        {
            // Χωρίς σημειώσεις απλώς θα ξανακατεβούν την επόμενη φορά — δεν χάνεται τίποτα
        }
        Changed?.Invoke();
    }

    /// <summary>Γράφει τοπικά μια μέρα όπως ήρθε από το κύριο ταμείο. ΑΝΤΙΚΑΘΙΣΤΑ (δεν συγχωνεύει όπως
    /// το ArchiveDay): εδώ το κύριο ταμείο είναι η μοναδική πηγή αλήθειας, το δεύτερο δεν γράφει ποτέ
    /// δικό του αρχείο.</summary>
    private static bool WriteMirroredDay(DateTime businessDay, ArchivedDayContent content)
    {
        try
        {
            AtomicFile.WriteAllText(OrdersPath(businessDay), JsonSerializer.Serialize(content.Orders, JsonOpts));
            AtomicFile.WriteAllText(CancellationsPath(businessDay), JsonSerializer.Serialize(content.Cancellations, JsonOpts));
            InvalidateSummaries();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("archive", $"Το δεύτερο ταμείο δεν μπόρεσε να γράψει τη μέρα {businessDay:yyyy-MM-dd}: {ex}");
            return false;
        }
    }
}
