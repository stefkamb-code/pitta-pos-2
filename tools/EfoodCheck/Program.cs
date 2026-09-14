using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using PittaPos.Core.Data;
using PittaPos.Core.Efood;
using PittaPos.Core.Models;

// ΕΛΕΓΧΟΣ της ανάγνωσης παραγγελιών e-food, χωρίς ταμείο: «παραγγελίες» με τα ονόματα του καταλόγου του ταμείου
// (MenuSeed) πρέπει να βγαίνουν ΑΚΡΙΒΩΣ όπως θα τις περνούσε ο ταμίας — ίδιο όνομα, ίδιες λεπτομέρειες, ίδιο χαρτί.
// Η αντιστοίχιση με τα ονόματα του e-food χτίζεται πάνω στον πραγματικό κατάλογο του e-food.
//
//   dotnet run --project tools/EfoodCheck

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("el-GR");
Console.OutputEncoding = Encoding.UTF8;
var menu = new SeedMenu();
var fails = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "OK   " : "FAIL ") + what);
    if (!ok) fails++;
}

Console.WriteLine("--- παραγγελίες: βγαίνουν όπως θα τις περνούσε ο ταμίας");
var (pita, pitaCategory) = Find((p, c) => menu.HasBreadChoice(c.Name) && menu.FuseBreadIntoName(c.Name) && menu.SupportsDoublePita(c.Name)
    && p.Customizable && menu.IngredientsFor(p).Count >= 4 && EfoodMenuRules.ExtrasFor(p, menu.Extras).Count > 0);
Check(pita is not null, $"πίττα με ψωμί στο όνομα, υλικά, έξτρα και διπλή πίτα: {pita?.Name} ({pitaCategory?.Name})");
if (pita is not null && pitaCategory is not null)
{
    var price = pita.DeliveryPrice ?? pita.Price;
    var ingredient = menu.IngredientsFor(pita)[1];
    var extras = EfoodMenuRules.ExtrasFor(pita, menu.Extras);
    var extra = extras.FirstOrDefault(e => e.Price > 0) ?? extras[0];

    var line = Map(Order(Item(pita.Name, price, 2, "καλοψημένη", "",
        ("Αραβική", 0m), ("Χωρίς " + Lower(ingredient), 0m), (extra.Name, extra.Price)))).Lines[0];
    Check(line.Name == MenuSeed.ComposeCustomizedName(pita.Name, "Αραβική")
          && line.PrintName == MenuSeed.ComposeCustomizedName(pita.NameForPrint, "Αραβική"), $"ψωμί μέσα στο όνομα, όπως του ταμία: «{line.Name}»");
    Check(line.Details == $"καλοψημένη\nχωρίς:\n{Lower(ingredient)}\n+ {extra.Name}",
        "σημείωση, «χωρίς:», «+ έξτρα», με τη σειρά του ταμία: " + line.Details.Replace("\n", " | "));
    Check(line.Quantity == 2 && line.Revenue == (price + extra.Price) * 2, $"τζίρος (τιμή + έξτρα) × 2 = {line.Revenue}");
    Check(line.ProductId == pita.Id && line.Customization is { Bread: "Αραβική", DoublePita: false } custom
          && custom.Removed.SequenceEqual(new[] { ingredient }) && custom.Extras.GetValueOrDefault(extra.Name) == 1,
        "ίδιο προϊόν και ιδιαιτερότητες — για στατιστικά, ΦΠΑ και «ΜΙΑ ΑΠΟ ΤΑ ΙΔΙΑ»");

    var doublePrice = menu.DoublePitaPriceFor(pitaCategory.Name);
    var doubled = Map(Order(Item(pita.Name, price, 1, "", "", ("Διπλή πίτα", doublePrice)))).Lines[0];
    Check(doubled.Name == MenuSeed.ComposeDoublePitaName(pita.Name, pitaCategory.Name, "Ελληνική")
          && doubled.Revenue == price + doublePrice, $"διπλή πίτα: «{doubled.Name}» {doubled.Revenue}");

    var everything = menu.IngredientsFor(pita).Select(i => ("Χωρίς " + Lower(i), 0m)).ToArray();
    Check(Map(Order(Item(pita.Name, price, 1, "", "", everything))).Lines[0].Details == "σκέτο", "όλα «Χωρίς» → σκέτο");

    var plain = Map(Order(Item(pita.Name, price, 1, "", ""))).Lines[0];
    Check(plain.Name == MenuSeed.ComposeCustomizedName(pita.Name, "Ελληνική") && plain.Details == "",
        "χωρίς καμία επιλογή → η προεπιλογή του ταμείου (Ελληνική), τίποτα από κάτω");

    var byCode = Map(Order(Item("Όνομα όπως το γράφει το e-food", price, 1, "", pita.Id))).Lines[0];
    Check(byCode.ProductId == pita.Id && byCode.Name == MenuSeed.ComposeCustomizedName(pita.Name, "Ελληνική"),
        "με κωδικό του ταμείου: βγαίνει με το όνομα του ταμείου, όχι του e-food");

    var odd = Map(Order(Item(pita.Name, price, 1, "", "", ("Τρούφα", 2.5m))));
    Check(odd.Lines[0].Details == "+ Τρούφα" && odd.Lines[0].Revenue == price + 2.5m && odd.Unmatched.Count == 1,
        "άγνωστη επιλογή → «+ Τρούφα» στο χαρτί και στον τζίρο, και γράφεται στο log");
}

