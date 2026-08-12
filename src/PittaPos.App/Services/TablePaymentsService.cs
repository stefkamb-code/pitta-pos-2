using System.IO;
using System.Text.Json;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>Μία είσπραξη από τραπέζι — ποσό και τρόπος πληρωμής, τη στιγμή που έγινε.
/// <para>Το <paramref name="OrderNumber"/> λέει ΠΟΙΑΝΟΥ είναι η είσπραξη: με μία παραγγελία ανά άτομο,
/// είναι στην πράξη το άτομο. Χρειάζεται για να διορθώνεται εκ των υστέρων από το Ιστορικό ο τρόπος
/// πληρωμής ΕΝΟΣ ατόμου (βλ. SwitchMethod) — π.χ. πέρασε κάρτα ενώ πλήρωσε μετρητά. Παλιές εγγραφές
/// (πριν μπει το πεδίο) έχουν 0 και απλώς δεν διορθώνονται.</para></summary>
public sealed record TablePayment(int Table, decimal Amount, PaymentMethod Method, DateTime At, int OrderNumber = 0);

/// <summary>
/// Ημερολόγιο εισπράξεων από τραπέζια (μετρητά/κάρτα), ώστε να μπαίνουν και αυτά στον διαχωρισμό
/// «Μετρητά / Κάρτα» της αναφοράς ημέρας.
///
/// Ξεχωριστό αρχείο από τις εξοφλήσεις (TableSettlementService) ΓΙΑ ΛΟΓΟ: εκείνες σβήνονται μόλις
/// κλείσει το τραπέζι (καθαρό ξεκίνημα για τον επόμενο πελάτη), οπότε αν κρατούσαμε εκεί τον τρόπο
/// πληρωμής θα χανόταν πριν προλάβει να μετρήσει στην αναφορά. Εδώ οι εγγραφές μένουν μέχρι το
/// κλείσιμο της μέρας, όπως και τα υπόλοιπα στοιχεία ημέρας.
///
/// Δεν επηρεάζει τον τζίρο — ο τζίρος βγαίνει πάντα από τις ίδιες τις παραγγελίες. Εδώ καταγράφεται
/// μόνο ΠΩΣ πληρώθηκε, για τον διαχωρισμό.
/// </summary>
public class TablePaymentsService
{
    public static TablePaymentsService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly List<TablePayment> _payments = [];

    public IReadOnlyList<TablePayment> Payments => _payments;

    private TablePaymentsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "table-payments.json");
        Load();
    }

    /// <summary>Καταγράφει είσπραξη. Καλείται μόνο στο κύριο ταμείο — οι εξοφλήσεις του δεύτερου
    /// ταμείου περνάνε ούτως ή άλλως από εκεί (βλ. TableSettlementService).</summary>
    public void Add(int table, int orderNumber, decimal amount, PaymentMethod method)
    {
        if (amount <= 0)
            return;
        _payments.Add(new TablePayment(table, amount, method, DateTime.Now, orderNumber));
        Save();
    }

    public decimal TotalFor(PaymentMethod method) =>
        _payments.Where(p => p.Method == method).Sum(p => p.Amount);

    /// <summary>Πώς πληρώθηκε αυτή η παραγγελία (= αυτό το άτομο): null αν δεν έχει εισπραχθεί τίποτα
    /// ακόμα, ή αν πληρώθηκε με τα δύο μαζί (σπάνιο, μισά-μισά — τότε δεν δείχνουμε ψέματα).</summary>
    public PaymentMethod? MethodFor(int orderNumber)
    {
        var methods = _payments.Where(p => p.OrderNumber == orderNumber).Select(p => p.Method).Distinct().ToList();
        return methods.Count == 1 ? methods[0] : null;
    }

    /// <summary>Σβήνει τις εισπράξεις μιας παραγγελίας — καλείται όταν διαγράφεται η ίδια η παραγγελία
    /// (π.χ. ένα άτομο του τραπεζιού ακυρώνεται από το Ιστορικό). Χωρίς αυτό, το ποσό του θα συνέχιζε
    /// να μετράει στα μετρητά/κάρτα της ημέρας ενώ ο τζίρος του θα είχε αφαιρεθεί.</summary>
    public void RemoveFor(int orderNumber)
    {
        if (_payments.RemoveAll(p => p.OrderNumber == orderNumber) > 0)
            Save();
    }

    public decimal AmountFor(int orderNumber) =>
        _payments.Where(p => p.OrderNumber == orderNumber).Sum(p => p.Amount);

    /// <summary>
    /// Διόρθωση εκ των υστέρων: ο ταμίας εξόφλησε ένα άτομο με κάρτα ενώ πλήρωσε μετρητά. Γυρίζει ΟΛΕΣ
    /// τις εισπράξεις αυτής της παραγγελίας στον σωστό τρόπο, ώστε να μετακινηθεί και το ποσό στην
    /// αναφορά ημέρας (βλ. DayReportService) — αλλιώς η διόρθωση θα ήταν μόνο στα λόγια.
    /// </summary>
    /// <returns>Πόσα ευρώ μετακινήθηκαν· 0 αν δεν βρέθηκε είσπραξη γι' αυτή την παραγγελία.</returns>
    public decimal SwitchMethod(int orderNumber, PaymentMethod method)
    {
        if (orderNumber <= 0)
            return 0;

        var moved = 0m;
        for (var i = 0; i < _payments.Count; i++)
        {
            var p = _payments[i];
            if (p.OrderNumber != orderNumber || p.Method == method)
                continue;
            _payments[i] = p with { Method = method };
            moved += p.Amount;
        }
        if (moved > 0)
            Save();
        return moved;
    }

    /// <summary>Καθαρίζει μαζί με τα υπόλοιπα στοιχεία ημέρας (βλ. DayReportService.CloseDay).</summary>
    public void Clear()
    {
        if (_payments.Count == 0)
            return;
        _payments.Clear();
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            _payments.AddRange(JsonSerializer.Deserialize<List<TablePayment>>(File.ReadAllText(_path)) ?? []);
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
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_payments, JsonOpts));
        }
        catch (Exception)
        {
            // Αποτυχία εγγραφής δεν πρέπει να μπλοκάρει το ταμείο
        }
    }
}
