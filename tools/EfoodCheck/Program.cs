using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using PittaPos.Core.Data;
using PittaPos.Core.Efood;
using PittaPos.Core.Models;

// ΕΛΕΓΧΟΣ του e-food στο ταμείο, χωρίς ταμείο: οι ΠΡΑΓΜΑΤΙΚΕΣ παραγγελίες του sandbox (14/9/2026, με σβησμένα
// προσωπικά στοιχεία) πάνω στον κατάλογο του e-food και στον MenuSeed, και ό,τι γίνεται όταν δεν υπάρχει κατάλογος.
//
//   dotnet run --project tools/EfoodCheck [-- <ολόκληρος κατάλογος e-food.json>]

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("el-GR");
Console.OutputEncoding = Encoding.UTF8;
var menu = new SeedMenu();
var fails = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "OK   " : "FAIL ") + what);
    if (!ok) fails++;
}

Console.WriteLine("--- πραγματικές παραγγελίες του sandbox (14/9/2026), με τον κατάλογο του e-food");
var samples = FindSamples();
Check(samples is not null, "βρέθηκαν τα δείγματα (tools/EfoodCheck/samples)");
if (samples is not null)
{
    var subset = EfoodCatalogParser.Parse(File.ReadAllText(Path.Combine(samples, "catalog-subset.json"), Encoding.UTF8));
    var context = EfoodContext.Build(subset, [], menu);
    JsonObject OrderFrom(string file) =>
        (JsonNode.Parse(File.ReadAllText(Path.Combine(samples, file), Encoding.UTF8))!["orders"]![0] as JsonObject)!;

    var chicken = Product("Πίττα κοτόπουλο");
    var pork = Product("Πίττα χοιρινό");
    var o11 = EfoodOrderReader.Map(OrderFrom("order-11.json"), menu, context);
    Check(o11.Lines.Count == 3 && o11.Unmatched.Count == 0, $"#11: 3 γραμμές, όλες βρέθηκαν ({string.Join(", ", o11.Unmatched)})");
    Check(o11.Lines[0].Name == MenuSeed.ComposeCustomizedName(chicken.Product.Name, "Ελληνική") && o11.Lines[0].Details == ""
          && o11.Lines[0].Revenue == 7.20m && o11.Lines[0].ProductId == chicken.Product.Id,
        $"Πίττα κοτόπουλο «Απ' όλα» → «{o11.Lines[0].Name}», τίποτα από κάτω, 7,20");
    Check(o11.Lines[1].Name == "Κοτόπουλο μερίδα" && o11.Lines[1].Details == "Αραβική" && o11.Lines[1].Revenue == 10.80m,
        $"Κοτόπουλο μερίδα με αραβική → «Αραβική» από κάτω, 10,80 ({o11.Lines[1].Details.Replace("\n", " | ")})");
    Check(o11.Lines[2] is { Name: "Coca-Cola 330ml", Details: "" } && o11.Lines[2].Revenue == 1.90m, "Coca-Cola 330ml, 1,90");
    Check(o11.Total == 19.90m && o11.PlatformTotal == 20.40m && o11.Notes.StartsWith("ΜΕΤΡΗΤΑ €20,40") && o11.Notes.Contains("xwris maxairopirouno"),
        $"19,90 + 0,50 μεταφορικά = 20,40 του e-food · «{o11.Notes}»");

    var o12 = EfoodOrderReader.Map(OrderFrom("order-12.json"), menu, context);
    Check(o12.Lines.Count == 2 && o12.Unmatched.Count == 0, "#12: 2 γραμμές, όλες βρέθηκαν");
    Check(o12.Lines[0].Name == MenuSeed.ComposeDoublePitaName(pork.Product.Name, pork.Category.Name, "Ελληνική"),
        $"διπλή ελληνική → «{o12.Lines[0].Name}»");
    Check(o12.Lines[0].Details == "μόνο με:\nντομάτα\nπατάτες\n+ Τυρί gouda",
        "«Μόνο με» ντομάτα, πατάτες + gouda: " + o12.Lines[0].Details.Replace("\n", " | "));
    Check(o12.Lines[0].Revenue == 9.10m && o12.Lines[0].Customization is { DoublePita: true, Bread: "Ελληνική" },
        "9,10 όπως το χρέωσε το e-food (η τιμή έχει ήδη μέσα διπλή πίτα και gouda — δεν διπλομετράμε)");
    Check(o12.Lines[1].Name == "Burger απλό" && o12.Lines[1].Details == "χωρίς:\nντομάτα" && o12.Lines[1].Revenue == 6.40m,
        "burger με ξετσεκαρισμένη ντομάτα → «χωρίς: ντομάτα» (το e-food στέλνει όσα έμειναν): " + o12.Lines[1].Details.Replace("\n", " | "));
    Check(o12.Total == 15.50m && o12.PlatformTotal == 16m, "15,50 + 0,50 = 16,00 του e-food");

    var noCatalog = EfoodOrderReader.Map(OrderFrom("order-12.json"), menu);
    Check(noCatalog.Lines[0].Name == MenuSeed.ComposeDoublePitaName(pork.Product.Name, pork.Category.Name, "Ελληνική")
          && noCatalog.Lines[0].Details == "μόνο με:\nντομάτα\nπατάτες\n+ Τυρί gouda" && noCatalog.Total == 15.50m,
        "ακόμα και χωρίς κατάλογο: βρίσκεται με το όνομα, διπλή πίτα, «μόνο με», σωστό σύνολο");

    var explicitNone = new EfoodContext(subset, [new EfoodMatch { EfoodId = "1423306810", EfoodName = "Burger απλό" }]);
    var none = EfoodOrderReader.Map(OrderFrom("order-12.json"), menu, explicitNone);
    Check(none.Lines[1] is { Name: "Burger απλό", ProductId: "" } && none.Lines[1].Details == "χωρίς:\nντομάτα",
        "ρητό «δεν υπάρχει στο ταμείο» → με το όνομα του e-food, χωρίς μαντεψιές, αλλά με τα υλικά του");
}

