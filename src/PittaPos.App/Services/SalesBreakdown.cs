using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>Ένα προϊόν μέσα σε κατηγορία — πόσα τεμάχια και πόσα ευρώ.</summary>
public sealed record ProductSale(string Name, int Quantity, decimal Revenue);

/// <summary>Πωλήσεις μιας κατηγορίας καταλόγου, με τα προϊόντα της από κάτω (φθίνουσα σειρά τεμαχίων).</summary>
public sealed record CategorySale(string Name, int Quantity, decimal Revenue, IReadOnlyList<ProductSale> Products);

/// <summary>
/// Πώς σπάνε οι πωλήσεις της ημέρας σε κατηγορίες και προϊόντα — ΜΙΑ πηγή αλήθειας για την οθόνη
/// Στατιστικών (<see cref="ViewModels.StatsViewModel"/>) και για την αναφορά/email
/// (<see cref="DayReportService"/>). Ζούσε μέσα στο StatsViewModel· βγήκε εδώ όταν η αναφορά
/// χρειάστηκε τα ίδια ακριβώς νούμερα — δύο αντίγραφα της ίδιας ομαδοποίησης σημαίνει ότι μια μέρα
/// το email θα έλεγε άλλα από την οθόνη.
/// </summary>
public static class SalesBreakdown
{
    /// <summary>Το όνομα του προϊόντος όπως το λέει ο ΚΑΤΑΛΟΓΟΣ — καθαρό, χωρίς ψωμί και «ΔΙΠΛΗ ΠΙΤΑ»
    /// μπροστά. Αν το προϊόν έχει διαγραφεί (ή η παραγγελία είναι παλιά, χωρίς ProductId), πέφτει πίσω
    /// στο όνομα που πουλήθηκε περισσότερο — κάτι είναι πάντα καλύτερο από κενή γραμμή.</summary>
    public static string ProductDisplayName(IEnumerable<SoldLine> lines)
    {
        var list = lines.ToList();
        var id = list[0].ProductId;
        if (id.Length > 0)
        {
            var product = MenuStore.Instance.Categories
                .SelectMany(c => c.Products)
                .FirstOrDefault(p => p.Id == id);
            if (product is not null)
                return product.Name;
        }
        return list.GroupBy(l => l.Name).OrderByDescending(g => g.Sum(l => l.Quantity)).First().Key;
    }

    /// <summary>Πωλήσεις ανά κατηγορία καταλόγου, φθίνουσα σειρά τζίρου.</summary>
    public static List<CategorySale> ByCategory(IEnumerable<CompletedOrder> orders)
    {
        // TryAdd και όχι ToDictionary: ένας διπλός κωδικός προϊόντος (κατάλογος από αλλού, χειροκίνητη
        // επέμβαση) θα έριχνε ολόκληρη την οθόνη Στατιστικών με «απρόσμενο σφάλμα».
        var categoryOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var category in MenuStore.Instance.Categories)
            foreach (var product in category.Products)
                categoryOf.TryAdd(product.Id, category.Name);

        return orders
            .SelectMany(o => o.Lines)
            .GroupBy(l => categoryOf.GetValueOrDefault(l.ProductId, "— ΕΚΤΟΣ ΚΑΤΑΛΟΓΟΥ —"))
            .Select(g => new CategorySale(
                g.Key,
                g.Sum(l => l.Quantity),
                g.Sum(l => l.Revenue),
                g.GroupBy(l => l.ProductId.Length > 0 ? l.ProductId : l.Name)
                    .Select(p => new ProductSale(ProductDisplayName(p), p.Sum(l => l.Quantity), p.Sum(l => l.Revenue)))
                    .OrderByDescending(p => p.Quantity)
                    .ToList()))
            .OrderByDescending(c => c.Revenue)
            .ToList();
    }
}
