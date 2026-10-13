using System.Text.Encodings.Web;
using System.Text.Json;

namespace PittaPos.Core.Fiscal;

/// <summary>Μία γραμμή του ημερολογίου ελέγχου: τι έγινε, σε ποιο παραστατικό, πότε.</summary>
public sealed record FiscalAuditRecord(
    DateTime At,
    string Event,
    string EntryId,
    string Kind,
    string State,
    string Orders,
    decimal Total,
    string Pay,
    string Detail);

/// <summary>Πού γράφεται το ημερολόγιο ελέγχου. Δεν ρίχνει ΠΟΤΕ — ένα χαμένο log δεν σταματά απόδειξη.</summary>
public interface IFiscalAudit
{
    void Write(FiscalAuditRecord record);
}

public sealed class NoFiscalAudit : IFiscalAudit
{
    public static readonly NoFiscalAudit Instance = new();
    public void Write(FiscalAuditRecord record) { }
}

/// <summary>Στη μνήμη — για τις δοκιμές.</summary>
public sealed class MemoryFiscalAudit : IFiscalAudit
{
    private readonly List<FiscalAuditRecord> _records = [];
    public IReadOnlyList<FiscalAuditRecord> Records { get { lock (_records) return [.. _records]; } }
    public void Write(FiscalAuditRecord record) { lock (_records) _records.Add(record); }
}

/// <summary>
/// Ένα αρχείο ανά μήνα (JSON, μία γραμμή ανά γεγονός), μόνο προσθήκες. Σε αντίθεση με το ημερολόγιο παραστατικών (που
/// κρατά την τρέχουσα κατάσταση και σβήνει τα κλεισμένα μετά από 10 μέρες), αυτό κρατά ΚΑΘΕ βήμα και δεν σβήνεται ποτέ από
/// το ταμείο: πότε γράφτηκε κάθε απόδειξη, γιατί περίμενε, τι την ελευθέρωσε, κάθε αποστολή και απάντηση του παρόχου,
/// ακυρώσεις, αλλαγές πληρωμής, τυπώματα, και ποιος άλλαξε τη ρύθμιση.
/// </summary>
public sealed class FileFiscalAudit(string folder) : IFiscalAudit
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly object _gate = new();

    public string PathFor(DateTime at) => Path.Combine(folder, $"fiscal-audit-{at:yyyy-MM}.jsonl");

    public void Write(FiscalAuditRecord record)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(folder);
                File.AppendAllText(PathFor(record.At), JsonSerializer.Serialize(record, Options) + "\n");
            }
        }
        catch
        {
            // Γεμάτος δίσκος / κλείδωμα: το παραστατικό συνεχίζει κανονικά (το ημερολόγιο παραστατικών το έχει ήδη).
        }
    }
}
