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
/// <param name="PlatformTotal">Το σύνολο που δήλωσε το e-food (price, μαζί με μεταφορικά) — για έλεγχο, όχι για τον τζίρο.</param>
/// <param name="Unmatched">Ό,τι δεν βρέθηκε στον κατάλογο του ταμείου — για το log.</param>
public sealed record EfoodImport(string Ref, string Who, string Phone, string Address, string Floor, string Notes,
    decimal Total, IReadOnlyList<EfoodLine> Lines, bool OwnDelivery, decimal PlatformTotal, IReadOnlyList<string> Unmatched);

/// <summary>
/// Μεταφράζει μια παραγγελία του e-food σε παραγγελία ταμείου — πάνω σε ό,τι έδειξαν οι πρώτες πραγματικές (sandbox,
/// 14/9/2026), όχι μόνο στην τεκμηρίωση:
///
/// <list type="bullet">
/// <item>Κάθε προϊόν και κάθε επιλογή φέρνει τον αριθμό του καταλόγου του e-food ως <c>integrator_id</c>· με την
///   ΑΝΤΙΣΤΟΙΧΙΣΗ (<see cref="EfoodContext"/>) γίνεται προϊόν του ταμείου με ψωμί, και η γραμμή βγαίνει με το όνομα και το
///   όνομα εκτύπωσης του ταμείου, όπως του ταμία («ΑΡ. Κοτόπουλο», «ΕΛ. ΔΙΠΛΗ ΠΙΤΑ χοιρινό»).</item>
/// <item>Η τιμή του προϊόντος ΠΕΡΙΕΧΕΙ ήδη τις επιλογές (διπλή πίτα + gouda → 9,10, οι επιλογές με 0). Αν ποτέ έρθουν
///   τιμές και στις επιλογές, κρίνεται με το σύνολο του e-food αν προστίθενται (βλ. <see cref="MaterialsAreExtra"/>).</item>
/// <item>«Απ' όλα» = όπως είναι· «Μόνο με» + υλικά = «μόνο με: …»· σε προεπιλεγμένα υλικά (burger) το e-food στέλνει όσα
///   ΕΜΕΙΝΑΝ, οπότε όσα λείπουν = «χωρίς: …»· «Προσθέστε Χ» = «+ Χ»· κάθε άλλη επιλογή (σάλτσα, τυρί) τυπώνεται όπως ήρθε.</item>
/// </list>
///
/// <para><b>ΠΟΤΕ δεν πετάει εξαίρεση, ΠΟΤΕ δεν χάνει κάτι.</b> Ό,τι δεν αναγνωρίζεται τυπώνεται με το όνομα που ήρθε: το
/// χειρότερο που επιτρέπεται είναι ένα δελτίο με ονόματα του e-food — ποτέ μια παραγγελία που δεν μπήκε.</para>
/// </summary>
public static class EfoodOrderReader
{
    private const string AddPrefix = "Προσθέστε ";
    private const string WithoutPrefix = "Χωρίς ";

    public static EfoodImport Map(JsonObject order, IEfoodMenu menu) => Map(order, menu, EfoodContext.Empty);

    public static EfoodImport Map(JsonObject order, IEfoodMenu menu, EfoodContext context)
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

