using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using PittaPos.Core.Data;
using PittaPos.Core.Efood;
using PittaPos.Core.Models;

// ΕΛΕΓΧΟΣ του e-food στο ταμείο, χωρίς ταμείο: χτίζει τον κατάλογο για το e-food από τον MenuSeed (τον πραγματικό
// κατάλογο του μαγαζιού) και «παραγγέλνει» πάνω του όπως θα παρήγγελνε πελάτης στο e-food — για να φανεί ότι κάθε
// γραμμή γυρίζει ΑΚΡΙΒΩΣ όπως θα την περνούσε ο ταμίας.
//
//   dotnet run --project tools/EfoodCheck [-- <αρχείο όπου θα γραφτεί ο κατάλογος JSON>]

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("el-GR");
Console.OutputEncoding = Encoding.UTF8;
var menu = new SeedMenu();
var fails = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "OK   " : "FAIL ") + what);
    if (!ok) fails++;
}

Console.WriteLine("--- κατάλογος για το e-food (από τον MenuSeed)");
var catalog = EfoodCatalogBuilder.Build(menu);
var products = catalog.Body["products"]!.AsArray();
var categories = catalog.Body["categories"]!.AsArray();
var onEfood = menu.Categories.Where(c => c.OnEfood ?? MenuSeed.GuessOnEfood(c.Name)).ToList();
var expected = onEfood.Sum(c => c.Products.Count);
foreach (var warning in catalog.Warnings)
    Console.WriteLine("     ! " + warning);
Check(catalog.Products == expected && catalog.Warnings.Count == 0,
    $"όλα τα προϊόντα μέσα: {catalog.Products}/{expected}, σε {catalog.Categories} κατηγορίες");
var productIds = products.Select(p => Str(p!["id"])).ToList();
Check(productIds.Distinct().Count() == productIds.Count, "κάθε προϊόν με μοναδικό κωδικό");
var categoryIds = categories.Select(c => Str(c!["id"])).ToHashSet();
Check(products.All(p => categoryIds.Contains(Str(p!["category"]!["id"]))), "κάθε προϊόν σε κατηγορία που στάλθηκε");
var tiers = products.SelectMany(p => p!["tiers"] as JsonArray ?? []).OfType<JsonObject>().ToList();
var tierIds = tiers.Select(t => Str(t["id"])).ToList();
var optionIds = tiers.SelectMany(t => t["options"]!.AsArray()).Select(o => Str(o!["id"])).ToList();
Check(tierIds.Distinct().Count() == tierIds.Count && optionIds.Distinct().Count() == optionIds.Count,
    $"μοναδικοί κωδικοί σε {tierIds.Count} ομάδες και {optionIds.Count} επιλογές");
Check(optionIds.All(id => EfoodCodes.ParseOption(id) is not null), "κάθε κωδικός επιλογής διαβάζεται πίσω");
Check(products.All(p => Str(p!["name"]).Length > 0 && Dec(p["price"]) >= 0) && tiers.All(t => t["options"]!.AsArray().Count > 0),
    "ονόματα, τιμές, καμία άδεια ομάδα επιλογών");
var staff = menu.Categories.Where(c => c.Name == "ΠΡΟΣΩΠΙΚΟ").SelectMany(c => c.Products).ToList();
Check(staff.Count > 0 && catalog.Excluded.SequenceEqual(new[] { "ΠΡΟΣΩΠΙΚΟ" }) && staff.All(p => !productIds.Contains(p.Id))
      && !categoryIds.Any(id => menu.Categories.First(c => c.Id == id).Name == "ΠΡΟΣΩΠΙΚΟ"),
    $"το ΠΡΟΣΩΠΙΚΟ ({staff.Count} προϊόντα) δεν στάλθηκε — ούτε η κατηγορία, ούτε κανένα προϊόν του");
Check(onEfood.SelectMany(c => c.Products).All(p => Dec(Entry(p)["price"]) == (p.DeliveryPrice ?? p.Price)),
    "τιμή = τιμή εφαρμογών (αλλιώς η κανονική)");
Check(onEfood.SelectMany(c => c.Products).Where(p => !menu.OpensIngredients(p)).All(p => Entry(p)["tiers"] is null),
    "ό,τι δεν ανοίγει υλικά στο ταμείο, δεν έχει επιλογές ούτε στο e-food");
Check(tiers.Where(t => Str(t["id"]).EndsWith("__" + EfoodCodes.Ingredient))
        .All(t => t["options"]!.AsArray().All(o => o!["selected"]!.GetValue<bool>() == false && Str(o["name"]).StartsWith("Χωρίς "))),
    "υλικά ως «Χωρίς …», κανένα προεπιλεγμένο");
