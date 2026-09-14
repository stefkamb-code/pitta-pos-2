using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PittaPos.Core.Models;

namespace PittaPos.Core.Efood;

/// <summary>
/// Μία γραμμή της ΑΝΤΙΣΤΟΙΧΙΣΗΣ: προϊόν του e-food → προϊόν του ταμείου, και με ποιο ψωμί. Αποθηκεύεται στο ταμείο·
/// ο κατάλογος του e-food δεν αλλάζει ποτέ.
/// </summary>
public sealed class EfoodMatch
{
    /// <summary>Ο κωδικός του προϊόντος στο e-food (π.χ. «IT_000000002449») — σταθερός, το κλειδί της γραμμής.</summary>
    public string EfoodCode { get; set; } = "";

    /// <summary>Ο αριθμός του προϊόντος στο e-food — εναλλακτικό κλειδί, αν η παραγγελία φέρνει αυτόν.</summary>
    public string EfoodId { get; set; } = "";

    public string EfoodName { get; set; } = "";
    public string EfoodCategory { get; set; } = "";

    /// <summary>Το προϊόν του ταμείου — κενό = «δεν υπάρχει στο ταμείο», οπότε τυπώνεται με το όνομα του e-food.</summary>
    public string ProductId { get; set; } = "";

    /// <summary>Το ψωμί που ΕΙΝΑΙ το προϊόν του e-food (π.χ. «Αραβική» για «Αραβική πίττα κοτόπουλο») — κενό = όποιο
    /// διαλέξει ο πελάτης, αλλιώς η προεπιλογή του ταμείου.</summary>
    public string Bread { get; set; } = "";

    /// <summary>Το είδε και το επιβεβαίωσε άνθρωπος («Σωστό»). Οι αυτόματες με ίδια τιμή δεν το χρειάζονται.</summary>
    public bool Confirmed { get; set; }
}

/// <summary>Αυτόματη πρόταση για ένα προϊόν του e-food.</summary>
/// <param name="SamePrice">Ίδια τιμή με την τιμή εφαρμογών του ταμείου — αλλιώς θέλει ματιά από άνθρωπο (π.χ. η
/// «Λαχανικών» του νηστίσιμου μενού δεν είναι η σαλάτα «Λαχανικών» του ταμείου).</param>
public sealed record EfoodSuggestion(Product Product, MenuCategory Category, string Bread, bool SamePrice);

/// <summary>
/// Προτείνει ποιο προϊόν του ταμείου είναι ένα προϊόν του e-food, με τους κανόνες που φάνηκαν στον πραγματικό
/// κατάλογο: ίδιο όνομα (χωρίς τόνους/κεφαλαία), ή όνομα με το ψωμί μπροστά — «Αραβική πίττα κοτόπουλο» = «Πίττα
/// κοτόπουλο» με Αραβική, «Σάντουιτς κοτόπουλο» = «Πίττα κοτόπουλο» με Ψωμί. Ό,τι δεν βγαίνει σίγουρα, το
/// αποφασίζει άνθρωπος στην οθόνη ΑΝΤΙΣΤΟΙΧΙΣΗΣ.
/// </summary>
public static class EfoodMatcher
{
    private static readonly (string Prefix, string Bread)[] BreadPrefixes =
    [
        ("αραβικη πιττα ", "Αραβική"),
        ("αραβικη πιτα ", "Αραβική"),
        ("σαντουιτσ ", "Ψωμί"),
    ];

    public static EfoodSuggestion? Suggest(EfoodCatalogItem item, IEfoodMenu menu)
    {
        var byName = new Dictionary<string, (Product Product, MenuCategory Category)>(StringComparer.Ordinal);
        foreach (var category in menu.Categories)
            foreach (var product in category.Products)
                byName.TryAdd(Normalize(product.Name), (product, category));
        return Suggest(item, menu, byName);
    }

    /// <summary>Για πολλά προϊόντα μαζί — το ευρετήριο ονομάτων φτιάχνεται μία φορά.</summary>
    public static IReadOnlyDictionary<string, EfoodSuggestion?> SuggestAll(IReadOnlyList<EfoodCatalogItem> items, IEfoodMenu menu)
    {
        var byName = new Dictionary<string, (Product Product, MenuCategory Category)>(StringComparer.Ordinal);
        foreach (var category in menu.Categories)
            foreach (var product in category.Products)
                byName.TryAdd(Normalize(product.Name), (product, category));
        var result = new Dictionary<string, EfoodSuggestion?>(StringComparer.Ordinal);
        foreach (var item in items)
            result[KeyOf(item)] = Suggest(item, menu, byName);
        return result;
    }

    /// <summary>Το κλειδί ενός προϊόντος του e-food: ο ΑΡΙΘΜΟΣ του — αυτόν φέρνει κάθε παραγγελία ως integrator_id
    /// (έτσι ήρθαν οι πρώτες πραγματικές, 14/9/2026· το product.id έρχεται «no-valid-code-found»).</summary>
    public static string KeyOf(EfoodCatalogItem item) => item.Id.Length > 0 ? item.Id : item.Code;

    private static EfoodSuggestion? Suggest(EfoodCatalogItem item, IEfoodMenu menu,
        Dictionary<string, (Product Product, MenuCategory Category)> byName)
    {
        var name = Normalize(item.Name);
        if (byName.TryGetValue(name, out var exact))
            return Make(item, menu, exact, "");

        foreach (var (prefix, bread) in BreadPrefixes)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var rest = name[prefix.Length..];
            foreach (var candidate in new[] { "πιττα " + rest, "πιτα " + rest, rest })
                if (byName.TryGetValue(candidate, out var found) && menu.HasBreadChoice(found.Category.Name))
                    return Make(item, menu, found, bread);
        }
        return null;
    }

    private static EfoodSuggestion Make(EfoodCatalogItem item, IEfoodMenu menu, (Product Product, MenuCategory Category) found, string bread)
    {
        var price = found.Product.DeliveryPrice ?? found.Product.Price;
        return new EfoodSuggestion(found.Product, found.Category, bread, Math.Abs(price - item.Price) < 0.005m);
    }

    /// <summary>Για σύγκριση ονομάτων: πεζά, χωρίς τόνους, «ς»→«σ», «&amp;»→«και», ένα κενό.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch == 'ς' ? 'σ' : ch);
        var plain = builder.ToString().Normalize(NormalizationForm.FormC).Replace('`', '\'').Replace("&", " και ");
        return Regex.Replace(plain, @"\s+", " ").Trim();
    }
}
