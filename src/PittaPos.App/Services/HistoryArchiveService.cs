using System.Globalization;
using System.IO;
using System.Text.Json;

namespace PittaPos.App.Services;

/// <summary>
/// Αρχείο ιστορικού ανά ημέρα-επιχείρησης — ένα JSON ανά μέρα (παραγγελίες + ακυρωμένα), γραμμένο
/// στο κλείσιμο κάθε μέρας (βλ. DayReportService.CloseDay) ΠΡΙΝ καθαρίσουν τα τρέχοντα στατιστικά.
/// Επιτρέπει στο Ιστορικό να δείχνει παλιότερες μέρες, όχι μόνο τη σημερινή.
/// </summary>
public static class HistoryArchiveService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

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
        try
        {
            foreach (var path in Directory.EnumerateFiles(ArchiveDir, prefix + "-*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Length <= prefix.Length + 1)
                    continue;
                if (!DateTime.TryParseExact(name[(prefix.Length + 1)..], "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    continue;
                if (day < from.Date || day > to.Date)
                    continue;
                result.AddRange(ReadFile<T>(path));
            }
        }
        catch (Exception)
        {
            // Πρόβλημα προσπέλασης φακέλου — καλύτερα άδειο ιστορικό παρά σφάλμα στην οθόνη
        }
        return result;
    }
}
