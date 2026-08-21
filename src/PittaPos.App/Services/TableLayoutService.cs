using System.IO;
using System.Text.Json;
using System.Windows;

namespace PittaPos.App.Services;

/// <summary>
/// Πού βρίσκεται στην κάτοψη κάθε τραπέζι — ο χρήστης τα σέρνει ελεύθερα ώστε να ταιριάξουν με
/// την πραγματική διάταξη του εξωτερικού χώρου. JSON στο %AppData%\PittaPos, όπως τα υπόλοιπα stores.
/// </summary>
public class TableLayoutService
{
    public static TableLayoutService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Dictionary<int, Point> _positions = [];

    /// <summary>Σηκώνεται όταν αλλάζει μια θέση τραπεζιού (τοπικά ή από sync με το host).</summary>
    public event Action? Changed;

    private TableLayoutService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "table-layout.json");
        if (RemoteSync.IsClient)
        {
            RemoteSync.StartPolling(TimeSpan.FromSeconds(2), RefreshFromHostAsync);
            return;
        }
        Load();
    }

    /// <summary>Δεύτερο ταμείο (client) — αντικαθιστά την τοπική εικόνα με τη διάταξη του host.</summary>
    private async Task RefreshFromHostAsync()
    {
        var data = await RemoteSync.GetAsync<Dictionary<int, double[]>>("/api/sync/table-layout");
        if (data is null)
            return;
        _positions.Clear();
        foreach (var (table, xy) in data)
            if (xy.Length == 2)
                _positions[table] = new Point(xy[0], xy[1]);
        Changed?.Invoke();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var saved = JsonSerializer.Deserialize<Dictionary<int, double[]>>(File.ReadAllText(_path)) ?? [];
            foreach (var (table, xy) in saved)
                if (xy.Length == 2)
                    _positions[table] = new Point(xy[0], xy[1]);
        }
        catch (Exception)
        {
            // Χαλασμένο αρχείο — ξεκίνα με την προεπιλεγμένη διάταξη
        }
    }

    private void Save()
    {
        try
        {
            var toSave = _positions.ToDictionary(p => p.Key, p => new[] { p.Value.X, p.Value.Y });
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(toSave, JsonOpts));
        }
        catch (Exception ex)
        {
            // Δεν μπλοκάρει το ταμείο — αλλά ΓΡΑΦΕΤΑΙ. Μια αποτυχία εγγραφής (γεμάτος δίσκος,
            // κλείδωμα από antivirus, χαλασμένος δίσκος) σήμαινε ότι τα δεδομένα ζούσαν πια μόνο
            // στη μνήμη και θα χάνονταν στο επόμενο κλείσιμο — χωρίς κανένα ίχνος πουθενά.
            AppLog.Write("save", $"Δεν γράφτηκε το «{Path.GetFileName(_path)}»: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Θέση τραπεζιού· αν δεν έχει μετακινηθεί ποτέ, προεπιλεγμένη διάταξη σε 2 σειρές των 4.</summary>
    public Point GetPosition(int table) => _positions.TryGetValue(table, out var p) ? p : DefaultPosition(table);

    private static Point DefaultPosition(int table)
    {
        var index = table - 1;
        var col = index % 4;
        var row = index / 4;
        return new Point(30 + col * 200, 30 + row * 160);
    }

    public void SetPosition(int table, double x, double y)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync(table, x, y);
            return;
        }
        _positions[table] = new Point(x, y);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Δεύτερο ταμείο (client) — στέλνει τη νέα θέση στο host, μετά ξαναδιαβάζει την αληθινή κατάσταση.</summary>
    private async Task SyncThenRefreshAsync(int table, double x, double y)
    {
        await RemoteSync.PostAsync("/api/sync/table-layout", new { Table = table, X = x, Y = y });
        await RefreshFromHostAsync();
    }
}
