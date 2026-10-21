using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using PittaPos.Core.Box;
using PittaPos.Core.Efood;

namespace PittaPos.App.Services;

/// <summary>Τι διαφέρει ανά πλατφόρμα στην ΑΝΤΙΣΤΟΙΧΙΣΗ: τα αρχεία, η ανάγνωση του καταλόγου και τα ονόματα στα μηνύματα.</summary>
/// <param name="Stem">Το πρώτο κομμάτι των αρχείων: «efood» → efood-catalog.json, efood-matching.json.</param>
/// <param name="Label">Όπως γράφεται στις οθόνες: «e-food», «Box».</param>
/// <param name="Of">Με άρθρο, για τις προτάσεις: «του e-food», «της Box».</param>
/// <param name="SettingsName">Το κουμπί στις Ρυθμίσεις: «E-FOOD», «BOX».</param>
public sealed record MatchPlatform(string Stem, string Label, string Of, string SettingsName,
    Func<string, IReadOnlyList<EfoodCatalogItem>> Parse);

/// <summary>
/// Η ΑΝΤΙΣΤΟΙΧΙΣΗ του e-food — και της Box, με τον ίδιο τρόπο — στο ταμείο: ένα αντίγραφο του καταλόγου του
/// καταστήματος στην πλατφόρμα (efood-catalog.json / box-catalog.json, μόνο για ανάγνωση — από τη γέφυρα) και ποιο
/// προϊόν του ταμείου είναι το καθένα (efood-matching.json / box-matching.json).
///
/// <para>Ο κατάλογος της πλατφόρμας δεν αλλάζει ποτέ από εδώ, ούτε το μενού του ταμείου ή το πώς τυπώνει: αλλάζει μόνο
/// το πώς διαβάζονται οι παραγγελίες της (βλ. <see cref="EfoodOrderReader"/>, <see cref="BoxOrderReader"/>).</para>
/// </summary>
public sealed class EfoodMatchStore
{
    public static readonly MatchPlatform EfoodPlatform = new("efood", "e-food", "του e-food", "E-FOOD", EfoodCatalogParser.Parse);
    public static readonly MatchPlatform BoxPlatform = new("box", "Box", "της Box", "BOX", BoxCatalogParser.Parse);

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);

    private static readonly Lazy<EfoodMatchStore> Shared = new(() => new EfoodMatchStore(
        Folder,
        EfoodMenu.Live,
        () => (SettingsStore.Instance.Settings.EfoodBridgeUrl.Trim().TrimEnd('/'), SettingsStore.Instance.Settings.EfoodTillKey.Trim()),
        message => AppLog.Write("efood", message)));

    private static readonly Lazy<EfoodMatchStore> SharedBox = new(() => new EfoodMatchStore(
        Folder,
        EfoodMenu.Live,
        () => (SettingsStore.Instance.Settings.BoxBridgeUrl.Trim().TrimEnd('/'), SettingsStore.Instance.Settings.BoxTillKey.Trim()),
        message => AppLog.Write("box", message),
        BoxPlatform));

    public static EfoodMatchStore Instance => Shared.Value;

    /// <summary>Η ΑΝΤΙΣΤΟΙΧΙΣΗ της Box (βλ. BoxBridgeService).</summary>
    public static EfoodMatchStore Box => SharedBox.Value;

    /// <summary>Το e-food αργεί να δώσει τον κατάλογο (η γέφυρα περιμένει ως 150″).</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _catalogPath;
    private readonly string _matchesPath;
    private readonly IEfoodMenu _menu;
    private readonly Func<(string Url, string Key)> _bridge;
    private readonly Action<string> _log;
    private readonly Dictionary<string, EfoodMatch> _matches = new(StringComparer.Ordinal);
    private HashSet<string> _catalogIds = new(StringComparer.Ordinal);
    private Task<(bool Ok, string Message)>? _refreshing;
    private DateTime _lastAttempt = DateTime.MinValue;
    private string _lastLogged = "";

    public EfoodMatchStore(string folder, IEfoodMenu menu, Func<(string Url, string Key)> bridge, Action<string> log,
        MatchPlatform? platform = null)
    {
        Platform = platform ?? EfoodPlatform;
        _menu = menu;
        _bridge = bridge;
        _log = log;
        Directory.CreateDirectory(folder);
        _catalogPath = Path.Combine(folder, Platform.Stem + "-catalog.json");
        _matchesPath = Path.Combine(folder, Platform.Stem + "-matching.json");
        Load();
    }

    public MatchPlatform Platform { get; }

    /// <summary>Ο κατάλογος του καταστήματος στο e-food, όπως ήρθε την τελευταία φορά — άδειος αν δεν έχει έρθει ποτέ.</summary>
    public IReadOnlyList<EfoodCatalogItem> Catalog { get; private set; } = [];

    public DateTime? CatalogUpdated { get; private set; }

    public EfoodMatch? MatchFor(string efoodId) => _matches.GetValueOrDefault(efoodId);

    public EfoodMatchState StateOf(EfoodCatalogItem item) => EfoodMatcher.StateOf(MatchFor(item.Id), _menu);

    public int PendingCount => Catalog.Count(i => StateOf(i) == EfoodMatchState.Pending);

    /// <summary>Αν ο κατάλογος ξέρει αυτό το προϊόν του e-food (το integrator_id μιας παραγγελίας).</summary>
    public bool Knows(string efoodId) => _catalogIds.Contains(efoodId);

    /// <summary>Ό,τι χρειάζεται η ανάγνωση μιας παραγγελίας.</summary>
    public EfoodContext Context() => new(Catalog, _matches.Values);

    /// <summary>Απόφαση ανθρώπου: αυτό το προϊόν του e-food είναι αυτό το προϊόν του ταμείου (κενό = δεν υπάρχει στο ταμείο).</summary>
    public void Set(EfoodCatalogItem item, string productId, string bread)
    {
        _matches[item.Id] = new EfoodMatch
        {
            EfoodId = item.Id,
            EfoodCode = item.Code,
            EfoodName = item.Name,
            EfoodCategory = item.Category,
            ProductId = productId,
            Bread = bread,
            Confirmed = true,
        };
        SaveMatches();
    }

    /// <summary>Φέρνει τον κατάλογο από τη γέφυρα. Δύο κλήσεις μαζί περιμένουν την ίδια ερώτηση.</summary>
    public async Task<(bool Ok, string Message)> RefreshAsync()
    {
        if (_refreshing is { } running)
            return await running;
        _refreshing = FetchAsync();
        try
        {
            return await _refreshing;
        }
        finally
        {
            _refreshing = null;
        }
    }

    /// <summary>Στο παρασκήνιο, όταν ο κατάλογος λείπει ή είναι παλιότερος από <paramref name="maxAge"/> — το πολύ μία
    /// προσπάθεια ανά 10 λεπτά, ώστε μια πεσμένη σύνδεση να μη ρωτάει το e-food κάθε 5 δευτερόλεπτα.</summary>
    public void RefreshIfDue(TimeSpan maxAge)
    {
        var now = DateTime.Now;
        if (_refreshing is not null || now - _lastAttempt < TimeSpan.FromMinutes(10))
            return;
        if (CatalogUpdated is { } updated && now - updated < maxAge)
            return;
        _ = RefreshAsync();
    }

    private async Task<(bool Ok, string Message)> FetchAsync()
    {
        _lastAttempt = DateTime.Now;
        var (url, key) = _bridge();
        if (url.Length == 0 || key.Length == 0)
            return (false, $"Δεν έχει ρυθμιστεί η γέφυρα (Ρυθμίσεις → {Platform.SettingsName}).");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url + "/till/catalog");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await Http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return Failed("Η γέφυρα δεν δέχεται το κλειδί αυτού του ταμείου.");
            if (!response.IsSuccessStatusCode)
                return Failed($"Η γέφυρα απάντησε {(int)response.StatusCode} — ο κατάλογος {Platform.Of} δεν ήρθε.");
            var body = await response.Content.ReadAsStringAsync();
            var items = await Task.Run(() => Platform.Parse(body));
            if (items.Count == 0)
                return Failed($"Ήρθε άδειος ο κατάλογος {Platform.Of} — μένει ο προηγούμενος.");

            SetCatalog(items, DateTime.Now);
            try
            {
                AtomicFile.WriteAllText(_catalogPath, body);
            }
            catch (Exception ex)
            {
                _log($"Δεν γράφτηκε το «{Path.GetFileName(_catalogPath)}»: {ex.GetType().Name}: {ex.Message}");
            }
            AddAutoMatches();
            _lastLogged = "";
            return (true, $"Ήρθε ο κατάλογος {Platform.Of} — {items.Count} προϊόντα.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException
                                       or UriFormatException or InvalidOperationException)
        {
            return Failed($"Δεν ήρθε ο κατάλογος {Platform.Of}: " + ex.Message);
        }
    }

    /// <summary>Γράφεται στο log μόνο όταν αλλάζει — όχι το ίδιο μήνυμα σε κάθε προσπάθεια.</summary>
    private (bool, string) Failed(string message)
    {
        if (message != _lastLogged)
            _log(message);
        _lastLogged = message;
        return (false, message);
    }

    private void SetCatalog(IReadOnlyList<EfoodCatalogItem> items, DateTime updated)
    {
        Catalog = items;
        CatalogUpdated = updated;
        _catalogIds = new HashSet<string>(items.Select(i => i.Id), StringComparer.Ordinal);
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_catalogPath))
                SetCatalog(Platform.Parse(File.ReadAllText(_catalogPath)), File.GetLastWriteTime(_catalogPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            _log($"Δεν διαβάστηκε το «{Path.GetFileName(_catalogPath)}» — ξαναέρχεται από τη γέφυρα: " + ex.Message);
        }

        try
        {
            if (File.Exists(_matchesPath))
                foreach (var match in JsonSerializer.Deserialize<List<EfoodMatch>>(File.ReadAllText(_matchesPath)) ?? [])
                    if (match.EfoodId.Length > 0)
                        _matches[match.EfoodId] = match;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Να ΜΗ γραφτεί από πάνω σαν άδειο: κρατιέται δίπλα όπως ήταν, για να μη χαθούν οι επιβεβαιώσεις.
            var kept = _matchesPath + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Copy(_matchesPath, kept, overwrite: true); } catch (Exception) { }
            _log($"Δεν διαβάστηκε το «{Path.GetFileName(_matchesPath)}» ({ex.Message}) — κρατήθηκε ως {Path.GetFileName(kept)}");
        }

        AddAutoMatches();
    }

    /// <summary>Ό,τι ταιριάζει αυτόματα (ίδιο όνομα, ίδια τιμή) γράφεται μία φορά και μένει — βλ. <see cref="EfoodContext.AutoMatches"/>.</summary>
    private void AddAutoMatches()
    {
        if (Catalog.Count == 0 || _menu.Categories.Count == 0)
            return;
        var added = EfoodContext.AutoMatches(Catalog, _matches.Values, _menu);
        if (added.Count == 0)
            return;
        foreach (var match in added)
            _matches[match.EfoodId] = match;
        SaveMatches();
    }

    private void SaveMatches()
    {
        try
        {
            var ordered = _matches.Values
                .OrderBy(m => m.EfoodCategory, StringComparer.CurrentCulture)
                .ThenBy(m => m.EfoodName, StringComparer.CurrentCulture)
                .ToList();
            AtomicFile.WriteAllText(_matchesPath, JsonSerializer.Serialize(ordered, JsonOpts));
        }
        catch (Exception ex)
        {
            _log($"Δεν γράφτηκε το «{Path.GetFileName(_matchesPath)}»: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
