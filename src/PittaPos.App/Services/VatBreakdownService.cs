using PittaPos.Core.Data;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>
/// Πόσο από το σύνολο μιας παραγγελίας είναι με 13% και πόσο με 24% — το μαγαζί το πληκτρολογεί στη
/// (ξεχωριστή) ταμειακή μηχανή, γι' αυτό τυπώνεται κάτω από το ΣΥΝΟΛΟ.
///
/// <para>Ο κανόνας: φαγητό 13%, ποτά 24% πάντα, αναψυκτικά 13% — <b>εκτός στο ΤΡΑΠΕΖΙ, όπου τα
/// αναψυκτικά πάνε κι αυτά 24%</b>.</para>
///
/// <para>Η κατηγορία ΦΠΑ ζει πάνω στην ΚΑΤΗΓΟΡΙΑ του καταλόγου (<see cref="MenuCategory.VatKind"/>)
/// και εντοπίζεται από το <see cref="SoldLine.ProductId"/>. Γραμμή χωρίς αναγνωρίσιμο προϊόν —
/// παλιά παραγγελία από πριν μπει το ProductId, ή προϊόν που διαγράφηκε από τον κατάλογο — μετράει
/// ως ΦΑΓΗΤΟ: είναι η συντριπτική πλειοψηφία του καταλόγου και το ασφαλές default, αλλά το ξέρουμε
/// και το λέμε (<see cref="VatSplit.HasUnknown"/>) αντί να το κρύψουμε.</para>
/// </summary>
public static class VatBreakdownService
{
    public const int ReducedRate = 13;
    public const int StandardRate = 24;

    /// <summary>Τα δύο ποσά του χαρτιού. Είναι ΤΖΙΡΟΣ ανά συντελεστή (με τον ΦΠΑ μέσα), όχι ο ίδιος ο
    /// φόρος — αυτό ζητάει η ταμειακή.</summary>
    public sealed record VatSplit(decimal Reduced, decimal Standard, bool HasUnknown)
    {
        public bool HasReduced => Reduced > 0;
        public bool HasStandard => Standard > 0;
        public string ReducedLabel => Order.FormatPrice(Reduced);
        public string StandardLabel => Order.FormatPrice(Standard);
    }

    /// <summary>Ο συντελεστής μιας γραμμής, δεδομένου του τύπου παραγγελίας.</summary>
    public static int RateFor(SoldLine line, OrderType type) => KindFor(line) switch
    {
        VatKind.Alcohol => StandardRate,
        // Το αναψυκτικό στο τραπέζι σερβίρεται στον χώρο — 24%, όπως τα ποτά.
        VatKind.SoftDrink => type == OrderType.Table ? StandardRate : ReducedRate,
        _ => ReducedRate,
    };

    /// <summary>
    /// Χωρίζει το σύνολο της παραγγελίας στους δύο συντελεστές.
    ///
    /// <para>Τα δύο ποσά <b>πρέπει</b> να αθροίζουν ΑΚΡΙΒΩΣ στο ΣΥΝΟΛΟ που τυπώνεται από πάνω τους —
    /// αλλιώς δεν βγαίνει η ταμειακή. Γι' αυτό οι γραμμές δίνουν μόνο την ΑΝΑΛΟΓΙΑ και μετά
    /// μοιράζεται το πραγματικό <see cref="CompletedOrder.Total"/>: έτσι μπαίνει μέσα και όποια
    /// έκπτωση παραγγελίας, ακόμα και στο συγκεντρωτικό δελτίο ολόκληρου τραπεζιού, όπου το ποσοστό
    /// έκπτωσης δεν ταξιδεύει μαζί με τις γραμμές (βλ. OrderWizardViewModel.PrintTableRounds).
    /// Το ένα ποσό βγαίνει τέλος με ΑΦΑΙΡΕΣΗ, ώστε να μη χαθεί λεπτό στη στρογγυλοποίηση.</para>
    /// </summary>
    public static VatSplit Split(CompletedOrder order)
    {
        var reduced = 0m;
        var standard = 0m;
        var unknown = false;

        foreach (var line in order.Lines)
        {
            if (KindOrNull(line) is null)
                unknown = true;
            if (RateFor(line, order.Type) == StandardRate)
                standard += line.Revenue;
            else
                reduced += line.Revenue;
        }

        var raw = reduced + standard;
        if (raw <= 0)
            return new VatSplit(0, 0, unknown);

        var total = decimal.Round(order.Total, 2);
        var roundedReduced = standard == 0 ? total : decimal.Round(total * (reduced / raw), 2);
        return new VatSplit(roundedReduced, total - roundedReduced, unknown);
    }

    private static VatKind KindFor(SoldLine line) => KindOrNull(line) ?? VatKind.Food;

    private static VatKind? KindOrNull(SoldLine line)
    {
        if (line.ProductId.Length == 0)
            return null;
        var category = MenuStore.Instance.Categories
            .FirstOrDefault(c => c.Products.Any(p => p.Id == line.ProductId));
        return category is null ? null : category.VatKind ?? MenuSeed.GuessVatKind(category.Name);
    }
}
