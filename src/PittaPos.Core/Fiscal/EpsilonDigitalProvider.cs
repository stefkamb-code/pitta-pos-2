using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PittaPos.Core.Fiscal;

/// <summary>
/// Ο «μεταφραστής» για την Epsilon Digital — και για το Parochos Online, που τρέχει στην ίδια πλατφόρμα (ίδιο «myAccount»,
/// ίδιο <c>/api/send</c> με <c>X-Version: 3.0</c>, ίδια ονόματα πεδίων).
///
/// <para>Γραμμένος από τη ΔΗΜΟΣΙΑ τεκμηρίωση της πλατφόρμας (parochos.gr/files/API_Documentation_Provider.pdf, v3) και τις
/// διευθύνσεις της Epsilon από τον ανοιχτό client <c>@phoenixdev/epsilondigital-rest-api</c> (13/10/2026). <b>Δεν έχει
/// δοκιμαστεί σε sandbox</b> — γι' αυτό είναι στους <see cref="FiscalProviders.Experimental"/>: δεν ενεργοποιείται σε μαγαζί
/// πριν περάσει το ίδιο «συμβόλαιο» που πέρασε ο Wrapp.</para>
///
/// <list type="bullet">
/// <item>Σύνδεση: <c>POST {myAccount}/api/account/loginToSubscription</c> με subscription key + email + κωδικό → JWT και η
///   διεύθυνση του API (<c>url1</c>).</item>
/// <item>Παραστατικό: <c>POST {url1}/api/send</c> με το παραστατικό σε JSON (<c>source</c>). Σειρά και αύξοντα αριθμό τα δίνει
///   το ταμείο (βλ. FiscalJournal.SeriesFor). Ίδιο <c>externalSystemId</c> δεν κόβεται δεύτερη φορά.</item>
/// <item>Τραπέζι: «σημείο εξυπηρέτησης» — το πρώτο δελτίο το ανοίγει (openPointOfService), η απόδειξη που αφήνει το τραπέζι
///   χωρίς ανοιχτό δελτίο το κλείνει (closePointOfService).</item>
/// <item><b>Κάρτα: ΟΧΙ ακόμα.</b> Εδώ ο πάροχος δεν μιλά με το τερματικό· δίνει «υπογραφή πληρωμής» (<c>requestPayment</c>) που
///   πρέπει να στείλει το ίδιο το ταμείο στο POS της τράπεζας (WebECR). Μέχρι να γίνει αυτό, η κάρτα απορρίπτεται καθαρά.</item>
/// </list>
/// </summary>
public sealed class EpsilonDigitalProvider(FiscalSettings settings, HttpClient http, EpsilonDigitalProvider.Platform platform) : IFiscalProvider
{
    public enum Platform { Epsilon, Parochos }

    public const string EpsilonName = "Epsilon Digital";
    public const string ParochosName = "Parochos Online";

    public string Name => platform == Platform.Epsilon ? EpsilonName : ParochosName;

    private string AccountHost => (platform, settings.Production) switch
    {
        (Platform.Epsilon, true) => "https://myaccount.epsilonnet.gr",
        (Platform.Epsilon, false) => "https://beta-myaccount.epsilonnet.gr",
        (Platform.Parochos, true) => "https://account.parochos.gr",
        _ => "https://beta-account.parochos.gr",
    };

    /// <summary>Το «template» της συνδρομής — ορίζεται στο UI του παρόχου· προεπιλογή όπως στα παραδείγματά τους.</summary>
    private string Template => settings.Template.Length > 0 ? settings.Template : platform == Platform.Epsilon ? "eInvoicing" : "Parochos";

    private string _jwt = "";
    private DateTime _jwtUntil;
    private string _api = "";
    /// <summary>Η τεκμηρίωση λέει ότι το <c>source</c> είναι «string (JSON)», ενώ τα παραδείγματά της στέλνουν αντικείμενο.
    /// Ξεκινά με αντικείμενο· αν ο server το απορρίψει, δοκιμάζει κείμενο και το θυμάται.</summary>
    private bool _sourceAsText;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // ---------------------------------------------------------------- σύνδεση

