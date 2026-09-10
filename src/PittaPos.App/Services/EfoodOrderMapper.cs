using System.Globalization;
using System.Text.Json.Nodes;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>Μια παραγγελία e-food μεταφρασμένη σε ό,τι καταλαβαίνει το ταμείο.</summary>
/// <param name="Ref">Ο αριθμός που βλέπει το μαγαζί και το e-food: ο σύντομος κωδικός (short_code) ή, αν
/// λείπει, ο αριθμός παραγγελίας της πλατφόρμας.</param>
/// <param name="OwnDelivery">Τη φέρνει ΔΙΚΟΣ ΜΑΣ διανομέας (vendor_delivery) — μόνο τότε κρατάμε
/// τηλέφωνο και διεύθυνση, γιατί μόνο τότε τα χρειαζόμαστε.</param>
/// <param name="PlatformTotal">Το σύνολο που δήλωσε το e-food (price) — για έλεγχο, όχι για τον τζίρο.</param>
public sealed record EfoodImport(string Ref, string Who, string Phone, string Address, string Floor, string Notes,
    decimal Total, IReadOnlyList<SoldLine> Lines, bool OwnDelivery, decimal PlatformTotal);

/// <summary>
/// Μεταφράζει το JSON του e-food (βλ. «Integration with 3rd Party Systems (v2)») σε παραγγελία ταμείου.
///
/// <para><b>ΠΟΤΕ δεν πετάει εξαίρεση.</b> Ό,τι λείπει ή έχει άλλη μορφή απ' ό,τι περιμέναμε γίνεται κενό ή
/// προεπιλογή: το χειρότερο που επιτρέπεται να συμβεί είναι ένα δελτίο με λιγότερες λεπτομέρειες — ποτέ
/// μια παραγγελία που δεν μπήκε, γιατί το e-food δεν την ξαναστέλνει και ο πελάτης περιμένει.</para>
///
/// <para>ΣΗΜΕΙΑ ΠΡΟΣ ΕΠΙΒΕΒΑΙΩΣΗ ΣΤΟ SANDBOX (η τεκμηρίωση δεν τα λέει ρητά): αν η τιμή του προϊόντος
/// περιλαμβάνει ήδη τα έξτρα (materials) ή αν προστίθενται — εδώ προστίθενται· και αν τα προϊόντα μιας
/// προσφοράς (offers) εμφανίζονται ΚΑΙ στα products.</para>
/// </summary>
public static class EfoodOrderMapper
{
    public static EfoodImport Map(JsonObject order, Func<string, Product?> findProduct)
    {
        var customer = order["customer"] as JsonObject;
        var transport = order["transport_method"] as JsonObject;
        var type = Text(order["type"]).ToLowerInvariant();
        var provider = Text(transport?["delivery_provider"]).ToLowerInvariant();
        var payment = Text(order["payment_type"]).ToLowerInvariant();
        var platformTotal = Money(order["price"]) ?? 0m;

        var takeaway = type == "takeaway" || Text(transport?["key"]).ToLowerInvariant() == "takeaway";
        var ownDelivery = !takeaway && provider == "vendor_delivery";

        var shortCode = Text(order["short_code"]);
        var reference = shortCode.Length > 0 && shortCode != "null" ? shortCode : Text(order["id"]);
        var who = $"{Text(customer?["name"])} {Text(customer?["surname"])}".Trim();

        var lines = new List<SoldLine>();
        foreach (var node in order["products"] as JsonArray ?? [])
            if (node is JsonObject product)
                lines.Add(Line(product, findProduct));
        foreach (var node in order["offers"] as JsonArray ?? [])
            if (node is JsonObject offer)
                lines.Add(OfferLine(offer));

        var notes = new List<string>();
        if (ownDelivery)
            notes.Add(payment == "cash"
                ? "ΜΕΤΡΗΤΑ " + Order.FormatPrice(platformTotal) + " — εισπράττει ο διανομέας"
                : "ΠΛΗΡΩΜΕΝΗ ΜΕ ΚΑΡΤΑ");
        else if (takeaway)
            notes.Add(payment == "cash"
                ? "ΠΑΡΑΛΑΒΗ — πληρώνει μετρητά " + Order.FormatPrice(platformTotal)
                : "ΠΑΡΑΛΑΒΗ — πληρωμένη");
        else
            notes.Add("ΔΙΑΝΟΜΗ e-food");
        if ((order["extra_parameters"] as JsonArray ?? []).Any(p => Text(p) == "no-cutlery"))
            notes.Add("ΧΩΡΙΣ ΜΑΧΑΙΡΟΠΙΡΟΥΝΑ");
        // Τα σχόλια του πελάτη («χτύπα δυνατά») αφορούν όποιον παραδίδει — αν είναι ο rider του e-food,
        // τα έχει ήδη εκείνος στην εφαρμογή του.
        var customerNotes = Text(customer?["notes"]);
        if ((ownDelivery || takeaway) && customerNotes.Length > 0)
            notes.Add(customerNotes);

        return new EfoodImport(
            Ref: reference,
            Who: who,
            Phone: ownDelivery ? Text(customer?["telephone"]) : "",
            Address: ownDelivery ? AddressOf(customer) : "",
            Floor: ownDelivery ? FloorOf(customer) : "",
            Notes: string.Join(" · ", notes),
            Total: lines.Sum(l => l.Revenue),
            Lines: lines,
            OwnDelivery: ownDelivery,
            PlatformTotal: platformTotal);
    }

