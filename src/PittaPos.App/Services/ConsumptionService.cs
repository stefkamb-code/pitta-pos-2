using System.Globalization;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>Σύνολο μιας α' ύλης — πόσα γραμμάρια έφυγαν και από πόσα τεμάχια.</summary>
public sealed record MaterialTotal(string Name, double Grams, int Pieces)
{
    private static readonly CultureInfo Greek = CultureInfo.GetCultureInfo("el-GR");

    public double Kilos => Grams / 1000;

    /// <summary>«12,40 κιλά» — κάτω από ένα κιλό μένει σε γραμμάρια, γιατί «0,30 κιλά» δεν διαβάζεται.</summary>
    public string AmountLabel => Grams >= 1000
        ? Kilos.ToString("0.00", Greek) + " κιλά"
        : Math.Round(Grams).ToString("0", Greek) + " γρ.";

    /// <summary>Από πόσα πουλημένα τεμάχια μαζεύτηκε — μετράνε και τα έξτρα, γι' αυτό «από»: δέκα
    /// πίττες κοτόπουλο με δύο έξτρα κοτόπουλο κάνουν δώδεκα.</summary>
    public string PiecesLabel => "από " + Pieces + (Pieces == 1 ? " τεμάχιο" : " τεμάχια");
}

/// <summary>
/// Πόση α' ύλη έχει φύγει από την αποθήκη — «σήμερα πουλήθηκαν τόσα κιλά κοτόπουλο, τόσα χοιρινό».
///
/// <para>Η κατανάλωση δηλώνεται μία φορά πάνω στο προϊόν (βλ. <see cref="MaterialUse"/>: μεγάλη πίττα
/// 100 γρ., μικρή 60, καλαμάκι 110 και η μεγάλη έχει δύο μέσα) και στα έξτρα, ώστε ένα «έξτρα
/// κοτόπουλο» να μετράει κι αυτό. Εδώ απλώς πολλαπλασιάζεται με ό,τι πουλήθηκε.</para>
///
/// <para><b>Δεν είναι απογραφή.</b> Είναι το τι <i>έπρεπε</i> να φύγει με βάση τις παραγγελίες —
/// φύρα, δοκιμές και το χέρι του ψήστη δεν τα ξέρει κανείς από το ταμείο.</para>
/// </summary>
public static class ConsumptionService
{
    /// <summary>Οι καταναλώσεις των παραγγελιών που δίνονται, από τη μεγαλύτερη στη μικρότερη.</summary>
    public static IReadOnlyList<MaterialTotal> For(IEnumerable<CompletedOrder> orders)
    {
        // Ο κατάλογος διαβάζεται ΜΙΑ φορά σε λεξικά: με 124 προϊόντα και δεκάδες παραγγελίες, το
        // ψάξιμο μέσα στις κατηγορίες για κάθε γραμμή είναι σκέτη σπατάλη.
        var byProduct = new Dictionary<string, List<MaterialUse>>(StringComparer.Ordinal);
        foreach (var category in MenuStore.Instance.Categories)
            foreach (var product in category.Products)
                if (product.Materials is { Count: > 0 } m)
                    byProduct[product.Id] = m;

        var byExtra = new Dictionary<string, List<MaterialUse>>(StringComparer.OrdinalIgnoreCase);
        foreach (var extra in MenuStore.Instance.Extras)
            if (extra.Materials is { Count: > 0 } m)
                byExtra[extra.Name] = m;

        if (byProduct.Count == 0 && byExtra.Count == 0)
            return [];

        // Κλειδί χωρίς πεζά/κεφαλαία ώστε «Κοτόπουλο» και «κοτόπουλο» να μη μετρηθούν χωριστά· κρατάμε
        // την πρώτη γραφή που συναντήσαμε για την οθόνη.
        var totals = new Dictionary<string, (string Name, double Grams, int Pieces)>(StringComparer.OrdinalIgnoreCase);
        void Add(MaterialUse use, int times)
        {
            if (use.Grams <= 0 || use.Name.Trim().Length == 0)
                return;
            var name = use.Name.Trim();
            var had = totals.TryGetValue(name, out var cur);
            totals[name] = (had ? cur.Name : name, (had ? cur.Grams : 0) + use.Grams * times,
                (had ? cur.Pieces : 0) + times);
        }

        foreach (var order in orders)
            foreach (var line in order.Lines)
            {
                if (line.ProductId.Length > 0 && byProduct.TryGetValue(line.ProductId, out var uses))
                    foreach (var use in uses)
                        Add(use, line.Quantity);

                // Τα έξτρα είναι ανά τεμάχιο της γραμμής: δύο πίττες με έξτρα κοτόπουλο = δύο έξτρα.
                if (line.Customization?.Extras is { Count: > 0 } extras)
                    foreach (var (name, qty) in extras)
                        if (byExtra.TryGetValue(name, out var extraUses))
                            foreach (var use in extraUses)
                                Add(use, qty * line.Quantity);
            }

        return [.. totals.Values
            .Select(t => new MaterialTotal(t.Name, t.Grams, t.Pieces))
            .OrderByDescending(t => t.Grams)];
    }

    /// <summary>Ό,τι έχει φύγει από το άνοιγμα της ημέρας μέχρι τώρα.</summary>
    public static IReadOnlyList<MaterialTotal> Today() => For(SalesStatsService.Instance.Orders);

    /// <summary>Έχει δηλωθεί κατανάλωση έστω σε ένα προϊόν; Όσο δεν έχει, οι οθόνες δεν δείχνουν
    /// άδειους πίνακες — δεν υπάρχει τίποτα να πουν ακόμα.</summary>
    public static bool IsConfigured =>
        MenuStore.Instance.Categories.Any(c => c.Products.Any(p => p.Materials is { Count: > 0 }))
        || MenuStore.Instance.Extras.Any(e => e.Materials is { Count: > 0 });
}
