using System.IO;
using System.Text.Json;
using PittaPos.Core.Data;

namespace PittaPos.App.Services;

/// <summary>Τι επιτρέπεται να ανοίξει ένας κωδικός προσωπικού. Κλειδιά — γράφονται στο settings.json,
/// μην τα μετονομάσεις.</summary>
public static class StaffRight
{
    public const string Cancel = "cancel";
    public const string Stats = "stats";
    public const string History = "history";
    public const string Menu = "menu";
    public const string Customers = "customers";
    public const string Consumption = "consumption";

    /// <summary>Με τη σειρά που εμφανίζονται στην οθόνη κωδικών.</summary>
    public static readonly (string Key, string Label)[] All =
    [
        (Cancel, "ΑΚΥΡΩΣΕΙΣ"),
        (Stats, "ΣΤΑΤΙΣΤΙΚΑ"),
        (History, "ΙΣΤΟΡΙΚΟ"),
        (Menu, "ΚΑΤΑΛΟΓΟΣ"),
        (Customers, "ΠΕΛΑΤΕΣ"),
        (Consumption, "ΚΑΤΑΝΑΛΩΣΕΙΣ"),
    ];
}

/// <summary>Ένας ονομαστικός κωδικός προσωπικού — κενό Name/Pin σημαίνει αδειανή, ανενεργή θέση.</summary>
public class StaffPin
{
    public string Name { get; set; } = "";
    public string Pin { get; set; } = "";

    /// <summary>
    /// Τι ανοίγει αυτός ο κωδικός (βλ. <see cref="StaffRight"/>) — τα «τικ» της οθόνης κωδικών.
    ///
    /// <para><c>null</c> σημαίνει ΟΛΑ: έτσι οι κωδικοί που υπήρχαν πριν μπουν τα δικαιώματα
    /// συνεχίζουν να δουλεύουν ακριβώς όπως χθες, χωρίς να χρειαστεί να μπει κανείς να τα τικάρει.</para>
    /// </summary>
    public List<string>? Rights { get; set; }

    /// <summary>Επιτρέπεται αυτό το σημείο;</summary>
    public bool Allows(string right) => Rights is null || Rights.Contains(right);
}

public class AppSettings
{
    /// <summary>"light" ή "dark".</summary>
    public string Theme { get; set; } = "light";
    /// <summary>"el" ή "en".</summary>
    public string Language { get; set; } = "el";
    /// <summary>Κωδικός για κλειδωμένα πεδία (Στατιστικά/Ιστορικό/Κατάλογος).</summary>
    public string Pin { get; set; } = "1992";
    /// <summary>Πόσα τραπέζια δείχνει το Βήμα 1 όταν επιλέγεται «ΤΡΑΠΕΖΙ».</summary>
    public int TableCount { get; set; } = MenuSeed.TableCount;
    /// <summary>Τρέχουσα βάρδια — χειροκίνητος διακόπτης, ποτέ αυτόματος (για να μην μπερδεύεται).</summary>
    public bool IsEveningShift { get; set; }
    /// <summary>Όνομα εκτυπωτή (Windows print queue) για σιωπηλή αυτόματη εκτύπωση — κενό = ανενεργή.</summary>
    public string PrinterName { get; set; } = "";

    /// <summary>Κωδικός ΣΕΡΒΙΤΟΡΟΥ — μόνο για την εφαρμογή του κινητού (βλ. WaiterApiService). Δεν
    /// ανοίγει Στατιστικά/Ιστορικό/Κατάλογο στο ταμείο και δεν ακυρώνει: ο σερβιτόρος τον έχει στο
    /// τηλέφωνό του και δεν πρέπει να του δίνει τίποτα άλλο. Κενό = δεν έχει οριστεί, οπότε το κινητό
    /// δουλεύει με τον κωδικό του καταστήματος, όπως πάντα.</summary>
    public string WaiterPin { get; set; } = "";

    /// <summary>Διεύθυνση καταστήματος — σημείο εκκίνησης/επιστροφής της προτεινόμενης διαδρομής
    /// στον Χάρτη Διανομής (βλ. DeliveryRouteService). Κενό = δεν δείχνεται σημείο καταστήματος.</summary>
    public string ShopAddress { get; set; } = "";

    /// <summary>Κλειδί Google Maps API (Geocoding + Places + Directions + Maps JavaScript) — κενό =
    /// ο Χάρτης Διανομής/autocomplete διεύθυνσης δουλεύει με το δωρεάν OpenStreetMap/Nominatim/OSRM.</summary>
    public string GoogleMapsApiKey { get; set; } = "";