var json = catalog.Body.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
Console.WriteLine($"     μέγεθος {Encoding.UTF8.GetByteCount(json) / 1024} KB");
if (args.Length > 0)
{
    File.WriteAllText(args[0], json, new UTF8Encoding(false));
    Console.WriteLine("     γράφτηκε: " + args[0]);
}

Console.WriteLine("--- παραγγελίες πάνω στον κατάλογο: γυρίζουν όπως θα τις περνούσε ο ταμίας");
var (pita, pitaCategory) = Find((p, c) => menu.HasBreadChoice(c.Name) && menu.FuseBreadIntoName(c.Name) && menu.SupportsDoublePita(c.Name)
    && p.Customizable && menu.IngredientsFor(p).Count >= 4 && EfoodMenuRules.ExtrasFor(p, menu.Extras).Count > 0);
Check(pita is not null, $"πίττα με ψωμί στο όνομα, υλικά, έξτρα και διπλή πίτα: {pita?.Name} ({pitaCategory?.Name})");
if (pita is not null && pitaCategory is not null)
{
    var price = pita.DeliveryPrice ?? pita.Price;
    var ingredient = menu.IngredientsFor(pita)[1];
    var extras = EfoodMenuRules.ExtrasFor(pita, menu.Extras);
    var extra = extras.FirstOrDefault(e => e.Price > 0) ?? extras[0];

    var line = Map(Order(Item(pita, 2, "καλοψημένη",
        (EfoodCodes.Bread, "Αραβική"), (EfoodCodes.Ingredient, ingredient), (EfoodCodes.Extra, extra.Name)))).Lines[0];
    Check(line.Name == MenuSeed.ComposeCustomizedName(pita.Name, "Αραβική")
          && line.PrintName == MenuSeed.ComposeCustomizedName(pita.NameForPrint, "Αραβική"), $"ψωμί μέσα στο όνομα: «{line.Name}»");
    Check(line.Details == $"καλοψημένη\nχωρίς:\n{char.ToLower(ingredient[0]) + ingredient[1..]}\n+ {extra.Name}",
        "σημείωση, «χωρίς:», «+ έξτρα», με τη σειρά του ταμία: " + line.Details.Replace("\n", " | "));
    Check(line.Quantity == 2 && line.Revenue == (price + extra.Price) * 2, $"τζίρος (τιμή + έξτρα) × 2 = {line.Revenue}");
    Check(line.ProductId == pita.Id && line.Customization is { Bread: "Αραβική", DoublePita: false } custom
          && custom.Removed.SequenceEqual(new[] { ingredient }) && custom.Extras.GetValueOrDefault(extra.Name) == 1,
        "ίδιο προϊόν και ιδιαιτερότητες — για στατιστικά, ΦΠΑ και «ΜΙΑ ΑΠΟ ΤΑ ΙΔΙΑ»");

    var doubled = Map(Order(Item(pita, 1, "", (EfoodCodes.DoublePita, EfoodCatalogBuilder.DoublePitaName)))).Lines[0];
    Check(doubled.Name == MenuSeed.ComposeDoublePitaName(pita.Name, pitaCategory.Name, "Ελληνική")
          && doubled.Revenue == price + menu.DoublePitaPriceFor(pitaCategory.Name), $"διπλή πίτα: «{doubled.Name}» {doubled.Revenue}");

    var everything = menu.IngredientsFor(pita).Select(i => (EfoodCodes.Ingredient, i)).ToArray();
    Check(Map(Order(Item(pita, 1, "", everything))).Lines[0].Details == "σκέτο", "όλα «Χωρίς» → σκέτο");

    var plain = Map(Order(Item(pita, 1, ""))).Lines[0];
    Check(plain.Name == MenuSeed.ComposeCustomizedName(pita.Name, "Ελληνική") && plain.Details == "",
        "χωρίς καμία επιλογή → η προεπιλογή του ταμείου (Ελληνική), τίποτα από κάτω");

    // Χωρίς κωδικούς (κατάλογος που δεν τον έστειλε το ταμείο): βρίσκεται με τα ονόματα.
    var byName = Item(pita, 1, "", (EfoodCodes.Bread, "Αραβική"), (EfoodCodes.Ingredient, ingredient));
    byName["integrator_id"] = "";
    foreach (var material in byName["materials"]!.AsArray())
        material!["integrator_id"] = "";
    var named = Map(Order(byName)).Lines[0];
    Check(named.ProductId == pita.Id && named.Name == MenuSeed.ComposeCustomizedName(pita.Name, "Αραβική")
          && named.Customization!.Removed.SequenceEqual(new[] { ingredient }), "χωρίς κωδικούς → βρίσκεται με τα ονόματα");

    // Επιλογή που δεν υπάρχει πια (μετονομάστηκε μετά την αποστολή): τυπώνεται όπως ήρθε και χρεώνεται.
    var renamed = Item(pita, 1, "");
    renamed["materials"]!.AsArray().Add(new JsonObject
    {
        ["name"] = "Τρούφα", ["quantity"] = 1, ["price"] = 2.5m,
        ["integrator_id"] = EfoodCodes.Option(pita.Id, EfoodCodes.Extra, "Τρούφα"),
    });
    var odd = Map(Order(renamed));
    Check(odd.Lines[0].Details == "+ Τρούφα" && odd.Lines[0].Revenue == price + 2.5m && odd.Unmatched.Count == 1,
        "άγνωστη επιλογή → «+ Τρούφα» στο χαρτί και στον τζίρο, και γράφεται στο log");
}

