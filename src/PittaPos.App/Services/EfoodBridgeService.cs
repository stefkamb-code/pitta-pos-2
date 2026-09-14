using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using PittaPos.Core.Efood;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>
/// Παραγγελίες e-food ΜΟΝΕΣ τους στο ταμείο, μέσω της γέφυρας (pitta-efood-bridge).
///
/// <para>Το e-food στέλνει κάθε παραγγελία στη γέφυρα (server στο internet, με σταθερή IP)· το ταμείο
/// ρωτάει τη γέφυρα κάθε λίγα δευτερόλεπτα, με δική του σύνδεση προς τα έξω — τίποτα δεν ανοίγει στο router
/// του μαγαζιού. Κάθε νέα παραγγελία μπαίνει ΑΚΡΙΒΩΣ όπως θα την περνούσε ο ταμίας με το χέρι σαν «e-food»:
/// αριθμός, Ζωντανές Παραγγελίες (περνάει μόνη της στο κανάλι e-food), τζίρος, δελτίο στον εκτυπωτή.</para>
///
/// <para><b>Ούτε χαμένη, ούτε διπλή.</b> Η γέφυρα την κρατά μέχρι να της πούμε «την πήρα» (ack). Εδώ
/// γράφεται ΠΡΩΤΑ ότι καταχωρήθηκε (efood-imported.json) και ΜΕΤΑ στέλνεται το ack: αν κοπεί το δίκτυο
/// ανάμεσα, η γέφυρα θα την ξαναδώσει και εδώ θα αναγνωριστεί — θα ξαναπάει μόνο το ack, όχι το δελτίο.</para>
///
/// <para>ΜΟΝΟ στο κύριο ταμείο (αυτό έχει τον εκτυπωτή). Ίδιος κώδικας για κάθε κατάστημα: ό,τι το
/// ξεχωρίζει είναι η διεύθυνση και το κλειδί στις ρυθμίσεις (Ρυθμίσεις → E-FOOD).</para>
/// </summary>
public sealed class EfoodBridgeService
{
    public static EfoodBridgeService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _importedPath;
    private readonly Dictionary<long, DateTime> _imported = [];
    private DispatcherTimer? _timer;
    private bool _busy;
    private string? _lastLoggedError;

    /// <summary>Τελευταία φορά που απάντησε η γέφυρα — για την οθόνη ρυθμίσεων.</summary>
    public DateTime? LastContact { get; private set; }

    /// <summary>Γιατί δεν μιλάμε με τη γέφυρα αυτή τη στιγμή (κενό = όλα καλά).</summary>
    public string LastError { get; private set; } = "";

    /// <summary>Πόσες μπήκαν από τη γέφυρα από το άνοιγμα του ταμείου.</summary>
    public int ImportedSinceStart { get; private set; }

    public event Action? StatusChanged;

