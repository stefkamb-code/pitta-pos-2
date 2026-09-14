using System.Globalization;
using System.Text.Json.Nodes;
using PittaPos.Core.Data;
using PittaPos.Core.Models;

namespace PittaPos.Core.Efood;

/// <summary>Μία γραμμή παραγγελίας e-food όπως θα την περνούσε ο ταμίας — ίδια πεδία με το SoldLine του ταμείου.</summary>
public sealed record EfoodLine(string Name, int Quantity, decimal Revenue, string Details = "", string ProductId = "",
    LineCustomization? Customization = null, string PrintName = "");

/// <summary>Μια παραγγελία e-food μεταφρασμένη σε ό,τι καταλαβαίνει το ταμείο.</summary>
/// <param name="Ref">Ο αριθμός που βλέπει το μαγαζί και το e-food: ο σύντομος κωδικός (short_code) ή, αν λείπει, ο
/// αριθμός παραγγελίας της πλατφόρμας.</param>
/// <param name="OwnDelivery">Τη φέρνει ΔΙΚΟΣ ΜΑΣ διανομέας (vendor_delivery) — μόνο τότε κρατάμε τηλέφωνο και
/// διεύθυνση, γιατί μόνο τότε τα χρειαζόμαστε.</param>
/// <param name="PlatformTotal">Το σύνολο που δήλωσε το e-food (price) — για έλεγχο, όχι για τον τζίρο.</param>
/// <param name="Unmatched">Ό,τι δεν βρέθηκε στον κατάλογο του ταμείου (προϊόντα ή επιλογές) — για το log.</param>
public sealed record EfoodImport(string Ref, string Who, string Phone, string Address, string Floor, string Notes,
    decimal Total, IReadOnlyList<EfoodLine> Lines, bool OwnDelivery, decimal PlatformTotal, IReadOnlyList<string> Unmatched);

/// <summary>
/// Μεταφράζει το JSON του e-food (βλ. «Integration with 3rd Party Systems (v2)») σε παραγγελία ταμείου.
///
/// <para><b>Ο κατάλογος του e-food μένει όπως τον έχει το κατάστημα</b> — δεν τον αλλάζουμε ποτέ. Κάθε προϊόν του
/// e-food βρίσκεται στον κατάλογο του ταμείου (σήμερα με το όνομα· η ΑΝΤΙΣΤΟΙΧΙΣΗ χτίζεται πάνω στον πραγματικό
/// κατάλογο του e-food) και η γραμμή βγαίνει ΑΚΡΙΒΩΣ όπως θα την έβγαζε ο customizer του ταμείου — «ΑΡ. Γύρος»,
/// «χωρίς: κρεμμύδι», «+ Μπέικον», με το όνομα εκτύπωσης του ταμείου — και μετράει στο σωστό προϊόν στα στατιστικά
/// και στον ΦΠΑ. Τίποτα από όσα έχει ρυθμίσει το μαγαζί δεν αλλάζει.</para>
///
/// <para><b>ΠΟΤΕ δεν πετάει εξαίρεση, ΠΟΤΕ δεν χάνει κάτι.</b> Ό,τι δεν αναγνωρίζεται τυπώνεται με το όνομα που
/// ήρθε: το χειρότερο που επιτρέπεται είναι ένα δελτίο με ονόματα του e-food — ποτέ μια παραγγελία που δεν μπήκε,
/// γιατί το e-food δεν την ξαναστέλνει και ο πελάτης περιμένει.</para>
///
/// <para>ΠΡΟΣ ΕΠΙΒΕΒΑΙΩΣΗ ΣΤΟ SANDBOX (η τεκμηρίωση δεν τα λέει ρητά): αν η τιμή του προϊόντος περιλαμβάνει ήδη
/// τις επιλογές (materials) — εδώ προστίθενται· πώς ονομάζει το e-food τις αφαιρέσεις υλικών· και αν τα προϊόντα
/// μιας προσφοράς εμφανίζονται ΚΑΙ στα products.</para>
/// </summary>
public static class EfoodOrderReader
{
    /// <summary>Πώς γράφεται μια αφαίρεση υλικού: «Χωρίς κρεμμύδι».</summary>
    private const string WithoutPrefix = "Χωρίς ";

    private const string DoublePitaName = "Διπλή πίτα";

    public static EfoodImport Map(JsonObject order, IEfoodMenu menu)
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