var (meal, mealCategory) = Find((p, c) => menu.HasBreadChoice(c.Name) && !menu.FuseBreadIntoName(c.Name) && menu.OpensIngredients(p));
Check(meal is not null, $"μερίδα με ψωμί σε δική του γραμμή: {meal?.Name} ({mealCategory?.Name})");
if (meal is not null)
{
    var line = Map(Order(Item(meal, 1, "", (EfoodCodes.Bread, "Ψωμί")))).Lines[0];
    Check(line.Name == meal.Name && line.Details.Split('\n')[0] == "Ψωμί", $"«{line.Name}» με «Ψωμί» από κάτω");
}

var (drink, _) = Find((p, _) => !menu.OpensIngredients(p));
Check(drink is not null, $"προϊόν χωρίς υλικά: {drink?.Name}");
if (drink is not null)
{
    var line = Map(Order(Item(drink, 3, "χωρίς πάγο"))).Lines[0];
    Check(line.Name == drink.Name && line.Details == "χωρίς πάγο" && line.Customization is null && line.Quantity == 3
          && line.ProductId == drink.Id, "χωρίς υλικά: όνομα όπως είναι, μόνο η σημείωση του πελάτη");

    var unknown = new JsonObject
    {
        ["name"] = "Κάτι άλλο", ["price"] = 3m, ["quantity"] = 1, ["integrator_id"] = "den-yparxei",
        ["materials"] = new JsonArray(new JsonObject { ["name"] = "Σάλτσα", ["price"] = 0.5m, ["quantity"] = 1 }),
    };
    var stranger = Map(Order(unknown));
    Check(stranger.Lines[0].Name == "Κάτι άλλο" && stranger.Lines[0].ProductId == "" && stranger.Lines[0].Details == "+ Σάλτσα"
          && stranger.Lines[0].Revenue == 3.5m && stranger.Unmatched.Contains("Κάτι άλλο"),
        "άγνωστο προϊόν → μπαίνει με το όνομα του e-food, τίποτα δεν χάνεται");

    Console.WriteLine("--- πάνω μέρος του δελτίου");
    var own = Map(Order(Item(drink, 1, "")));
    Check(own.OwnDelivery && own.Phone == "6900000000" && own.Notes.StartsWith("ΜΕΤΡΗΤΑ") && own.Notes.Contains("ΧΩΡΙΣ ΜΑΧΑΙΡΟΠΙΡΟΥΝΑ"),
        "δική μας διανομή: τηλέφωνο, «ΜΕΤΡΗΤΑ … εισπράττει ο διανομέας», «ΧΩΡΙΣ ΜΑΧΑΙΡΟΠΙΡΟΥΝΑ»");
    var platform = Order(Item(drink, 1, ""));
    platform["transport_method"]!["delivery_provider"] = "platform_delivery";
    var rider = Map(platform);
    Check(!rider.OwnDelivery && rider.Phone == "" && rider.Address == "", "rider του e-food: κανένα τηλέφωνο ή διεύθυνση στο χαρτί");
    var pickup = Order(Item(drink, 1, ""));
    pickup["type"] = "takeaway";
    pickup["payment_type"] = "credit_card";
    Check(Map(pickup).Notes.StartsWith("ΠΑΡΑΛΑΒΗ — πληρωμένη"), "παραλαβή πληρωμένη με κάρτα");
}

Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
return fails == 0 ? 0 : 1;