    private async Task<string?> LoginAsync(CancellationToken ct)
    {
        if (_jwt.Length > 0 && DateTime.UtcNow < _jwtUntil) return null;
        if (settings.Key.Length == 0 || settings.User.Length == 0 || settings.Password.Length == 0)
            return $"Λείπουν τα στοιχεία σύνδεσης {Name} (subscription key, email, κωδικός) στις Ρυθμίσεις του μαγαζιού.";
        using var res = await http.PostAsJsonAsync(AccountHost + "/api/account/loginToSubscription",
            new { subscriptionKey = settings.Key, email = settings.User, password = settings.Password }, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) return $"{Name}: δεν δέχτηκε τα στοιχεία σύνδεσης ({(int)res.StatusCode}).";
        var o = Parse(text) as JsonObject;
        var jwt = S(o, "jwt");
        var api = S(o, "url1");
        if (jwt.Length == 0 || api.Length == 0) return $"{Name}: η σύνδεση δεν έδωσε κλειδί ή διεύθυνση API.";
        _jwt = jwt;
        _api = api.TrimEnd('/');
        _jwtUntil = DateTime.TryParse(S(o, "jwtExpiration"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var until)
            ? until.AddMinutes(-2)
            : DateTime.UtcNow.AddMinutes(20);
        return null;
    }

    private async Task<(JsonObject? Node, int Status, string Raw)> PostAsync(string path, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _api + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwt);
        req.Headers.Add("X-Version", "3.0");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = JsonContent.Create(body);
        using var res = await http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if ((int)res.StatusCode == 401) _jwt = "";
        return (Parse(text) as JsonObject, (int)res.StatusCode, text);
    }

    // ---------------------------------------------------------------- παραστατικά

    public async Task<FiscalResult> IssueAsync(FiscalDocument doc, CancellationToken ct = default)
    {
        if (doc.Pay == FiscalPay.Card && doc.Type != FiscalDocType.OrderSlip)
            return FiscalResult.Fail($"{Name}: η κάρτα θέλει σύνδεση ταμείου–τερματικού (WebECR της τράπεζας) — δεν έχει γίνει ακόμα.");
        await _gate.WaitAsync(ct);
        try
        {
            if (await LoginAsync(ct) is { } loginError) return FiscalResult.Fail(loginError);
            var source = Source(doc, settings);
            var (node, status, raw) = await PostAsync("/api/send", Body(doc.ExternalId, source), ct);
            if (status == 400 && !_sourceAsText)
            {
                _sourceAsText = true;
                (node, status, raw) = await PostAsync("/api/send", Body(doc.ExternalId, source), ct);
            }
            if (status is < 200 or >= 300) return FiscalResult.Fail($"{Name} {status}: {Short(raw)}");

            var result = await WaitAsync(node, doc, ct);
            // Ίδιο externalSystemId = κόπηκε ήδη σε προηγούμενη απόπειρα: φέρνουμε εκείνο, δεν κόβεται δεύτερο.
            if (!result.Ok && doc.ExternalId.Length > 0 && Code(node) is "00010022" or "00080003")
            {
                var (again, againStatus, _) = await PostAsync("/api/get", new { externalSystemId = doc.ExternalId }, ct);
                if (againStatus is >= 200 and < 300) result = await WaitAsync(again, doc, ct);
            }
            return result;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return FiscalResult.Fail($"{Name}: δεν απάντησε εγκαίρως.");
        }
        catch (HttpRequestException ex)
        {
            return FiscalResult.Fail($"Δεν υπάρχει σύνδεση με {Name} ({ex.Message}).");
        }
        finally { _gate.Release(); }
    }