Console.WriteLine("--- χωρίς κατάλογο: με τα ονόματα, και τίποτα δεν χάνεται");
{
    var line = Map(Order(Item("Coca-Cola 330ml", 1.9m, 3, "χωρίς πάγο"))).Lines[0];
    Check(line is { Name: "Coca-Cola 330ml", Details: "χωρίς πάγο", Customization: null, Quantity: 3 } && line.Revenue == 5.70m,
        "αναψυκτικό × 3 με σημείωση πελάτη");
    var meal = Map(Order(Item("Κοτόπουλο μερίδα", 10.8m, 1, "", ("Αραβική πίττα", 0m)))).Lines[0];
    Check(meal.Name == "Κοτόπουλο μερίδα" && meal.Details == "Αραβική", "μερίδα με «Αραβική πίττα» → «Αραβική» από κάτω");
    var stranger = Map(Order(Item("Κάτι άλλο", 3m, 1, "", ("Σάλτσα", 0m))));
    Check(stranger.Lines[0] is { Name: "Κάτι άλλο", ProductId: "", Details: "Σάλτσα" } && stranger.Lines[0].Revenue == 3m
          && stranger.Unmatched.Contains("Κάτι άλλο"), "άγνωστο προϊόν → με το όνομα του e-food και ό,τι επιλογές ήρθαν");
    var documented = Map(Order(Item("Coca-Cola 330ml", 1.9m, 2, "", ("Λεμόνι", 0.5m))));
    Check(documented.Total == 4.80m, $"επιλογές με τιμή (όπως στην τεκμηρίωση) και σύνολο του e-food που τις περιέχει → προστίθενται ({documented.Total})");

    Console.WriteLine("--- πάνω μέρος του δελτίου");
    var own = Map(Order(Item("Coca-Cola 330ml", 1.9m, 1, "")));
    Check(own.OwnDelivery && own.Phone == "6900000000" && own.Notes.StartsWith("ΜΕΤΡΗΤΑ") && own.Notes.Contains("ΧΩΡΙΣ ΜΑΧΑΙΡΟΠΙΡΟΥΝΑ"),
        "δική μας διανομή: τηλέφωνο, «ΜΕΤΡΗΤΑ … εισπράττει ο διανομέας», «ΧΩΡΙΣ ΜΑΧΑΙΡΟΠΙΡΟΥΝΑ»");
    var platform = Order(Item("Coca-Cola 330ml", 1.9m, 1, ""));
    platform["transport_method"]!["delivery_provider"] = "platform_delivery";
    var rider = Map(platform);
    Check(!rider.OwnDelivery && rider.Phone == "" && rider.Address == "", "rider του e-food: κανένα τηλέφωνο ή διεύθυνση στο χαρτί");
    var pickup = Order(Item("Coca-Cola 330ml", 1.9m, 1, ""));
    pickup["type"] = "takeaway";
    pickup["payment_type"] = "credit_card";
    Check(Map(pickup).Notes.StartsWith("ΠΑΡΑΛΑΒΗ — πληρωμένη"), "παραλαβή πληρωμένη με κάρτα");
}

