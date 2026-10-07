using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using PittaPos.Core.Fiscal;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>
/// Η σύνδεση του ταμείου με το site διαχείρισης (pitta-admin).
///
/// <list type="bullet">
/// <item><b>Από το site:</b> οι ρυθμίσεις ΑΑΔΕ του μαγαζιού — πάροχος, κλειδί, τερματικά, πού τυπώνεται η απόδειξη. Στο
///   ταμείο δεν ρυθμίζεται τίποτα από αυτά· τα αλλάζει το μαγαζί στην οθόνη «Ρυθμίσεις» του site.</item>
/// <item><b>Προς το site:</b> οι παραγγελίες και οι ακυρώσεις της ημέρας, ώστε το διαχειριστικό να δείχνει τα αληθινά
///   νούμερα. Ό,τι αλλάζει (π.χ. μετρητά→κάρτα από το Ιστορικό) ξαναστέλνεται και ο server ΑΝΤΙΚΑΘΙΣΤΑ την παλιά.
///   Κάθε παραγγελία πηγαίνει με τον ΔΙΚΟ ΤΟΥ ταμείου διαχωρισμό λεφτών (μετρητά, κάρτα, όρθιος, εφαρμογές,
///   ανεξόφλητα — βλ. DayReportService.SplitMoney), ώστε το site να λέει ό,τι και το χαρτί της ημέρας.</item>
/// <item><b>Και ποιες παραγγελίες έχει</b> κάθε ανοιχτή μέρα (OpenDays): μια παραγγελία που σβήστηκε ολόκληρη στο ταμείο
///   σβήνεται και από το site, αλλιώς ο τζίρος του site έμενε μεγαλύτερος από του ταμείου.</item>
/// <item><b>Και οι παλιές μέρες</b> του αρχείου, μία ανά γύρο (οι νεότερες πρώτες), μία φορά — ξανά μόνο αν
///   ξαναγραφτεί η μέρα.</item>
/// <item><b>Ζωντανός πίνακας:</b> διανομές που περιμένουν και ανοιχτά τραπέζια, σε κάθε «είμαι εδώ».</item>
/// </list>
///
/// <para>Ποτέ δεν σταματά την πώληση: αν το site δεν απαντά, το ταμείο δουλεύει με τις τελευταίες ρυθμίσεις που ήξερε
/// (κρυπτογραφημένο αντίγραφο με DPAPI) και στέλνει ό,τι έμεινε πίσω μόλις ξαναβρεί σύνδεση. Κανένα μήνυμα στην οθόνη
/// του ταμία — η κατάσταση φαίνεται μόνο στις Ρυθμίσεις → SITE.</para>
///
/// <para>ΜΟΝΟ στο κύριο ταμείο: αυτό έχει τις παραγγελίες· ένα δεύτερο ταμείο θα έστελνε τις ίδιες δύο φορές.</para>
/// </summary>
public sealed class SiteLinkService
{
    // ΠΡΟΣΟΧΗ ΣΤΗ ΣΕΙΡΑ: τα στατικά πεδία παίρνουν τιμή με τη σειρά που είναι γραμμένα. Το Instance φτιάχνεται ΜΕΤΑ
    // το Http — αλλιώς ο constructor έβλεπε Http = null, και το «ΑΠΟΘΗΚΕΥΣΗ ΚΑΙ ΔΟΚΙΜΗ» έβγαζε «απρόσμενο σφάλμα»
    // (1.0.106, στο μαγαζί), ενώ οι παραγγελίες δεν έφευγαν ποτέ.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static SiteLinkService Instance { get; } = new();

    private readonly FiscalSettingsClient _settingsClient;
    private readonly Dictionary<string, string> _sent = [];   // κλειδί παραγγελίας → αποτύπωμα που στάλθηκε
    private readonly HashSet<string> _sentCancels = [];
    private string _sentDays = "";                            // η λίστα ανοιχτών ημερών που στάλθηκε τελευταία
    private readonly string _historyPath;
    private Dictionary<string, long> _historySent = [];       // μέρα αρχείου → stamp που στάλθηκε
    private DispatcherTimer? _timer;
    private bool _busy;
    private DateTime _settingsAt = DateTime.MinValue;

