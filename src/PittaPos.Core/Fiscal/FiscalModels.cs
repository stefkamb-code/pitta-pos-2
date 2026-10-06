namespace PittaPos.Core.Fiscal;

/// <summary>Τα παραστατικά της myDATA που κόβει ένα μαγαζί εστίασης μέσω παρόχου.</summary>
public enum FiscalDocType
{
    /// <summary>11.1 Απόδειξη λιανικής — όρθιος, διανομή, εφαρμογές, και το κλείσιμο τραπεζιού.</summary>
    RetailReceipt,
    /// <summary>8.6 Δελτίο παραγγελίας εστίασης — κάθε γύρος τραπεζιού, τη στιγμή που γράφεται. Το χαρτί του δεν δείχνει σύνολο.</summary>
    OrderSlip,
    /// <summary>11.4 Πιστωτικό λιανικής — επιστροφή χρημάτων για κάτι που είχε ήδη αποδειχθεί.</summary>
    RetailCredit,
    /// <summary>1.1 Τιμολόγιο πώλησης — σε επιχείρηση με ΑΦΜ.</summary>
    SalesInvoice,
}

/// <summary>Πώς πληρώθηκε. Το Online είναι πληρωμένο μέσα στην πλατφόρμα (e-food/Wolt) — δεν περνά από το μαγαζί.</summary>
public enum FiscalPay { Cash, Card, Online }

public static class FiscalCodes
{
    /// <summary>Ο κωδικός της myDATA για κάθε τύπο.</summary>
    public static string Code(FiscalDocType t) => t switch
    {
        FiscalDocType.RetailReceipt => "11.1",
        FiscalDocType.OrderSlip => "8.6",
        FiscalDocType.RetailCredit => "11.4",
        FiscalDocType.SalesInvoice => "1.1",
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };
}

/// <summary>
/// Μία γραμμή παραστατικού. Οι τιμές του ταμείου είναι ΜΕ τον ΦΠΑ μέσα (όπως στον κατάλογο), οπότε κρατάμε το μικτό
/// ποσό και ο καθαρός/φόρος βγαίνουν από τον συντελεστή — ο φόρος με αφαίρεση, ώστε καθαρό + φόρος = μικτό ακριβώς.
/// </summary>
/// <param name="VatRate">13 ή 24 — όπως τον έχει ορίσει το μαγαζί στην κατηγορία του καταλόγου.</param>
public sealed record FiscalLine(string Name, decimal Quantity, decimal Gross, int VatRate)
{
    public decimal Net => decimal.Round(Gross / (1 + VatRate / 100m), 2, MidpointRounding.AwayFromZero);
    public decimal Vat => Gross - Net;
    public decimal UnitNet => Quantity == 0 ? 0 : decimal.Round(Net / Quantity, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Ένα παραστατικό όπως το ετοιμάζει το ταμείο, πριν φύγει στον πάροχο. Ίδιο για όλους τους παρόχους —
/// ο οδηγός του καθενός το μεταφράζει στη δική του μορφή.</summary>
public sealed class FiscalDocument
{
    public FiscalDocType Type { get; init; }
    public IReadOnlyList<FiscalLine> Lines { get; init; } = [];
    public FiscalPay Pay { get; init; } = FiscalPay.Cash;
    /// <summary>Για κάρτα: σε ποιο τερματικό πάει το ποσό (το Terminal ID από τις Ρυθμίσεις του μαγαζιού).</summary>
    public string TerminalId { get; init; } = "";
    /// <summary>Για τραπέζι: «Τραπέζι 5». Ίδιο σε όλα τα δελτία και στην απόδειξη που τα κλείνει.</summary>
    public string TableName { get; init; } = "";
    /// <summary>Ό,τι μας έδωσε ο πάροχος για το ανοιχτό τραπέζι στο πρώτο δελτίο (π.χ. το catering_table_id του Wrapp).</summary>
    public string ProviderTableRef { get; init; } = "";
    /// <summary>Τα ΜΑΡΚ που κλείνει (τα δελτία του τραπεζιού) ή πιστώνει (η αρχική απόδειξη).</summary>
    public IReadOnlyList<string> CorrelatedMarks { get; init; } = [];
    /// <summary>Μόνο στο τιμολόγιο: ΑΦΜ και επωνυμία του πελάτη.</summary>
    public string CustomerVat { get; init; } = "";
    public string CustomerName { get; init; } = "";
    /// <summary>Μοναδικό από το ταμείο (μαγαζί + ημέρα + παραγγελία + άτομο) — ξαναστάλσιμο δεν κόβει δεύτερο.</summary>
    public string ExternalId { get; init; } = "";

    public decimal Gross => Lines.Sum(l => l.Gross);
    public decimal Net => Lines.Sum(l => l.Net);
    public decimal Vat => Lines.Sum(l => l.Vat);
}

/// <summary>Τι γύρισε ο πάροχος.</summary>
public sealed record FiscalResult
{
    public bool Ok { get; init; }
    public string Error { get; init; } = "";
    public string Mark { get; init; } = "";
    public string Uid { get; init; } = "";
    public string QrUrl { get; init; } = "";
    public string Series { get; init; } = "";
    public long Number { get; init; }
    /// <summary>Ο πάροχος δεν έφτασε ακόμα την ΑΑΔΕ· θα το στείλει μόνος του. Το QR ισχύει ήδη — δεν ξανατυπώνεται.</summary>
    public bool PendingTransmission { get; init; }
    /// <summary>Η συναλλαγή του τερματικού (μόνο κάρτα).</summary>
    public string PosTxn { get; init; } = "";
    /// <summary>Η ταυτότητα του παραστατικού στον πάροχο.</summary>
    public string ProviderId { get; init; } = "";
    /// <summary>Για τραπέζι: τι να στείλουμε στα επόμενα δελτία/στην απόδειξη (βλ. <see cref="FiscalDocument.ProviderTableRef"/>).</summary>
    public string ProviderTableRef { get; init; } = "";

    public static FiscalResult Fail(string error) => new() { Ok = false, Error = error };
}

public sealed record FiscalTerminal(string Name, string Acquirer, string MerchantId, string TerminalId);

/// <summary>Οι ρυθμίσεις ΑΑΔΕ του μαγαζιού, όπως τις έβαλε στο site διαχείρισης (Ρυθμίσεις).</summary>
public sealed record FiscalSettings(
    string Provider, string Env, string User, string Key, IReadOnlyList<FiscalTerminal> Terminals,
    string Receipt, string ReceiptPrinter, string Afm)
{
    public bool Production => Env == "production";
    public static FiscalSettings None { get; } = new("", "sandbox", "", "", [], "same", "", "");
}
