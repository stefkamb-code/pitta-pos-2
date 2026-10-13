using System.Text;
using PittaPos.Core.Models;

namespace PittaPos.Core.Fiscal;

/// <summary>
/// Το χαρτί της απόδειξης του παρόχου (ΜΑΡΚ + QR) — ΞΕΧΩΡΙΣΤΟ από το δελτίο που τυπώνει σήμερα το ταμείο, που μένει
/// ακριβώς όπως είναι (απόφαση 6/10/2026). Στενό, για ρολό 80mm· το QR μπαίνει από κάτω από όποιον τυπώνει.
/// </summary>
public static class FiscalReceiptText
{
    public const int Width = 32;

    public static string Build(FiscalEntry e, string storeName, string address, string afm, string providerName)
    {
        var r = e.Result ?? new FiscalResult();
        var sb = new StringBuilder();
        if (storeName.Length > 0) sb.AppendLine(Center(storeName.ToUpperInvariant()));
        if (address.Length > 0) sb.AppendLine(Center(address));
        if (afm.Length > 0) sb.AppendLine(Center("ΑΦΜ " + afm));
        sb.AppendLine();
        sb.AppendLine(Center(e.Kind == FiscalEntryKind.Credit ? "ΠΙΣΤΩΤΙΚΟ ΣΤΟΙΧΕΙΟ ΛΙΑΝΙΚΗΣ" : "ΑΠΟΔΕΙΞΗ ΛΙΑΝΙΚΗΣ ΠΩΛΗΣΗΣ"));
        var number = (r.Series + " " + (r.Number > 0 ? r.Number.ToString() : "")).Trim();
        sb.AppendLine(Pair(number, e.CreatedAt.ToString("dd/MM/yyyy HH:mm")));
        if (e.TableName.Length > 0) sb.AppendLine(e.TableName);
        sb.AppendLine(new string('-', Width));

        foreach (var i in e.Items)
            sb.AppendLine(Pair((i.Quantity != 1 ? i.Quantity + " x " : "") + i.Name, Money(i.Revenue)));
        sb.AppendLine(new string('-', Width));
        sb.AppendLine(Pair("ΣΥΝΟΛΟ", Money(e.Total)));

        // Ανάλυση ΦΠΑ ανά συντελεστή — ίδιος υπολογισμός με αυτόν που πήγε στον πάροχο.
        var lines = FiscalDocumentBuilder.Lines(e.Items.Select(i => new TillLine(i.Name, i.Quantity, i.Revenue, i.VatRate)), e.Total);
        foreach (var g in lines.GroupBy(l => l.VatRate).OrderBy(g => g.Key))
            sb.AppendLine(Pair($"ΦΠΑ {g.Key}% (καθ. {Money(g.Sum(l => l.Net))})", Money(g.Sum(l => l.Vat))));

        sb.AppendLine(Pair("ΠΛΗΡΩΜΗ", e.Pay switch
        {
            FiscalPay.Card => "ΚΑΡΤΑ",
            FiscalPay.Online => "ΜΕΣΩ ΠΛΑΤΦΟΡΜΑΣ",
            _ => "ΜΕΤΡΗΤΑ",
        }));
        sb.AppendLine();
        if (r.Mark.Length > 0) sb.AppendLine("ΜΑΡΚ " + r.Mark);
        if (r.Uid.Length > 0) sb.AppendLine("UID " + r.Uid);
        if (r.PendingTransmission) sb.AppendLine("Σε αναμονή διαβίβασης στην ΑΑΔΕ");
        if (providerName.Length > 0) sb.AppendLine("Πάροχος: " + providerName);
        // Σκέτο \n, όπως όλα τα κείμενα που τυπώνει το ταμείο (η προσαρμογή πλάτους μετράει γραμμές με αυτό).
        return sb.ToString().Replace("\r\n", "\n").TrimEnd();
    }

    private static string Money(decimal v) => Order.FormatPrice(v);

    private static string Center(string s) => s.Length >= Width ? s : new string(' ', (Width - s.Length) / 2) + s;

    /// <summary>Αριστερά το κείμενο, δεξιά το ποσό· αν δεν χωράνε, το κείμενο σπάει σε γραμμή από πάνω.</summary>
    private static string Pair(string left, string right)
    {
        var room = Width - right.Length - 1;
        if (left.Length <= room) return left + new string(' ', Width - left.Length - right.Length) + right;
        return left + "\n" + new string(' ', Width - right.Length) + right;
    }
}