    // ---- Δεύτερο ταμείο (βλ. RemoteSync) ----
    /// <summary>"host" (κύριο ταμείο, όπως σήμερα — τοπικά δεδομένα) ή "client" (δεύτερο ταμείο, διαβάζει από το host).</summary>
    public string NetworkMode { get; set; } = "host";
    /// <summary>Τοπική IP του κύριου ταμείου στο δίκτυο του μαγαζιού — μόνο όταν NetworkMode == "client".</summary>
    public string HostAddress { get; set; } = "";

    // ---- Αναγνώριση κλήσεων μέσω AMI του Grandstream UCM (βλ. AmiClientService) ----
    /// <summary>IP του τηλεφωνικού κέντρου (UCM) — κενό = ανενεργή αναγνώριση κλήσεων.</summary>
    public string UcmHost { get; set; } = "";
    public int AmiPort { get; set; } = 5038;
    public string AmiUsername { get; set; } = "";
    public string AmiPassword { get; set; } = "";

    // ---- Email αναφοράς κλεισίματος ημέρας ----
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    /// <summary>Λογαριασμός αποστολής (π.χ. το gmail του καταστήματος).</summary>
    public string SmtpUser { get; set; } = "";
    /// <summary>Κωδικός εφαρμογής (app password) του λογαριασμού αποστολής.</summary>
    public string SmtpPassword { get; set; } = "";
    /// <summary>Παραλήπτης της αναφοράς — κενό = ίδιο με τον λογαριασμό αποστολής.</summary>
    public string ReportEmail { get; set; } = "";

    /// <summary>4 ονομαστικοί κωδικοί ακύρωσης — ώστε το ιστορικό ακυρωμένων να δείχνει ποιος ακύρωσε.
    /// Διαχειρίζονται μόνο από τον admin (βλ. SettingsWindow, πίσω από τον γενικό κωδικό).</summary>
    public List<StaffPin> CancelStaffPins { get; set; } = [new(), new(), new(), new()];

    // ---- Τι δείχνει η απόδειξη ----
    public string ReceiptTitle { get; set; } = "ΠΙΤΤΑ ΤΟΥ ΠΑΠΠΟΥ";
    /// <summary>Στοιχεία καταστήματος κάτω από τον τίτλο (διεύθυνση/τηλ./ΑΦΜ) — πολλαπλές γραμμές.</summary>
    public string ReceiptInfo { get; set; } = "";
    /// <summary>ΔΕΝ χρησιμοποιείται πια στην απόδειξη — στο τέλος τυπώνεται αυτόματα ο τύπος και ο
    /// αριθμός («ΤΡΑΠΕΖΙ #6»), βλ. ReceiptWindow. Η ιδιότητα μένει ώστε τα υπάρχοντα settings.json και
    /// το SharedSettingsDto (συγχρονισμός με το άλλο ταμείο) να διαβάζονται όπως πριν· αν έφευγε, ένα
    /// ταμείο παλιότερης έκδοσης θα έστελνε πεδίο που δεν υπάρχει.</summary>
    public string ReceiptFooter { get; set; } = "";
    public bool ReceiptShowDateTime { get; set; } = true;
    public bool ReceiptShowCustomer { get; set; } = true;
    /// <summary>Λεπτομέρειες προϊόντων (ψωμί/έξτρα/χωρίς).</summary>
    public bool ReceiptShowDetails { get; set; } = true;
    // ---- Μεγέθη γραμματοσειράς απόδειξης — ανεξάρτητα ανά ενότητα (βλ. ReceiptWindow), όχι μία
    // κοινή κλίμακα, ώστε π.χ. να μεγαλώνει ο τίτλος χωρίς να μεγαλώνουν οι γραμμές παραγγελίας. ----
    //
    // ΤΑ ΟΡΙΑ ΖΟΥΝ ΕΔΩ, στα ίδια τα πεδία. Πριν έμπαιναν μόνο στην οθόνη που τα ρυθμίζει, οπότε μια
    // τιμή που ερχόταν από αλλού — χαλασμένο ή πειραγμένο settings.json, ή συγχρονισμός από το δεύτερο
    // ταμείο — περνούσε ανέγγιχτη. Και το μηδέν ΔΕΝ είναι απλώς άσχημο: το WPF δεν δέχεται FontSize 0
    // και πετάει εξαίρεση, δηλαδή δεν θα τυπωνόταν ΚΑΜΙΑ απόδειξη μέχρι να το βρει κάποιος.
    private double _receiptTitleFontSize = 15;
    private double _receiptItemsFontSize = 14;
    private double _receiptTotalFontSize = 16;
    private double _receiptMetaFontSize = 13;

