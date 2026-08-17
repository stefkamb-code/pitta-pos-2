using System.IO;
using System.Text.Json;

namespace PittaPos.App.Services;

/// <summary>Το σημείο μιας διεύθυνσης πάνω στον χάρτη.</summary>
public sealed record AddressPoint(double Lat, double Lon, bool Manual);

/// <summary>
/// Τα σημεία των διευθύνσεων που ξέρει ΤΟ ΜΑΓΑΖΙ, όχι ο χάρτης. Κάθε διεύθυνση που εντοπίστηκε (ή που
/// ο ταμίας την έδειξε ο ίδιος πάνω στον χάρτη) μένει εδώ και δεν ξαναρωτιέται ποτέ κανένας geocoder.
///
/// Γιατί: η αναζήτηση διεύθυνσης (Nominatim/Google) έβγαζε συχνά λάθος σημείο σε ελληνικές διευθύνσεις
/// — ίδιο όνομα δρόμου σε άλλη περιοχή, αριθμός που δεν υπάρχει στον χάρτη — και το ίδιο λάθος
/// επαναλαμβανόταν κάθε φορά που άνοιγε ο χάρτης, χωρίς τρόπο να διορθωθεί. Τώρα διορθώνεται ΜΙΑ φορά
/// (βλ. DeliveryMapWindow: κλικ στη σωστή θέση) και ισχύει για πάντα, για κάθε επόμενη παραγγελία στην
/// ίδια διεύθυνση. Χειροκίνητο σημείο δεν αντικαθίσταται ΠΟΤΕ από αυτόματο.
///
/// JSON στο %AppData%\PittaPos2\address-points.json· στο δεύτερο ταμείο τα διαβάζει/γράφει πάνω στο
/// κύριο, ώστε μια διόρθωση να ισχύει και στα δύο μηχανήματα.
/// </summary>
public class AddressPointsService
{
    public static AddressPointsService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private Dictionary<string, AddressPoint> _points = new(StringComparer.Ordinal);

    /// <summary>Η γεωκωδικοποίηση τρέχει σε νήματα παρασκηνίου (βλ. DeliveryRouteService) ενώ ο χάρτης
    /// διαβάζει σημεία από το UI — χωρίς κλείδωμα δύο ταυτόχρονες εγγραφές μπορούν να χαλάσουν το
    /// λεξικό ή να «φάνε» ένα σημείο, σιωπηλά.</summary>
    private readonly object _gate = new();

    public event Action? Changed;

    private AddressPointsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "address-points.json");
        // Το «στήσιμο πάνω στο UI thread» το αναλαμβάνει πλέον το ίδιο το StartPolling — αυτό εδώ ήταν
        // το store που το χρειάστηκε πρώτο (γεννιέται τεμπέλικα, συχνά σε νήμα παρασκηνίου).
        if (RemoteSync.IsClient)
            RemoteSync.StartPolling(TimeSpan.FromSeconds(30), RefreshFromHostAsync);
        else
            Load();
    }

    /// <summary>
    /// Το κλειδί μιας διεύθυνσης. Κεφαλαία χωρίς τόνους (βλ. GreekText) και χωρίς σημεία στίξης/διπλά
    /// κενά, ώστε «Μαγνησίας 12, Δάφνη» και «ΜΑΓΝΗΣΙΑΣ 12 ΔΑΦΝΗ» να είναι η ΙΔΙΑ διεύθυνση — αλλιώς η
    /// ίδια πόρτα θα αποθηκευόταν δύο φορές και η διόρθωση του ταμία δεν θα έπιανε την επόμενη φορά.
    /// </summary>
    public static string Key(string address)
    {
        var upper = GreekText.Upper(address);
        var chars = upper.Select(c => char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(" ", string.Concat(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Σταθερό «κλειδί» για το σημείο του ίδιου του καταστήματος, ανεξάρτητο από το τι γράφει (ή δεν
    /// γράφει) το πεδίο διεύθυνσης στις Ρυθμίσεις. Έτσι ο ταμίας μπορεί να δείξει το μαγαζί του πάνω στον
    /// χάρτη με ένα κλικ, χωρίς να χρειάζεται να βρεθεί η διεύθυνσή του σε καμία υπηρεσία αναζήτησης —
    /// που είναι ακριβώς η περίπτωση όπου λείπει η πινέζα του.
    /// </summary>
    public const string ShopPointKey = "ΚΑΤΑΣΤΗΜΑ";

    public AddressPoint? Get(string address)
    {
        var key = Key(address);
        if (key.Length == 0)
            return null;
        lock (_gate)
            return _points.TryGetValue(key, out var point) ? point : null;
    }

    /// <summary>
    /// Αποθηκεύει το σημείο μιας διεύθυνσης. <paramref name="manual"/> = ο ταμίας το έδειξε ο ίδιος στον
    /// χάρτη — τέτοιο σημείο δεν το πατάει ποτέ αυτόματη γεωκωδικοποίηση από πάνω.
    /// </summary>
    public void Set(string address, double lat, double lon, bool manual)
    {
        var key = Key(address);
        if (key.Length == 0)
            return;

        lock (_gate)
        {
            // Χειροκίνητο σημείο δεν το πατάει ποτέ αυτόματη γεωκωδικοποίηση από πάνω.
            if (_points.TryGetValue(key, out var existing) && existing.Manual && !manual)
                return;
            _points[key] = new AddressPoint(lat, lon, manual);
        }

        if (RemoteSync.IsClient)
        {
            Changed?.Invoke();
            _ = RemoteSync.PostAsync("/api/sync/address-points/set",
                new { Address = address, Lat = lat, Lon = lon, Manual = manual });
            return;
        }

        Save();
        Changed?.Invoke();
    }

    /// <summary>Δεύτερο ταμείο — παίρνει την εικόνα του κύριου. Αραιό poll (30 δλ): τα σημεία αλλάζουν
    /// ελάχιστα, μόνο όταν εμφανιστεί καινούρια διεύθυνση ή διορθώσει κάποιος μία.</summary>
    private async Task RefreshFromHostAsync()
    {
        var data = await RemoteSync.GetAsync<Dictionary<string, AddressPoint>>("/api/sync/address-points");
        if (data is null)
            return;
        lock (_gate)
            _points = new Dictionary<string, AddressPoint>(data, StringComparer.Ordinal);
        Changed?.Invoke();
    }

    /// <summary>Για το endpoint συγχρονισμού (host) — αντίγραφο, ώστε ο καλών να μην κρατάει στα χέρια
    /// του το ζωντανό λεξικό όσο το γράφει άλλο νήμα.</summary>
    public IReadOnlyDictionary<string, AddressPoint> All
    {
        get { lock (_gate) return new Dictionary<string, AddressPoint>(_points, StringComparer.Ordinal); }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, AddressPoint>>(File.ReadAllText(_path));
            if (saved is not null)
                _points = new Dictionary<string, AddressPoint>(saved, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            AppLog.Write("address-points", $"Δεν διαβάστηκαν τα σημεία διευθύνσεων: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            // Αντίγραφο μέσα στο κλείδωμα: η σειριοποίηση διαρκεί, και μια ταυτόχρονη εγγραφή από άλλο
            // νήμα θα έριχνε «η συλλογή άλλαξε» στη μέση του αρχείου.
            string json;
            lock (_gate)
                json = JsonSerializer.Serialize(_points, JsonOpts);
            AtomicFile.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            AppLog.Write("address-points", $"Δεν αποθηκεύτηκαν τα σημεία διευθύνσεων: {ex.Message}");
        }
    }
}
