using System.Text.Json.Nodes;
using PittaPos.Core.Data;
using PittaPos.Core.Models;

namespace PittaPos.Core.Efood;

/// <summary>
/// Ο κατάλογος του ταμείου στη μορφή του e-food (<c>/catalog/ingest</c>: categories + products), με τους κωδικούς του
/// ταμείου (βλ. <see cref="EfoodCodes"/>). Ό,τι βλέπει ο ταμίας όταν ανοίγει ένα φαγητό, το βλέπει και ο πελάτης:
///
/// <list type="bullet">
/// <item>ΨΩΜΙ — μία επιλογή, προεπιλεγμένη η πρώτη (Ελληνική), όπως στον customizer.</item>
/// <item>ΧΩΡΙΣ — «Χωρίς κρεμμύδι», «Χωρίς ντομάτα»: ΑΠΟΕΠΙΛΕΓΜΕΝΑ κουτάκια για ό,τι βγαίνει. Επίτηδες όχι
///   «προεπιλεγμένα υλικά που ξετσεκάρεις»: έτσι η παραγγελία φέρνει ΜΟΝΟ ό,τι διάλεξε ο πελάτης, και ένα «Χωρίς
///   κρεμμύδι» δεν μπορεί ποτέ να διαβαστεί ανάποδα, όπως κι αν στέλνει το e-food τις προεπιλογές.</item>
/// <item>ΔΙΠΛΗ ΠΙΤΑ — με τη χρέωση της κατηγορίας.</item>
/// <item>ΕΞΤΡΑ — με τις τιμές τους, στη σειρά του προϊόντος.</item>
/// </list>
///
/// <para>Τιμή = η τιμή εφαρμογών του προϊόντος (αλλιώς η κανονική) — ίδια με όσα χρεώνει ο ταμίας σε e-food.
/// <b>Χωρίς «offers»</b>: το ingest σβήνει τις προσφορές μόνο όταν του σταλεί ρητά άδεια λίστα.</para>
/// </summary>
public static class EfoodCatalogBuilder
{
    public const string BreadTierName = "Ψωμί";
    public const string WithoutTierName = "Χωρίς";
    public const string WithoutPrefix = "Χωρίς ";
    public const string DoublePitaName = "Διπλή πίτα";
    public const string ExtrasTierName = "Έξτρα";

    /// <param name="Excluded">Κατηγορίες που ΔΕΝ είναι για το e-food (π.χ. ΠΡΟΣΩΠΙΚΟ) — βλ. MenuCategory.OnEfood.</param>
    /// <param name="Warnings">Ό,τι δεν στάλθηκε λόγω λάθους (π.χ. διπλός κωδικός προϊόντος) — για το log.</param>
    public sealed record Result(JsonObject Body, int Categories, int Products, IReadOnlyList<string> Excluded,
        IReadOnlyList<string> Warnings);

    public static Result Build(IEfoodMenu menu)
    {
        var categories = new JsonArray();
        var products = new JsonArray();
        var excluded = new List<string>();
        var warnings = new List<string>();
        var seenCategories = new HashSet<string>(StringComparer.Ordinal);
        var seenProducts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var category in menu.Categories)
        {
            if (category.Products.Count == 0)
                continue;
            if (!(category.OnEfood ?? MenuSeed.GuessOnEfood(category.Name)))
            {
                excluded.Add(category.Name);
                continue;
            }
            if (!seenCategories.Add(category.Id))
            {
                warnings.Add($"διπλός κωδικός κατηγορίας «{category.Id}» ({category.Name}) — δεν στάλθηκε");
                continue;
            }
            var vat = (category.VatKind ?? MenuSeed.GuessVatKind(category.Name)) == VatKind.Alcohol ? 24 : 13;
            var productOrder = 0;
            foreach (var product in category.Products)
            {
                if (string.IsNullOrWhiteSpace(product.Id) || string.IsNullOrWhiteSpace(product.Name) || !seenProducts.Add(product.Id))
                {
                    warnings.Add($"προϊόν «{product.Name}» ({product.Id}) χωρίς όνομα/κωδικό ή με διπλό κωδικό — δεν στάλθηκε");
                    continue;
                }
                var item = new JsonObject
                {
                    ["id"] = product.Id,
                    ["category"] = new JsonObject { ["id"] = category.Id },
                    ["name"] = product.Name.Trim(),
                    ["price"] = Money(product.DeliveryPrice ?? product.Price),
                    ["vat"] = vat,
                    ["order"] = ++productOrder,
                    ["is_available"] = true,
                    ["is_hidden"] = false,
                    ["max_item_count"] = 0,
                };
                if (!string.IsNullOrWhiteSpace(product.Description))
                    item["description"] = product.Description.Trim();
                if (Tiers(menu, category, product) is { Count: > 0 } tiers)
                    item["tiers"] = tiers;
                products.Add(item);
            }
            if (productOrder > 0)
                categories.Add(new JsonObject
                {
                    ["id"] = category.Id,
                    ["name"] = category.Name.Trim(),
                    ["order"] = categories.Count + 1,
                    ["is_hidden"] = false,
                });
        }

