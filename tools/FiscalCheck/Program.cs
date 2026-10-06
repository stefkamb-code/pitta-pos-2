using System.Text;
using System.Text.Json;
using PittaPos.Core.Fiscal;

// Έλεγχος παραστατικών χωρίς ταμείο.
//
//   dotnet run --project tools/FiscalCheck                 όλα τα σενάρια με τον εικονικό πάροχο + έλεγχοι ποσών
//   dotnet run --project tools/FiscalCheck -- payloads     τι ακριβώς θα σταλεί στον Wrapp για κάθε σενάριο
//   WRAPP_EMAIL=… WRAPP_KEY=… dotnet run --project tools/FiscalCheck -- wrapp
//                                                         τα ίδια σενάρια στο δοκιμαστικό (sandbox) του Wrapp

Console.OutputEncoding = Encoding.UTF8;
var mode = args.FirstOrDefault() ?? "simulated";
var failures = 0;

void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "  ✓ " : "  ✗ ") + what);
    if (!ok) failures++;
}

// Ο κατάλογος του μαγαζιού: φαγητό 13%, ποτά 24%, αναψυκτικά 13% — 24% στο τραπέζι. Όπως τον όρισε το μαγαζί.
TillLine Food(string n, int q, decimal r) => new(n, q, r, 13);
TillLine Soft(string n, int q, decimal r, bool table) => new(n, q, r, table ? 24 : 13);
TillLine Beer(string n, int q, decimal r) => new(n, q, r, 24);

var terminal = Environment.GetEnvironmentVariable("WRAPP_TERMINAL") ?? "T88011";
var day = DateTime.Now.ToString("yyyyMMddHHmmss");

// ---------------------------------------------------------------- 1. τα ποσά βγαίνουν ακριβώς

Console.WriteLine("Ποσά και ΦΠΑ");
{
    var lines = FiscalDocumentBuilder.Lines([Food("ΠΙΤΤΑ ΚΟΤΟΠΟΥΛΟ", 2, 14.00m), Soft("Coca-Cola 330ml", 1, 1.80m, false), Beer("Amstel 500ml", 1, 3.50m)], 19.30m);
    Check(lines.Sum(l => l.Gross) == 19.30m, "Σύνολο γραμμών = σύνολο παραγγελίας (19,30)");
    Check(lines.All(l => l.Net + l.Vat == l.Gross), "Καθαρό + ΦΠΑ = μικτό σε κάθε γραμμή");
    Check(lines[0].Net == 12.39m && lines[0].Vat == 1.61m, $"Πίττες 14,00 με 13% → καθαρό 12,39 / ΦΠΑ 1,61 (βγήκε {lines[0].Net} / {lines[0].Vat})");
    Check(lines[2].Net == 2.82m && lines[2].Vat == 0.68m, $"Μπύρα 3,50 με 24% → καθαρό 2,82 / ΦΠΑ 0,68 (βγήκε {lines[2].Net} / {lines[2].Vat})");

    var table = FiscalDocumentBuilder.Lines([Soft("Coca-Cola 330ml", 1, 1.80m, true)], 1.80m);
    Check(table[0].VatRate == 24, "Αναψυκτικό στο τραπέζι = 24%");

    // Έκπτωση κουπονιού μοιρασμένη στις γραμμές: τα λεπτά της στρογγυλοποίησης πάνε στη μεγαλύτερη γραμμή.
    var discounted = FiscalDocumentBuilder.Lines([Food("Α", 1, 3.333m), Food("Β", 1, 3.333m), Food("Γ", 1, 3.333m)], 10.00m);
    Check(discounted.Sum(l => l.Gross) == 10.00m, "Μετά από έκπτωση το σύνολο μένει ακριβώς 10,00");

    var free = FiscalDocumentBuilder.Lines([Food("ΠΙΤΤΑ", 1, 7.00m), Food("Κέρασμα", 1, 0m)], 7.00m);
    Check(free.Count == 1, "Το κέρασμα (0 €) δεν μπαίνει στην απόδειξη");
}

// ---------------------------------------------------------------- 2. τα σενάρια του μαγαζιού