    /// <summary>Μέγεθος γραμμάτων που δέχεται σίγουρα το WPF: εκτός ορίων μαζεύεται, και το NaN —
    /// που ούτε το Math.Clamp το πιάνει, το γυρνάει ως έχει — πέφτει στην προεπιλογή.</summary>
    private static double SafeFontSize(double value, double min, double max, double fallback) =>
        double.IsNaN(value) ? fallback : Math.Clamp(value, min, max);

    public double ReceiptTitleFontSize
    {
        get => _receiptTitleFontSize;
        set => _receiptTitleFontSize = SafeFontSize(value, 8, 30, 15);
    }

    /// <summary>Γραμμές προϊόντων (όνομα/τιμή) — οι λεπτομέρειες (ψωμί/έξτρα) ακολουθούν σε μικρότερη αναλογία.</summary>
    public double ReceiptItemsFontSize
    {
        get => _receiptItemsFontSize;
        set => _receiptItemsFontSize = SafeFontSize(value, 8, 24, 14);
    }

    /// <summary>Ποσό συνόλου — η ετικέτα «ΣΥΝΟΛΟ» ακολουθεί σε μικρότερη αναλογία.</summary>
    public double ReceiptTotalFontSize
    {
        get => _receiptTotalFontSize;
        set => _receiptTotalFontSize = SafeFontSize(value, 8, 32, 16);
    }

    /// <summary>Λοιπά κείμενα: στοιχεία καταστήματος, αρ. παραγγελίας/ώρα, τύπος/πελάτης, υποσέλιδο.</summary>
    public double ReceiptMetaFontSize
    {
        get => _receiptMetaFontSize;
        set => _receiptMetaFontSize = SafeFontSize(value, 6, 20, 13);
    }
}

/// <summary>Ρυθμίσεις εφαρμογής — JSON στο %AppData%\PittaPos\settings.json.</summary>
public class SettingsStore
{
    public static SettingsStore Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;

    public AppSettings Settings { get; private set; } = new();

    public bool IsDark => Settings.Theme == "dark";

    /// <summary>Σηκώνεται όταν αλλάζουν ρυθμίσεις που χρειάζονται ζωντανή ανανέωση αλλού (π.χ. αριθμός τραπεζιών).</summary>
    public event Action? Changed;

    private SettingsStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        try
        {
            if (File.Exists(_path))
                Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
        }
        catch (Exception)
        {
            Settings = new();
        }

        // Μετάβαση από τα παλιά (μικρότερα) προεπιλεγμένα μεγέθη γραμματοσειράς απόδειξης στα νέα — μόνο
        // αν ο χρήστης δεν έχει ήδη αλλάξει το καθένα ξεχωριστά από τις προηγούμενες προεπιλογές (αν το
        // έκανε, π.χ. το μεγάλωσε ακόμα παραπάνω μόνος του, δεν το πειράζουμε). Χρειάζεται γιατί το Save()
        // γράφει ΟΛΗ την τρέχουσα τιμή στο δίσκο μόλις αλλάξει οτιδήποτε άλλο στις ρυθμίσεις — μια απλή
        // αλλαγή στο default της κλάσης δεν φτάνει ποτέ σε ήδη αποθηκευμένα settings.json.
        var migratedFontSize = false;
        if (Settings.ReceiptTitleFontSize is 16 or 18) { Settings.ReceiptTitleFontSize = 15; migratedFontSize = true; }
        // Το μέγεθος προϊόντων ΔΕΝ μεταναστεύει πια. Οι παλιές αυτόματες αυξήσεις (13→15→17→20→23→26)
        // γίνονταν επειδή η ρύθμιση δεν έφτανε ποτέ στο χαρτί (βλ. ReceiptWindow.ItemText_Loaded) και
        // νόμιζα ότι απλώς δεν ήταν αρκετά μεγάλη. Τώρα που εφαρμόζεται σωστά, οποιαδήποτε αυτόματη
        // αλλαγή θα ακύρωνε την επιλογή του χρήστη σε κάθε εκκίνηση.
        // (Όπως και το μέγεθος προϊόντων, το σύνολο δεν μεταναστεύει πια — αποφασίζει ο χρήστης.)
        if (Settings.ReceiptMetaFontSize == 11) { Settings.ReceiptMetaFontSize = 13; migratedFontSize = true; }
        if (migratedFontSize)
            Save();

