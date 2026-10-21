using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using PittaPos.Core.Box;
using PittaPos.Core.Data;
using PittaPos.Core.Efood;
using PittaPos.Core.Models;

// ΕΛΕΓΧΟΣ του e-food στο ταμείο, χωρίς ταμείο: οι ΠΡΑΓΜΑΤΙΚΕΣ παραγγελίες του sandbox (14/9/2026, με σβησμένα
// προσωπικά στοιχεία) πάνω στον κατάλογο του e-food και στον MenuSeed, και ό,τι γίνεται όταν δεν υπάρχει κατάλογος.
// Και η Box (21/10/2026), που διαβάζεται από τον ίδιο δρόμο: ό,τι δίνει η γέφυρα Box στο ταμείο (box-till-orders.json,
// box-catalog.json — το παράδειγμα της τεκμηρίωσης της Box, μέσα από την πραγματική γέφυρα και την ψεύτικη Box).
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
    var relayed = new JsonObject { ["ok"] = true, ["status"] = 200, ["body"] = File.ReadAllText(Path.Combine(samples, "catalog-subset.json"), Encoding.UTF8) };
    Check(EfoodCatalogParser.Parse(relayed.ToJsonString()).Count == subset.Count,
        "ο κατάλογος όπως τον φέρνει η γέφυρα (GET /till/catalog: { ok, status, body }) διαβάζεται ίδιος");
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

    var known = EfoodOrderReader.Map(OrderFrom("order-11.json"), menu, new EfoodContext(subset, []));
    Check(known.Lines.All(l => l.ProductId == "") && known.Lines[0].Name == "Πίττα κοτόπουλο" && known.Unmatched.Count == 3,
        "το ξέρει ο κατάλογος του e-food αλλά δεν έχει αντιστοίχιση → με το όνομα του e-food, όχι μαντεψιά με το όνομα");

    Console.WriteLine("--- οι 4 του e-food (24/9/2026): κάρτα, κουπόνι, παραλαβή, joker");
    bool Adds(string file, EfoodImport map) =>
        map.PlatformTotal == map.Total + EfoodOrderReader.Fees(OrderFrom(file)) && map.Total == map.Lines.Sum(l => l.Revenue);
    var o16 = EfoodOrderReader.Map(OrderFrom("order-16-card.json"), menu, context);
    Check(o16.OwnDelivery && o16.Total == 17.10m && Adds("order-16-card.json", o16),
        $"#16 κάρτα: 17,10 + 0,50 = 17,60 του e-food — το φιλοδώρημα 1,00 ΔΕΝ είναι μέσα ({o16.Total})");
    Check(o16.Notes.StartsWith("ΠΛΗΡΩΜΕΝΗ ΜΕ ΚΑΡΤΑ · ΦΙΛΟΔΩΡΗΜΑ €1,00"), $"«{o16.Notes}»");

    var o17 = EfoodOrderReader.Map(OrderFrom("order-17-coupon.json"), menu, context);
    Check(o17.Total == 12.40m && o17.Lines.All(l => l.Revenue == 6.20m) && Adds("order-17-coupon.json", o17),
        $"#17 κουπόνι του καταστήματος: τζίρος 14,40 − 2,00 = 12,40, μοιρασμένο 6,20 + 6,20 (+ 0,50 = 12,90 του e-food) ({o17.Total})");
    Check(o17.Notes.StartsWith("ΠΛΗΡΩΜΕΝΗ ΜΕ ΚΑΡΤΑ · ΕΚΠΤΩΣΗ ΚΟΥΠΟΝΙΟΥ −€2,00") && !o17.Notes.Contains("ΦΙΛΟΔΩΡΗΜΑ"), $"«{o17.Notes}»");

    var o18 = EfoodOrderReader.Map(OrderFrom("order-18-takeaway.json"), menu, context);
    Check(o18.Lines.Count == 1 && o18.Lines[0].Quantity == 2 && o18.Lines[0].Revenue == 14.40m
          && o18.Lines[0].Name == MenuSeed.ComposeCustomizedName(chicken.Product.Name, "Ελληνική") && Adds("order-18-takeaway.json", o18),
        $"#18 παραλαβή: 2 × «{o18.Lines[0].Name}» = 14,40 (το price του προϊόντος είναι ανά τεμάχιο)");
    Check(!o18.OwnDelivery && o18.Phone == "" && o18.Address == "" && o18.Notes.StartsWith("ΠΑΡΑΛΑΒΗ — πληρωμένη"), $"«{o18.Notes}»");

    var o19 = EfoodOrderReader.Map(OrderFrom("order-19-joker.json"), menu, context);
    Check(o19.Total == 24.30m && Adds("order-19-joker.json", o19),
        $"#19 Πεινιάτα: τζίρος 34,30 − 10 = 24,30 (+ 0,50 = 24,80) — το standard_discount είναι το ίδιο 10, όχι δεύτερη έκπτωση ({o19.Total})");
    Check(o19.Lines[0].Revenue + o19.Lines[1].Revenue == 24.30m && o19.Lines[0].Revenue == 19.27m,
        $"μοιρασμένο αναλογικά: λουκάνικο 27,20 → {o19.Lines[0].Revenue}, φιλέτο 7,10 → {o19.Lines[1].Revenue}");
    Check(o19.Notes.StartsWith("ΜΕΤΡΗΤΑ €24,80 — εισπράττει ο διανομέας · ΕΚΠΤΩΣΗ ΠΕΙΝΙΑΤΑΣ −€10,00"),
        $"μετρητά = ό,τι πληρώνει ο πελάτης, και γράφει την Πεινιάτα: «{o19.Notes}»");
    var efoodPays = OrderFrom("order-17-coupon.json");
    efoodPays["coupons"]![0]!["paid_by_vendor"] = false;
    Check(EfoodOrderReader.Map(efoodPays, menu, context).Total == 14.40m, "κουπόνι που πληρώνει το e-food → ο τζίρος μένει ολόκληρος");
    Console.WriteLine("     δεν βρέθηκαν χωρίς αντιστοίχιση: " + string.Join(", ", new[] { o16, o17, o18, o19 }.SelectMany(o => o.Unmatched)));

    Console.WriteLine("--- γραμμές της ΑΝΤΙΣΤΟΙΧΙΣΗΣ");
    var autos = EfoodContext.AutoMatches(subset, [], menu);
    Check(autos.Count == subset.Count && EfoodContext.AutoMatches(subset, autos, menu).Count == 0,
        $"αυτόματες γραμμές για τα {subset.Count} με ίδια τιμή ({autos.Count}), και δεύτερη φορά καμία καινούρια");
    Check(EfoodMatcher.StateOf(null, menu) == EfoodMatchState.Pending
          && EfoodMatcher.StateOf(autos[0], menu) == EfoodMatchState.Auto
          && EfoodMatcher.StateOf(new EfoodMatch { EfoodId = "1", ProductId = "δεν-υπάρχει-πια", Confirmed = true }, menu) == EfoodMatchState.Pending
          && EfoodMatcher.StateOf(new EfoodMatch { EfoodId = "1", Confirmed = true }, menu) == EfoodMatchState.Confirmed,
        "κατάσταση: καμία γραμμή / αυτόματη / σβησμένο προϊόν ταμείου = προς επιβεβαίωση / «δεν υπάρχει στο ταμείο» επιβεβαιωμένο");
    var chickenItem = subset.First(i => i.Name == "Πίττα κοτόπουλο");
    var chickenPreview = EfoodOrderReader.Preview(chickenItem, autos.First(m => m.EfoodId == chickenItem.Id), menu);
    Check(chickenPreview == o11.Lines[0].PrintName, $"προεπισκόπηση «{chickenPreview}» = ό,τι τύπωσε η παραγγελία");
    var burgerItem = subset.First(i => i.Name == "Burger απλό");
    var burgerPreview = EfoodOrderReader.Preview(burgerItem, autos.First(m => m.EfoodId == burgerItem.Id), menu);
    Check(burgerPreview == o12.Lines[1].PrintName, $"προεπισκόπηση burger «{burgerPreview}» χωρίς «χωρίς:» από τα προεπιλεγμένα");
    Check(EfoodOrderReader.Preview(burgerItem, null, menu) == "Burger απλό", "χωρίς αντιστοίχιση → το όνομα του e-food");
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