    /// <summary>Οι ρυθμίσεις ΑΑΔΕ που ισχύουν τώρα (από το site ή το αντίγραφο).</summary>
    public FiscalSettings Fiscal { get; private set; } = FiscalSettings.None;

    /// <summary>Ο πάροχος του μαγαζιού, έτοιμος για χρήση. Αλλάζει μόνος του όταν αλλάξει στις Ρυθμίσεις του site.</summary>
    public IFiscalProvider Provider { get; private set; } = FiscalProviders.Create(FiscalSettings.None, Http);

    public DateTime? LastContact { get; private set; }
    public string LastError { get; private set; } = "";
    public int SentSinceStart { get; private set; }

    public event Action? StatusChanged;

    private SiteLinkService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _settingsClient = new FiscalSettingsClient(Http, Path.Combine(dir, "fiscal-settings.dat"), Protect, Unprotect);
        _historyPath = Path.Combine(dir, "site-history.json");
        try
        {
            if (File.Exists(_historyPath))
                _historySent = JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(_historyPath)) ?? [];
        }
        catch (Exception) { _historySent = []; }
    }

    // Το αντίγραφο έχει το κλειδί του παρόχου — κρυπτογραφείται με τον λογαριασμό Windows αυτού του υπολογιστή.
    private static string Protect(string s) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser));
    private static string Unprotect(string s) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser));

    private static bool Configured(out string url, out string key)
    {
        var s = SettingsStore.Instance.Settings;
        url = s.SiteUrl.Trim().TrimEnd('/');
        key = s.SiteKey.Trim();
        return url.Length > 0 && key.Length > 0;
    }

    /// <summary>Καλείται μία φορά στο άνοιγμα του ταμείου (App.OnStartup).</summary>
    public void Start()
    {
        if (RemoteSync.IsClient || _timer is not null)
            return;
        _ = RefreshSettingsAsync(force: true);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += async (_, _) =>
        {
            // Τίποτα από εδώ δεν φτάνει ποτέ στην οθόνη του ταμία· γράφεται και ξαναδοκιμάζεται στον επόμενο γύρο.
            try { await TickAsync(); }
            catch (Exception ex) { SetError("site: " + ex.GetType().Name + ": " + ex.Message); }
        };
        _timer.Start();
    }

    private async Task TickAsync()
    {
        if (_busy || !Configured(out var url, out var key))
            return;
        _busy = true;
        try
        {
            if (DateTime.Now - _settingsAt > TimeSpan.FromMinutes(5))
                await RefreshSettingsAsync(force: false);
            await UploadAsync(url, key);
            await HeartbeatAsync(url, key);
            // Demo: το demo του site έχει ήδη τις ίδιες μέρες (βγαίνουν από την ίδια γεννήτρια) — δεν ξαναστέλνονται.
            if (!DemoMode.IsOn)
                await UploadHistoryDayAsync(url, key);
        }
        finally { _busy = false; }
    }

    /// <summary>Ξαναδιαβάζει τις ρυθμίσεις ΑΑΔΕ από το site — στο άνοιγμα, κάθε 5 λεπτά, και με το «ΑΠΟΘΗΚΕΥΣΗ».</summary>
    public async Task RefreshSettingsAsync(bool force)
    {
        Configured(out var url, out var key);
        FiscalSettings settings;
        bool fresh;
        string error;
        try
        {
            (settings, fresh, error) = await _settingsClient.LoadAsync(url, key);
        }
        catch (Exception ex)
        {
            // Ό,τι κι αν συμβεί εδώ δεν φτάνει ποτέ στην οθόνη του ταμία — μένουν οι ρυθμίσεις που ήδη ισχύουν.
            SetError("site: " + ex.GetType().Name + ": " + ex.Message);
            return;
        }
        _settingsAt = DateTime.Now;
        if (force || settings != Fiscal)
        {
            var changed = settings.Provider != Fiscal.Provider || settings.Env != Fiscal.Env
                          || settings.User != Fiscal.User || settings.Key != Fiscal.Key;
            Fiscal = settings;
            if (changed || force)
            {
                Provider = FiscalProviders.Create(settings, Http);
                AppLog.Write("site", $"Πάροχος: {(settings.Provider.Length > 0 ? settings.Provider : "—")} ({settings.Env}){(fresh ? "" : " — από το αντίγραφο")}");
            }
        }
        if (fresh) { LastContact = DateTime.Now; SetError(""); }
        else if (url.Length > 0) SetError(error);
    }

    // ---------------------------------------------------------------- παραγγελίες προς το site

    private sealed record TillLineDto(string ProductId, string Name, int Quantity, decimal Revenue, string Details, string Category);
    /// <summary>Πού πήγαν τα λεφτά της παραγγελίας — οι ίδιες κατηγορίες με το χαρτί της ημέρας, συν «χωρίς στοιχεία» για
    /// τραπέζια παλιών ημερών που έκλεισαν πριν αρχίσουν να αρχειοθετούνται οι εισπράξεις τους.</summary>
    private sealed record MoneyDto(decimal Cash, decimal Card, decimal Online, decimal Counter, decimal Unsettled, decimal Unknown);
    private sealed record TillOrderDto(int OrderNumber, int Type, string? Channel, string? AppOrderRef, int? PaymentMethod,
        string Who, int? TablePerson, decimal Total, List<TillLineDto> Lines, DateTime PlacedAt, bool IsEveningShift,
        MoneyDto Money);
    private sealed record TillCancelDto(int OrderNumber, string Name, int Quantity, decimal Revenue, DateTime CancelledAt,
        string CancelledBy, string Channel);
    private sealed record TillOrderKeyDto(int OrderNumber, int? TablePerson);
    private sealed record TillDayDto(string Date, List<TillOrderKeyDto> Orders);

    /// <summary>
    /// Για κάθε εργάσιμη ημέρα που είναι ακόμα ανοιχτή στο ταμείο (πάντα και η σημερινή, ακόμα κι άδεια): ΟΛΕΣ οι
    /// παραγγελίες που μετράνε τώρα στον τζίρο. Ό,τι έχει το site για εκείνη τη μέρα και λείπει από εδώ σβήστηκε στο
    /// ταμείο (ολόκληρη παραγγελία, ή το τελευταίο της προϊόν) — και σβήνεται και από το site, ώστε ο τζίρος να μένει
    /// ίδιος. Χωρίς αυτό, μια σβησμένη παραγγελία έμενε για πάντα στο site.
    /// </summary>
    private static List<TillDayDto> OpenDays(List<CompletedOrder> counted)
    {
        var today = SalesStatsService.BusinessDay(DateTime.Now);
        return counted.GroupBy(o => SalesStatsService.BusinessDay(o.PlacedAt))
            .Select(g => g.Key)
            .Append(today)
            .Distinct()
            .OrderBy(d => d)
            .Select(d => new TillDayDto(d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                counted.Where(o => SalesStatsService.BusinessDay(o.PlacedAt) == d)
                    .OrderBy(o => o.OrderNumber).ThenBy(o => o.TablePerson)
                    .Select(o => new TillOrderKeyDto(o.OrderNumber, o.TablePerson)).ToList()))
            .ToList();
    }

    private static string KeyOf(CompletedOrder o) => $"{o.OrderNumber}/{o.TablePerson}/{o.PlacedAt:yyyyMMdd}";

    /// <summary>Αποτύπωμα του περιεχομένου — αλλάζει όταν αλλάξει οτιδήποτε που φαίνεται στο site (και όταν
    /// πληρώσει ένα άτομο του τραπεζιού, αφού αλλάζει ο διαχωρισμός).</summary>
    private static string Fingerprint(CompletedOrder o, MoneyDto m) =>
        $"{o.Type}|{o.Channel}|{o.AppOrderRef}|{o.PaymentMethod}|{o.Total}|{o.Who}|{o.IsEveningShift}|{m}|"
        + string.Join(";", o.Lines.Select(l => $"{l.ProductId}:{l.Quantity}:{l.Revenue}"));

    /// <summary>
    /// Ο διαχωρισμός μιας παραγγελίας με ΤΟΥΣ ΙΔΙΟΥΣ κανόνες που βγάζει το χαρτί της ημέρας (DayReportService.SplitMoney):
    /// τραπέζι από τις εισπράξεις του ατόμου (ό,τι λείπει = ανεξόφλητο), e-food/Wolt = εφαρμογές, ΔΙΑΝΟΜΗ/BOX από τον
    /// τρόπο πάνω στην παραγγελία, ΟΡΘΙΟΣ χωρίς τρόπο = όρθιος, και ό,τι μένει = ανεξόφλητο.
    /// </summary>
    /// <param name="payments">Οι εισπράξεις τραπεζιών της μέρας· null = παλιά μέρα χωρίς αρχειοθετημένες εισπράξεις.</param>
    private static MoneyDto MoneyOf(CompletedOrder o, IReadOnlyList<TablePayment>? payments)
    {
        var total = o.Total;
        if (o.Type == OrderType.Table)
        {
            if (payments is null) return new MoneyDto(0, 0, 0, 0, 0, total);
            var mine = payments.Where(p => p.OrderNumber == o.OrderNumber).ToList();
            var cash = mine.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount);
            var card = mine.Where(p => p.Method == PaymentMethod.Card).Sum(p => p.Amount);
            return new MoneyDto(cash, card, 0, 0, Math.Max(0, total - cash - card), 0);
        }
        if (o.Type == OrderType.Apps && o.Channel != "BOX") return new MoneyDto(0, 0, total, 0, 0, 0);
        if (o.PaymentMethod == PaymentMethod.Cash) return new MoneyDto(total, 0, 0, 0, 0, 0);
        if (o.PaymentMethod == PaymentMethod.Card) return new MoneyDto(0, total, 0, 0, 0, 0);
        if (o.Type == OrderType.Pickup) return new MoneyDto(0, 0, 0, total, 0, 0);
        return new MoneyDto(0, 0, 0, 0, total, 0);
    }

    private TillOrderDto Dto(CompletedOrder o, MoneyDto money) => new(o.OrderNumber, (int)o.Type, o.Channel, o.AppOrderRef,
        o.PaymentMethod is { } p ? (int)p : null, o.Who, o.TablePerson, o.Total,
        o.Lines.Select(l => new TillLineDto(l.ProductId, l.Name, l.Quantity, l.Revenue, l.Details, CategoryOf(l.ProductId))).ToList(),
        o.PlacedAt, o.IsEveningShift, money);

    private static TillCancelDto CancelDto(CancelledLine c) =>
        new(c.OrderNumber, c.Name, c.Quantity, c.Revenue, c.CancelledAt, c.CancelledBy, c.Channel);

    private static async Task<HttpStatusCode> PostOrdersAsync(string url, string key, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url + "/api/till/orders") { Content = JsonContent.Create(body, options: Json) };
        req.Headers.Add("X-Store-Key", key);
        using var res = await Http.SendAsync(req);
        return res.StatusCode;
    }

    private static string CategoryOf(string productId)
    {
        if (productId.Length == 0) return "";
        return MenuStore.Instance.Categories.FirstOrDefault(c => c.Products.Any(p => p.Id == productId))?.Name ?? "";
    }

    private async Task UploadAsync(string url, string key)
    {
        // Μόνο ό,τι μετράει ήδη στον τζίρο του ταμείου (όχι όσα περιμένουν κανάλι) — ίδια νούμερα σε ταμείο και site.
        var payments = TablePaymentsService.Instance.Payments;
        var counted = SalesStatsService.Instance.CountedOrders;
        var days = OpenDays(counted);
        var daysKey = string.Join(";", days.Select(d => d.Date + ":" + string.Join(",", d.Orders.Select(k => $"{k.OrderNumber}/{k.TablePerson}"))));
        var daysChanged = daysKey != _sentDays;
        var orders = counted
            .Select(o => (Order: o, Money: MoneyOf(o, payments)))
            .Where(x => !_sent.TryGetValue(KeyOf(x.Order), out var fp) || fp != Fingerprint(x.Order, x.Money))
            .Take(200)
            .ToList();
        var cancels = CancellationLogService.Instance.Entries
            .Where(c => !_sentCancels.Contains($"{c.OrderNumber}/{c.CancelledAt:O}/{c.Name}"))
            .Take(200)
            .ToList();
        if (orders.Count == 0 && cancels.Count == 0 && !daysChanged)
            return;

        var body = new
        {
            orders = orders.Select(x => Dto(x.Order, x.Money)).ToList(),
            cancellations = cancels.Select(CancelDto).ToList(),
            days = daysChanged ? days : null,
        };
        var status = await PostOrdersAsync(url, key, body);
        if (status == HttpStatusCode.Unauthorized) { SetError("Το site δεν δέχεται το κλειδί αυτού του ταμείου"); return; }
        if ((int)status >= 300) { SetError("Το site απάντησε " + (int)status); return; }

        foreach (var x in orders) _sent[KeyOf(x.Order)] = Fingerprint(x.Order, x.Money);
        foreach (var c in cancels) _sentCancels.Add($"{c.OrderNumber}/{c.CancelledAt:O}/{c.Name}");
        _sentDays = daysKey;
        SentSinceStart += orders.Count;
        LastContact = DateTime.Now;
        SetError("");
    }

    /// <summary>Μία παλιά μέρα του αρχείου ανά γύρο — η νεότερη που δεν έχει σταλεί (ή ξαναγράφτηκε από τότε).</summary>
    private async Task UploadHistoryDayAsync(string url, string key)
    {
        var day = HistoryArchiveService.ArchivedDays()
            .OrderByDescending(d => d.Day, StringComparer.Ordinal)
            .FirstOrDefault(d => !_historySent.TryGetValue(d.Day, out var stamp) || stamp != d.Stamp);
        if (day is null || !DateTime.TryParseExact(day.Day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
            return;

        var content = HistoryArchiveService.ContentFor(date);
        // Εισπράξεις τραπεζιών: της ίδιας μέρας και της επόμενης (ένα τραπέζι που πλήρωσε μετά τις 5 το πρωί).
        var payments = HistoryArchiveService.PaymentsFor(date) is { } pay
            ? pay.Concat(HistoryArchiveService.PaymentsFor(date.AddDays(1)) ?? []).ToList()
            : null;
        foreach (var chunk in content.Orders.Chunk(250))
        {
            var status = await PostOrdersAsync(url, key, new
            {
                orders = chunk.Select(o => Dto(o, MoneyOf(o, payments))).ToList(),
                cancellations = Array.Empty<TillCancelDto>(),
            });
            if ((int)status >= 300) return; // ξαναδοκιμάζει στον επόμενο γύρο
        }
        foreach (var chunk in content.Cancellations.Chunk(250))
        {
            var status = await PostOrdersAsync(url, key, new { orders = Array.Empty<TillOrderDto>(), cancellations = chunk.Select(CancelDto).ToList() });
            if ((int)status >= 300) return;
        }
        _historySent[day.Day] = day.Stamp;
        try { AtomicFile.WriteAllText(_historyPath, JsonSerializer.Serialize(_historySent)); }
        catch (Exception) { /* στη χειρότερη ξαναστέλνεται — ο server αντικαθιστά, δεν διπλομετρά */ }
    }

    // ---------------------------------------------------------------- ζωντανός πίνακας

    /// <summary>Το κανάλι όπως το ονομάζει το site (Delivery/Pickup/Table/Efood/Wolt/Box).</summary>
    private static string SiteChannel(OrderType type, string? channel)
    {
        var ch = (channel ?? "").ToUpperInvariant();
        return type switch
        {
            OrderType.Table => "Table",
            OrderType.Pickup => "Pickup",
            OrderType.Delivery when ch == "BOX" => "Box",
            OrderType.Delivery => "Delivery",
            _ when ch.Contains("WOLT") => "Wolt",
            _ when ch.Contains("BOX") => "Box",
            _ => "Efood",
        };
    }

    private static (object Board, object Tables) LiveBoard()
    {
        var board = OrderBoardService.Instance.Orders.Select(b => new
        {
            orderNumber = b.OrderNumber,
            display = b.DisplayNumber,
            channel = SiteChannel(b.Type, b.Channel),
            placedAt = b.PlacedAt,
            dispatchedAt = b.IsPending ? (DateTime?)null : b.SentAt,
            total = b.Total,
        }).ToList();

        var orders = SalesStatsService.Instance.Orders;
        var tables = TableStatusService.Instance.OpenSince.Select(t =>
        {
            var mine = orders.Where(o => o.Type == OrderType.Table && o.TableNumberLabel == t.Key.ToString() && o.PlacedAt >= t.Value).ToList();
            return new
            {
                table = t.Key,
                persons = Math.Max(1, mine.Select(o => o.TablePerson).Distinct().Count()),
                openedAt = t.Value,
                running = mine.Sum(o => o.Total),
                slipMarks = Array.Empty<string>(),
            };
        }).ToList();
        return (board, tables);
    }

    private async Task HeartbeatAsync(string url, string key)
    {
        var version = "";
        try { version = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "version.txt")).Trim(); } catch (IOException) { }
        var (board, tables) = LiveBoard();
        using var req = new HttpRequestMessage(HttpMethod.Post, url + "/api/till/heartbeat")
        {
            Content = JsonContent.Create(new
            {
                version, evening = SettingsStore.Instance.Settings.IsEveningShift,
                board, tables,
            }, options: Json),
        };
        req.Headers.Add("X-Store-Key", key);
        using var res = await Http.SendAsync(req);
        if (res.IsSuccessStatusCode) LastContact = DateTime.Now;
    }

    /// <summary>Για το «ΑΠΟΘΗΚΕΥΣΗ ΚΑΙ ΔΟΚΙΜΗ» των Ρυθμίσεων: απαντά το site, δέχεται το κλειδί, ποιον πάροχο έχει το μαγαζί.</summary>
    public static async Task<(bool Ok, string Message)> TestAsync(string url, string key)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url.TrimEnd('/') + "/api/till/settings");
            req.Headers.Add("X-Store-Key", key);
            using var res = await Http.SendAsync(req);
            if (res.StatusCode == HttpStatusCode.Unauthorized) return (false, "Το site δεν δέχεται αυτό το κλειδί.");
            if (!res.IsSuccessStatusCode) return (false, "Το site απάντησε " + (int)res.StatusCode + ".");
            var s = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
            var provider = s.TryGetProperty("provider", out var p) ? p.GetString() : "";
            var env = s.TryGetProperty("env", out var e) && e.GetString() == "production" ? "κανονική" : "δοκιμαστική";
            var terminals = s.TryGetProperty("terminals", out var t) ? t.GetArrayLength() : 0;
            return (true, string.IsNullOrEmpty(provider)
                ? "Συνδέθηκε. Το μαγαζί δεν έχει ορίσει ακόμα πάροχο στο site."
                : $"Συνδέθηκε. Πάροχος: {provider} ({env}) · {terminals} τερματικά.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, "Δεν απαντά το site — έλεγξε τη διεύθυνση και το internet.");
        }
    }

    private void SetError(string error)
    {
        if (error == LastError) return;
        LastError = error;
        if (error.Length > 0) AppLog.Write("site", error);
        StatusChanged?.Invoke();
    }
}
