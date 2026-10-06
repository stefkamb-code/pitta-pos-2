using System.Security.Cryptography;

namespace PittaPos.Core.Fiscal;

/// <summary>
/// Εικονικός πάροχος: κάνει ό,τι και ένας πραγματικός (ΜΑΡΚ, UID, QR, έγκριση κάρτας, ανοιχτό τραπέζι) χωρίς να
/// στέλνει τίποτα πουθενά. Για να δοκιμάζεται όλη η ροή του ταμείου πριν υπάρξει κλειδί από πάροχο — και για εκπαίδευση.
/// Τα ΜΑΡΚ του ξεκινούν από 9 ώστε να μη μπερδεύονται ποτέ με αληθινά.
/// </summary>
public sealed class SimulatedProvider : IFiscalProvider
{
    public string Name => FiscalProviders.Simulated;

    private long _next = 900_000_000_000_000 + Random.Shared.Next(1_000_000);
    private readonly Dictionary<string, long> _numbers = new();
    private readonly HashSet<string> _cancelled = new();
    private readonly object _gate = new();

    public async Task<FiscalResult> IssueAsync(FiscalDocument doc, CancellationToken ct = default)
    {
        if (doc.Lines.Count == 0) return FiscalResult.Fail("Παραστατικό χωρίς γραμμές.");
        if (doc.Type == FiscalDocType.SalesInvoice && doc.CustomerVat.Length != 9)
            return FiscalResult.Fail("Το τιμολόγιο θέλει ΑΦΜ πελάτη 9 ψηφίων.");
        if (doc.Pay == FiscalPay.Card && doc.Type != FiscalDocType.OrderSlip && doc.TerminalId.Length == 0)
            return FiscalResult.Fail("Δεν έχει οριστεί τερματικό κάρτας στις Ρυθμίσεις.");

        // Κάρτα: «ο πελάτης βάζει την κάρτα» — λίγη αναμονή, όπως στο αληθινό τερματικό.
        await Task.Delay(doc.Pay == FiscalPay.Card && doc.Type != FiscalDocType.OrderSlip ? 1500 : 150, ct);

        lock (_gate)
        {
            var code = FiscalCodes.Code(doc.Type);
            _numbers[code] = _numbers.GetValueOrDefault(code) + 1;
            var mark = (++_next).ToString();
            return new FiscalResult
            {
                Ok = true,
                Mark = mark,
                Uid = Convert.ToHexString(SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(mark + doc.ExternalId))),
                QrUrl = "https://example.invalid/simulated/" + mark,
                Series = doc.Type switch { FiscalDocType.OrderSlip => "ΔΠ", FiscalDocType.RetailCredit => "ΠΛ", FiscalDocType.SalesInvoice => "ΤΠ", _ => "Α" },
                Number = _numbers[code],
                PosTxn = doc.Pay == FiscalPay.Card && doc.Type != FiscalDocType.OrderSlip ? "SIM-" + Random.Shared.Next(100000, 999999) : "",
                ProviderId = Guid.NewGuid().ToString(),
                ProviderTableRef = doc.TableName.Length > 0 ? (doc.ProviderTableRef.Length > 0 ? doc.ProviderTableRef : "table-" + Guid.NewGuid().ToString("N")[..8]) : "",
            };
        }
    }

    public Task<FiscalResult> CancelOrderSlipAsync(string mark, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_cancelled.Add(mark)
                ? new FiscalResult { Ok = true, Mark = (++_next).ToString() }
                : FiscalResult.Fail("Το δελτίο είναι ήδη ακυρωμένο."));
    }
}
