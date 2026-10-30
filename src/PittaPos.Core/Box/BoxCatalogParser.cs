using System.Text.Json;
using System.Text.Json.Nodes;
using PittaPos.Core.Efood;

namespace PittaPos.Core.Box;

/// <summary>
/// Διαβάζει τον κατάλογο του καταστήματος όπως τον δίνει η Box μέσω της γέφυρας (<c>GET /till/catalog</c>:
/// <c>{ ok, products: [ … ], offers: [ … ] }</c> — ή σκέτη την απάντηση της Box, <c>{ payload: { products } }</c>). Μόνο
/// ανάγνωση — ο κατάλογος μένει όπως τον έχει η Box· από εδώ βγαίνει η λίστα της ΑΝΤΙΣΤΟΙΧΙΣΗΣ.
///
/// <para>Τα προϊόντα γίνονται <see cref="EfoodCatalogItem"/>, ώστε ΑΝΤΙΣΤΟΙΧΙΣΗ και ανάγνωση να είναι οι ίδιες με του
/// e-food. Κλειδί είναι το <c>friendlyId</c> (αυτό φέρνει κάθε παραγγελία). Τιμές η Box δεν δίνει εδώ — έρχονται με κάθε
/// παραγγελία — οπότε καμία πρόταση δεν γίνεται «αυτόματα με ίδια τιμή»: κάθε μία θέλει ένα «ΣΩΣΤΟ».</para>
/// </summary>
public static class BoxCatalogParser
{
    /// <exception cref="FormatException">Όταν η απάντηση δεν είναι κατάλογος της Box.</exception>
    public static IReadOnlyList<EfoodCatalogItem> Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new FormatException("Ο κατάλογος της Box δεν διαβάζεται: " + ex.Message, ex); }

        if ((root?["products"] ?? root?["payload"]?["products"]) is not JsonArray products)
            throw new FormatException("Η απάντηση δεν έχει κατάλογο της Box (products).");

        var items = new List<EfoodCatalogItem>();
        foreach (var product in products.OfType<JsonObject>())
        {
            var id = Text(product["friendlyId"]);
            var name = Text(product["name"]);
            if (id.Length == 0 || name.Length == 0)
                continue;
            var category = Text(product["category"]?["name"]);
            // «offers» = υπάρχει μόνο μέσα σε προσφορές — φαίνεται στην ΑΝΤΙΣΤΟΙΧΙΣΗ, για να μην ψάχνεται στο μενού της Box.
            if (Text(product["visibleTo"]) == "offers")
                category = category.Length > 0 ? category + " · μόνο σε προσφορές" : "μόνο σε προσφορές";
            var tiers = (product["selections"] as JsonArray ?? []).OfType<JsonObject>().Select(s => new EfoodCatalogTier(
                Text(s["friendlyId"]),
                Text(s["title"]),
                "",
                "",
                (s["choices"] as JsonArray ?? []).OfType<JsonObject>().Select(c => new EfoodCatalogOption(
                    Text(c["friendlyId"]), Text(c["friendlyId"]), Text(c["title"]), "", 0m, false)).ToList())).ToList();
            items.Add(new EfoodCatalogItem(id, id, name, category, 0m, true, tiers));
        }
        return items;
    }

    private static string Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? s) ? s?.Trim() ?? ""
        : node is JsonValue other ? other.ToJsonString().Trim('"')
        : "";
}