        var index = new CatalogIndex(menu);
        var unmatched = new List<string>();
        var lines = new List<EfoodLine>();
        foreach (var node in order["products"] as JsonArray ?? [])
            if (node is JsonObject product)
                lines.Add(Line(product, menu, index, unmatched));
        foreach (var node in order["offers"] as JsonArray ?? [])
            if (node is JsonObject offer)
                lines.Add(OfferLine(offer, menu, index, unmatched));

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
        // Τα σχόλια του πελάτη («χτύπα δυνατά») αφορούν όποιον παραδίδει — αν είναι ο rider του e-food, τα έχει
        // ήδη εκείνος στην εφαρμογή του.
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
            PlatformTotal: platformTotal,
            Unmatched: unmatched);
    }

    private static EfoodLine Line(JsonObject item, IEfoodMenu menu, CatalogIndex index, List<string> unmatched)
    {
        var efoodName = Text(item["name"]);
        if (efoodName.Length == 0)
            efoodName = "Προϊόν e-food";
        var quantity = Math.Max(1, Count(item["quantity"]));
        var price = Money(item["price"]) ?? 0m;
        // Τιμή προσφοράς, όταν υπάρχει και είναι όντως μικρότερη.
        if (Money(item["discount_price"]) is { } discounted && discounted > 0 && discounted < price)
            price = discounted;
        var note = Text(item["notes"]);
        var materials = (item["materials"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var materialsMoney = materials.Sum(m => (Money(m["price"]) ?? 0m) * Math.Max(1, Count(m["quantity"])));
        var revenue = Math.Round((price + materialsMoney) * quantity, 2, MidpointRounding.AwayFromZero);

        if (index.Find(Text(item["integrator_id"]), efoodName) is not { } found)
        {
            unmatched.Add(efoodName);
            var plain = new List<string>();
            if (note.Length > 0)
                plain.Add(note);
            plain.AddRange(materials.Select(Describe).Where(d => d.Length > 0));
            return new EfoodLine(efoodName, quantity, revenue, string.Join("\n", plain));
        }

        var (product, category) = found;
        var customization = new LineCustomization { Bread = MenuSeed.BreadOptions[0], Note = note };
        var foreign = new List<string>();
        foreach (var material in materials)
        {
            if (!Apply(material, product, category, menu, customization))
            {
                // Κάτι που δεν ξέρει το ταμείο: τυπώνεται με το όνομα που ήρθε — ποτέ δεν χάνεται.
                unmatched.Add(efoodName + " → " + Text(material["name"]));
                if (Describe(material) is { Length: > 0 } text)
                    foreign.Add(text);
            }
        }

        var opens = menu.OpensIngredients(product);
        var composed = Compose(menu, category, product, customization);
        var details = string.Join("\n", new[] { composed.Details, string.Join("\n", foreign) }.Where(s => s.Length > 0));
        return new EfoodLine(composed.Name, quantity, revenue, details, product.Id, opens ? customization : null, composed.PrintName);
    }

    /// <summary>Μία επιλογή του e-food πάνω στη γραμμή, με το όνομα: ψωμί, «Χωρίς …», διπλή πίτα ή έξτρα του ταμείου.</summary>
    /// <returns>false αν δεν αναγνωρίστηκε.</returns>
    private static bool Apply(JsonObject material, Product product, MenuCategory category, IEfoodMenu menu, LineCustomization c)
    {
        var name = Text(material["name"]);
        if (name.Length == 0 || !menu.OpensIngredients(product))
            return false;

        if (menu.HasBreadChoice(category.Name) && Match(MenuSeed.BreadOptions, name) is { } bread)
        {
            c.Bread = bread;
            return true;
        }
        if (name.StartsWith(WithoutPrefix, StringComparison.CurrentCultureIgnoreCase)
            && Match(menu.IngredientsFor(product), name[WithoutPrefix.Length..]) is { } ingredient)
        {
            if (!c.Removed.Contains(ingredient))
                c.Removed.Add(ingredient);
            return true;
        }
        if (menu.SupportsDoublePita(category.Name) && string.Equals(name, DoublePitaName, StringComparison.CurrentCultureIgnoreCase))
        {
            c.DoublePita = true;
            return true;
        }
        if (Match(menu.Extras.Select(e => e.Name).ToList(), name) is { } extra)
        {
            c.Extras[extra] = c.Extras.GetValueOrDefault(extra) + Math.Max(1, Count(material["quantity"]));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Όνομα γραμμής, όνομα για το χαρτί και λεπτομέρειες — ΑΚΡΙΒΩΣ όπως τα συνθέτει ο customizer του ταμείου
    /// (CustomizerViewModel.Add): το ψωμί χωνεύεται στο όνομα («ΑΡ. Γύρος») ή πάει σε δική του γραμμή (ΜΕΡΙΔΕΣ), η
    /// διπλή πίτα μπαίνει μπροστά, και από κάτω σημείωση, «χωρίς:/μόνο με:/σκέτο» και «+ έξτρα».
    /// </summary>
    private static (string Name, string PrintName, string Details) Compose(IEfoodMenu menu, MenuCategory category, Product product,
        LineCustomization c)
    {
        var opens = menu.OpensIngredients(product);
        var hasBread = opens && menu.HasBreadChoice(category.Name);
        var doublePita = opens && c.DoublePita && menu.SupportsDoublePita(category.Name);
        var fuse = hasBread && menu.FuseBreadIntoName(category.Name);

        string ComposeName(string baseName) => doublePita
            ? MenuSeed.ComposeDoublePitaName(baseName, category.Name, c.Bread)
            : fuse ? MenuSeed.ComposeCustomizedName(baseName, c.Bread) : baseName;

        var breadLine = hasBread && !fuse && !doublePita ? c.Bread : "";
        var mods = new List<string>();
        if (opens)
        {
            mods.AddRange(MenuSeed.DescribeRemovedIngredients(c.Removed, menu.IngredientsFor(product).ToList()));
            var extrasOrder = EfoodMenuRules.ExtrasFor(product, menu.Extras).Select(e => e.Name).ToList();
            foreach (var (extra, quantity) in c.Extras.Where(kv => kv.Value > 0)
                         .OrderBy(kv => extrasOrder.IndexOf(kv.Key) is var i && i >= 0 ? i : int.MaxValue))
                mods.Add("+ " + extra + (quantity > 1 ? " ×" + quantity : ""));
        }
        var details = string.Join("\n", new[] { breadLine, c.Note, string.Join("\n", mods) }.Where(s => s.Length > 0));
        return (ComposeName(product.Name), ComposeName(product.NameForPrint), details);
    }

    private static EfoodLine OfferLine(JsonObject offer, IEfoodMenu menu, CatalogIndex index, List<string> unmatched)
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
            if (node is not JsonObject item || Text(item["name"]).Length == 0)
                continue;
            // Ίδια ανάγνωση με τα κανονικά προϊόντα — μόνο για το κείμενο· ο τζίρος είναι η τιμή της προσφοράς.
            var line = Line(item, menu, index, unmatched);
            details.Add((line.Quantity > 1 ? line.Quantity + " × " : "") + line.Name);
            details.AddRange(line.Details.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(d => "  " + d));
        }
        return new EfoodLine(name, quantity, Math.Round(price * quantity, 2, MidpointRounding.AwayFromZero),
            string.Join("\n", details));
    }

    /// <summary>Το όνομα του ταμείου που ταιριάζει (χωρίς διάκριση πεζών/κεφαλαίων), αλλιώς null.</summary>
    private static string? Match(IReadOnlyList<string> candidates, string name)
    {
        var wanted = name.Trim();
        return candidates.FirstOrDefault(n => string.Equals(n.Trim(), wanted, StringComparison.CurrentCultureIgnoreCase));
    }

    private static string Describe(JsonObject material)
    {
        var name = Text(material["name"]);
        if (name.Length == 0)
            return "";
        var quantity = Math.Max(1, Count(material["quantity"]));
        var notes = Text(material["notes"]);
        var prefix = name.StartsWith(WithoutPrefix, StringComparison.CurrentCultureIgnoreCase) ? "" : "+ ";
        return prefix + name + (quantity > 1 ? " ×" + quantity : "") + (notes.Length > 0 ? " (" + notes + ")" : "");
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

    /// <summary>Προϊόντα του καταλόγου με κωδικό και με όνομα. Διπλός κωδικός ή όνομα: μετράει το πρώτο, όπως και στο
    /// κινητό του σερβιτόρου.</summary>
    private sealed class CatalogIndex
    {
        private readonly Dictionary<string, (Product, MenuCategory)> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (Product, MenuCategory)> _byName = new(StringComparer.CurrentCultureIgnoreCase);

        public CatalogIndex(IEfoodMenu menu)
        {
            foreach (var category in menu.Categories)
                foreach (var product in category.Products)
                {
                    _byId.TryAdd(product.Id, (product, category));
                    _byName.TryAdd(product.Name.Trim(), (product, category));
                }
        }

        public (Product Product, MenuCategory Category)? Find(string code, string name) =>
            code.Length > 0 && _byId.TryGetValue(code, out var byId) ? byId
            : _byName.TryGetValue(name.Trim(), out var byName) ? byName
            : null;
    }
}
