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
    }

    private static List<CompletedOrder> ReadOrders(DateTime businessDay) => ReadFile<CompletedOrder>(OrdersPath(businessDay));

    private static List<CancelledLine> ReadCancellations(DateTime businessDay) => ReadFile<CancelledLine>(CancellationsPath(businessDay));

    private static List<T> ReadFile<T>(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Παραγγελίες αρχειοθετημένων ημερών μέσα στο εύρος (χωρίς τη σημερινή — αυτή έρχεται ζωντανή από το SalesStatsService).</summary>
    public static List<CompletedOrder> LoadOrders(DateTime from, DateTime to) =>
        LoadRange<CompletedOrder>("orders", from, to);

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
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("archive", $"Το δεύτερο ταμείο δεν μπόρεσε να γράψει τη μέρα {businessDay:yyyy-MM-dd}: {ex}");
            return false;
        }
    }
}