// Ο ολόκληρος κατάλογος του e-food (αποθηκευμένος από τη γέφυρα): dotnet run --project tools/EfoodCheck -- <κατάλογος.json>
if (args.Length > 0 && File.Exists(args[0]))
{
    Console.WriteLine("--- ολόκληρος κατάλογος του e-food: ανάγνωση και αυτόματη αντιστοίχιση");
    var catalogItems = EfoodCatalogParser.Parse(File.ReadAllText(args[0], Encoding.UTF8));
    var suggestions = EfoodMatcher.SuggestAll(catalogItems, menu);
    var green = catalogItems.Count(i => suggestions[EfoodMatcher.KeyOf(i)] is { SamePrice: true });
    var yellow = catalogItems.Count(i => suggestions[EfoodMatcher.KeyOf(i)] is { SamePrice: false });
    Check(catalogItems.Count > 0 && catalogItems.All(i => i.Id.Length > 0 && i.Name.Length > 0),
        $"διαβάστηκαν {catalogItems.Count} προϊόντα, όλα με αριθμό και όνομα");
    Check(catalogItems.Select(EfoodMatcher.KeyOf).Distinct().Count() == catalogItems.Count, "κάθε προϊόν του e-food με μοναδικό αριθμό");
    Console.WriteLine($"     αυτόματα με ίδια τιμή: {green} · πρόταση με άλλη τιμή: {yellow} · χωρίς πρόταση: {catalogItems.Count - green - yellow}");
    EfoodSuggestion? For(string efoodName, string category) =>
        catalogItems.FirstOrDefault(i => i.Name == efoodName && i.Category == category) is { } found ? suggestions[EfoodMatcher.KeyOf(found)] : null;
    var arabic = For("Αραβική πίττα κοτόπουλο", "Αραβικές πίττες");
    Check(arabic is { Bread: "Αραβική", SamePrice: true } && EfoodMatcher.Normalize(arabic.Product.Name) == "πιττα κοτοπουλο",
        $"«Αραβική πίττα κοτόπουλο» → {arabic?.Product.Name} + {arabic?.Bread}");
    var sandwich = For("Σάντουιτς κοτόπουλο", "Σάντουιτς");
    Check(sandwich is { Bread: "Ψωμί" } && EfoodMatcher.Normalize(sandwich.Product.Name) == "πιττα κοτοπουλο",
        $"«Σάντουιτς κοτόπουλο» → {sandwich?.Product.Name} + {sandwich?.Bread}");
    Check(For("Λαχανικών", "Νηστίσιμο menu") is { SamePrice: false }, "ίδιο όνομα με άλλη τιμή («Λαχανικών» νηστίσιμο) → θέλει επιβεβαίωση");
}

Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
return fails == 0 ? 0 : 1;

// ── βοηθητικά ─────────────────────────────────────────────────────────────────────────────────────

EfoodImport Map(JsonObject order) => EfoodOrderReader.Map(order, menu);

(Product Product, MenuCategory Category) Product(string name)
{
    foreach (var category in menu.Categories)
        foreach (var product in category.Products)
            if (EfoodMatcher.Normalize(product.Name) == EfoodMatcher.Normalize(name))
                return (product, category);
    throw new InvalidOperationException("δεν υπάρχει στον MenuSeed: " + name);
}

static string? FindSamples()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "tools", "EfoodCheck", "samples");
        if (Directory.Exists(candidate))
            return candidate;
    }
    return null;
}

/// <summary>Ένα προϊόν όπως έρχεται μέσα σε παραγγελία e-food: η τιμή περιέχει ήδη τις επιλογές.</summary>
static JsonObject Item(string name, decimal price, int quantity, string notes, params (string Name, decimal Price)[] materials) => new()
{
    ["id"] = "no-valid-code-found", ["name"] = name, ["price"] = price, ["discount_price"] = null, ["quantity"] = quantity,
    ["notes"] = notes, ["integrator_id"] = "",
    ["materials"] = new JsonArray(materials.Select(m => (JsonNode?)new JsonObject
    {
        ["id"] = "no-valid-code-found", ["name"] = m.Name, ["quantity"] = 1, ["price"] = m.Price, ["notes"] = "", ["integrator_id"] = "",
    }).ToArray()),
};

/// <summary>Παραγγελία με σύνολο = γραμμές + επιλογές με τιμή (όπως στην τεκμηρίωση).</summary>
static JsonObject Order(params JsonObject[] items)
{
    var total = items.Sum(i => (i["price"]!.GetValue<decimal>() + i["materials"]!.AsArray().Sum(m => m!["price"]!.GetValue<decimal>()))
                               * i["quantity"]!.GetValue<int>());
    return new JsonObject
    {
        ["id"] = 123456, ["short_code"] = "", ["type"] = "delivery", ["price"] = total, ["payment_type"] = "cash",
        ["customer"] = new JsonObject { ["name"] = "Δοκιμή", ["surname"] = "Πελάτης", ["telephone"] = "6900000000", ["address"] = "Λεωφ. Δοκιμής 1" },
        ["transport_method"] = new JsonObject { ["key"] = "delivery", ["delivery_provider"] = "vendor_delivery" },
        ["products"] = new JsonArray(items.Cast<JsonNode?>().ToArray()),
        ["offers"] = new JsonArray(),
        ["extra_parameters"] = new JsonArray("no-cutlery"),
    };
}

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