    private object Body(string externalId, JsonObject source) => new Dictionary<string, object>
    {
        ["externalSystemId"] = externalId,
        // Η τεκμηρίωση v3 το λέει «identifier», ο client της Epsilon «templateIdentifier» — στέλνονται και τα δύο.
        ["identifier"] = Template,
        ["templateIdentifier"] = Template,
        ["transmissionType"] = 0,
        ["source"] = _sourceAsText ? source.ToJsonString() : source,
    };

    /// <summary>Η πρώτη απάντηση μπορεί να είναι «σε εξέλιξη» (status 0) — ρωτάμε το <c>/api/get</c> ως 30''.</summary>
    private async Task<FiscalResult> WaitAsync(JsonObject? node, FiscalDocument doc, CancellationToken ct)
    {
        for (var i = 0; i < 15 && Status(node) == 0; i++)
        {
            await Task.Delay(2000, ct);
            var (again, status, _) = await PostAsync("/api/get", new { externalSystemId = doc.ExternalId }, ct);
            if (status is >= 200 and < 300) node = again;
        }
        return Read(node, doc);
    }

    private FiscalResult Read(JsonObject? node, FiscalDocument doc)
    {
        var signing = node?["signing"] as JsonObject;
        var mark = S(signing, "mark");
        if (Status(node) != 1 || mark.Length == 0)
        {
            var message = S(node, "errorMessage");
            var code = Code(node);
            return Status(node) == 0
                ? FiscalResult.Fail($"{Name}: το παραστατικό είναι ακόμα σε επεξεργασία — θα ξαναδοκιμάσει.")
                : FiscalResult.Fail($"{Name}: {(message.Length > 0 ? message : "δεν κόπηκε")}{(code.Length > 0 ? $" ({code})" : "")}");
        }
        return new FiscalResult
        {
            Ok = true,
            Mark = mark,
            Uid = S(signing, "uid"),
            QrUrl = S(signing, "qrCode"),
            Series = doc.Series,
            Number = doc.Number,
            ProviderId = S(node, "processId"),
            // Το τραπέζι εδώ είναι το ΟΝΟΜΑ του σημείου εξυπηρέτησης — τα επόμενα δελτία δεν το ξανανοίγουν.
            ProviderTableRef = doc.Type == FiscalDocType.OrderSlip ? doc.TableName : "",
        };
    }

    public Task<FiscalResult> CancelOrderSlipAsync(string mark, CancellationToken ct = default) =>
        Task.FromResult(FiscalResult.Fail(
            $"{Name}: η ακύρωση δελτίου παραγγελίας δεν περιγράφεται στη δημόσια τεκμηρίωση — θέλει επιβεβαίωση από τον πάροχο."));

    // ---------------------------------------------------------------- το παραστατικό σε JSON (ενότητα 4.1 της τεκμηρίωσης)