var (meal, mealCategory) = Find((p, c) => menu.HasBreadChoice(c.Name) && !menu.FuseBreadIntoName(c.Name) && menu.OpensIngredients(p));
Check(meal is not null, $"μερίδα με ψωμί σε δική του γραμμή: {meal?.Name} ({mealCategory?.Name})");
if (meal is not null)
{
    var line = Map(Order(Item(meal.Name, meal.Price, 1, "", "", ("Ψωμί", 0m)))).Lines[0];
    Check(line.Name == meal.Name && line.Details.Split('\n')[0] == "Ψωμί", $"«{line.Name}» με «Ψωμί» από κάτω");
}

var (drink, _) = Find((p, _) => !menu.OpensIngredients(p));
Check(drink is not null, $"προϊόν χωρίς υλικά: {drink?.Name}");
if (drink is not null)
{
    var line = Map(Order(Item(drink.Name, drink.Price, 3, "χωρίς πάγο", ""))).Lines[0];
    Check(line.Name == drink.Name && line.Details == "χωρίς πάγο" && line.Customization is null && line.Quantity == 3
          && line.ProductId == drink.Id, "χωρίς υλικά: όνομα όπως είναι, μόνο η σημείωση του πελάτη");

    var stranger = Map(Order(Item("Κάτι άλλο", 3m, 1, "", "den-yparxei", ("Σάλτσα", 0.5m))));
    Check(stranger.Lines[0].Name == "Κάτι άλλο" && stranger.Lines[0].ProductId == "" && stranger.Lines[0].Details == "+ Σάλτσα"
          && stranger.Lines[0].Revenue == 3.5m && stranger.Unmatched.Contains("Κάτι άλλο"),
        "άγνωστο προϊόν → μπαίνει με το όνομα του e-food, τίποτα δεν χάνεται");

    Console.WriteLine("--- πάνω μέρος του δελτίου");
    var own = Map(Order(Item(drink.Name, drink.Price, 1, "", "")));
    Check(own.OwnDelivery && own.Phone == "6900000000" && own.Notes.StartsWith("ΜΕΤΡΗΤΑ") && own.Notes.Contains("ΧΩΡΙΣ ΜΑΧΑΙΡΟΠΙΡΟΥΝΑ"),
        "δική μας διανομή: τηλέφωνο, «ΜΕΤΡΗΤΑ … εισπράττει ο διανομέας», «ΧΩΡΙΣ ΜΑΧΑΙΡΟΠΙΡΟΥΝΑ»");
    var platform = Order(Item(drink.Name, drink.Price, 1, "", ""));
    platform["transport_method"]!["delivery_provider"] = "platform_delivery";
    var rider = Map(platform);
    Check(!rider.OwnDelivery && rider.Phone == "" && rider.Address == "", "rider του e-food: κανένα τηλέφωνο ή διεύθυνση στο χαρτί");
    var pickup = Order(Item(drink.Name, drink.Price, 1, "", ""));
    pickup["type"] = "takeaway";
    pickup["payment_type"] = "credit_card";
    Check(Map(pickup).Notes.StartsWith("ΠΑΡΑΛΑΒΗ — πληρωμένη"), "παραλαβή πληρωμένη με κάρτα");
}

Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
return fails == 0 ? 0 : 1;

// ── βοηθητικά ─────────────────────────────────────────────────────────────────────────────────────

EfoodImport Map(JsonObject order) => EfoodOrderReader.Map(order, menu);

(Product? Product, MenuCategory? Category) Find(Func<Product, MenuCategory, bool> match)
{
    foreach (var category in menu.Categories)
        foreach (var product in category.Products)
            if (match(product, category))
                return (product, category);
    return (null, null);
}

/// <summary>Ένα προϊόν όπως θα ερχόταν μέσα σε παραγγελία e-food.</summary>
static JsonObject Item(string name, decimal price, int quantity, string notes, string integratorId,
    params (string Name, decimal Price)[] materials) => new()
{
    ["id"] = "e1", ["name"] = name, ["price"] = price, ["discount_price"] = null, ["quantity"] = quantity,
    ["notes"] = notes, ["integrator_id"] = integratorId,
    ["materials"] = new JsonArray(materials.Select(m => (JsonNode?)new JsonObject
    {
        ["id"] = "m", ["name"] = m.Name, ["quantity"] = 1, ["price"] = m.Price, ["notes"] = "", ["integrator_id"] = "",
    }).ToArray()),
};

static JsonObject Order(params JsonObject[] items) => new()
{
    ["id"] = 123456, ["short_code"] = "", ["type"] = "delivery", ["price"] = 10m, ["payment_type"] = "cash",
    ["customer"] = new JsonObject { ["name"] = "Δοκιμή", ["surname"] = "Πελάτης", ["telephone"] = "6900000000", ["address"] = "Λεωφ. Δοκιμής 1" },
    ["transport_method"] = new JsonObject { ["key"] = "delivery", ["delivery_provider"] = "vendor_delivery" },
    ["products"] = new JsonArray(items.Cast<JsonNode?>().ToArray()),
    ["offers"] = new JsonArray(),
    ["extra_parameters"] = new JsonArray("no-cutlery"),
};

static string Lower(string text) => text.Length > 0 ? char.ToLower(text[0]) + text[1..] : text;

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
