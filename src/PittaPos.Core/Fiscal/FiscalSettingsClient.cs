using System.Text.Json;

namespace PittaPos.Core.Fiscal;

/// <summary>
/// Παίρνει τις ρυθμίσεις ΑΑΔΕ του μαγαζιού από το site διαχείρισης (<c>GET /api/till/settings</c>, με το κλειδί του
/// ταμείου). Στο ταμείο δεν ρυθμίζεται τίποτα από αυτά — τα αλλάζει το μαγαζί στη δική του οθόνη «Ρυθμίσεις».
///
/// <para>Κρατά αντίγραφο σε αρχείο: αν το site δεν απαντά (ίντερνετ, συντήρηση), το ταμείο δουλεύει με τις τελευταίες
/// ρυθμίσεις που ήξερε — η πώληση δεν σταματά επειδή έπεσε το site.</para>
/// </summary>
/// <param name="protect">Κρυπτογράφηση του αντιγράφου στον δίσκο (στο ταμείο: DPAPI των Windows), ώστε το κλειδί του
/// παρόχου να μη μένει σε καθαρό κείμενο. Χωρίς αυτό γράφεται ως έχει (μόνο για δοκιμές).</param>
public sealed class FiscalSettingsClient(HttpClient http, string cachePath,
    Func<string, string>? protect = null, Func<string, string>? unprotect = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Dto(string? Provider, string? Env, string? User, string? Key, List<FiscalTerminal>? Terminals,
        string? Receipt, string? ReceiptPrinter, string? Afm);

    /// <summary>Οι ρυθμίσεις από το site· αν δεν απαντά, οι τελευταίες γνωστές· αν δεν υπάρχουν ούτε αυτές, καμία.</summary>
    public async Task<(FiscalSettings Settings, bool Fresh, string Error)> LoadAsync(string siteUrl, string storeKey, CancellationToken ct = default)
    {
        siteUrl = siteUrl.Trim().TrimEnd('/');
        if (siteUrl.Length > 0 && storeKey.Trim().Length > 0)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, siteUrl + "/api/till/settings");
                req.Headers.Add("X-Store-Key", storeKey.Trim());
                using var res = await http.SendAsync(req, ct);
                if (res.IsSuccessStatusCode)
                {
                    var text = await res.Content.ReadAsStringAsync(ct);
                    var settings = From(JsonSerializer.Deserialize<Dto>(text, Json));
                    Save(text);
                    return (settings, true, "");
                }
                var why = (int)res.StatusCode == 401 ? "Το site δεν δέχεται το κλειδί αυτού του ταμείου." : "Το site απάντησε " + (int)res.StatusCode + ".";
                return (Cached(), false, why);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return (Cached(), false, "Δεν υπάρχει σύνδεση με το site.");
            }
        }
        return (Cached(), false, "Δεν έχει οριστεί το site στο ταμείο.");
    }

    private static FiscalSettings From(Dto? d) => d is null ? FiscalSettings.None : new FiscalSettings(
        d.Provider ?? "", d.Env ?? "sandbox", d.User ?? "", d.Key ?? "", d.Terminals ?? [],
        d.Receipt ?? "same", d.ReceiptPrinter ?? "", d.Afm ?? "");

    private FiscalSettings Cached()
    {
        try
        {
            if (!File.Exists(cachePath)) return FiscalSettings.None;
            var text = File.ReadAllText(cachePath);
            return From(JsonSerializer.Deserialize<Dto>(unprotect is null ? text : unprotect(text), Json));
        }
        catch { return FiscalSettings.None; }
    }

    private void Save(string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var tmp = cachePath + ".tmp";
            File.WriteAllText(tmp, protect is null ? json : protect(json));
            File.Move(tmp, cachePath, overwrite: true);
        }
        catch (IOException) { /* το αντίγραφο είναι βοήθεια, όχι προϋπόθεση */ }
    }
}