// ── βοηθητικά ─────────────────────────────────────────────────────────────────────────────────────

EfoodImport Map(JsonObject order) => EfoodOrderReader.Map(order, menu);

JsonObject Entry(Product product) => products.OfType<JsonObject>().First(p => Str(p["id"]) == product.Id);

(Product? Product, MenuCategory? Category) Find(Func<Product, MenuCategory, bool> match)
{
    foreach (var category in onEfood)
        foreach (var product in category.Products)
            if (match(product, category))
                return (product, category);
    return (null, null);
}

/// <summary>Ένα προϊόν όπως θα ερχόταν μέσα σε παραγγελία e-food: από τον κατάλογο που στάλθηκε, με τις επιλογές
/// (είδος, όνομα στον κατάλογο του ταμείου) που «πάτησε» ο πελάτης.</summary>
JsonObject Item(Product product, int quantity, string notes, params (string Kind, string Name)[] picks)
{
    var entry = Entry(product);
    var options = (entry["tiers"] as JsonArray ?? []).OfType<JsonObject>()
        .SelectMany(t => t["options"]!.AsArray()).OfType<JsonObject>().ToList();
    var materials = new JsonArray();
    foreach (var (kind, name) in picks)
    {
        var code = EfoodCodes.Option(product.Id, kind, name);
        var option = options.FirstOrDefault(o => Str(o["id"]) == code)
                     ?? throw new InvalidOperationException($"δεν υπάρχει επιλογή {kind} «{name}» στο «{product.Name}»");
        materials.Add(new JsonObject
        {
            ["id"] = "m" + materials.Count, ["name"] = Str(option["name"]), ["quantity"] = 1, ["price"] = Dec(option["price"]),
            ["notes"] = "", ["integrator_id"] = code,
        });
    }
    return new JsonObject
    {
        ["id"] = "e-" + product.Id, ["name"] = Str(entry["name"]), ["price"] = Dec(entry["price"]), ["discount_price"] = null,
        ["quantity"] = quantity, ["notes"] = notes, ["integrator_id"] = product.Id, ["materials"] = materials,
    };
}

static JsonObject Order(params JsonObject[] items) => new()
{
    ["id"] = 123456, ["short_code"] = "", ["type"] = "delivery", ["price"] = 10m, ["payment_type"] = "cash",
    ["customer"] = new JsonObject { ["name"] = "Δοκιμή", ["surname"] = "Πελάτης", ["telephone"] = "6900000000", ["address"] = "Λεωφ. Δοκιμής 1" },
    ["transport_method"] = new JsonObject { ["key"] = "delivery", ["delivery_provider"] = "vendor_delivery" },
    ["products"] = new JsonArray(items.Cast<JsonNode?>().ToArray()),
    ["offers"] = new JsonArray(),
    ["extra_parameters"] = new JsonArray("no-cutlery"),
};

static string Str(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : "";

static decimal Dec(JsonNode? node) =>
    node is JsonValue value && value.TryGetValue(out decimal d) ? d
    : node is JsonValue other && other.TryGetValue(out double x) ? (decimal)x
    : -1m;

/// <summary>Ο MenuSeed με τους κανόνες του MenuStore (ιδιότητες κατηγορίας, αλλιώς οι παλιοί κανόνες ονόματος).</summary>
sealed class SeedMenu : IEfoodMenu
{
    public IReadOnlyList<MenuCategory> Categories => MenuSeed.Categories;
    public IReadOnlyList<ExtraItem> Extras => MenuSeed.Extras;
    public bool OpensIngredients(Product product) => product.Customizable || product.Ingredients is { Count: > 0 };
    public IReadOnlyList<string> IngredientsFor(Product product) => product.Ingredients ?? MenuSeed.IncludedIngredients;
    public bool HasBreadChoice(string name) => Find(name)?.HasBread ?? MenuSeed.HasBreadChoice(name);
    public bool FuseBreadIntoName(string name) => Find(name)?.FuseBreadIntoName ?? MenuSeed.FuseBreadIntoName(name);
    public bool SupportsDoublePita(string name) => Find(name)?.SupportsDoublePita ?? MenuSeed.SupportsDoublePita(name);
    public decimal DoublePitaPriceFor(string name) => SupportsDoublePita(name) ? MenuSeed.DoublePitaPrices.GetValueOrDefault(name) : 0m;
    private static MenuCategory? Find(string name) => MenuSeed.Categories.FirstOrDefault(c => c.Name == name);
}
