using System.IO;
using System.Text.Json;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>Μία είσπραξη από τραπέζι — ποσό και τρόπος πληρωμής, τη στιγμή που έγινε.</summary>
public sealed record TablePayment(int Table, decimal Amount, PaymentMethod Method, DateTime At);

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
    public void Add(int table, decimal amount, PaymentMethod method)
    {
        if (amount <= 0)
            return;
        _payments.Add(new TablePayment(table, amount, method, DateTime.Now));
        Save();
    }

    public decimal TotalFor(PaymentMethod method) =>
        _payments.Where(p => p.Method == method).Sum(p => p.Amount);

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