var scenarios = new List<(string Name, Func<IFiscalProvider, Task<bool>> Run)>
{
    ("Όρθιος, μετρητά", async p =>
    {
        var r = await p.IssueAsync(FiscalDocumentBuilder.Receipt([Food("ΠΙΤΤΑ ΧΟΙΡΙΝΟ", 1, 7.00m)], 7.00m, FiscalPay.Cash, $"t1-{day}-1"));
        return Show(r);
    }),
    ("Διανομή, κάρτα στο τερματικό", async p =>
    {
        var r = await p.IssueAsync(FiscalDocumentBuilder.Receipt([Food("ΠΙΤΤΑ ΚΟΤΟΠΟΥΛΟ", 2, 14.40m), Soft("Coca-Cola 330ml", 2, 3.60m, false)], 18.00m, FiscalPay.Card, $"t1-{day}-2", terminal));
        return Show(r) && r.PosTxn.Length > 0;
    }),
    ("e-food πληρωμένη online", async p =>
    {
        var r = await p.IssueAsync(FiscalDocumentBuilder.Receipt([Food("Πίττα Κοτόπουλο Παππού", 1, 8.90m)], 8.90m, FiscalPay.Online, $"t1-{day}-3"));
        return Show(r);
    }),
    ("Τραπέζι: 2 γύροι (δελτία) και απόδειξη που τα κλείνει", async p =>
    {
        var s1 = await p.IssueAsync(FiscalDocumentBuilder.OrderSlip([Food("Χοιρινό μερίδα", 2, 21.00m), Soft("Coca-Cola 330ml", 2, 3.60m, true)], "Τραπέζι 5", $"t1-{day}-t5-1"));
        if (!Show(s1, "δελτίο 1")) return false;
        var s2 = await p.IssueAsync(FiscalDocumentBuilder.OrderSlip([Beer("Amstel 500ml", 2, 7.00m)], "Τραπέζι 5", $"t1-{day}-t5-2", s1.ProviderTableRef));
        if (!Show(s2, "δελτίο 2")) return false;
        var r = await p.IssueAsync(FiscalDocumentBuilder.Receipt(
            [Food("Χοιρινό μερίδα", 2, 21.00m), Soft("Coca-Cola 330ml", 2, 3.60m, true), Beer("Amstel 500ml", 2, 7.00m)], 31.60m,
            FiscalPay.Cash, $"t1-{day}-t5-p", tableName: "Τραπέζι 5", providerTableRef: s1.ProviderTableRef, slips: [s1.Mark, s2.Mark]));
        return Show(r, "απόδειξη");
    }),
    ("Τραπέζι: πιάτο που ακυρώθηκε πριν βγει (ακύρωση δελτίου)", async p =>
    {
        var s = await p.IssueAsync(FiscalDocumentBuilder.OrderSlip([Food("Σαλάτα Χωριάτικη", 1, 7.50m)], "Τραπέζι 7", $"t1-{day}-t7-1"));
        if (!Show(s, "δελτίο")) return false;
        var c = await p.CancelOrderSlipAsync(s.Mark);
        return Show(c, "ακύρωση");
    }),
    ("Επιστροφή χρημάτων (πιστωτικό)", async p =>
    {
        var r = await p.IssueAsync(FiscalDocumentBuilder.Receipt([Food("ΠΙΤΤΑ ΧΟΙΡΙΝΟ", 1, 7.00m)], 7.00m, FiscalPay.Cash, $"t1-{day}-6"));
        if (!Show(r, "απόδειξη")) return false;
        var c = await p.IssueAsync(FiscalDocumentBuilder.Credit([Food("ΠΙΤΤΑ ΧΟΙΡΙΝΟ", 1, 7.00m)], FiscalPay.Cash, r.Mark, $"t1-{day}-6c"));
        return Show(c, "πιστωτικό");
    }),
    ("Τιμολόγιο σε επιχείρηση", async p =>
    {
        var r = await p.IssueAsync(FiscalDocumentBuilder.Invoice([Food("ΠΙΤΤΑ ΚΟΤΟΠΟΥΛΟ", 10, 72.00m)], 72.00m, FiscalPay.Card, "094014201", "ΠΕΛΑΤΗΣ ΑΕ", $"t1-{day}-7", terminal));
        return Show(r);
    }),
};

