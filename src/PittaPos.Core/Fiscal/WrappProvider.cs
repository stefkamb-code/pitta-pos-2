using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PittaPos.Core.Fiscal;

/// <summary>
/// Ο «μεταφραστής» για τον Wrapp (πάροχος με κωδικό ΑΑΔΕ 029), πάνω στη δημόσια τεκμηρίωση του API τους
/// (wrapp.ai/api/documentation, 6/10/2026).
///
/// <list type="bullet">
/// <item>Είσοδος: <c>POST /login</c> με email + api_key → JWT που ισχύει 24 ώρες (εδώ ανανεώνεται στις 23).</item>
/// <item>Κάθε τύπος παραστατικού κόβεται από ένα «βιβλίο» (billing book) του λογαριασμού — <c>GET /billing_books</c>.</item>
/// <item>Παραστατικό: <c>POST /invoices</c>. Κάρτα = <c>pos_device_id</c>: ο Wrapp στέλνει το ποσό στο τερματικό.</item>
/// <item>Τραπέζι: το 8.6 ανοίγει τραπέζι με <c>catering_table_name</c>· η 11.1 το κλείνει με <c>correlated_invoices</c>
///   (τα ΜΑΡΚ των δελτίων).</item>
/// <item>Σειρές του δοκιμαστικού (email Wrapp 11/10/2026): ΕΑΛΠ = 11.1, ΕΔΠΕ = 8.6, ΕΑΠΛ = 11.4. Κάθε παραστατικό έχει
///   <c>counterpart</c>, όπως στα παραδείγματά τους. Επανάληψη με ίδιο <c>external_id</c> δεν κόβει δεύτερο.</item>
/// </list>
///
/// <para><b>ΠΡΟΣ ΕΠΙΒΕΒΑΙΩΣΗ στο sandbox</b>: αν η κάρτα γυρίζει αμέσως ή θέλει αναμονή (εδώ: αναμονή μέχρι να βγει ΜΑΡΚ),
/// ο χαρακτηρισμός εσόδου (εδώ category1_3 / E3_561_003· ο Wrapp δείχνει category1_1 — απόφαση λογιστή), και ο τρόπος
/// πληρωμής για παραγγελίες πληρωμένες μέσα στην πλατφόρμα (ερώτηση στον λογιστή).</para>
/// </summary>
public sealed class WrappProvider(FiscalSettings settings, HttpClient http) : IFiscalProvider
{
    public string Name => "Wrapp";

    private string Base => settings.Production ? "https://wrapp.ai/api/v1" : "https://staging.wrapp.ai/api/v1";