        // Ο αριθμός τραπεζιών είναι το μόνο πεδίο ρυθμίσεων που έχει νόημα να ταιριάζει ανάμεσα στα δύο
        // ταμεία (θέμα/γλώσσα/εκτυπωτής μένουν σκόπιμα τοπικά) — αλλιώς το δεύτερο ταμείο μπορεί να δείχνει
        // λιγότερα τραπέζια από όσα υπάρχουν πραγματικά στο κύριο.
        // Σημείωση: χρησιμοποιεί το ήδη φορτωμένο Settings.NetworkMode αντί για RemoteSync.IsClient —
        // εκείνο περνάει από SettingsStore.Instance, που ΕΔΩ μέσα στον constructor δεν έχει ακόμα οριστεί
        // (η στατική ανάθεση `Instance = new SettingsStore()` δεν έχει ολοκληρωθεί), θα γύριζε null.
        if (Settings.NetworkMode == "client")
        {
            RemoteSync.StartPolling(TimeSpan.FromSeconds(5), RefreshTableCountFromHostAsync);
            RemoteSync.StartPolling(TimeSpan.FromSeconds(5), RefreshSharedSettingsFromHostAsync);
        }
    }

    // ΓΙΑΤΙ ΥΠΑΡΧΟΥΝ ΑΥΤΟΙ ΟΙ ΔΥΟ ΜΕΤΡΗΤΕΣ — ΤΟ ΠΙΟ ΔΥΣΚΟΛΟ ΣΗΜΕΙΟ ΤΟΥ ΑΡΧΕΙΟΥ.
    //
    // Το δεύτερο ταμείο ρωτάει το κύριο κάθε 5" ΚΑΙ σπρώχνει τις δικές του αλλαγές προς τα εκεί. Οι δύο
    // κινήσεις διασταυρώνονταν: ο ταμίας πατούσε ΒΡΑΔΙΝΗ, η αλλαγή έφευγε προς το κύριο, και εν τω
    // μεταξύ επέστρεφε μια απάντηση polling που είχε ζητηθεί ΠΡΙΝ το πάτημα — με την παλιά βάρδια.
    // Το κουμπί «γυρνούσε» μόνο του πίσω για ένα πεντάλεπτο δευτερολέπτων και ο ταμίας ξαναπατούσε.
    // Μετρημένο: η βάρδια άλλαζε σωστά στο κύριο ταμείο, αλλά στο δεύτερο έδειχνε την παλιά.
    //
    // Κανόνας: όσο ταξιδεύει δική μας αλλαγή — ή αν ξεκίνησε καινούρια όσο περιμέναμε απάντηση — η
    // απάντηση του κυρίου είναι ΗΔΗ ΠΑΛΙΑ και αγνοείται. Το επόμενο polling (σε 5") φέρνει την αλήθεια.
    private int _pushesInFlight;
    private int _localChangeVersion;

    /// <summary>Ξεκινάει τοπική αλλαγή που θα σταλεί στο κύριο ταμείο.</summary>
    private void MarkLocalChange() => Interlocked.Increment(ref _localChangeVersion);

    /// <summary>Έχει προσπεραστεί η απάντηση του κυρίου από δική μας αλλαγή;</summary>
    private bool OutdatedByLocalChange(int versionBefore) =>
        Volatile.Read(ref _pushesInFlight) > 0 || Volatile.Read(ref _localChangeVersion) != versionBefore;

    /// <summary>Δεύτερο ταμείο (client) — ευθυγραμμίζει τον αριθμό τραπεζιών με το host.</summary>
    private async Task RefreshTableCountFromHostAsync()
    {
        if (Volatile.Read(ref _pushesInFlight) > 0)
            return;
        var version = Volatile.Read(ref _localChangeVersion);
        var count = await RemoteSync.GetAsync<int?>("/api/sync/table-count");
        if (count is null || count == Settings.TableCount || OutdatedByLocalChange(version))
            return;
        Settings.TableCount = count.Value;
        Changed?.Invoke();
    }

    /// <summary>Όλες οι ρυθμίσεις καταστήματος που έχει νόημα να ταιριάζουν ανάμεσα στα δύο ταμεία —
    /// βλ. SharedSettingsDto για ποια πεδία μπαίνουν/μένουν σκόπιμα εκτός (PrinterName/NetworkMode/
    /// HostAddress).</summary>
    public SharedSettingsDto BuildSharedSettingsDto() => new(
        Settings.Theme, Settings.Language, Settings.Pin, Settings.IsEveningShift,
        Settings.ShopAddress, Settings.GoogleMapsApiKey,
        Settings.UcmHost, Settings.AmiPort, Settings.AmiUsername, Settings.AmiPassword,
        Settings.SmtpHost, Settings.SmtpPort, Settings.SmtpUser, Settings.SmtpPassword, Settings.ReportEmail,
        Settings.CancelStaffPins,
        Settings.ReceiptTitle, Settings.ReceiptInfo, Settings.ReceiptFooter,
        Settings.ReceiptShowDateTime, Settings.ReceiptShowCustomer, Settings.ReceiptShowDetails,
        Settings.ReceiptTitleFontSize, Settings.ReceiptItemsFontSize, Settings.ReceiptTotalFontSize, Settings.ReceiptMetaFontSize,
        Settings.WaiterPin);

    /// <summary>Εφαρμόζει ένα SharedSettingsDto πάνω στις τοπικές ρυθμίσεις (host που δέχεται push από
    /// client, ή client που τραβάει από host) — Save()/Changed μόνο αν κάτι πραγματικά άλλαξε, ώστε να μην
    /// έχουμε άσκοπο I/O/UI refresh κάθε 5" στο polling του client όταν δεν άλλαξε τίποτα. Επιστρέφει αν
    /// άλλαξε κάτι, για όποιον καλούντα θέλει να ξέρει (π.χ. δοκιμές).</summary>
    public bool ApplySharedSettingsDto(SharedSettingsDto dto)
    {
        var before = JsonSerializer.Serialize(BuildSharedSettingsDto(), JsonOpts);
        Settings.Theme = dto.Theme;
        Settings.Language = dto.Language;
        Settings.Pin = dto.Pin;
        Settings.IsEveningShift = dto.IsEveningShift;
        Settings.ShopAddress = dto.ShopAddress;
        Settings.GoogleMapsApiKey = dto.GoogleMapsApiKey;
        Settings.UcmHost = dto.UcmHost;
        Settings.AmiPort = dto.AmiPort;
        Settings.AmiUsername = dto.AmiUsername;
        Settings.AmiPassword = dto.AmiPassword;
        Settings.SmtpHost = dto.SmtpHost;
        Settings.SmtpPort = dto.SmtpPort;
        Settings.SmtpUser = dto.SmtpUser;
        Settings.SmtpPassword = dto.SmtpPassword;
        Settings.ReportEmail = dto.ReportEmail;
        Settings.CancelStaffPins = dto.CancelStaffPins;
        Settings.WaiterPin = dto.WaiterPin ?? "";
        Settings.ReceiptTitle = dto.ReceiptTitle;
        Settings.ReceiptInfo = dto.ReceiptInfo;
        Settings.ReceiptFooter = dto.ReceiptFooter;
        Settings.ReceiptShowDateTime = dto.ReceiptShowDateTime;
        Settings.ReceiptShowCustomer = dto.ReceiptShowCustomer;
        Settings.ReceiptShowDetails = dto.ReceiptShowDetails;
        Settings.ReceiptTitleFontSize = dto.ReceiptTitleFontSize;
        Settings.ReceiptItemsFontSize = dto.ReceiptItemsFontSize;
        Settings.ReceiptTotalFontSize = dto.ReceiptTotalFontSize;
        Settings.ReceiptMetaFontSize = dto.ReceiptMetaFontSize;
        var after = JsonSerializer.Serialize(BuildSharedSettingsDto(), JsonOpts);
        var changed = before != after;
        if (changed)
        {
            Save();
            Changed?.Invoke();
        }
        return changed;
    }

    /// <summary>Δεύτερο ταμείο (client) — τραβάει τις κοινές ρυθμίσεις από το host κάθε 5". Το host είναι
    /// πάντα η αυθεντική πηγή· απλή τελευταία-νίκη λογική (όχι merge), αρκετό για ένα μικρό μαγαζί όπου
    /// σπάνια αλλάζουν ρυθμίσεις ταυτόχρονα και από τα δύο ταμεία.</summary>
    private async Task RefreshSharedSettingsFromHostAsync()
    {
        if (Volatile.Read(ref _pushesInFlight) > 0)
            return;
        var version = Volatile.Read(ref _localChangeVersion);
        var dto = await RemoteSync.GetAsync<SharedSettingsDto>("/api/sync/settings");
        if (dto is null || OutdatedByLocalChange(version))
            return;
        ApplySharedSettingsDto(dto);
    }

    /// <summary>Δεύτερο ταμείο (client) — μόλις αλλάξει κάτι τοπικά, το στέλνει και στο host (fire-and-
    /// forget) ώστε να μην περιμένει το επόμενο 5" polling του host-ίδιου-του-εαυτού του (το host δεν
    /// τραβάει τίποτα μόνο του — μόνο δέχεται). Στο host αυτό δεν κάνει τίποτα (IsClient == false).</summary>
    /// <summary>Δημόσιο γιατί το καλεί και ο ίδιος ο server όταν δεχτεί ρυθμίσεις από αλλού
    /// (βλ. WaiterApiService, POST /api/sync/settings): αν ΑΥΤΟ το ταμείο είναι δεύτερο, οι ρυθμίσεις
    /// πρέπει να συνεχίσουν το ταξίδι τους ως το κύριο, αλλιώς μένουν εδώ και τις σβήνει το επόμενο
    /// polling.</summary>
    public void PushSharedSettingsIfClient()
    {
        if (!RemoteSync.IsClient)
            return;
        MarkLocalChange();
        _ = PushSharedSettingsAsync();
    }

    private async Task PushSharedSettingsAsync()
    {
        Interlocked.Increment(ref _pushesInFlight);
        try
        {
            await RemoteSync.PostAsync("/api/sync/settings", BuildSharedSettingsDto());
        }
        finally
        {
            Interlocked.Decrement(ref _pushesInFlight);
        }
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(Settings, JsonOpts));
        }
        catch (Exception ex)
        {
            // Δεν μπλοκάρει το ταμείο — αλλά ΓΡΑΦΕΤΑΙ. Μια αποτυχία εγγραφής (γεμάτος δίσκος,
            // κλείδωμα από antivirus, χαλασμένος δίσκος) σήμαινε ότι τα δεδομένα ζούσαν πια μόνο
            // στη μνήμη και θα χάνονταν στο επόμενο κλείσιμο — χωρίς κανένα ίχνος πουθενά.
            AppLog.Write("save", $"Δεν γράφτηκε το «{Path.GetFileName(_path)}»: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void SetTheme(string theme)
    {
        Settings.Theme = theme;
        Save();
        ThemeManager.Apply(IsDark);
        PushSharedSettingsIfClient();
    }

    public void SetLanguage(string language)
    {
        Settings.Language = language;
        Save();
        PushSharedSettingsIfClient();
    }

    /// <summary>Ορίζει πόσα τραπέζια εμφανίζονται στο Βήμα 1 (ελάχιστο 1).</summary>
    public void SetTableCount(int count)
    {
        if (RemoteSync.IsClient)
        {
            MarkLocalChange();
            _ = SyncTableCountAsync(count);
            return;
        }
        Settings.TableCount = Math.Max(1, count);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Δεύτερο ταμείο (client) — στέλνει τον νέο αριθμό στο host, μετά ξαναδιαβάζει την αληθινή τιμή.</summary>
    private async Task SyncTableCountAsync(int count)
    {
        await RemoteSync.PostAsync("/api/sync/table-count", new { Count = Math.Max(1, count) });
        await RefreshTableCountFromHostAsync();
    }

    /// <summary>Χειροκίνητη αλλαγή τρέχουσας βάρδιας — δεν αλλάζει ποτέ μόνη της.</summary>
    public void SetShift(bool evening)
    {
        Settings.IsEveningShift = evening;
        Save();
        Changed?.Invoke();
        PushSharedSettingsIfClient();
    }

    /// <summary>Ορίζει τον εκτυπωτή για σιωπηλή αυτόματη εκτύπωση.</summary>
    public void SetPrinter(string name)
    {
        Settings.PrinterName = name;
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Ορίζει αν αυτό το ταμείο είναι το κύριο (host, τοπικά δεδομένα — όπως πάντα) ή δεύτερο
    /// ταμείο (client, διαβάζει/γράφει πάνω στο host). Χρειάζεται restart της εφαρμογής για να
    /// πιάσει η αλλαγή, γιατί τα stores διαλέγουν λειτουργία στην αρχική τους φόρτωση.
    /// </summary>
    public void SetNetworkMode(string mode, string hostAddress)
    {
        Settings.NetworkMode = mode == "client" ? "client" : "host";
        Settings.HostAddress = CleanHostAddress(hostAddress);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Ανέκτηση μόνο της IP από ό,τι κι αν επικολλήσει ο χρήστης — π.χ. αν αντιγράψει τη
    /// διεύθυνση του σερβιτόρου ("http://192.168.1.50:5190") αντί για την ψιλή IP που ζητά η οθόνη
    /// δεύτερου ταμείου, δεν πρέπει να σπάσει το BaseUrl στο RemoteSync.</summary>
    private static string CleanHostAddress(string hostAddress)
    {
        var s = hostAddress.Trim();
        var schemeIdx = s.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx >= 0)
            s = s[(schemeIdx + 3)..];
        s = s.TrimEnd('/');
        var colonIdx = s.IndexOf(':');
        if (colonIdx >= 0)
            s = s[..colonIdx];
        return s;
    }

    /// <summary>
    /// Ρυθμίσεις σύνδεσης στο AMI του Grandstream UCM για αναγνώριση κλήσεων. Κενό UcmHost = ανενεργό.
    /// Χρειάζεται restart της εφαρμογής για να πιάσει η αλλαγή (το AmiClientService συνδέεται μία
    /// φορά στην εκκίνηση).
    /// </summary>
    public void SetShopAddress(string address)
    {
        Settings.ShopAddress = address.Trim();
        Save();
        PushSharedSettingsIfClient();
    }

    public void SetGoogleMapsApiKey(string key)
    {
        Settings.GoogleMapsApiKey = key.Trim();
        Save();
        PushSharedSettingsIfClient();
    }

    public void SetAmiConfig(string ucmHost, int amiPort, string username, string password)
    {
        Settings.UcmHost = ucmHost.Trim();
        Settings.AmiPort = amiPort;
        Settings.AmiUsername = username.Trim();
        Settings.AmiPassword = password;
        Save();
        Changed?.Invoke();
        PushSharedSettingsIfClient();
    }

    public void SetEmail(string host, int port, string user, string password, string reportEmail)
    {
        Settings.SmtpHost = host.Trim();
        Settings.SmtpPort = port;
        Settings.SmtpUser = user.Trim();
        Settings.SmtpPassword = password;
        Settings.ReportEmail = reportEmail.Trim();
        Save();
        PushSharedSettingsIfClient();
    }

    /// <summary>Το υποσέλιδο δεν περνιέται πια: στη θέση του τυπώνεται αυτόματα ο τύπος και ο αριθμός
    /// της παραγγελίας (βλ. ReceiptWindow), οπότε δεν υπάρχει τίποτα να ρυθμίσει ο χρήστης.</summary>
    public void SetReceipt(string title, string info,
        bool showDateTime, bool showCustomer, bool showDetails,
        double titleFontSize, double itemsFontSize, double totalFontSize, double metaFontSize)
    {
        Settings.ReceiptTitle = title.Trim();
        Settings.ReceiptInfo = info.Trim();
        Settings.ReceiptShowDateTime = showDateTime;
        Settings.ReceiptShowCustomer = showCustomer;
        Settings.ReceiptShowDetails = showDetails;
        Settings.ReceiptTitleFontSize = Math.Clamp(titleFontSize, 8, 30);
        Settings.ReceiptItemsFontSize = Math.Clamp(itemsFontSize, 8, 24);
        Settings.ReceiptTotalFontSize = Math.Clamp(totalFontSize, 8, 32);
        Settings.ReceiptMetaFontSize = Math.Clamp(metaFontSize, 6, 20);
        Save();
        PushSharedSettingsIfClient();
    }

    /// <summary>
    /// Ανοίγει αυτός ο κωδικός ΑΥΤΟ το σημείο; Τρεις περιπτώσεις περνάνε: ο admin, ο κωδικός του
    /// καταστήματος, και κάθε άτομο του προσωπικού που έχει τικαρισμένο το συγκεκριμένο δικαίωμα
    /// (βλ. <see cref="StaffRight"/>, οθόνη ΚΩΔΙΚΟΙ ΠΡΟΣΩΠΙΚΟΥ).
    /// </summary>
    public bool VerifyPin(string pin, string right) =>
        VerifyOwnerPin(pin) || HasStaffRight(pin, right);

    /// <summary>
    /// Το ΕΝΑ κλειδί που ανοίγει τα πάντα: ο admin.
    ///
    /// <para>Ο παλιός «κωδικός καταστήματος» (<c>Settings.Pin</c>, εργοστασιακά 1992) ΔΕΝ ανοίγει πια
    /// τίποτα — ζητήθηκε ρητά, γιατί τον ήξεραν όλοι. Μένει στο αρχείο ρυθμίσεων μόνο ως εφεδρεία για
    /// τα ήδη στημένα κινητά, μέχρι να μπει κωδικός σερβιτόρου (βλ. VerifyWaiterPin).</para>
    /// </summary>
    public bool VerifyOwnerPin(string pin) => pin == AdminPin;

    /// <summary>Υπάρχει άτομο με αυτόν τον κωδικό ΚΑΙ με αυτό το δικαίωμα τικαρισμένο;</summary>
    public bool HasStaffRight(string pin, string right) =>
        pin.Length > 0 && Settings.CancelStaffPins.Any(p => p.Pin.Length > 0 && p.Pin == pin && p.Allows(right));

    /// <summary>Ανοίγει αυτός ο κωδικός το συγκεκριμένο σημείο; (ίδιο με VerifyPin — υπάρχει για να
    /// διαβάζεται καθαρά εκεί που ελέγχουμε κωδικό που δόθηκε ΝΩΡΙΤΕΡΑ, π.χ. το ΙΣΤΟΡΙΚΟ μέσα στα
    /// Στατιστικά, χωρίς να ξαναζητηθεί.)</summary>
    public bool PinOpens(string? pin, string right) => pin is not null && VerifyPin(pin, right);

    /// <summary>
    /// Ο κωδικός που δέχεται το ΚΙΝΗΤΟ του σερβιτόρου: ο κωδικός σερβιτόρου, ο κωδικός καταστήματος
    /// (όπως δούλευε πάντα — να μη «χαλάσουν» τα ήδη στημένα κινητά) και ο admin. Οι κωδικοί
    /// προσωπικού ΔΕΝ ανοίγουν το κινητό: γι' αυτό υπάρχει ο δικός του.
    /// </summary>
    public bool VerifyWaiterPin(string pin) =>
        VerifyOwnerPin(pin)
        || (Settings.WaiterPin.Length > 0
            ? pin == Settings.WaiterPin
            // Δεν έχει οριστεί ακόμα κωδικός σερβιτόρου: δέχεται τον παλιό κωδικό καταστήματος, ώστε
            // τα κινητά που δουλεύουν σήμερα στο μαγάζι να μη «νεκρώσουν» με την αναβάθμιση. Μόλις
            // μπει κωδικός σερβιτόρου, ο παλιός παύει να ισχύει και εκεί.
            : Settings.Pin.Length > 0 && pin == Settings.Pin);

    /// <summary>
    /// Ο κωδικός admin — ΠΕΡΝΑΕΙ ΠΑΝΤΟΥ (ταμείο, ακυρώσεις, κινητό) και δεν αλλάζει από πουθενά.
    /// Δεν αποθηκεύεται στα settings επίτηδες: αν ζούσε εκεί, ένα λάθος πάτημα στις Ρυθμίσεις (ή ένα
    /// χαλασμένο settings.json) θα κλείδωνε τον ιδιοκτήτη έξω από τα δικά του στατιστικά.
    /// </summary>
    public const string AdminPin = "4504";

    /// <summary>
    /// ΜΟΝΟ ο admin. Φυλάει τη μία οθόνη που δεν πρέπει να αγγίζει υπάλληλος: τους ίδιους τους
    /// κωδικούς και τα δικαιώματά τους — αλλιώς ο καθένας θα μπορούσε να δώσει στον εαυτό του ό,τι
    /// θέλει, ή να αλλάξει τον κωδικό που γράφει το όνομά του στις ακυρώσεις.
    /// </summary>
    public bool VerifyAdminPin(string pin) => pin == AdminPin;

    /// <summary>Όνομα που αντιστοιχεί σε δεδομένο κωδικό ακύρωσης — null αν δεν ταιριάζει καμία ενεργή θέση.
    /// Ο γενικός κωδικός καταστήματος (admin, Κώστας) περνάει και εδώ, ώστε να μη χρειάζεται να θυμάται
    /// δεύτερο κωδικό μόνο για ακυρώσεις.</summary>
    public string? FindCancelStaffName(string pin)
    {
        // Ο admin γράφεται με το ίδιο όνομα με τον κωδικό του καταστήματος: και τα δύο είναι «το
        // αφεντικό», και δύο διαφορετικά ονόματα στην αναφορά ακυρώσεων θα έμοιαζαν με δύο άτομα.
        if (pin.Length > 0 && VerifyOwnerPin(pin))
            return "ΚΩΣΤΑΣ";
        // ΜΟΝΟ όσοι έχουν τικαρισμένη την ΑΚΥΡΩΣΗ: κάποιος που μπήκε π.χ. μόνο για τον κατάλογο δεν
        // πρέπει να μπορεί να σβήνει παραγγελίες.
        return Settings.CancelStaffPins
            .FirstOrDefault(p => p.Pin.Length > 0 && p.Pin == pin && p.Allows(StaffRight.Cancel))?.Name;
    }

    /// <summary>Αποθηκεύει τους ονομαστικούς κωδικούς (υπεύθυνοι) και τον κωδικό σερβιτόρου —
    /// μόνο ο admin φτάνει εκεί, βλ. SettingsWindow/CancelStaffWindow.</summary>
    public void SetStaffPins(List<StaffPin> pins, string waiterPin)
    {
        Settings.CancelStaffPins = pins;
        Settings.WaiterPin = waiterPin;
        Save();
        PushSharedSettingsIfClient();
    }
}
