using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PittaPos.Core.Efood;

/// <summary>Μία επιλογή μέσα σε ομάδα του καταλόγου του e-food (π.χ. «Διπλή ελληνική πίττα», «Τυρί gouda»).</summary>
/// <param name="Selected">Προεπιλεγμένη (π.χ. τα υλικά ενός burger, που ο πελάτης ξετσεκάρει).</param>
public sealed record EfoodCatalogOption(string Code, string Name, string PrintedName, decimal Price, bool Selected);

/// <summary>Ομάδα επιλογών ενός προϊόντος του e-food (π.χ. «Επιλέξτε πίττα», «Υλικά», «Προσθέστε extra»).</summary>
/// <param name="DependsOn">Ο κωδικός της ομάδας από την οποία εξαρτάται (π.χ. «Υλικά» ανοίγει μόνο με «ή επιλέξτε υλικά») — κενό αν καμία.</param>
public sealed record EfoodCatalogTier(string Code, string Name, string Type, string DependsOn, IReadOnlyList<EfoodCatalogOption> Options);

/// <summary>Ένα προϊόν όπως το έχει το κατάστημα στο e-food.</summary>
public sealed record EfoodCatalogItem(string Id, string Code, string Name, string Category, decimal Price, bool IsAvailable,
    IReadOnlyList<EfoodCatalogTier> Tiers);

/// <summary>
/// Διαβάζει τον κατάλογο του καταστήματος όπως τον δίνει το e-food (<c>GET /api/v1/shop/{vendorId}/catalog</c>:
/// <c>{ data: { menu: { categories: [ { name, items: [ … ] } ] } } }</c>). Μόνο ανάγνωση — ο κατάλογος του e-food
/// μένει όπως τον έχει το κατάστημα· από εδώ βγαίνει η λίστα της ΑΝΤΙΣΤΟΙΧΙΣΗΣ.
/// </summary>
public static class EfoodCatalogParser
{
    /// <exception cref="FormatException">Όταν η απάντηση δεν είναι κατάλογος του e-food.</exception>
    public static IReadOnlyList<EfoodCatalogItem> Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new FormatException("Ο κατάλογος του e-food δεν διαβάζεται: " + ex.Message, ex); }

        if (root?["data"]?["menu"]?["categories"] is not JsonArray categories)
            throw new FormatException("Η απάντηση του e-food δεν έχει κατάλογο (data.menu.categories).");

        var items = new List<EfoodCatalogItem>();
        foreach (var category in categories.OfType<JsonObject>())
        {
            var categoryName = Text(category["name"]);
            foreach (var item in (category["items"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var name = Text(item["name"]);
                if (name.Length == 0)
                    continue;
                var tiers = (item["tiers"] as JsonArray ?? []).OfType<JsonObject>().Select(t => new EfoodCatalogTier(
                    Text(t["code"]),
                    Text(t["name"]),
                    Text(t["type"]),
                    Text(t["dependency_code"]) is var dependsOn && dependsOn != "0" ? dependsOn : "",
                    (t["options"] as JsonArray ?? []).OfType<JsonObject>().Select(o => new EfoodCatalogOption(
                        Text(o["code"]),
                        Text(o["name"]),
                        Text(o["printed_name"]),
                        Money(o["price"]),
                        Flag(o["selected"]) || Flag(o["is_default"]))).ToList())).ToList();
                items.Add(new EfoodCatalogItem(Text(item["id"]), Text(item["code"]), name, categoryName, Money(item["price"]),
                    item["is_available"] is null || Flag(item["is_available"]), tiers));
            }
        }
        return items;
    }

    private static string Text(JsonNode? node)
    {
        if (node is not JsonValue value)
            return "";
        if (value.TryGetValue(out string? s))
            return s?.Trim() ?? "";
        if (value.TryGetValue(out long l))
            return l.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue(out double d))
            return d.ToString(CultureInfo.InvariantCulture);
        return "";
    }

    private static decimal Money(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out decimal m) ? m
        : node is JsonValue other && other.TryGetValue(out double d) ? (decimal)d
        : 0m;

    private static bool Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue(out bool b) && b;
}