    private EfoodBridgeService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _importedPath = Path.Combine(dir, "efood-imported.json");
    }

    /// <summary>Καλείται μία φορά στο άνοιγμα του ταμείου (App.OnStartup).</summary>
    public void Start()
    {
        if (RemoteSync.IsClient || _timer is not null)
            return;
        LoadImported();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) =>
        {
            // Τίποτα από εδώ δεν επιτρέπεται να φτάσει στο «Παρουσιάστηκε ένα απρόσμενο σφάλμα» του ταμείου:
            // γράφεται και ξαναδοκιμάζεται στον επόμενο γύρο.
            try { await PollAsync(); }
            catch (Exception ex) { SetError("e-food: " + ex.GetType().Name + ": " + ex.Message); }
        };
        _timer.Start();
    }

    private bool Configured(out string url, out string key)
    {
        var s = SettingsStore.Instance.Settings;
        url = s.EfoodBridgeUrl.Trim().TrimEnd('/');
        key = s.EfoodTillKey.Trim();
        return url.Length > 0 && key.Length > 0;
    }

    /// <summary>Ένας γύρος: ό,τι περιμένει στη γέφυρα → ταμείο → ack. Τρέχει στο UI thread (DispatcherTimer),
    /// όπως και κάθε άλλη καταχώρηση παραγγελίας, οπότε δεν μπλέκεται με όσα κάνει την ίδια ώρα ο ταμίας.</summary>
    private async Task PollAsync()
    {
        if (_busy || !Configured(out var url, out var key))
            return;
        _busy = true;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url + "/till/orders");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await Http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                SetError("Η γέφυρα δεν δέχεται το κλειδί αυτού του ταμείου");
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                SetError("Η γέφυρα απάντησε " + (int)response.StatusCode);
                return;
            }

            var items = JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonArray ?? [];
            LastContact = DateTime.Now;
            SetError("");

            foreach (var item in items)
            {
                if (item is not JsonObject entry
                    || entry["efoodId"] is not JsonValue idValue || !idValue.TryGetValue(out long efoodId)
                    || entry["order"] is not JsonObject order)
                    continue;

                if (!_imported.ContainsKey(efoodId))
                {
                    CompletedOrder recorded;
                    try
                    {
                        recorded = Record(order);
                    }
                    catch (Exception ex)
                    {
                        // Δεν καταχωρήθηκε: ΔΕΝ στέλνουμε ack, η γέφυρα την κρατά και ξαναδοκιμάζουμε.
                        SetError($"Η παραγγελία e-food {efoodId} δεν μπήκε: {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }
                    // Σημειώνεται ΑΜΕΣΩΣ μετά την καταχώρηση και ΠΡΙΝ από το χαρτί: αν σκάσει ο εκτυπωτής,
                    // η παραγγελία δεν πρέπει να ξαναμπεί στον επόμενο γύρο (διπλός τζίρος, δύο δελτία).
                    _imported[efoodId] = DateTime.Now;
                    SaveImported();
                    ImportedSinceStart++;
                    try
                    {
                        ReceiptPrinter.PrintOrder(recorded);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Write("efood", $"Η e-food #{recorded.DisplayNumber} μπήκε αλλά δεν τυπώθηκε: {ex.Message} — ξανατύπωσέ τη από το Ιστορικό.");
                    }
                    var autoAccept = SettingsStore.Instance.Settings.EfoodAutoAcceptMinutes;
                    if (autoAccept > 0)
                        _ = AcceptAsync(url, key, efoodId, autoAccept, recorded.DisplayNumber);
                }
                await PostAsync(url, key, $"/till/orders/{efoodId}/ack", null);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            SetError("Δεν απαντά η γέφυρα (" + ex.GetType().Name + ")");
        }
        finally
        {
            _busy = false;
            StatusChanged?.Invoke();
        }
    }

    /// <summary>Η παραγγελία μπαίνει στο ταμείο με την ίδια σειρά κινήσεων που κάνει ο οδηγός παραγγελίας
    /// για μια e-food (βλ. OrderWizardViewModel.ContinueStep3): αριθμός → πίνακας → τζίρος. Το δελτίο το
    /// τυπώνει όποιος καλεί, αφού σημειώσει ότι μπήκε.</summary>
    private static CompletedOrder Record(JsonObject json)
    {
        var map = EfoodOrderReader.Map(json, EfoodMenu.Live);
        var number = SalesStatsService.Instance.NextOrderNumber(hasOwnNumber: true);
        var evening = SettingsStore.Instance.Settings.IsEveningShift;

        OrderBoardService.Instance.Add(new BoardOrder
        {
            OrderNumber = number,
            Name = map.Who.Length > 0 ? map.Who : "—",
            Type = OrderType.Apps,
            Channel = "e-food",
            AppOrderRef = map.Ref,
            Total = map.Total,
            IsEveningShift = evening,
        });

        var order = new CompletedOrder
        {
            OrderNumber = number,
            Type = OrderType.Apps,
            Channel = "e-food",
            AppOrderRef = map.Ref,
            Who = map.Who,
            Phone = map.Phone,
            DeliveryAddress = map.Address,
            DeliveryFloor = map.Floor,
            DeliveryNotes = map.Notes,
            Total = map.Total,
            Lines = map.Lines.Select(l => new SoldLine(l.Name, l.Quantity, l.Revenue, l.Details, l.ProductId,
                l.Customization, l.PrintName)).ToList(),
            IsEveningShift = evening,
        };
        var final = SalesStatsService.Instance.Record(order);
        if (final != order.OrderNumber)
            order = SalesStatsService.WithOrderNumber(order, final);

        // Για το sandbox: αν το σύνολο των γραμμών δεν βγαίνει ίδιο με ό,τι λέει το e-food (αφού βγουν
        // μεταφορικά/σακούλες/φιλοδώρημα), γράφεται — εκεί θα φανεί αν τα έξτρα μετράνε σωστά.
        var fees = (Money(json["delivery_fee"]) ?? 0) + (Money((json["bags"] as JsonObject)?["amount"]) ?? 0) + (Money(json["tip"]) ?? 0);
        if (map.PlatformTotal > 0 && Math.Abs(map.PlatformTotal - fees - map.Total) >= 0.01m
            && !(json["discounts"] is JsonArray { Count: > 0 } || json["coupons"] is JsonArray { Count: > 0 }))
            AppLog.Write("efood", $"#{map.Ref}: γραμμές {map.Total} ≠ e-food {map.PlatformTotal} (μεταφορικά/σακούλες/tip {fees})");
        // Μπήκαν κανονικά με το όνομα του e-food — γράφεται για να φανεί αν ο κατάλογος του e-food είναι παλιός.
        if (map.Unmatched.Count > 0)
            AppLog.Write("efood", $"#{map.Ref}: δεν βρέθηκαν στον κατάλογο του ταμείου: {string.Join(", ", map.Unmatched)}");

        return order;
    }

    private static decimal? Money(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out decimal d) ? d : null;

    /// <summary>Ο κατάλογος είναι μεγάλο σώμα και η γέφυρα περιμένει το e-food να τον επεξεργαστεί ολόκληρο.</summary>
    private static readonly HttpClient CatalogHttp = new() { Timeout = TimeSpan.FromSeconds(180) };

    /// <summary>
    /// Στέλνει ΟΛΟ τον κατάλογο του ταμείου στο e-food, μέσω της γέφυρας (βλ. EfoodCatalogBuilder): ό,τι είχε το
    /// e-food αντικαθίσταται, και από εκεί και πέρα κάθε παραγγελία γυρίζει με τους κωδικούς του ταμείου.
    /// </summary>
    public static async Task<(bool Ok, string Message)> PushCatalogAsync(EfoodCatalogBuilder.Result catalog)
    {
        if (!Instance.Configured(out var url, out var key))
            return (false, "Αποθήκευσε πρώτα διεύθυνση γέφυρας και κλειδί.");
        foreach (var warning in catalog.Warnings)
            AppLog.Write("efood", "κατάλογος: " + warning);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url + "/till/catalog")
            {
                Content = new StringContent(catalog.Body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await CatalogHttp.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return (false, "Η γέφυρα δεν δέχεται το κλειδί αυτού του καταστήματος.");

            var text = await response.Content.ReadAsStringAsync();
            JsonObject? reply = null;
            try { reply = JsonNode.Parse(text) as JsonObject; }
            catch (JsonException) { }
            if (response.IsSuccessStatusCode && reply?["ok"] is JsonValue okValue && okValue.TryGetValue(out bool ok) && ok)
            {
                AppLog.Write("efood", $"κατάλογος: στάλθηκαν {catalog.Categories} κατηγορίες, {catalog.Products} προϊόντα");
                return (true, $"Στάλθηκε: {catalog.Categories} κατηγορίες, {catalog.Products} προϊόντα.");
            }

            // Η απάντηση του e-food, όπως ήρθε — κομμένη, ώστε να χωράει στην οθόνη· ολόκληρη στο log.
            var status = reply?["status"]?.ToString() ?? ((int)response.StatusCode).ToString();
            var body = reply?["body"]?.ToString() ?? text;
            AppLog.Write("efood", $"κατάλογος: δεν έγινε δεκτός ({status}): {body}");
            return (false, $"Το e-food δεν τον δέχτηκε ({status}): {(body.Length > 240 ? body[..240] + "…" : body)}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            return (false, "Δεν απαντά η γέφυρα: " + ex.Message);
        }
    }

    private async Task AcceptAsync(string url, string key, long efoodId, int minutes, string displayNumber)
    {
        var ok = await PostAsync(url, key, $"/till/orders/{efoodId}/accept", new JsonObject { ["minutes"] = minutes });
        if (!ok)
            AppLog.Write("efood", $"Η αυτόματη αποδοχή της e-food #{displayNumber} δεν πέρασε — έλεγξε το tablet του e-food.");
    }

    private static async Task<bool> PostAsync(string url, string key, string path, JsonObject? body)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            using var response = await Http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>Δοκιμή σύνδεσης από την οθόνη ρυθμίσεων — ΔΕΝ καταχωρεί τίποτα, μόνο μετράει όσες περιμένουν.</summary>
    public static async Task<(bool Ok, string Message)> TestAsync(string url, string key)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url.Trim().TrimEnd('/') + "/till/orders");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
            using var response = await Http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return (false, "Η γέφυρα δεν δέχεται αυτό το κλειδί.");
            if (!response.IsSuccessStatusCode)
                return (false, "Η γέφυρα απάντησε " + (int)response.StatusCode + ".");
            var count = (JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonArray)?.Count ?? 0;
            return (true, count == 0 ? "Συνδέθηκε. Καμία παραγγελία σε αναμονή." : $"Συνδέθηκε. {count} σε αναμονή — μπαίνουν τώρα.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException or InvalidOperationException)
        {
            return (false, "Δεν απαντά: " + ex.Message);
        }
    }

    /// <summary>Σφάλμα μόνο στο log και μόνο όταν ΑΛΛΑΞΕΙ — ένα πεσμένο internet δεν γεμίζει το log κάθε
    /// 5 δευτερόλεπτα, και δεν βγαίνει καμία μπάρα: ξαναδοκιμάζει μόνο του.</summary>
    private void SetError(string error)
    {
        LastError = error;
        if (error == (_lastLoggedError ?? ""))
            return;
        _lastLoggedError = error;
        if (error.Length > 0)
            AppLog.Write("efood", error);
    }

    private void LoadImported()
    {
        try
        {
            if (!File.Exists(_importedPath))
                return;
            var saved = JsonSerializer.Deserialize<Dictionary<long, DateTime>>(File.ReadAllText(_importedPath));
            foreach (var (id, at) in saved ?? [])
                _imported[id] = at;
        }
        catch (Exception)
        {
            // Χαλασμένο αρχείο: η γέφυρα κρατά ό,τι δεν έχει πάρει ack, οπότε το χειρότερο είναι ότι
            // κάτι που είχε ήδη μπει θα ξαναμπεί — γράφεται για να φανεί.
            AppLog.Write("efood", "Δεν διαβάστηκε το efood-imported.json");
        }
    }

    /// <summary>Κρατιούνται 7 μέρες — αρκετές για οποιαδήποτε επανάληψη, χωρίς να μεγαλώνει για πάντα.</summary>
    private void SaveImported()
    {
        foreach (var old in _imported.Where(kv => kv.Value < DateTime.Now.AddDays(-7)).Select(kv => kv.Key).ToList())
            _imported.Remove(old);
        try
        {
            AtomicFile.WriteAllText(_importedPath, JsonSerializer.Serialize(_imported, JsonOpts));
        }
        catch (Exception ex)
        {
            AppLog.Write("save", $"Δεν γράφτηκε το «efood-imported.json»: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