        var products = (order["products"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var offers = (order["offers"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var addMaterials = MaterialsAreExtra(order, products, offers);
        var index = new CatalogIndex(menu);
        var unmatched = new List<string>();
        var lines = new List<EfoodLine>();
        foreach (var product in products)
            lines.Add(Line(product, menu, context, index, unmatched, addMaterials));
        foreach (var offer in offers)
            lines.Add(OfferLine(offer, menu, context, index, unmatched));

        // Κουπόνι / Πεινιάτα του καταστήματος: τζίρος = ό,τι μένει μετά την έκπτωση, μοιρασμένη αναλογικά στις γραμμές
        // ώστε και οι κατηγορίες να βγαίνουν με το καθαρό (απόφαση χρήστη 24/9).
        var discounts = VendorDiscounts(order).ToList();
        var gross = lines.Sum(l => l.Revenue);
        var discount = Math.Min(gross, discounts.Sum(d => d.Amount));
        if (discount > 0)
            lines = Discounted(lines, gross, discount);

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
        foreach (var (joker, amount) in discounts)
            notes.Add((joker ? "ΕΚΠΤΩΣΗ ΠΕΙΝΙΑΤΑΣ −" : "ΕΚΠΤΩΣΗ ΚΟΥΠΟΝΙΟΥ −") + Order.FormatPrice(amount));
        // Το φιλοδώρημα ΔΕΝ είναι μέσα στο price (#16, 24/9: 17,10 + 0,50 = 17,60 με tip 1) — είναι του διανομέα μας.
        if ((ownDelivery || takeaway) && Money(order["tip"]) is > 0m and var tip)
            notes.Add("ΦΙΛΟΔΩΡΗΜΑ " + Order.FormatPrice(tip));
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

    /// <summary>Πώς βγαίνει στο χαρτί ένα προϊόν του e-food με αυτή την αντιστοίχιση, χωρίς επιλογές — η προεπισκόπηση της
    /// ΑΝΤΙΣΤΟΙΧΙΣΗΣ. Περνάει από τον ίδιο δρόμο με τις παραγγελίες, ώστε ό,τι δείχνει η οθόνη να είναι ό,τι τυπώνεται.</summary>
    public static string Preview(EfoodCatalogItem item, EfoodMatch? match, IEfoodMenu menu)
    {
        var product = new JsonObject
        {
            ["name"] = item.Name, ["price"] = item.Price, ["quantity"] = 1, ["integrator_id"] = item.Id, ["materials"] = new JsonArray(),
        };
        var order = new JsonObject { ["products"] = new JsonArray(product) };
        // Χωρίς τις ομάδες επιλογών: αλλιώς τα προεπιλεγμένα υλικά ενός burger θα έβγαιναν όλα «χωρίς».
        var matches = match is null ? new List<EfoodMatch>() : [match];
        var line = Map(order, menu, new EfoodContext([item with { Tiers = [] }], matches)).Lines[0];
        var name = line.PrintName.Length > 0 ? line.PrintName : line.Name;
        return line.Details.Length == 0 ? name : name + " · " + line.Details.Replace("\n", " · ");
    }

    private static EfoodLine Line(JsonObject item, IEfoodMenu menu, EfoodContext context, CatalogIndex index,
        List<string> unmatched, bool addMaterials)
    {
        var efoodName = Text(item["name"]);
        if (efoodName.Length == 0)
            efoodName = "Προϊόν e-food";
        var quantity = Quantity(item);
        var unit = UnitPrice(item) + (addMaterials ? MaterialsMoney(item) : 0m);
        var revenue = Math.Round(unit * quantity, 2, MidpointRounding.AwayFromZero);
        var note = Text(item["notes"]);
        var efoodId = Text(item["integrator_id"]);
        var materials = (item["materials"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var reading = ReadOptions(materials, context.Item(efoodId), context);

        (Product Product, MenuCategory Category)? found;
        var mappedBread = "";
        if (context.Match(efoodId) is { } match)
        {
            // Ρητή αντιστοίχιση — ακόμα και «δεν υπάρχει στο ταμείο» (κενό προϊόν) την ακολουθούμε, χωρίς μαντεψιές.
            found = match.ProductId.Length > 0 ? index.ById(match.ProductId) : null;
            mappedBread = match.Bread;
        }
        else if (context.Item(efoodId) is null)
            found = index.Find(efoodId, efoodName);
        else
            // Το e-food το ξέρει αλλά δεν έχει αντιστοιχιστεί (π.χ. ίδιο όνομα με άλλη τιμή): με το όνομα του e-food
            // μέχρι να το δει άνθρωπος στην ΑΝΤΙΣΤΟΙΧΙΣΗ — όχι μαντεψιά με το όνομα.
            found = null;

        if (found is not { } ours)
        {
            unmatched.Add(efoodName);
            var plain = new List<string>();
            if (reading.DoubleText.Length > 0)
                plain.Add(reading.DoubleText);
            else if (reading.BreadText.Length > 0)
                plain.Add(reading.BreadText);
            if (note.Length > 0)
                plain.Add(note);
            plain.AddRange(reading.Lines());
            return new EfoodLine(efoodName, quantity, revenue, string.Join("\n", plain));
        }

        var (product, category) = ours;
        var opens = menu.OpensIngredients(product);
        var customization = new LineCustomization
        {
            Bread = reading.Bread ?? (mappedBread.Length > 0 ? mappedBread : MenuSeed.BreadOptions[0]),
            DoublePita = reading.DoublePita,
            Note = note,
        };
        // Δομημένα ό,τι ταιριάζει με τον κατάλογο του ταμείου — για «ΜΙΑ ΑΠΟ ΤΑ ΙΔΙΑ» και στατιστικά· το χαρτί το λέει το κείμενο.
        foreach (var removed in reading.Without)
            if (Pick(menu.IngredientsFor(product), removed) is { } ingredient && !customization.Removed.Contains(ingredient))
                customization.Removed.Add(ingredient);
        var extrasNames = menu.Extras.Select(e => e.Name).ToList();
        foreach (var extra in reading.Extras)
            if (Pick(extrasNames, extra) is { } ourExtra)
                customization.Extras[ourExtra] = customization.Extras.GetValueOrDefault(ourExtra) + 1;

        var hasBread = opens && menu.HasBreadChoice(category.Name);
        var doublePita = opens && customization.DoublePita && menu.SupportsDoublePita(category.Name);
        var fuse = hasBread && menu.FuseBreadIntoName(category.Name);
        string Compose(string baseName) => doublePita
            ? MenuSeed.ComposeDoublePitaName(baseName, category.Name, customization.Bread)
            : fuse ? MenuSeed.ComposeCustomizedName(baseName, customization.Bread) : baseName;

        var details = new List<string>();
        if (hasBread && !fuse && !doublePita)
            details.Add(customization.Bread);
        else if (!hasBread && reading.BreadText.Length > 0)
            details.Add(reading.BreadText);
        if (reading.DoublePita && !doublePita)
            details.Add(reading.DoubleText);
        if (note.Length > 0)
            details.Add(note);
        details.AddRange(reading.Lines());
        if (context.Item(efoodId) is null)
            unmatched.AddRange(reading.Unknown.Select(u => efoodName + " → " + u));

        return new EfoodLine(Compose(product.Name), quantity, revenue, string.Join("\n", details), product.Id,
            opens ? customization : null, Compose(product.NameForPrint));
    }

    /// <summary>Τι σημαίνουν οι επιλογές (materials) ενός προϊόντος — με τον κατάλογο του e-food αν τον έχουμε, αλλιώς με τα ονόματα.</summary>
    private static OptionReading ReadOptions(List<JsonObject> materials, EfoodCatalogItem? item, EfoodContext context)
    {
        var reading = new OptionReading();
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var material in materials)
        {
            var name = Text(material["name"]);
            if (name.Length == 0)
                continue;
            var id = Text(material["integrator_id"]);
            present.Add(id);
            var quantity = Math.Max(1, Count(material["quantity"]));
            var label = quantity > 1 ? name + " ×" + quantity : name;
            var info = context.Option(id);
            var tier = info?.Tier;
            var normalized = EfoodMatcher.Normalize(name);
            var tierName = tier is null ? "" : EfoodMatcher.Normalize(tier.Name);

            // Ψωμί / διπλή πίτα («Επιλέξτε πίττα»: Ελληνική, Αραβική, Διπλή ελληνική).
            if (tierName.Contains("πιττα") || tierName.Contains("πιτα") || (tier is null && IsBreadName(normalized)))
            {
                if (normalized.Contains("διπλ"))
                {
                    reading.DoublePita = true;
                    reading.DoubleText = name;
                }
                if (BreadOf(normalized) is { } bread)
                {
                    reading.Bread = bread;
                    reading.BreadText = name;
                }
                else if (!normalized.Contains("διπλ"))
                    reading.Other.Add(label);
                continue;
            }
            // «Απ' όλα (…)» = όπως είναι — τίποτα στο χαρτί.
            if (normalized.StartsWith("απ' ολα", StringComparison.Ordinal) || normalized.StartsWith("απ ολα", StringComparison.Ordinal))
                continue;
            if (normalized is "μονο με" or "η επιλεξτε υλικα")
            {
                reading.OnlyWithMode = true;
                continue;
            }
            if (tier is not null && tier.Type == "checkbox")
            {
                if (tier.DependsOn.Length > 0)
                    reading.OnlyWith.Add(name);          // τα υλικά του «Μόνο με»
                else if (!info!.Value.Option.Selected)
                    reading.Extras.Add(StripAdd(label));  // προσθήκη (τα προεπιλεγμένα που έμειναν δεν γράφονται)
                continue;
            }
            if (tier is not null)
            {
                reading.Other.Add(label);                 // σάλτσα, τυρί, «Σβήσιμο σε μπύρα» — όπως ήρθε
                continue;
            }
            if (name.StartsWith(AddPrefix, StringComparison.CurrentCultureIgnoreCase))
                reading.Extras.Add(StripAdd(label));
            else if (name.StartsWith(WithoutPrefix, StringComparison.CurrentCultureIgnoreCase))
                reading.Without.Add(name[WithoutPrefix.Length..]);
            else
                reading.Unknown.Add(label);
        }
        // Χωρίς κατάλογο: μετά από «Μόνο με», ό,τι άγνωστο ήρθε είναι τα υλικά του.
        if (reading.OnlyWithMode && reading.Unknown.Count > 0)
        {
            reading.OnlyWith.AddRange(reading.Unknown);
            reading.Unknown.Clear();
        }
        // Προεπιλεγμένα υλικά (π.χ. burger): το e-food στέλνει όσα ΕΜΕΙΝΑΝ — όσα λείπουν τα έβγαλε ο πελάτης.
        if (item is not null)
            foreach (var tier in item.Tiers.Where(t => t.Type == "checkbox" && t.DependsOn.Length == 0))
                foreach (var option in tier.Options.Where(o => o.Selected && !present.Contains(o.Id)))
                    reading.Without.Add(option.PrintedName.Length > 0 ? option.PrintedName : option.Name);
        return reading;
    }

    private sealed class OptionReading
    {
        public string? Bread;
        public string BreadText = "";
        public bool DoublePita;
        public string DoubleText = "";
        public bool OnlyWithMode;
        public readonly List<string> OnlyWith = [];
        public readonly List<string> Without = [];
        public readonly List<string> Extras = [];
        public readonly List<string> Other = [];
        public readonly List<string> Unknown = [];

        /// <summary>Οι γραμμές κάτω από το όνομα, με το ύφος του ταμία: «μόνο με:» / «χωρίς:» σε δική τους γραμμή, ένα υλικό
        /// ανά γραμμή, «+ extra».</summary>
        public IEnumerable<string> Lines()
        {
            if (OnlyWithMode)
            {
                if (OnlyWith.Count == 0)
                    yield return "σκέτο";
                else
                {
                    yield return "μόνο με:";
                    foreach (var name in OnlyWith)
                        yield return Lower(name);
                }
            }
            if (Without.Count > 0)
            {
                yield return "χωρίς:";
                foreach (var name in Without)
                    yield return Lower(name);
            }
            foreach (var extra in Extras)
                yield return "+ " + extra;
            foreach (var other in Other)
                yield return other;
            foreach (var unknown in Unknown)
                yield return unknown;
        }
    }

    private static bool IsBreadName(string normalized) =>
        normalized is "αραβικη" or "ελληνικη" or "ψωμι"
        || (normalized.Contains("πιτ") && (normalized.Contains("αραβικ") || normalized.Contains("ελληνικ") || normalized.Contains("διπλ")));

    private static string? BreadOf(string normalized) =>
        normalized.Contains("αραβικ") ? "Αραβική"
        : normalized.Contains("ελληνικ") ? "Ελληνική"
        : normalized.Contains("ψωμ") ? "Ψωμί"
        : null;

    private static string StripAdd(string label) =>
        label.StartsWith(AddPrefix, StringComparison.CurrentCultureIgnoreCase) ? label[AddPrefix.Length..] : label;

    private static string Lower(string text) => text.Length > 0 ? char.ToLower(text[0]) + text[1..] : text;

    /// <summary>
    /// Η τιμή του προϊόντος περιέχει ήδη τις επιλογές; Στις πραγματικές παραγγελίες ΝΑΙ (οι επιλογές έρχονται με 0), ενώ η
    /// τεκμηρίωση δείχνει τιμές και στις επιλογές. Όταν έρθουν τιμές, κρίνεται ανά παραγγελία με το σύνολο που δήλωσε το
    /// e-food: προστίθενται μόνο αν έτσι βγαίνει πιο κοντά στο σύνολο — ποτέ διπλομέτρημα.
    /// </summary>
    private static bool MaterialsAreExtra(JsonObject order, List<JsonObject> products, List<JsonObject> offers)
    {
        var materials = products.Sum(p => MaterialsMoney(p) * Quantity(p));
        if (materials == 0)
            return false;
        var fees = Fees(order);
        var offersMoney = offers.Sum(o => OfferPrice(o) * Math.Max(1, Count(o["iteration"])));
        var without = products.Sum(p => UnitPrice(p) * Quantity(p)) + offersMoney + fees - VendorDiscount(order);
        var platform = Money(order["price"]) ?? 0m;
        return Math.Abs(without + materials - platform) < Math.Abs(without - platform);
    }

    /// <summary>Ό,τι έχει μέσα το price του e-food πέρα από τα προϊόντα: μεταφορικά και σακούλες. ΟΧΙ το φιλοδώρημα.</summary>
    public static decimal Fees(JsonObject order) =>
        (Money(order["delivery_fee"]) ?? 0m) + (Money((order["bags"] as JsonObject)?["amount"]) ?? 0m);

    /// <summary>Έκπτωση που πληρώνει το κατάστημα: κουπόνι ή «Τυχερή Πεινιάτα» (joker). Το e-food στέλνει κουπόνι ΜΟΝΟ όταν
    /// το πληρώνει το κατάστημα, και το joker έρχεται ΚΑΙ ως standard_discount με το ίδιο ποσό (#19, 24/9) — γι' αυτό
    /// μετράνε τα coupons + joker, ποτέ και τα discounts.</summary>
    public static decimal VendorDiscount(JsonObject order) => VendorDiscounts(order).Sum(d => d.Amount);

    private static IEnumerable<(bool Joker, decimal Amount)> VendorDiscounts(JsonObject order) =>
        new[] { order["coupons"], order["joker"] }.SelectMany(n => (n as JsonArray ?? []).OfType<JsonObject>())
            .Where(d => d["paid_by_vendor"] is not JsonValue paid || !paid.TryGetValue(out bool byVendor) || byVendor)
            .Select(d => (d["is_joker"] is JsonValue j && j.TryGetValue(out bool isJoker) && isJoker, Money(d["amount"]) ?? 0m))
            .Where(d => d.Item2 > 0);

    /// <summary>Οι γραμμές με την έκπτωση μοιρασμένη αναλογικά· ό,τι λεπτό περισσεύει από τη στρογγυλοποίηση πάει στη
    /// μεγαλύτερη γραμμή, ώστε το σύνολο να είναι ακριβώς μικτό − έκπτωση.</summary>
    private static List<EfoodLine> Discounted(List<EfoodLine> lines, decimal gross, decimal discount)
    {
        var net = gross - discount;
        var result = lines.Select(l => l with
        {
            Revenue = gross == 0 ? 0m : Math.Round(l.Revenue * net / gross, 2, MidpointRounding.AwayFromZero),
        }).ToList();
        var rest = net - result.Sum(l => l.Revenue);
        if (rest != 0 && result.Count > 0)
        {
            var largest = result.IndexOf(result.MaxBy(l => l.Revenue)!);
            result[largest] = result[largest] with { Revenue = result[largest].Revenue + rest };
        }
        return result;
    }

    private static int Quantity(JsonObject item) => Math.Max(1, Count(item["quantity"]));

    private static decimal UnitPrice(JsonObject item)
    {
        var price = Money(item["price"]) ?? 0m;
        // Τιμή προσφοράς, όταν υπάρχει και είναι όντως μικρότερη.
        return Money(item["discount_price"]) is { } discounted && discounted > 0 && discounted < price ? discounted : price;
    }

    private static decimal MaterialsMoney(JsonObject item) =>
        (item["materials"] as JsonArray ?? []).OfType<JsonObject>().Sum(m => (Money(m["price"]) ?? 0m) * Math.Max(1, Count(m["quantity"])));

    private static decimal OfferPrice(JsonObject offer) =>
        Money(offer["discount_price"]) is { } discounted && discounted > 0 ? discounted : Money(offer["price"]) ?? 0m;

    private static EfoodLine OfferLine(JsonObject offer, IEfoodMenu menu, EfoodContext context, CatalogIndex index, List<string> unmatched)
    {
        var name = Text(offer["name"]);
        if (name.Length == 0)
            name = "Προσφορά e-food";
        var quantity = Math.Max(1, Count(offer["iteration"]));
        var details = new List<string>();
        foreach (var node in offer["products"] as JsonArray ?? [])
        {
            if (node is not JsonObject item || Text(item["name"]).Length == 0)
                continue;
            // Ίδια ανάγνωση με τα κανονικά προϊόντα — μόνο για το κείμενο· ο τζίρος είναι η τιμή της προσφοράς.
            var line = Line(item, menu, context, index, unmatched, addMaterials: false);
            details.Add((line.Quantity > 1 ? line.Quantity + " × " : "") + line.Name);
            details.AddRange(line.Details.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(d => "  " + d));
        }
        return new EfoodLine(name, quantity, Math.Round(OfferPrice(offer) * quantity, 2, MidpointRounding.AwayFromZero),
            string.Join("\n", details));
    }

    /// <summary>Το όνομα του ταμείου που είναι το ίδιο (χωρίς τόνους/κεφαλαία), αλλιώς null.</summary>
    private static string? Pick(IReadOnlyList<string> candidates, string name)
    {
        var wanted = EfoodMatcher.Normalize(name);
        return candidates.FirstOrDefault(n => EfoodMatcher.Normalize(n) == wanted);
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

    /// <summary>Προϊόντα του καταλόγου του ταμείου με κωδικό και με όνομα (χωρίς τόνους/κεφαλαία). Διπλός κωδικός ή όνομα:
    /// μετράει το πρώτο, όπως και στο κινητό του σερβιτόρου.</summary>
    private sealed class CatalogIndex
    {
        private readonly Dictionary<string, (Product, MenuCategory)> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (Product, MenuCategory)> _byName = new(StringComparer.Ordinal);

        public CatalogIndex(IEfoodMenu menu)
        {
            foreach (var category in menu.Categories)
                foreach (var product in category.Products)
                {
                    _byId.TryAdd(product.Id, (product, category));
                    _byName.TryAdd(EfoodMatcher.Normalize(product.Name), (product, category));
                }
        }

        public (Product Product, MenuCategory Category)? ById(string productId) =>
            _byId.TryGetValue(productId, out var found) ? found : null;

        public (Product Product, MenuCategory Category)? Find(string code, string name) =>
            code.Length > 0 && _byId.TryGetValue(code, out var byId) ? byId
            : _byName.TryGetValue(EfoodMatcher.Normalize(name), out var byName) ? byName
            : null;
    }
}
