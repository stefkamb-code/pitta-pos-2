using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using PittaPos.Core.Fiscal;

namespace PittaPos.App.Services;

/// <summary>
/// Η σύνδεση του ταμείου με το site διαχείρισης (pitta-admin).
///
/// <list type="bullet">
/// <item><b>Από το site:</b> οι ρυθμίσεις ΑΑΔΕ του μαγαζιού — πάροχος, κλειδί, τερματικά, πού τυπώνεται η απόδειξη. Στο
///   ταμείο δεν ρυθμίζεται τίποτα από αυτά· τα αλλάζει το μαγαζί στην οθόνη «Ρυθμίσεις» του site.</item>
/// <item><b>Προς το site:</b> οι παραγγελίες και οι ακυρώσεις της ημέρας, ώστε το διαχειριστικό να δείχνει τα αληθινά
///   νούμερα. Ό,τι αλλάζει (π.χ. μετρητά→κάρτα από το Ιστορικό) ξαναστέλνεται και ο server ΑΝΤΙΚΑΘΙΣΤΑ την παλιά.</item>
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
    public static SiteLinkService Instance { get; } = new();

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly FiscalSettingsClient _settingsClient;
    private readonly Dictionary<string, string> _sent = [];   // κλειδί παραγγελίας → αποτύπωμα που στάλθηκε
    private readonly HashSet<string> _sentCancels = [];
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
        }
        finally { _busy = false; }
    }

    /// <summary>Ξαναδιαβάζει τις ρυθμίσεις ΑΑΔΕ από το site — στο άνοιγμα, κάθε 5 λεπτά, και με το «ΑΠΟΘΗΚΕΥΣΗ».</summary>
    public async Task RefreshSettingsAsync(bool force)
    {
        Configured(out var url, out var key);
        var (settings, fresh, error) = await _settingsClient.LoadAsync(url, key);
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
    private sealed record TillOrderDto(int OrderNumber, int Type, string? Channel, string? AppOrderRef, int? PaymentMethod,
        string Who, int? TablePerson, decimal Total, List<TillLineDto> Lines, DateTime PlacedAt, bool IsEveningShift);
    private sealed record TillCancelDto(int OrderNumber, string Name, int Quantity, decimal Revenue, DateTime CancelledAt,
        string CancelledBy, string Channel);

    private static string KeyOf(CompletedOrder o) => $"{o.OrderNumber}/{o.TablePerson}/{o.PlacedAt:yyyyMMdd}";

    /// <summary>Αποτύπωμα του περιεχομένου — αλλάζει όταν αλλάξει οτιδήποτε που φαίνεται στο site.</summary>
    private static string Fingerprint(CompletedOrder o) =>
        $"{o.Type}|{o.Channel}|{o.AppOrderRef}|{o.PaymentMethod}|{o.Total}|{o.Who}|{o.IsEveningShift}|"
        + string.Join(";", o.Lines.Select(l => $"{l.ProductId}:{l.Quantity}:{l.Revenue}"));

    private static string CategoryOf(string productId)
    {
        if (productId.Length == 0) return "";
        return MenuStore.Instance.Categories.FirstOrDefault(c => c.Products.Any(p => p.Id == productId))?.Name ?? "";
    }

    private async Task UploadAsync(string url, string key)
    {
        // Μόνο ό,τι μετράει ήδη στον τζίρο του ταμείου (όχι όσα περιμένουν κανάλι) — ίδια νούμερα σε ταμείο και site.
        var orders = SalesStatsService.Instance.CountedOrders
            .Where(o => !_sent.TryGetValue(KeyOf(o), out var fp) || fp != Fingerprint(o))
            .Take(200)
            .ToList();
        var cancels = CancellationLogService.Instance.Entries
            .Where(c => !_sentCancels.Contains($"{c.OrderNumber}/{c.CancelledAt:O}/{c.Name}"))
            .Take(200)
            .ToList();
        if (orders.Count == 0 && cancels.Count == 0)
            return;

        var body = new
        {
            orders = orders.Select(o => new TillOrderDto(o.OrderNumber, (int)o.Type, o.Channel, o.AppOrderRef,
                o.PaymentMethod is { } p ? (int)p : null, o.Who, o.TablePerson, o.Total,
                o.Lines.Select(l => new TillLineDto(l.ProductId, l.Name, l.Quantity, l.Revenue, l.Details, CategoryOf(l.ProductId))).ToList(),
                o.PlacedAt, o.IsEveningShift)).ToList(),
            cancellations = cancels.Select(c => new TillCancelDto(c.OrderNumber, c.Name, c.Quantity, c.Revenue, c.CancelledAt,
                c.CancelledBy, c.Channel)).ToList(),
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, url + "/api/till/orders") { Content = JsonContent.Create(body, options: Json) };
        req.Headers.Add("X-Store-Key", key);
        using var res = await Http.SendAsync(req);
        if (res.StatusCode == HttpStatusCode.Unauthorized) { SetError("Το site δεν δέχεται το κλειδί αυτού του ταμείου"); return; }
        if (!res.IsSuccessStatusCode) { SetError("Το site απάντησε " + (int)res.StatusCode); return; }

        foreach (var o in orders) _sent[KeyOf(o)] = Fingerprint(o);
        foreach (var c in cancels) _sentCancels.Add($"{c.OrderNumber}/{c.CancelledAt:O}/{c.Name}");
        SentSinceStart += orders.Count;
        LastContact = DateTime.Now;
        SetError("");
    }

    private async Task HeartbeatAsync(string url, string key)
    {
        var version = "";
        try { version = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "version.txt")).Trim(); } catch (IOException) { }
        using var req = new HttpRequestMessage(HttpMethod.Post, url + "/api/till/heartbeat")
        {
            Content = JsonContent.Create(new { version, evening = SettingsStore.Instance.Settings.IsEveningShift }, options: Json),
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
