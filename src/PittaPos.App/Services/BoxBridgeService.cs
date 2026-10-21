using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using PittaPos.Core.Box;
using PittaPos.Core.Efood;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>
/// Παραγγελίες Box ΜΟΝΕΣ τους στο ταμείο, μέσω της γέφυρας Box (pitta-box-bridge) — ίδια λογική με το e-food
/// (βλ. <see cref="EfoodBridgeService"/>).
///
/// <para>Η Box στέλνει κάθε παραγγελία στη γέφυρα ΑΦΟΥ την αποδεχτεί το μαγαζί στο tablet της· το ταμείο ρωτάει τη
/// γέφυρα κάθε λίγα δευτερόλεπτα, με δική του σύνδεση προς τα έξω. Κάθε νέα μπαίνει ΑΚΡΙΒΩΣ όπως θα την περνούσε ο
/// ταμίας σαν «BOX»: πελάτης με διεύθυνση, μετρητά/κάρτα, κωδικός της Box, Ζωντανές Παραγγελίες, τζίρος, πελατολόγιο,
/// δελτίο στον εκτυπωτή.</para>
///
/// <para><b>Μετρητά → «BOX Μετρητά», κάρτα → «BOX Κάρτα»</b> (απάντηση χρήστη, 21/10/2026). Η κάρτα είναι πληρωμένη
/// στην εφαρμογή — η Box βάζει τα λεφτά αργότερα, όπως το e-food — οπότε η απόδειξη γράφει «μέσω πλατφόρμας» και ΤΙΠΟΤΑ
/// δεν προφορτώνεται στο POS του διανομέα (βλ. FiscalService.PayFor).</para>
///
/// <para><b>Δοκιμαστική της Box</b> (isTestOrder): ΔΕΝ μπαίνει στα έσοδα, στις Ζωντανές ή στην ΑΑΔΕ — τυπώνεται μόνο
/// ένα χαρτί «ΔΟΚΙΜΗ BOX», για να φανεί ότι η σύνδεση δουλεύει.</para>
///
/// <para><b>Ούτε χαμένη, ούτε διπλή</b>, όπως στο e-food: γράφεται ΠΡΩΤΑ ότι καταχωρήθηκε (box-imported.json) και ΜΕΤΑ
/// στέλνεται το ack. Ακυρώσεις η Box δεν στέλνει.</para>
///
/// <para>ΜΟΝΟ στο κύριο ταμείο (αυτό έχει τον εκτυπωτή). Ό,τι ξεχωρίζει κάθε κατάστημα είναι η διεύθυνση και το κλειδί
/// στις ρυθμίσεις (Ρυθμίσεις → BOX).</para>
/// </summary>
public sealed class BoxBridgeService
{
    public static BoxBridgeService Instance { get; } = new();

    /// <summary>Το κανάλι όπως το διαλέγει ο ταμίας στις ΕΦΑΡΜΟΓΕΣ (βλ. OrderWizardViewModel.AppMethods).</summary>
    public const string Channel = "BOX";

    /// <summary>Ο τίτλος του χαρτιού για τις δοκιμαστικές της Box.</summary>
    public const string TestTitle = "Δοκιμή Box";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Πότε μπήκε μια παραγγελία Box και με ποιον αριθμό ταμείου (0 = δοκιμαστική, μόνο τυπώθηκε).</summary>
    private sealed record Imported(DateTime At, int OrderNumber, string Ref = "");

    private readonly string _importedPath;
    private readonly Dictionary<string, Imported> _imported = new(StringComparer.Ordinal);
    private DispatcherTimer? _timer;
    private bool _busy;
    private string? _lastLoggedError;

    /// <summary>Τελευταία φορά που απάντησε η γέφυρα — για την οθόνη ρυθμίσεων.</summary>
    public DateTime? LastContact { get; private set; }

    /// <summary>Γιατί δεν μιλάμε με τη γέφυρα αυτή τη στιγμή (κενό = όλα καλά).</summary>
    public string LastError { get; private set; } = "";

    /// <summary>Πόσες μπήκαν από τη γέφυρα από το άνοιγμα του ταμείου.</summary>
    public int ImportedSinceStart { get; private set; }