    /// <summary>Το <c>source</c> — public για την προεπισκόπηση στο FiscalCheck.</summary>
    public static JsonObject Source(FiscalDocument doc, FiscalSettings settings)
    {
        var header = new JsonObject
        {
            ["series"] = doc.Series,
            ["aa"] = doc.Number,
            ["issueDate"] = doc.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["invoiceType"] = FiscalCodes.Code(doc.Type),
            ["invoiceTypeUbl"] = doc.Type == FiscalDocType.RetailCredit ? "381" : "380",
            ["currency"] = "EUR",
        };
        if (doc.TableName.Length > 0 && doc.Type == FiscalDocType.OrderSlip)
        {
            header["pointOfService"] = doc.TableName;
            header["openPointOfService"] = doc.ProviderTableRef.Length == 0 ? 1 : 0;
        }
        else if (doc.TableName.Length > 0 && doc.CorrelatedMarks.Count > 0)
        {
            header["pointOfService"] = doc.TableName;
            header["closePointOfService"] = doc.ClosesTable ? 1 : 0;
        }

        var slip = doc.Type == FiscalDocType.OrderSlip;
        var lines = new JsonArray();
        var n = 0;
        foreach (var l in doc.Lines)
        {
            var line = new JsonObject
            {
                ["lineNumber"] = ++n,
                ["quantity"] = l.Quantity,
                ["entityName"] = l.Name,
                ["netValue"] = l.Net,
                ["totalValue"] = l.Gross,
                ["vatCategory"] = VatCategory(l.VatRate),
                ["vatCategoryUbl"] = "S",
                ["vatAmount"] = l.Vat,
                ["vatPercent"] = l.VatRate,
                ["measurementUnit"] = 1,
                ["measurementUnitUbl"] = "H87",
            };
            if (!slip)
            {
                // Όπως στον Wrapp: πώληση αγαθών, ιδιωτική πελατεία (απόφαση χρήστη 13/10/2026 — αλλάζει με τον λογιστή).
                line["classificationCategory"] = "category1_1";
                line["classificationType"] = doc.Type == FiscalDocType.SalesInvoice ? "E3_561_001" : "E3_561_003";
            }
            lines.Add(line);
        }

        var invoice = new JsonObject
        {
            ["issuer"] = new JsonObject { ["vatNumber"] = settings.Afm, ["branch"] = 0, ["country"] = "GR" },
            ["invoiceHeader"] = header,
            ["invoiceDetails"] = lines,
            ["paymentMethods"] = new JsonArray(new JsonObject { ["type"] = PayType(doc.Pay), ["amount"] = doc.Gross }),
            ["invoiceSummary"] = new JsonObject { ["totalNetValue"] = doc.Net, ["totalVatAmount"] = doc.Vat, ["totalValue"] = doc.Gross },
        };
        if (doc.Type == FiscalDocType.SalesInvoice)
            invoice["counterpart"] = new JsonObject
            {
                ["vatNumber"] = doc.CustomerVat, ["name"] = doc.CustomerName, ["country"] = "GR",
                ["street"] = doc.CustomerStreet, ["streetNumber"] = doc.CustomerNumber,
                ["postalCode"] = doc.CustomerPostalCode, ["city"] = doc.CustomerCity,
            };
        if (doc.CorrelatedMarks.Count > 0)
            invoice["correlatedInvoices"] = new JsonArray([.. doc.CorrelatedMarks.Select(m =>
                (JsonNode)new JsonObject { ["mark"] = long.TryParse(m, out var mk) ? mk : 0 })]);
        return new JsonObject { ["invoice"] = invoice };
    }

    /// <summary>Κατηγορία ΦΠΑ της myDATA: 1 = 24%, 2 = 13%, 3 = 6%, 4 = 17%, 5 = 9%, 6 = 4%, 7 = 0%, 8 = χωρίς ΦΠΑ.</summary>
    private static int VatCategory(int rate) => rate switch
    {
        24 => 1, 13 => 2, 6 => 3, 17 => 4, 9 => 5, 4 => 6, 0 => 7, _ => 8,
    };

    /// <summary>Τρόπος πληρωμής της myDATA: 3 = μετρητά, 7 = POS, 5 = επί πιστώσει (πληρωμένη μέσα στην πλατφόρμα).</summary>
    private static int PayType(FiscalPay pay) => pay switch
    {
        FiscalPay.Card => 7,
        FiscalPay.Online => 5,
        _ => 3,
    };

    // ---------------------------------------------------------------- βοηθητικά

    private static JsonNode? Parse(string text)
    {
        try { return text.Length > 0 ? JsonNode.Parse(text) : null; }
        catch (JsonException) { return null; }
    }

    private static string S(JsonObject? o, string key) =>
        o?[key] is JsonNode v && v.GetValueKind() != JsonValueKind.Null ? v.ToString() : "";

    private static int Status(JsonObject? o) => int.TryParse(S(o, "status"), out var s) ? s : 2;

    private static string Code(JsonObject? o) => S(o, "errorCode");

    private static string Short(string raw) => raw.Length > 300 ? raw[..300] : raw;
}