        return new Result(new JsonObject { ["categories"] = categories, ["products"] = products },
            categories.Count, products.Count, excluded, warnings);
    }

    /// <summary>Οι ομάδες επιλογών ενός προϊόντος — μόνο για όσα ανοίγουν customizer στο ταμείο.</summary>
    private static JsonArray Tiers(IEfoodMenu menu, MenuCategory category, Product product)
    {
        var tiers = new JsonArray();
        if (!menu.OpensIngredients(product))
            return tiers;

        if (menu.HasBreadChoice(category.Name))
            tiers.Add(Tier(product.Id, EfoodCodes.Bread, BreadTierName, "radio", tiers.Count + 1, 1,
                MenuSeed.BreadOptions.Select((bread, i) => Option(product.Id, EfoodCodes.Bread, bread, bread, 0m, i == 0, i + 1))));

        var ingredients = menu.IngredientsFor(product).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        if (ingredients.Count > 0)
            tiers.Add(Tier(product.Id, EfoodCodes.Ingredient, WithoutTierName, "checkbox", tiers.Count + 1, ingredients.Count,
                ingredients.Select((ingredient, i) => Option(product.Id, EfoodCodes.Ingredient, ingredient,
                    WithoutPrefix + Lowercase(ingredient.Trim()), 0m, false, i + 1))));

        if (menu.SupportsDoublePita(category.Name))
            tiers.Add(Tier(product.Id, EfoodCodes.DoublePita, DoublePitaName, "checkbox", tiers.Count + 1, 1,
                [Option(product.Id, EfoodCodes.DoublePita, DoublePitaName, DoublePitaName, menu.DoublePitaPriceFor(category.Name), false, 1)]));

        var extras = EfoodMenuRules.ExtrasFor(product, menu.Extras);
        if (extras.Count > 0)
            tiers.Add(Tier(product.Id, EfoodCodes.Extra, ExtrasTierName, "checkbox", tiers.Count + 1, extras.Count,
                extras.Select((extra, i) => Option(product.Id, EfoodCodes.Extra, extra.Name, extra.Name.Trim(), extra.Price, false, i + 1))));

        return tiers;
    }

    private static JsonObject Tier(string productId, string kind, string name, string type, int order, int maxSelections,
        IEnumerable<JsonObject> options) => new()
    {
        ["id"] = EfoodCodes.Tier(productId, kind),
        ["name"] = name,
        ["type"] = type,
        ["order"] = order,
        ["free_options"] = 0,
        ["maximum_selections"] = maxSelections,
        ["options"] = new JsonArray(options.Cast<JsonNode?>().ToArray()),
    };

    /// <param name="codeName">Το όνομα από το οποίο βγαίνει ο κωδικός (το υλικό σκέτο, όχι «Χωρίς …»).</param>
    private static JsonObject Option(string productId, string kind, string codeName, string label, decimal price,
        bool selected, int order) => new()
    {
        ["id"] = EfoodCodes.Option(productId, kind, codeName),
        ["name"] = label,
        ["price"] = Money(price),
        ["selected"] = selected,
        ["is_available"] = true,
        ["order"] = order,
    };

    private static decimal Money(decimal amount) => decimal.Round(Math.Max(0m, amount), 2, MidpointRounding.AwayFromZero);

    /// <summary>«Κρεμμύδι» → «κρεμμύδι», για να διαβάζεται «Χωρίς κρεμμύδι».</summary>
    internal static string Lowercase(string text) => text.Length > 0 ? char.ToLower(text[0]) + text[1..] : text;
}