    /// <summary>Πόσες δοκιμαστικές της Box τυπώθηκαν (χωρίς να μπουν στα έσοδα) από το άνοιγμα του ταμείου.</summary>
    public int TestsSinceStart { get; private set; }

    public event Action? StatusChanged;

    private BoxBridgeService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _importedPath = Path.Combine(dir, "box-imported.json");
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
            catch (Exception ex) { SetError("Box: " + ex.GetType().Name + ": " + ex.Message); }
        };
        _timer.Start();
    }

    private static bool Configured(out string url, out string key)
    {
        var s = SettingsStore.Instance.Settings;
        url = s.BoxBridgeUrl.Trim().TrimEnd('/');
        key = s.BoxTillKey.Trim();
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
                SetError("Η γέφυρα Box δεν δέχεται το κλειδί αυτού του ταμείου");
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                SetError("Η γέφυρα Box απάντησε " + (int)response.StatusCode);
                return;
            }

            var items = JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonArray ?? [];
            LastContact = DateTime.Now;
            SetError("");

            foreach (var item in items)
            {
                if (item is not JsonObject entry || entry["order"] is not JsonObject order
                    || entry["boxId"] is not JsonValue idValue || !idValue.TryGetValue(out string? boxId) || string.IsNullOrWhiteSpace(boxId))
                    continue;

                if (!_imported.ContainsKey(boxId))
                {
                    BoxImport map;
                    CompletedOrder? recorded = null;
                    try
                    {
                        map = BoxOrderReader.Map(order, EfoodMenu.Live, MatchContext(order));
                        if (!map.IsTest)
                            recorded = Record(map);
                    }
                    catch (Exception ex)
                    {
                        // Δεν καταχωρήθηκε: ΔΕΝ στέλνουμε ack, η γέφυρα την κρατά και ξαναδοκιμάζουμε.
                        SetError($"Η παραγγελία Box {boxId} δεν μπήκε: {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }
                    // Σημειώνεται ΑΜΕΣΩΣ μετά την καταχώρηση και ΠΡΙΝ από το χαρτί: αν σκάσει ο εκτυπωτής,
                    // η παραγγελία δεν πρέπει να ξαναμπεί στον επόμενο γύρο (διπλός τζίρος, δύο δελτία).
                    _imported[boxId] = new Imported(DateTime.Now, recorded?.OrderNumber ?? 0, recorded?.AppOrderRef ?? map.Order.Ref);
                    SaveImported();
                    if (recorded is null)
                    {
                        TestsSinceStart++;
                        AppLog.Write("box", $"Δοκιμαστική Box #{map.Order.Ref} ({boxId}) — τυπώνεται, ΔΕΝ μπαίνει στα έσοδα.");
                        Print(() => ReceiptPrinter.PrintText(TestSlip(map), TestTitle), $"Η δοκιμαστική Box #{map.Order.Ref}");
                    }
                    else
                    {
                        ImportedSinceStart++;
                        Print(() => ReceiptPrinter.PrintOrder(recorded), $"Η Box #{recorded.DisplayNumber} μπήκε αλλά");
                    }
                }
                await PostAsync(url, key, $"/till/orders/{Uri.EscapeDataString(boxId)}/ack");
            }

            // Ο κατάλογος της Box για την ΑΝΤΙΣΤΟΙΧΙΣΗ, φρέσκος κάθε λίγες ώρες — στο παρασκήνιο, και ποτέ εις βάρος
            // των παραγγελιών που μόλις μπήκαν.
            try { EfoodMatchStore.Box.RefreshIfDue(TimeSpan.FromHours(6)); }
            catch (Exception ex) { SetError("Box κατάλογος: " + ex.Message); }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            SetError("Δεν απαντά η γέφυρα Box (" + ex.GetType().Name + ")");
        }
        finally
        {
            _busy = false;
            StatusChanged?.Invoke();
        }
    }

    private static void Print(Action print, string what)
    {
        try
        {
            print();
        }
        catch (Exception ex)
        {
            AppLog.Write("box", $"{what} δεν τυπώθηκε: {ex.Message} — ξανατύπωσέ τη από το Ιστορικό.");
        }
    }

    /// <summary>Η παραγγελία μπαίνει στο ταμείο με την ίδια σειρά κινήσεων που κάνει ο οδηγός παραγγελίας για μια BOX
    /// (βλ. OrderWizardViewModel.PushBoardOrder/RecordStats): αριθμός → πίνακας → τζίρος → πελατολόγιο. Το δελτίο το
    /// τυπώνει όποιος καλεί, αφού σημειώσει ότι μπήκε.</summary>
    private static CompletedOrder Record(BoxImport box)
    {
        var map = box.Order;
        var number = SalesStatsService.Instance.NextOrderNumber(hasOwnNumber: true);
        var evening = SettingsStore.Instance.Settings.IsEveningShift;

        OrderBoardService.Instance.Add(new BoardOrder
        {
            OrderNumber = number,
            Name = map.Who.Length > 0 ? map.Who : "—",
            // Όπως ο ταμίας σε BOX (ComposeDeliveryInfo): διεύθυνση · όροφος — σχόλια πελάτη.
            Address = BoardAddress(map.Address, map.Floor, box.Comments),
            Type = OrderType.Apps,
            Channel = Channel,
            AppOrderRef = map.Ref,
            PaymentMethod = box.Payment,
            Total = map.Total,
            IsEveningShift = evening,
        });

        var order = new CompletedOrder
        {
            OrderNumber = number,
            Type = OrderType.Apps,
            Channel = Channel,
            AppOrderRef = map.Ref,
            PaymentMethod = box.Payment,
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
        // «BOX Κάρτα» = πληρωμένη στην εφαρμογή: η απόδειξη βγαίνει «μέσω πλατφόρμας», χωρίς προφόρτωση στο POS του
        // διανομέα — ο ίδιος κανόνας με όσες περνάει ο ταμίας (βλ. FiscalService.PayFor). «BOX Μετρητά»: τα εισπράττει ο διανομέας.
        var final = SalesStatsService.Instance.Record(order);
        if (final != order.OrderNumber)
            order = SalesStatsService.WithOrderNumber(order, final);

        // Στο πελατολόγιο, όπως όταν ο ταμίας γράφει τη φόρμα πελάτη — μόνο όταν τη φέρνει δικός μας διανομέας.
        if (map.Who.Length > 0 && map.Phone.Length > 0)
            CustomerStore.Instance.RecordOrder(map.Who, map.Phone, box.Street, box.StreetNumber, box.Area, box.PostalCode,
                map.Floor, box.Comments, map.Total, map.Lines.Select(l => (l.Name, l.Quantity)).ToList());

        // Αν οι γραμμές δεν βγαίνουν ίδιες με ό,τι λέει η Box για τα προϊόντα, γράφεται — εκεί θα φανεί αν κάτι δεν μετράει.
        if (box.ItemsTotal > 0 && Math.Abs(box.ItemsTotal - map.Total) >= 0.01m)
            AppLog.Write("box", $"#{map.Ref}: γραμμές {map.Total} ≠ Box {box.ItemsTotal}");
        // Μπήκαν κανονικά με το όνομα της Box — γράφεται για να φανεί τι λείπει από την αντιστοίχιση.
        if (map.Unmatched.Count > 0)
            AppLog.Write("box", $"#{map.Ref}: δεν βρέθηκαν στον κατάλογο του ταμείου: {string.Join(", ", map.Unmatched)}");

        return order;
    }

    /// <summary>Η διεύθυνση στις Ζωντανές, όπως τη συνθέτει ο ταμίας (OrderWizardViewModel.ComposeDeliveryInfo).</summary>
    private static string BoardAddress(string address, string floor, string comments)
    {
        var info = address;
        if (floor.Length > 0)
            info = info.Length > 0 ? info + " · " + floor : floor;
        if (comments.Length > 0)
            info = info.Length > 0 ? info + " — " + comments : comments;
        return info;
    }

    /// <summary>Το χαρτί μιας δοκιμαστικής: ό,τι θα έβγαινε στο δελτίο, με μεγάλα γράμματα ότι ΔΕΝ είναι πραγματική.</summary>
    private static string TestSlip(BoxImport box)
    {
        var map = box.Order;
        var lines = new List<string> { "*** ΔΟΚΙΜΗ BOX ***", "δεν μπαίνει στα έσοδα", "", "BOX #" + map.Ref };
        if (map.Who.Length > 0)
            lines.Add(map.Who);
        lines.Add("");
        foreach (var line in map.Lines)
        {
            lines.Add($"{line.Quantity} × {(line.PrintName.Length > 0 ? line.PrintName : line.Name)}   {Order.FormatPrice(line.Revenue)}");
            lines.AddRange(line.Details.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(d => "   " + d));
        }
        lines.Add("");
        lines.Add("ΣΥΝΟΛΟ " + Order.FormatPrice(map.Total));
        lines.AddRange(map.Notes.Split(" · ", StringSplitOptions.RemoveEmptyEntries));
        lines.Add($"ήρθε {DateTime.Now:HH:mm}");
        return string.Join("\n", lines);
    }

    /// <summary>Ο κατάλογος της Box και η ΑΝΤΙΣΤΟΙΧΙΣΗ για την ανάγνωση. Ό,τι κι αν πάει στραβά εδώ, η παραγγελία μπαίνει
    /// με τα ονόματα της Box αντί να μείνει έξω. Αν έρθει προϊόν που δεν ξέρει ο κατάλογος (το μαγαζί πρόσθεσε κάτι στη
    /// Box), ο κατάλογος ξαναέρχεται στο παρασκήνιο για τις επόμενες.</summary>
    private static EfoodContext MatchContext(JsonObject order)
    {
        try
        {
            var matching = EfoodMatchStore.Box;
            var products = (order["products"] as JsonArray ?? []).OfType<JsonObject>()
                .Concat((order["offers"] as JsonArray ?? []).OfType<JsonObject>()
                    .SelectMany(o => (o["products"] as JsonArray ?? []).OfType<JsonObject>()));
            var ids = products.Select(p => p["friendlyId"] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "");
            if (ids.Any(id => id.Length > 0 && !matching.Knows(id)))
                matching.RefreshIfDue(TimeSpan.Zero);
            return matching.Context();
        }
        catch (Exception ex)
        {
            AppLog.Write("box", "Η αντιστοίχιση δεν διαβάστηκε — η παραγγελία μπαίνει με τα ονόματα της Box: " + ex.Message);
            return EfoodContext.Empty;
        }
    }

    private static async Task PostAsync(string url, string key, string path)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await Http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Το ack ξαναπάει στον επόμενο γύρο: η γέφυρα θα ξαναδώσει την παραγγελία και εδώ θα αναγνωριστεί.
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
            AppLog.Write("box", error);
    }

    private void LoadImported()
    {
        try
        {
            if (!File.Exists(_importedPath))
                return;
            foreach (var (id, entry) in JsonSerializer.Deserialize<Dictionary<string, Imported>>(File.ReadAllText(_importedPath)) ?? [])
                _imported[id] = entry;
        }
        catch (Exception)
        {
            // Χαλασμένο αρχείο: η γέφυρα κρατά ό,τι δεν έχει πάρει ack, οπότε το χειρότερο είναι ότι
            // κάτι που είχε ήδη μπει θα ξαναμπεί — γράφεται για να φανεί.
            AppLog.Write("box", "Δεν διαβάστηκε το box-imported.json");
        }
    }

    /// <summary>Κρατιούνται 7 μέρες — αρκετές για οποιαδήποτε επανάληψη, χωρίς να μεγαλώνει για πάντα.</summary>
    private void SaveImported()
    {
        foreach (var old in _imported.Where(kv => kv.Value.At < DateTime.Now.AddDays(-7)).Select(kv => kv.Key).ToList())
            _imported.Remove(old);
        try
        {
            AtomicFile.WriteAllText(_importedPath, JsonSerializer.Serialize(_imported, JsonOpts));
        }
        catch (Exception ex)
        {
            AppLog.Write("save", $"Δεν γράφτηκε το «box-imported.json»: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