static bool Show(FiscalResult r, string what = "")
{
    var label = what.Length > 0 ? what + ": " : "";
    Console.WriteLine(r.Ok
        ? $"      {label}ΜΑΡΚ {r.Mark}  {r.Series} {r.Number}{(r.PosTxn.Length > 0 ? "  POS " + r.PosTxn : "")}{(r.PendingTransmission ? "  (σε αναμονή διαβίβασης)" : "")}"
        : $"      {label}ΛΑΘΟΣ: {r.Error}");
    return r.Ok && r.Mark.Length > 0;
}

if (mode == "payloads")
{
    // Τι ακριβώς θα πάει στον Wrapp — για να το ελέγξουμε με το μάτι πριν τα πρώτα αληθινά.
    var json = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    var docs = new (string, FiscalDocument)[]
    {
        ("Όρθιος μετρητά", FiscalDocumentBuilder.Receipt([Food("ΠΙΤΤΑ ΧΟΙΡΙΝΟ", 1, 7.00m), Soft("Coca-Cola 330ml", 1, 1.80m, false)], 8.80m, FiscalPay.Cash, "t1-ex-1")),
        ("Κάρτα", FiscalDocumentBuilder.Receipt([Food("ΠΙΤΤΑ ΚΟΤΟΠΟΥΛΟ", 2, 14.40m)], 14.40m, FiscalPay.Card, "t1-ex-2", terminal)),
        ("Δελτίο τραπεζιού", FiscalDocumentBuilder.OrderSlip([Food("Χοιρινό μερίδα", 2, 21.00m), Soft("Coca-Cola 330ml", 2, 3.60m, true)], "Τραπέζι 5", "t1-ex-3")),
        ("Κλείσιμο τραπεζιού", FiscalDocumentBuilder.Receipt([Food("Χοιρινό μερίδα", 2, 21.00m), Soft("Coca-Cola 330ml", 2, 3.60m, true)], 24.60m, FiscalPay.Cash, "t1-ex-4", tableName: "Τραπέζι 5", providerTableRef: "<catering_table_id>", slips: ["400001948426203"])),
    };
    foreach (var (name, doc) in docs)
    {
        Console.WriteLine($"\n--- {name}");
        Console.WriteLine(JsonSerializer.Serialize(WrappProvider.Payload(doc, "<billing_book_id>", doc.Pay == FiscalPay.Card ? "<pos_device_id>" : null), json));
    }
    return 0;
}

IFiscalProvider provider;
if (mode == "wrapp")
{
    var email = Environment.GetEnvironmentVariable("WRAPP_EMAIL") ?? "";
    var key = Environment.GetEnvironmentVariable("WRAPP_KEY") ?? "";
    if (email.Length == 0 || key.Length == 0)
    {
        Console.WriteLine("Χρειάζονται WRAPP_EMAIL και WRAPP_KEY (δοκιμαστικός λογαριασμός).");
        return 2;
    }
    var settings = new FiscalSettings("Wrapp", "sandbox", email, key, [new FiscalTerminal("Πάγκος", "Viva", "", terminal)], "same", "", "");
    provider = FiscalProviders.Create(settings, new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
}
else provider = new SimulatedProvider();

Console.WriteLine($"\nΣενάρια με «{provider.Name}»");
foreach (var (name, run) in scenarios)
{
    Console.WriteLine("  " + name);
    bool ok;
    try { ok = await run(provider); }
    catch (Exception ex) { Console.WriteLine("      ΕΞΑΙΡΕΣΗ: " + ex.Message); ok = false; }
    Check(ok, name);
}

// ---------------------------------------------------------------- 3. ό,τι δεν έχει οδηγό το λέει καθαρά

Console.WriteLine("\nΠάροχοι χωρίς οδηγό ακόμα");
var none = FiscalProviders.Create(FiscalSettings.None with { Provider = "Epsilon Digital" }, new HttpClient());
var refused = await none.IssueAsync(FiscalDocumentBuilder.Receipt([Food("ΠΙΤΤΑ", 1, 7m)], 7m, FiscalPay.Cash, "x"));
Check(!refused.Ok && refused.Error.Contains("Epsilon Digital"), "«" + refused.Error + "»");

Console.WriteLine(failures == 0 ? "\nΌλα εντάξει." : $"\n{failures} έλεγχοι απέτυχαν.");
return failures == 0 ? 0 : 1;
