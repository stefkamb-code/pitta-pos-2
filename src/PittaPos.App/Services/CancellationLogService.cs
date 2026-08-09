using System.IO;
using System.Text.Json;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>Μία γραμμή που ακυρώθηκε (πραγματική διαγραφή) — για το ιστορικό ακυρωμένων.</summary>
public sealed record CancelledLine(int OrderNumber, string Who, string Name, int Quantity, decimal Revenue, DateTime CancelledAt, string CancelledBy = "")
{
    public string TimeLabel => CancelledAt.ToString("HH:mm");
    public string QtyNameLabel => Quantity + " × " + Name;
    public string RevenueLabel => Order.FormatPrice(Revenue);
    /// <summary>Ποιος ακύρωσε (ονομαστικός κωδικός) — «—» για παλιές εγγραφές πριν από αυτό το feature.</summary>
    public string CancelledByLabel => CancelledBy.Length > 0 ? CancelledBy : "—";
}

/// <summary>
/// Καταγραφή προϊόντων/γύρων που ακυρώθηκαν (πραγματική διαγραφή, όχι εξόφληση) — για λόγους ελέγχου.
/// Καθαρίζει μαζί με τα υπόλοιπα στατιστικά ημέρας (βλ. DayReportService.CloseDay).
/// JSON στο %AppData%\PittaPos.
/// </summary>
public class CancellationLogService
{
    public static CancellationLogService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly List<CancelledLine> _entries = [];

    public event Action? Changed;

    /// <summary>Πιο πρόσφατα πρώτα.</summary>
    public IReadOnlyList<CancelledLine> Entries => _entries;

    private CancellationLogService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "cancellations.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var entries = JsonSerializer.Deserialize<List<CancelledLine>>(File.ReadAllText(_path)) ?? [];
            _entries.AddRange(entries);
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
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_entries, JsonOpts));
        }
        catch (Exception)
        {
            // Αποτυχία εγγραφής δεν πρέπει να μπλοκάρει το ταμείο
        }
    }

    public void Log(CancelledLine line)
    {
        _entries.Insert(0, line);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Καθαρίζει το ημερήσιο log (κλείσιμο μέρας).</summary>
    public void Clear()
    {
        _entries.Clear();
        Save();
        Changed?.Invoke();
    }
}