Console.WriteLine("--- λόγος ακύρωσης του e-food («{ποιος}:{λόγος}», 13/10/2026) στο χαρτί ΑΚΥΡΩΣΗ");
Check(EfoodOrderReader.CancelReasonText("vendor: ITEM_UNAVAILABLE") == "ακύρωσε το κατάστημα · δεν υπάρχει προϊόν",
    "vendor: ITEM_UNAVAILABLE → " + EfoodOrderReader.CancelReasonText("vendor: ITEM_UNAVAILABLE"));
Check(EfoodOrderReader.CancelReasonText("customer:TOO_BUSY") == "ακύρωσε ο πελάτης · πολύς φόρτος", "χωρίς κενό μετά την «:» → ίδιο");
Check(EfoodOrderReader.CancelReasonText("platform: SOMETHING_NEW") == "ακύρωσε το e-food · SOMETHING_NEW",
    "άγνωστος λόγος → μένει όπως ήρθε, δεν χάνεται");
Check(EfoodOrderReader.CancelReasonText("") == "" && EfoodOrderReader.CancelReasonText("TEST_ORDER") == "δοκιμαστική",
    "κενό → κενό · χωρίς «ποιος» → μόνο ο λόγος");

Console.WriteLine("--- Box: ό,τι δίνει η γέφυρα Box στο ταμείο (το παράδειγμα της τεκμηρίωσης, 21/10/2026)");
if (samples is not null)
{
    var tillOrders = JsonNode.Parse(File.ReadAllText(Path.Combine(samples, "box-till-orders.json"), Encoding.UTF8)) as JsonArray;
    var docOrder = (tillOrders?[0]?["order"] as JsonObject)!;
    var doc = BoxOrderReader.Map(docOrder, menu);
    Check(tillOrders?[0]?["boxId"]?.GetValue<string>() == "A1B2C3D4E5F6" && doc.Order.Ref == "MIX001",
        $"η γέφυρα δίνει boxId και την παραγγελία όπως ήρθε · κωδικός στο μαγαζί = simplifiedId ({doc.Order.Ref})");
    Check(doc.Order.Lines.Count == 2 && doc.Order.Lines[0] is { Name: "Croissant", Quantity: 1 } && doc.Order.Lines[0].Revenue == 2.70m
          && doc.Order.Lines[1] is { Name: "Combo Καφέ", Quantity: 1 } && doc.Order.Lines[1].Revenue == 4.00m,
        "λεπτά → ευρώ: Croissant 2,70 και η προσφορά «Combo Καφέ» 4,00");
    Check(doc.Order.Lines[1].Details == "Cappuccino\nEspresso", "η προσφορά γράφει από κάτω τι έχει μέσα: " + doc.Order.Lines[1].Details.Replace("\n", " | "));
    Check(doc.Order.Total == 6.70m && doc.ItemsTotal == 6.70m && doc.Order.PlatformTotal == 8.20m,
        $"σύνολο γραμμών 6,70 = orderItemsPrice της Box · με μεταφορικά 1,50 = 8,20 ({doc.Order.Total})");
    Check(doc.IsTest, "δοκιμαστική (isTestOrder) → το ταμείο ΔΕΝ τη βάζει στα έσοδα");
    Check(doc.Payment == PaymentMethod.Card && doc.PaidInApp && doc.Order.Notes.StartsWith("ΠΛΗΡΩΜΕΝΗ ΜΕ ΚΑΡΤΑ"),
        $"κάρτα = πληρωμένη στην εφαρμογή: «BOX Κάρτα», χωρίς προφόρτωση στο POS του διανομέα · «{doc.Order.Notes}»");
    Check(doc.Order is { Who: "Γιώργος Παπαδόπουλος", Phone: "6912345678", Address: "Ερμού 15, Αθήνα, 11525", Floor: "1ος · Κουδούνι: Παπαδόπουλος" },
        $"δικός μας διανομέας: όνομα, τηλέφωνο χωρίς +30, διεύθυνση, όροφος και κουδούνι ({doc.Order.Phone} · {doc.Order.Address} · {doc.Order.Floor})");
    Check(doc is { Street: "Ερμού", StreetNumber: "15", Area: "Αθήνα", PostalCode: "11525" }, "η διεύθυνση και σε κομμάτια, για το πελατολόγιο");

    var boxCatalog = BoxCatalogParser.Parse(File.ReadAllText(Path.Combine(samples, "box-catalog.json"), Encoding.UTF8));
    Check(boxCatalog.Count == 3 && boxCatalog.All(i => i.Id == i.Code && i.Price == 0m)
          && boxCatalog[0] is { Id: "prod_001", Name: "Cappuccino", Category: "ΚΑΦΕΣ" } && boxCatalog[0].Tiers[0].Options.Count == 2,
        "ο κατάλογος της Box (GET /till/catalog της γέφυρας): 3 προϊόντα με friendlyId, κατηγορία και επιλογές, χωρίς τιμές");
    Check(boxCatalog[1].Category == "ΚΑΦΕΣ · μόνο σε προσφορές", "προϊόν μόνο για προσφορές → φαίνεται στην ΑΝΤΙΣΤΟΙΧΙΣΗ");
    Check(EfoodContext.AutoMatches(boxCatalog, [], menu).Count == 0, "χωρίς τιμές καμία αυτόματη αντιστοίχιση — κάθε πρόταση θέλει «ΣΩΣΤΟ»");
    var known = BoxOrderReader.Map(docOrder, menu, new EfoodContext(boxCatalog, []));
    Check(known.Order.Lines[0].Name == "Croissant" && known.Order.Unmatched.Contains("Croissant"),
        "το ξέρει ο κατάλογος της Box αλλά δεν έχει αντιστοίχιση → με το όνομα της Box");
}
{
    var chicken = Product("Πίττα κοτόπουλο");
    var pitaTier = new EfoodCatalogTier("SEL-PITA", "Επιλέξτε πίτα", "", "",
        [new EfoodCatalogOption("CH-ARAB", "CH-ARAB", "Αραβική", "", 0m, false), new EfoodCatalogOption("CH-GR", "CH-GR", "Ελληνική", "", 0m, false)]);
    var pitaItem = new EfoodCatalogItem("PITA-KOT", "PITA-KOT", "Πίτα κοτόπουλο", "Πίτες", 0m, true, [pitaTier]);
    var pitaMatch = new EfoodMatch { EfoodId = "PITA-KOT", ProductId = chicken.Product.Id, Confirmed = true };
    var matched = new EfoodContext([pitaItem], [pitaMatch]);

    var arabic = BoxOrderReader.Map(BoxOrder("cash", BoxProduct("PITA-KOT", "Πίτα κοτόπουλο", 420, 2, ("CH-ARAB", "Αραβική"))), menu, matched);
    Check(arabic.Order.Lines[0].Name == MenuSeed.ComposeCustomizedName(chicken.Product.Name, "Αραβική") && arabic.Order.Lines[0].Quantity == 2
          && arabic.Order.Lines[0].Revenue == 8.40m && arabic.Order.Lines[0].ProductId == chicken.Product.Id && arabic.Order.Unmatched.Count == 0,
        $"αντιστοιχισμένο προϊόν + «Αραβική» → «{arabic.Order.Lines[0].Name}», 2 × 4,20 = 8,40, προϊόν του ταμείου");
    Check(EfoodOrderReader.Preview(pitaItem, pitaMatch, menu) == MenuSeed.ComposeCustomizedName(chicken.Product.NameForPrint, "Ελληνική"),
        "προεπισκόπηση της ΑΝΤΙΣΤΟΙΧΙΣΗΣ για προϊόν της Box (ψωμί: η προεπιλογή του ταμείου)");
    Check(arabic.Payment == PaymentMethod.Cash && !arabic.PaidInApp && !arabic.IsTest
          && arabic.Order.Notes.StartsWith("ΜΕΤΡΗΤΑ €9,90 — εισπράττει ο διανομέας") && arabic.Order.Notes.Contains("ΦΙΛΟΔΩΡΗΜΑ €1,00"),
        $"μετρητά: ο διανομέας εισπράττει 8,40 + 1,50 μεταφορικά = 9,90, και το φιλοδώρημα χωριστά · «{arabic.Order.Notes}»");
    Check(arabic.Order.Total == 8.40m, "τζίρος = μόνο τα προϊόντα (το ταμείο δεν έχει μεταφορικά), όπως στο e-food");

    var pickup = BoxOrder("cash", BoxProduct("PITA-KOT", "Πίτα κοτόπουλο", 420, 1));
    pickup["orderShippingType"] = "takeAway";
    pickup.Remove("address");
    pickup["deliveryFee"] = 0;
    var p = BoxOrderReader.Map(pickup, menu, matched);
    Check(p.Order.Notes.StartsWith("ΠΑΡΑΛΑΒΗ — πληρώνει μετρητά €4,20") && p.Order.Phone == "" && p.Payment == PaymentMethod.Cash,
        $"παραλαβή με μετρητά: τα εισπράττει το μαγαζί · «{p.Order.Notes}»");

    var courier = BoxOrder("cash", BoxProduct("PITA-KOT", "Πίτα κοτόπουλο", 420, 1));
    courier["daasInfo"] = new JsonObject { ["minPreparationMinutesAllowed"] = 15, ["maxPreparationMinutesAllowed"] = 90, ["recommendedPreparationMinutes"] = 25 };
    courier.Remove("tip");
    courier.Remove("deliveryFee");
    var c = BoxOrderReader.Map(courier, menu, matched);
    Check(c.Order.Notes.StartsWith("ΔΙΑΝΟΜΗ Box") && c.Order.Phone == "" && c.PaidInApp && c.Payment == PaymentMethod.Card,
        $"κούριερ της Box (daasInfo): χωρίς τηλέφωνο/διεύθυνση, και τα λεφτά πάνε στη Box, όχι στο συρτάρι · «{c.Order.Notes}»");

    var later = BoxOrder("card", BoxProduct("PITA-KOT", "Πίτα κοτόπουλο", 420, 1));
    later["timeSlotStart"] = "2025-12-08T19:00:00.000Z";
    later["timeSlotEnd"] = "2025-12-08T19:30:00.000Z";
    Check(BoxOrderReader.Map(later, menu, matched).Order.Notes.StartsWith("ΓΙΑ ΤΙΣ 21:00–21:30 · "),
        "προγραμματισμένη: πρώτο στο χαρτί η ώρα, σε ώρα Ελλάδας (19:00Z → 21:00)");
    var tomorrow = BoxOrder("card", BoxProduct("PITA-KOT", "Πίτα κοτόπουλο", 420, 1));
    tomorrow["timeSlotStart"] = "2025-12-09T10:00:00.000Z";
    Check(BoxOrderReader.Map(tomorrow, menu, matched).Order.Notes.StartsWith("ΓΙΑ ΤΙΣ 12:00 (9/12)"), "άλλη μέρα → και η ημερομηνία");

    var unknown = BoxOrderReader.Map(BoxOrder("cash", BoxProduct("X-1", "Κάτι καινούριο", 350, 1, ("X-OPT", "Σάλτσα"))), menu, matched);
    Check(unknown.Order.Lines[0] is { Name: "Κάτι καινούριο", ProductId: "", Details: "Σάλτσα" } && unknown.Order.Unmatched.Contains("Κάτι καινούριο"),
        "άγνωστο προϊόν → με το όνομα της Box και ό,τι επιλογές ήρθαν — τίποτα δεν χάνεται");
    var empty = BoxOrderReader.Map(new JsonObject(), menu);
    Check(empty.Order.Lines.Count == 0 && empty.Order.Total == 0m && empty.Payment == PaymentMethod.Cash, "άδεια παραγγελία → καμία εξαίρεση");
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

/// <summary>Ένα προϊόν όπως έρχεται μέσα σε παραγγελία της Box: τιμή ανά τεμάχιο σε λεπτά, με τις επιλογές ήδη μέσα.</summary>
static JsonObject BoxProduct(string friendlyId, string name, int cents, int quantity, params (string Id, string Title)[] options) => new()
{
    ["name"] = name, ["category"] = "", ["quantity"] = quantity, ["comments"] = "", ["finalPrice"] = cents, ["totalEnvFee"] = 0,
    ["friendlyId"] = friendlyId,
    ["selections"] = new JsonArray(options.Length == 0 ? [] : [new JsonObject
    {
        ["title"] = "Επιλέξτε πίτα", ["friendlyId"] = "SEL-PITA",
        ["optionsSelected"] = new JsonArray(options.Select(o => (JsonNode?)new JsonObject
        {
            ["friendlyId"] = o.Id, ["changeInPrice"] = 0, ["title"] = o.Title,
        }).ToArray()),
    }]),
};

/// <summary>Παραγγελία Box με δικό μας διανομέα, 1,50 μεταφορικά και 1,00 φιλοδώρημα.</summary>
static JsonObject BoxOrder(string payment, params JsonObject[] products) => new()
{
    ["address"] = new JsonObject
    {
        ["street"] = "Λεωφ. Δοκιμής", ["streetNo"] = "1", ["city"] = "Δάφνη", ["postalCode"] = "17235", ["floor"] = "2ος", ["nameAtBell"] = "Πελάτης",
    },
    ["shopId"] = 255301, ["orderId"] = "ABCDEF123456", ["simplifiedId"] = "T1",
    ["user"] = new JsonObject { ["id"] = "x", ["firstName"] = "Δοκιμή", ["lastName"] = "Πελάτης" },
    ["paymentType"] = payment, ["isTestOrder"] = false, ["comments"] = "", ["orderShippingType"] = "delivery", ["orderShippingTime"] = 40,
    ["orderContactPhone"] = "6900000000",
    ["orderItemsPrice"] = products.Sum(p => p["finalPrice"]!.GetValue<int>() * p["quantity"]!.GetValue<int>()),
    ["createdAt"] = "2025-12-08T18:30:00.000Z", ["timeSlotStart"] = "", ["timeSlotEnd"] = "",
    ["products"] = new JsonArray(products.Cast<JsonNode?>().ToArray()),
    ["offers"] = new JsonArray(),
    ["tip"] = 100, ["deliveryFee"] = 150,
};

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