    private string _jwt = "";
    private DateTime _jwtUntil;
    private Dictionary<string, string>? _books;
    private readonly Dictionary<string, string> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null };

    // ---------------------------------------------------------------- σύνδεση

    private async Task<string?> LoginAsync(CancellationToken ct)
    {
        if (_jwt.Length > 0 && DateTime.UtcNow < _jwtUntil) return null;
        if (settings.User.Length == 0 || settings.Key.Length == 0)
            return "Λείπει το email ή το κλειδί του Wrapp στις Ρυθμίσεις του μαγαζιού.";
        using var res = await http.PostAsJsonAsync(Base + "/login", new { email = settings.User, api_key = settings.Key }, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) return "Ο Wrapp δεν δέχτηκε τα στοιχεία σύνδεσης (" + (int)res.StatusCode + ").";
        var jwt = JsonNode.Parse(body)?["data"]?["attributes"]?["jwt"]?.GetValue<string>();
        if (string.IsNullOrEmpty(jwt)) return "Ο Wrapp δεν έδωσε κλειδί εισόδου.";
        _jwt = jwt;
        _jwtUntil = DateTime.UtcNow.AddHours(23);
        return null;
    }

    private HttpRequestMessage Request(HttpMethod method, string path, object? body = null)
    {
        var req = new HttpRequestMessage(method, Base + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwt);
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);
        return req;
    }

    private async Task<(JsonNode? Node, string? Error)> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var req = Request(method, path, body);
        using var res = await http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        JsonNode? node = null;
        try { node = text.Length > 0 ? JsonNode.Parse(text) : null; } catch (JsonException) { }
        if (res.IsSuccessStatusCode) return (node, null);
        if ((int)res.StatusCode == 401) _jwt = ""; // να ξαναμπεί στην επόμενη
        return (node, "Wrapp " + (int)res.StatusCode + ": " + ErrorText(node, text));
    }

    private static string ErrorText(JsonNode? node, string raw)
    {
        // Τα λάθη γυρίζουν συνήθως ως errors[{title,detail}] ή message — ό,τι υπάρχει.
        var e = node?["errors"]?[0];
        var msg = e?["detail"]?.ToString() ?? e?["title"]?.ToString() ?? node?["message"]?.ToString() ?? node?["error"]?.ToString();
        return msg ?? (raw.Length > 200 ? raw[..200] : raw);
    }

    /// <summary>Το βιβλίο (σειρά) για κάθε τύπο παραστατικού — μία φορά ανά άνοιγμα.</summary>
    private async Task<(string? Book, string? Error)> BookAsync(FiscalDocType type, CancellationToken ct)
    {
        if (_books is null)
        {
            var (node, error) = await SendAsync(HttpMethod.Get, "/billing_books", null, ct);
            if (error is not null) return (null, error);
            _books = new Dictionary<string, string>();
            foreach (var b in Items(node))
            {
                var code = Attr(b, "invoice_type_code");
                var id = b?["id"]?.ToString() ?? Attr(b, "id");
                if (code is not null && id is not null) _books.TryAdd(code, id);
            }
        }
        var c = FiscalCodes.Code(type);
        return _books.TryGetValue(c, out var book)
            ? (book, null)
            : (null, $"Ο λογαριασμός Wrapp δεν έχει βιβλίο για {c} — φτιάξ' το στον Wrapp (Ρυθμίσεις → Σειρές).");
    }

    /// <summary>Το pos_device_id του Wrapp για το Terminal ID που έγραψε το μαγαζί στις Ρυθμίσεις.</summary>
    private async Task<(string? Device, string? Error)> DeviceAsync(string terminalId, CancellationToken ct)
    {
        if (_devices.TryGetValue(terminalId, out var known)) return (known, null);
        var (node, error) = await SendAsync(HttpMethod.Get, "/pos_devices", null, ct);
        if (error is not null) return (null, error);
        foreach (var d in Items(node))
        {
            var id = d?["id"]?.ToString();
            var tid = Attr(d, "terminal_id") ?? Attr(d, "tid") ?? Attr(d, "terminal") ?? Attr(d, "name");
            if (id is not null && tid is not null) _devices[tid] = id;
        }
        return _devices.TryGetValue(terminalId, out var device)
            ? (device, null)
            : (null, $"Το τερματικό {terminalId} δεν βρέθηκε στον Wrapp — πρόσθεσέ το εκεί (Ενσωματώσεις → POS).");
    }

    private static IEnumerable<JsonNode?> Items(JsonNode? node) =>
        (node?["data"] as JsonArray ?? node as JsonArray ?? []).AsEnumerable();

    /// <summary>Πεδίο είτε σκέτο είτε μέσα σε «attributes» (μορφή JSON:API).</summary>
    private static string? Attr(JsonNode? n, string name) =>
        n?[name]?.ToString() ?? n?["attributes"]?[name]?.ToString();

    // ---------------------------------------------------------------- παραστατικά

    /// <summary>myDATA: τρόπος πληρωμής του Wrapp (0 μετρητά, 3 κάρτα, 1 επί πιστώσει).</summary>
    private static int PayCode(FiscalPay pay) => pay switch
    {
        FiscalPay.Card => 3,
        // Πληρωμένη μέσα στην πλατφόρμα: τα χρήματα έρχονται αργότερα από το e-food/Wolt — «επί πιστώσει» μέχρι να
        // πει άλλο ο λογιστής.
        FiscalPay.Online => 1,
        _ => 0,
    };

    /// <summary>Χαρακτηρισμός εσόδου myDATA. Λιανική εστίασης: παροχή υπηρεσιών, ιδιωτική πελατεία. Το δελτίο 8.6 δεν
    /// είναι έσοδο (category1_95). Το τιμολόγιο: χονδρική σε επιτηδευματία.</summary>
    private static (string Category, string Type) Classification(FiscalDocType t) => t switch
    {
        FiscalDocType.OrderSlip => ("category1_95", "_"),
        FiscalDocType.SalesInvoice => ("category1_3", "E3_561_001"),
        _ => ("category1_3", "E3_561_003"),
    };

    public static object Payload(FiscalDocument doc, string bookId, string? deviceId)
    {
        var (cat, type) = Classification(doc.Type);
        var lines = doc.Lines.Select((l, i) => new Dictionary<string, object>
        {
            ["line_number"] = i + 1,
            ["name"] = l.Name,
            ["quantity"] = l.Quantity,
            ["quantity_type"] = 1,
            ["unit_price"] = l.UnitNet,
            ["net_total_price"] = l.Net,
            ["vat_rate"] = l.VatRate,
            ["vat_total"] = l.Vat,
            ["subtotal"] = l.Gross,
            ["classification_category"] = cat,
            ["classification_type"] = type,
        }).ToList();
        var body = new Dictionary<string, object>
        {
            ["invoice_type_code"] = FiscalCodes.Code(doc.Type),
            ["billing_book_id"] = bookId,
            ["payment_method_type"] = PayCode(doc.Pay),
            ["net_total_amount"] = doc.Net,
            ["vat_total_amount"] = doc.Vat,
            ["total_amount"] = doc.Gross,
            ["payable_total_amount"] = doc.Gross,
            ["invoice_lines"] = lines,
        };
        if (doc.ExternalId.Length > 0) body["external_id"] = doc.ExternalId;
        if (deviceId is not null) body["pos_device_id"] = deviceId;
        if (doc.Type == FiscalDocType.OrderSlip)
        {
            // Το πρώτο δελτίο ανοίγει το τραπέζι με το όνομα· τα επόμενα πάνε στο catering_table_id που γύρισε.
            if (doc.ProviderTableRef.Length > 0) body["catering_table_id"] = doc.ProviderTableRef;
            else if (doc.TableName.Length > 0) body["catering_table_name"] = doc.TableName;
        }
        else if (doc.CorrelatedMarks.Count > 0 && doc.ProviderTableRef.Length > 0)
            // Η απόδειξη που κλείνει το τραπέζι: ΜΟΝΟ το id. Ένα όνομα εδώ θα άνοιγε καινούριο τραπέζι.
            body["catering_table_id"] = doc.ProviderTableRef;
        if (doc.CorrelatedMarks.Count > 0) body["correlated_invoices"] = doc.CorrelatedMarks;
        body["counterpart"] = Counterpart(doc);
        return body;
    }

    /// <summary>Ο «αντισυμβαλλόμενος», όπως στα παραδείγματα που έστειλε ο Wrapp (11/10/2026): λιανική = ιδιώτης πελάτης,
    /// δελτίο = το τραπέζι, τιμολόγιο = η επιχείρηση.</summary>
    private static Dictionary<string, object> Counterpart(FiscalDocument doc) => doc.Type switch
    {
        FiscalDocType.SalesInvoice => new() { ["vat"] = doc.CustomerVat, ["name"] = doc.CustomerName },
        FiscalDocType.OrderSlip => new()
        {
            ["name"] = doc.TableName.Length > 0 ? doc.TableName : "Τραπέζι",
            ["vat"] = "", ["city"] = "", ["street"] = "", ["number"] = "", ["postal_code"] = "",
        },
        _ => new() { ["name"] = "Ιδιώτης πελάτης", ["country_code"] = "GR" },
    };

    public async Task<FiscalResult> IssueAsync(FiscalDocument doc, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (await LoginAsync(ct) is { } loginError) return FiscalResult.Fail(loginError);
            var (book, bookError) = await BookAsync(doc.Type, ct);
            if (bookError is not null) return FiscalResult.Fail(bookError);
            string? device = null;
            if (doc.Pay == FiscalPay.Card && doc.Type != FiscalDocType.OrderSlip)
            {
                if (doc.TerminalId.Length == 0) return FiscalResult.Fail("Δεν έχει οριστεί τερματικό κάρτας στις Ρυθμίσεις.");
                var (dev, devError) = await DeviceAsync(doc.TerminalId, ct);
                if (devError is not null) return FiscalResult.Fail(devError);
                device = dev;
            }

            var (node, error) = await SendAsync(HttpMethod.Post, "/invoices", Payload(doc, book!, device), ct);
            if (error is not null)
            {
                // Το external_id υπάρχει ήδη = το ίδιο παραστατικό κόπηκε σε προηγούμενη απόπειρα (π.χ. έληξε ο χρόνος πριν
                // έρθει η απάντηση). Δεν κόβεται δεύτερο· παίρνουμε αυτό που υπάρχει.
                if (doc.ExternalId.Length > 0 && error.Contains("external_id", StringComparison.OrdinalIgnoreCase))
                {
                    var (existing, existingError) = await SendAsync(HttpMethod.Get, "/invoices/" + Uri.EscapeDataString(doc.ExternalId), null, ct);
                    if (existingError is null) return await WaitForMarkAsync(Read(existing), device is not null, ct);
                }
                return FiscalResult.Fail(error);
            }
            return await WaitForMarkAsync(Read(node), device is not null, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return FiscalResult.Fail("Ο Wrapp δεν απάντησε εγκαίρως.");
        }
        catch (HttpRequestException ex)
        {
            return FiscalResult.Fail("Δεν υπάρχει σύνδεση με τον Wrapp (" + ex.Message + ").");
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Χωρίς ΜΑΡΚ ακόμα: είτε κάρτα που περιμένει το τερματικό, είτε απάντηση <c>{"status":"pending","invoice_id":…}</c>.
    /// Ρωτάμε το <c>GET /invoices/:id</c> μέχρι να βγει — κάρτα ως 2 λεπτά (ο πελάτης βάζει την κάρτα), αλλιώς ως 30''.
    /// Παραστατικό που ο Wrapp κράτησε επειδή δεν έφτασε την ΑΑΔΕ (transmission_failure) ΔΕΝ περιμένει: έχει ήδη έγκυρο QR και
    /// το στέλνει μόνος του.
    /// </summary>
    private async Task<FiscalResult> WaitForMarkAsync(FiscalResult result, bool card, CancellationToken ct)
    {
        if (result.Mark.Length > 0 || result.PendingTransmission || result.ProviderId.Length == 0)
            return result.Mark.Length > 0 || result.PendingTransmission
                ? result
                : FiscalResult.Fail("Ο Wrapp δεν έδωσε ΜΑΡΚ ούτε αριθμό παραστατικού.");
        var tries = card ? 60 : 15;
        for (var i = 0; i < tries && result.Mark.Length == 0 && !result.PendingTransmission; i++)
        {
            await Task.Delay(2000, ct);
            var (again, againError) = await SendAsync(HttpMethod.Get, "/invoices/" + result.ProviderId, null, ct);
            if (againError is null) result = Read(again) with { ProviderId = result.ProviderId };
        }
        if (result.Mark.Length > 0 || result.PendingTransmission) return result;
        return FiscalResult.Fail(card ? "Η πληρωμή στο τερματικό δεν ολοκληρώθηκε." : "Ο Wrapp δεν έβγαλε ακόμα ΜΑΡΚ — θα ξαναδοκιμάσει.");
    }

    private static FiscalResult Read(JsonNode? node)
    {
        var n = node?["data"] is JsonObject d ? (d["attributes"] as JsonObject ?? d) : node;
        string S(string k) => n?[k]?.ToString() ?? "";
        var mark = S("my_data_mark");
        return new FiscalResult
        {
            Ok = true,
            // «invoice_id»: η απάντηση {"status":"pending","invoice_id":…} (τεκμηρίωση Wrapp).
            ProviderId = S("id").Length > 0 ? S("id") : node?["data"]?["id"]?.ToString() ?? S("invoice_id"),
            Mark = mark,
            Uid = S("my_data_uid"),
            QrUrl = S("my_data_qr_url"),
            Series = S("series"),
            Number = long.TryParse(S("num"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) ? num : 0,
            PendingTransmission = S("transmission_failure").Length > 0,
            PosTxn = S("transaction_id").Length > 0 ? S("transaction_id") : S("authentication_code"),
            ProviderTableRef = S("catering_table_id"),
        };
    }

    public async Task<FiscalResult> CancelOrderSlipAsync(string mark, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (await LoginAsync(ct) is { } loginError) return FiscalResult.Fail(loginError);
            // Κόβει νέο 8.6 που ακυρώνει τα δελτία στο correlated_invoices — από το βιβλίο των δελτίων (τεκμηρίωση Wrapp).
            var (book, bookError) = await BookAsync(FiscalDocType.OrderSlip, ct);
            if (bookError is not null) return FiscalResult.Fail(bookError);
            var body = new Dictionary<string, object> { ["billing_book_id"] = book!, ["correlated_invoices"] = new[] { mark } };
            var (node, error) = await SendAsync(HttpMethod.Post, "/invoices/cancel_catering_order_note", body, ct);
            return error is null ? await WaitForMarkAsync(Read(node), false, ct) : FiscalResult.Fail(error);
        }
        catch (HttpRequestException ex)
        {
            return FiscalResult.Fail("Δεν υπάρχει σύνδεση με τον Wrapp (" + ex.Message + ").");
        }
        finally { _gate.Release(); }
    }
}