    private static SoldLine Line(JsonObject product, Func<string, Product?> findProduct)
    {
        var name = Text(product["name"]);
        if (name.Length == 0)
            name = "Προϊόν e-food";
        var quantity = Math.Max(1, Count(product["quantity"]));
        var price = Money(product["price"]) ?? 0m;
        // Τιμή προσφοράς, όταν υπάρχει και είναι όντως μικρότερη.
        if (Money(product["discount_price"]) is { } discounted && discounted > 0 && discounted < price)
            price = discounted;

        var details = new List<string>();
        var productNotes = Text(product["notes"]);
        if (productNotes.Length > 0)
            details.Add(productNotes);
        var extras = 0m;
        foreach (var node in product["materials"] as JsonArray ?? [])
        {
            if (node is not JsonObject material)
                continue;
            var materialName = Text(material["name"]);
            if (materialName.Length == 0)
                continue;
            var materialQuantity = Math.Max(1, Count(material["quantity"]));
            extras += (Money(material["price"]) ?? 0m) * materialQuantity;
            var materialNotes = Text(material["notes"]);
            details.Add("+ " + materialName + (materialQuantity > 1 ? " ×" + materialQuantity : "")
                        + (materialNotes.Length > 0 ? " (" + materialNotes + ")" : ""));
        }

        // Ίδιο προϊόν στον δικό μας κατάλογο: μετράει σωστά στα στατιστικά ανά κατηγορία και τυπώνεται με
        // το όνομα που ξέρει η κουζίνα. Αλλιώς μένει με το όνομα του e-food.
        var ours = findProduct(name);
        var revenue = Math.Round((price + extras) * quantity, 2, MidpointRounding.AwayFromZero);
        return new SoldLine(name, quantity, revenue, string.Join("\n", details),
            ProductId: ours?.Id ?? "", PrintName: ours?.PrintName ?? "");
    }

    private static SoldLine OfferLine(JsonObject offer)
    {
        var name = Text(offer["name"]);
        if (name.Length == 0)
            name = "Προσφορά e-food";
        var quantity = Math.Max(1, Count(offer["iteration"]));
        var price = Money(offer["discount_price"]) is { } discounted && discounted > 0
            ? discounted
            : Money(offer["price"]) ?? 0m;
        var details = new List<string>();
        foreach (var node in offer["products"] as JsonArray ?? [])
        {
            if (node is not JsonObject product)
                continue;
            var productName = Text(product["name"]);
            if (productName.Length == 0)
                continue;
            var productQuantity = Math.Max(1, Count(product["quantity"]));
            details.Add((productQuantity > 1 ? productQuantity + " × " : "") + productName);
            foreach (var m in product["materials"] as JsonArray ?? [])
                if (Text((m as JsonObject)?["name"]) is { Length: > 0 } materialName)
                    details.Add("  + " + materialName);
        }
        return new SoldLine(name, quantity, Math.Round(price * quantity, 2, MidpointRounding.AwayFromZero),
            string.Join("\n", details));
    }

    private static string AddressOf(JsonObject? customer)
    {
        var full = Text(customer?["address"]);
        if (full.Length > 0)
            return full;
        var street = $"{Text(customer?["street"])} {Text(customer?["street_number"])}".Trim();
        var area = Text(customer?["area"]);
        if (area.Length == 0)
            area = Text(customer?["neighborhood"]);
        return string.Join(", ", new[] { street, area, Text(customer?["postal_code"]) }.Where(p => p.Length > 0));
    }

    private static string FloorOf(JsonObject? customer)
    {
        var floor = Text(customer?["floor"]);
        var doorbell = Text(customer?["doorbell"]);
        return doorbell.Length == 0 ? floor
            : floor.Length == 0 ? "Κουδούνι: " + doorbell
            : floor + " · Κουδούνι: " + doorbell;
    }

    /// <summary>Κείμενο από οποιαδήποτε τιμή (string/αριθμός) — κενό για null ή ό,τι δεν είναι τιμή.</summary>
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

    /// <summary>Ποσό σε ευρώ — αριθμός ή κείμενο με τελεία. null όταν λείπει.</summary>
    private static decimal? Money(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        if (value.TryGetValue(out decimal m))
            return m;
        if (value.TryGetValue(out double d))
            return (decimal)d;
        if (value.TryGetValue(out string? s)
            && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static int Count(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue(out int i))
                return i;
            if (value.TryGetValue(out double d))
                return (int)Math.Round(d);
            if (value.TryGetValue(out string? s) && int.TryParse(s, out var parsed))
                return parsed;
        }
        return 0;
    }
}
