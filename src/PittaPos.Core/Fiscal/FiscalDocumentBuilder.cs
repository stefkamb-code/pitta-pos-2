namespace PittaPos.Core.Fiscal;

/// <summary>Μια γραμμή όπως την έχει το ταμείο: όνομα, τεμάχια, τζίρος (με ΦΠΑ, μετά από τυχόν έκπτωση) και ο
/// συντελεστής που βγαίνει από την κατηγορία του καταλόγου — όπως τον όρισε το μαγαζί.</summary>
public sealed record TillLine(string Name, int Quantity, decimal Revenue, int VatRate);

/// <summary>
/// Φτιάχνει παραστατικά από ό,τι ήδη ξέρει το ταμείο για μια παραγγελία. Δεν αλλάζει τίποτα στο χαρτί του ταμείου —
/// η απόδειξη είναι ξεχωριστή.
/// </summary>
public static class FiscalDocumentBuilder
{
    /// <summary>
    /// Οι γραμμές του παραστατικού. Γραμμές χωρίς χρέωση (κέρασμα) δεν μπαίνουν — παραστατικό λιανικής με μηδενική
    /// γραμμή δεν προσθέτει τίποτα και κάποιοι πάροχοι το απορρίπτουν. Αν το άθροισμα δεν βγαίνει ακριβώς ίσο με το
    /// σύνολο της παραγγελίας (στρογγυλοποίηση της έκπτωσης), η διαφορά μπαίνει στη μεγαλύτερη γραμμή: το χαρτί του
    /// ταμείου και η απόδειξη πρέπει να λένε ΤΟ ΙΔΙΟ ποσό.
    /// </summary>
    public static List<FiscalLine> Lines(IEnumerable<TillLine> lines, decimal total)
    {
        var list = lines.Where(l => l.Revenue != 0 && l.Quantity != 0)
            .Select(l => new FiscalLine(l.Name, l.Quantity, decimal.Round(l.Revenue, 2), l.VatRate))
            .ToList();
        if (list.Count == 0) return list;
        var diff = decimal.Round(total, 2) - list.Sum(l => l.Gross);
        if (diff != 0)
        {
            var i = list.FindIndex(l => l.Gross == list.Max(x => x.Gross));
            list[i] = list[i] with { Gross = list[i].Gross + diff };
        }
        return list;
    }

    /// <summary>Απόδειξη για όρθιο, διανομή ή εφαρμογή — ή για ένα άτομο τραπεζιού (τότε με τα ΜΑΡΚ των δελτίων).</summary>
    public static FiscalDocument Receipt(IEnumerable<TillLine> lines, decimal total, FiscalPay pay, string externalId,
        string terminalId = "", string tableName = "", string providerTableRef = "", IReadOnlyList<string>? slips = null) => new()
    {
        Type = FiscalDocType.RetailReceipt,
        Lines = Lines(lines, total),
        Pay = pay,
        TerminalId = pay == FiscalPay.Card ? terminalId : "",
        TableName = tableName,
        ProviderTableRef = providerTableRef,
        CorrelatedMarks = slips ?? [],
        ExternalId = externalId,
    };

    /// <summary>Δελτίο παραγγελίας για έναν γύρο τραπεζιού — φεύγει τη στιγμή που γράφεται ο γύρος.</summary>
    public static FiscalDocument OrderSlip(IEnumerable<TillLine> lines, string tableName, string externalId, string providerTableRef = "")
    {
        var l = lines.ToList();
        return new FiscalDocument
        {
            Type = FiscalDocType.OrderSlip,
            Lines = Lines(l, l.Sum(x => x.Revenue)),
            TableName = tableName,
            ProviderTableRef = providerTableRef,
            ExternalId = externalId,
        };
    }

    /// <summary>Πιστωτικό για κάτι που είχε ήδη αποδειχθεί (επιστροφή χρημάτων).</summary>
    public static FiscalDocument Credit(IEnumerable<TillLine> lines, FiscalPay pay, string originalMark, string externalId, string terminalId = "")
    {
        var l = lines.ToList();
        return new FiscalDocument
        {
            Type = FiscalDocType.RetailCredit,
            Lines = Lines(l, l.Sum(x => x.Revenue)),
            Pay = pay,
            TerminalId = pay == FiscalPay.Card ? terminalId : "",
            CorrelatedMarks = [originalMark],
            ExternalId = externalId,
        };
    }

    /// <summary>Τιμολόγιο σε επιχείρηση.</summary>
    public static FiscalDocument Invoice(IEnumerable<TillLine> lines, decimal total, FiscalPay pay, string customerVat,
        string customerName, string externalId, string terminalId = "") => new()
    {
        Type = FiscalDocType.SalesInvoice,
        Lines = Lines(lines, total),
        Pay = pay,
        TerminalId = pay == FiscalPay.Card ? terminalId : "",
        CustomerVat = new string(customerVat.Where(char.IsDigit).ToArray()),
        CustomerName = customerName,
        ExternalId = externalId,
    };
}
