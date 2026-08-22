using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using PittaPos.Core.Data;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>
/// Το ζωντανό μενού του καταστήματος — επεξεργάσιμο από τη Διαχείριση Καταλόγου.
/// JSON στο %AppData%\PittaPos\menu.json· την πρώτη φορά γεμίζει από το MenuSeed.
/// </summary>
public class MenuStore
{
    public static MenuStore Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;

    public List<MenuCategory> Categories { get; private set; } = [];
    /// <summary>Κοινός κατάλογος έξτρα — επεξεργάσιμος από τη Διαχείριση Καταλόγου (προσθήκη/διαγραφή).
    /// Ξεκινά από το MenuSeed.Extras, μετά ζει εντελώς μέσα στο menu.json.</summary>
    public List<ExtraItem> Extras { get; private set; } = [];

    /// <summary>Επιπλέον χρέωση «διπλή πίτα» ανά κατηγορία (βλ. MenuSeed.SupportsDoublePita) — π.χ.
    /// ΤΥΛΙΧΤΑ και ΚΛΑΣΙΚΑ ΜΙΝΙ έχουν διαφορετική χρέωση. Επεξεργάσιμο από τη Διαχείριση Καταλόγου.</summary>
    public Dictionary<string, decimal> DoublePitaPrices { get; private set; } = [];

    /// <summary>Κοινός κατάλογος βασικών υλικών — ό,τι μπορεί να «βγει» από ένα προϊόν. Κάθε προϊόν
    /// διαλέγει ποια από αυτά έχει (βλ. Product.Ingredients)· ξεκινά από το MenuSeed και μετά ζει
    /// ολόκληρος μέσα στο menu.json, όπως και τα έξτρα.</summary>
    public List<string> Ingredients { get; private set; } = [];

    /// <summary>Μορφή αποθήκευσης στο δίσκο — μαζί κατηγορίες, κοινά έξτρα και χρέωση διπλής πίτας.</summary>
    private sealed class MenuData
    {
        public List<MenuCategory> Categories { get; set; } = [];
        public List<ExtraItem> Extras { get; set; } = [];
        public Dictionary<string, decimal> DoublePitaPrices { get; set; } = [];
        public List<string> Ingredients { get; set; } = [];

        /// <summary>Έχει γίνει το ΜΙΑ ΦΟΡΑ αλφαβητικό στρώσιμο των έξτρα (βλ. SortExtrasAlphabetically);
        /// Μένει μέσα στο menu.json ώστε να μη γίνει ποτέ δεύτερη φορά και σβήσει τη σειρά που έφτιαξε
        /// στο μεταξύ ο ταμίας με σύρσιμο.</summary>
        public bool ExtrasSortedOnce { get; set; }
    }

    /// <summary>Βλ. <see cref="MenuData.ExtrasSortedOnce"/> — κρατιέται εδώ για να ξαναγραφτεί στο αρχείο.</summary>
    private bool _extrasSortedOnce;

    /// <summary>Σηκώνεται σε κάθε αποθήκευση — τα ανοιχτά παράθυρα ξαναχτίζουν το μενού τους.</summary>
    public event Action? Changed;

    private MenuStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "menu.json");
        // Καταγραφή ΔΙΠΛΑ ΣΤΟ EXE, όχι στο %AppData%: αν το πρόβλημα είναι ο ίδιος ο φάκελος
        // δεδομένων (δικαιώματα/ανακατεύθυνση), τότε και η κανονική καταγραφή χάνεται μαζί του και
        // μένουμε χωρίς κανένα ίχνος — ακριβώς ό,τι συνέβαινε.
        Trace($"φάκελος={dir} | IsClient={RemoteSync.IsClient} | NetworkMode={SettingsStore.Instance.Settings.NetworkMode} | Host='{SettingsStore.Instance.Settings.HostAddress}'");

        if (RemoteSync.IsClient)
        {
            Trace("λειτουργία CLIENT — ο κατάλογος ΔΕΝ διαβάζεται τοπικά, τραβιέται από το κύριο ταμείο");
            RemoteSync.StartPolling(TimeSpan.FromSeconds(10), RefreshFromHostAsync);
            return;
        }
        Load();
        MigrateCategoryFlags();
        SortExtrasOnce();
    }

    /// <summary>
    /// Στρώνει ΜΙΑ ΦΟΡΑ όλα τα έξτρα αλφαβητικά — η αλφαβητική σειρά είναι απλώς η ΒΑΣΗ («βάλ' τα
    /// αλφαβητικά και μετά αλλάζω εγώ ό,τι θέλω»), χωρίς να χρειάζεται να πατηθεί τίποτα. Ο δείκτης
    /// γράφεται μέσα στο menu.json, οπότε δεν ξαναγίνεται ποτέ: μια δεύτερη φορά θα έσβηνε τη σειρά
    /// που έχει φτιάξει στο μεταξύ ο ταμίας με σύρσιμο. Δεν υπάρχει κουμπί που να το ξανατρέχει —
    /// επίτηδες, γιατί ακριβώς αυτό θα ήταν ένα κουμπί «σβήσε τη δουλειά μου».
    /// </summary>
    private void SortExtrasOnce()
    {
        if (_extrasSortedOnce)
            return;
        _extrasSortedOnce = true;
        SortExtrasAlphabetically();
    }

    /// <summary>
    /// Γεμίζει μία φορά τις ιδιότητες κατηγορίας (ψωμί / διπλή πίτα / ψωμί-στο-όνομα) για κατηγορίες
    /// που δεν τις έχουν ακόμα, με βάση τους παλιούς κανόνες ονόματος — ώστε ένα υπάρχον κατάστημα να
    /// συνεχίσει να δουλεύει ακριβώς όπως πριν, χωρίς να χρειάζεται να τα ξαναρυθμίσει ο ταμίας.
    /// Από εκεί και πέρα οι τιμές είναι ρητές μέσα στο menu.json, οπότε μια μετονομασία κατηγορίας δεν
    /// τις επηρεάζει πια (αυτό ήταν το πραγματικό σφάλμα: «ΤΥΛΙΧΤΑ» → «ΠΙΤΤΕΣ» και σταμάτησαν σιωπηλά
    /// να δουλεύουν ψωμί και διπλή πίτα σε 18 προϊόντα).
    /// </summary>
    private void MigrateCategoryFlags()
    {
        var changed = false;
        foreach (var c in Categories)
        {
            if (c.HasBread is null)
            {
                c.HasBread = MenuSeed.HasBreadChoice(c.Name);
                changed = true;
            }
            if (c.FuseBreadIntoName is null)
            {
                c.FuseBreadIntoName = MenuSeed.FuseBreadIntoName(c.Name);
                changed = true;
            }
            if (c.SupportsDoublePita is null)
            {
                c.SupportsDoublePita = MenuSeed.SupportsDoublePita(c.Name);
                changed = true;
            }
            if (c.VatKind is null)
            {
                c.VatKind = MenuSeed.GuessVatKind(c.Name);
                changed = true;
            }
            if (c.DoublePitaLarge is null)
            {
                c.DoublePitaLarge = MenuSeed.GuessLargePita(c.Name);
                changed = true;
            }
        }
        if (changed | MigrateDoublePitaPrices())
            SaveToDisk();
    }

    /// <summary>
    /// Μεταφέρει τις χρεώσεις διπλής πίτας από «μία ανά κατηγορία» σε «μία ανά μέγεθος». Παλιά, τρεις
    /// κατηγορίες μεγάλης πίτας σήμαιναν τρεις γραμμές χρέωσης που έπρεπε να μένουν ίδιες με το χέρι —
    /// και μια ξεχασμένη έβγαζε άλλη τιμή στο ίδιο ακριβώς πράγμα.
    /// <para>Η τιμή κάθε μεγέθους παίρνεται από την πρώτη κατηγορία εκείνου του μεγέθους που είχε
    /// χρέωση — δηλαδή ΔΕΝ αλλάζει καμία τιμή που χρεώνεται σήμερα, απλώς παύει να είναι
    /// τριπλογραμμένη.</para>
    /// </summary>
    private bool MigrateDoublePitaPrices()
    {
        if (DoublePitaPrices.ContainsKey(SmallPitaKey) || DoublePitaPrices.ContainsKey(LargePitaKey))
            return false;

        decimal PriceOfFirst(bool large) => Categories
            .Where(c => (c.SupportsDoublePita ?? MenuSeed.SupportsDoublePita(c.Name))
                && (c.DoublePitaLarge ?? MenuSeed.GuessLargePita(c.Name)) == large)
            .Select(c => DoublePitaPrices.GetValueOrDefault(c.Name))
            .FirstOrDefault(p => p > 0);

        var small = PriceOfFirst(large: false);
        var large = PriceOfFirst(large: true);
        DoublePitaPrices.Clear();
        DoublePitaPrices[SmallPitaKey] = small;
        DoublePitaPrices[LargePitaKey] = large;
        AppLog.Write("menu", $"χρέωση διπλής πίτας ανά μέγεθος: μικρή {small:0.00}, μεγάλη {large:0.00}");
        return true;
    }

    /// <summary>Βρίσκει την κατηγορία με αυτό το όνομα — οι υπόλοιπες μέθοδοι δουλεύουν με ετικέτα
    /// (έτσι τις καλεί όλος ο υπόλοιπος κώδικας), αλλά η απάντηση βγαίνει από τις αποθηκευμένες
    /// ιδιότητες της κατηγορίας, όχι από σύγκριση του ονόματος.</summary>
    private MenuCategory? FindCategory(string categoryLabel) =>
        Categories.FirstOrDefault(c => c.Name == categoryLabel);

    /// <summary>Αν η κατηγορία ρωτά ψωμί. Fallback στους παλιούς κανόνες ονόματος μόνο αν δεν βρεθεί
    /// καθόλου η κατηγορία (π.χ. παραγγελία από το κινητό με κατηγορία που μόλις διαγράφηκε).</summary>
    public bool HasBreadChoice(string categoryLabel) =>
        FindCategory(categoryLabel)?.HasBread ?? MenuSeed.HasBreadChoice(categoryLabel);

    /// <summary>Αν το ψωμί χώνεται μέσα στο όνομα του προϊόντος.</summary>
    public bool FuseBreadIntoName(string categoryLabel) =>
        FindCategory(categoryLabel)?.FuseBreadIntoName ?? MenuSeed.FuseBreadIntoName(categoryLabel);

    /// <summary>Αν η κατηγορία προσφέρει «διπλή πίτα».</summary>
    public bool SupportsDoublePita(string categoryLabel) =>
        FindCategory(categoryLabel)?.SupportsDoublePita ?? MenuSeed.SupportsDoublePita(categoryLabel);

    /// <summary>Ολόκληρο το μενού για συγχρονισμό — κατηγορίες, κοινά έξτρα, χρεώσεις διπλής πίτας ΚΑΙ
    /// ο κοινός κατάλογος βασικών υλικών.</summary>
    public MenuSyncDto BuildSyncDto() => new(Categories, Extras, DoublePitaPrices, Ingredients);

    /// <summary>Εφαρμόζει μενού που ήρθε από το δίκτυο (host που δέχεται από client, ή client που
    /// τραβάει από host). Κενές λίστες αγνοούνται αντί να σβήσουν ό,τι υπάρχει — ένα αίτημα από
    /// παλιότερη έκδοση, που δεν στέλνει έξτρα/υλικά, δεν πρέπει να μηδενίσει τον κατάλογο.</summary>
    public void ApplySyncDto(MenuSyncDto dto)
    {
        // ΚΑΙ οι κατηγορίες με τον ίδιο κανόνα — ήταν η μόνη λίστα χωρίς προστασία, και η πιο ακριβή:
        // ένα άδειο μενού από το δίκτυο έγραφε «καμία κατηγορία» πάνω στον πραγματικό κατάλογο και
        // κατέβαινε στον δίσκο (ReplaceAll -> Save). Ολόκληρος ο κατάλογος του μαγαζιού, χαμένος.
        // Το δεύτερο ταμείο ξεκινά ΠΑΝΤΑ με άδειο μενού μέχρι να απαντήσει το κύριο (βλ. constructor),
        // οπότε αρκούσε να είναι κλειστό/απρόσιτο το κύριο τη στιγμή που κάποιος άγγιζε τη Διαχείριση
        // Καταλόγου στο δεύτερο.
        if (dto.Categories.Count > 0)
            Categories = dto.Categories;
        if (dto.Extras.Count > 0)
            Extras = dto.Extras;
        if (dto.DoublePitaPrices.Count > 0)
            DoublePitaPrices = dto.DoublePitaPrices;
        if (dto.Ingredients is { Count: > 0 })
            Ingredients = dto.Ingredients;
    }

    /// <summary>Δεύτερο ταμείο (client) — αντικαθιστά το τοπικό μενού με αυτό του host, ΜΑΖΙ με τα
    /// κοινά έξτρα και τις χρεώσεις διπλής πίτας (πριν έρχονταν μόνο οι κατηγορίες, οπότε τα έξτρα
    /// στο δεύτερο ταμείο έμεναν στις αρχικές τιμές του seed — λάθος τιμές στις παραγγελίες).</summary>
    private async Task RefreshFromHostAsync()
    {
        var dto = await RemoteSync.GetAsync<MenuSyncDto>("/api/sync/menu");
        if (dto is null)
            return;

        var before = JsonSerializer.Serialize(BuildSyncDto(), JsonOpts);
        ApplySyncDto(dto);
        if (Extras.Count == 0)
            Extras = SeedExtrasCopy();
        if (Ingredients.Count == 0)
            Ingredients = SeedIngredientsCopy();

        // Το Changed σηκώνεται ΜΟΝΟ αν άλλαξε πραγματικά ο κατάλογος.
        //
        // Πριν σηκωνόταν σε κάθε poll, δηλαδή κάθε 10 δευτερόλεπτα: η οθόνη προϊόντων ξανάχτιζε
        // κατηγορίες και πλακίδια, και ό,τι είχε ανοιχτό ο ταμίας έκλεινε μόνο του. Στην πράξη, στο
        // δεύτερο ταμείο το προϊόν «έβγαινε» μέσα από τα χέρια σου ενώ διάλεγες έξτρα. Ο κατάλογος
        // αλλάζει ελάχιστες φορές τον χρόνο — δεν υπάρχει λόγος να ξαναχτίζεται η οθόνη χωρίς αιτία.
        if (JsonSerializer.Serialize(BuildSyncDto(), JsonOpts) == before)
            return;
        Changed?.Invoke();
    }

    /// <summary>Το αρχείο υπήρχε αλλά δεν διαβάστηκε — τότε ΔΕΝ επιτρέπεται καμία εγγραφή, γιατί θα
    /// έγραφε τον εργοστασιακό κατάλογο πάνω στον πραγματικό.</summary>
    public bool LoadFailed { get; private set; }

    /// <summary>Γράφει δίπλα στο exe, σε διαδρομή που δεν εξαρτάται από τον φάκελο δεδομένων.</summary>
    private static void Trace(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "menu-diagnostic.txt"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Η διάγνωση δεν πρέπει ποτέ να ρίξει το ταμείο
        }
    }

    /// <summary>
    /// Διαβάζει τον κατάλογο από τον δίσκο.
    ///
    /// Το διάβασμα γίνεται με επαναλήψεις: το αρχείο μπορεί να είναι κλειδωμένο για κλάσματα του
    /// δευτερολέπτου (antivirus που το σαρώνει, συγχρονισμός cloud, προηγούμενο instance που κλείνει).
    /// Πριν, μια τέτοια στιγμιαία αποτυχία γύριζε ΣΙΩΠΗΛΑ ολόκληρο το ταμείο στον εργοστασιακό
    /// κατάλογο — και προσπαθούσε κιόλας να τον γράψει πάνω στον πραγματικό. Αν τύχαινε η εγγραφή να
    /// πετύχει, ο κατάλογος του μαγαζιού θα χανόταν οριστικά, χωρίς κανένα μήνυμα.
    /// </summary>
    private void Load()
    {
        // ΟΧΙ File.Exists εδώ: επιστρέφει «δεν υπάρχει» και όταν το αρχείο υπάρχει μεν, αλλά δεν είναι
        // προσπελάσιμο (κλειδωμένο, δικαιώματα, ανακατεύθυνση φακέλου). Το ταμείο τότε νόμιζε ότι είναι
        // πρώτη εγκατάσταση, έστηνε εργοστασιακό κατάλογο ΚΑΙ τον έγραφε πάνω στον πραγματικό — σιωπηλά,
        // χωρίς ούτε γραμμή στην καταγραφή. Εδώ ανοίγουμε ρητά το αρχείο ώστε «λείπει» και «δεν το φτάνω»
        // να είναι δύο ΞΕΧΩΡΙΣΤΕΣ περιπτώσεις.
        string? text = null;
        var missing = false;
        for (var attempt = 1; attempt <= 5 && text is null; attempt++)
        {
            try
            {
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                text = reader.ReadToEnd();
            }
            catch (FileNotFoundException)
            {
                missing = true;
                break;
            }
            catch (DirectoryNotFoundException)
            {
                missing = true;
                break;
            }
            catch (Exception ex)
            {
                Trace($"το menu.json δεν διαβάστηκε (προσπάθεια {attempt}/5): {ex.GetType().Name}: {ex.Message}");
                Thread.Sleep(150);
            }
        }

        if (missing)
        {
            // Πρώτη εκκίνηση: εδώ και μόνο εδώ είναι σωστό να γραφτεί ο εργοστασιακός κατάλογος.
            Trace($"δεν υπάρχει κατάλογος στο {_path} — στήνεται ο εργοστασιακός (πρώτη εκκίνηση)");
            Categories = SeedCopy();
            Extras = SeedExtrasCopy();
            DoublePitaPrices = SeedDoublePitaPrices();
            Ingredients = SeedIngredientsCopy();
            SaveToDisk();
            return;
        }

        if (text is not null)
        {
            try
            {
                // Τρέχουσα μορφή: αντικείμενο με Categories + Extras
                MenuData? data = null;
                try { data = JsonSerializer.Deserialize<MenuData>(text); }
                catch (JsonException) { /* παλιό αρχείο, δοκίμασε παρακάτω σαν απλή λίστα */ }

                if (data is not null && data.Categories.Count > 0)
                {
                    Categories = data.Categories;
                    Extras = data.Extras.Count > 0 ? data.Extras : SeedExtrasCopy();
                    DoublePitaPrices = data.DoublePitaPrices.Count > 0 ? data.DoublePitaPrices : SeedDoublePitaPrices();
                    Ingredients = data.Ingredients.Count > 0 ? data.Ingredients : SeedIngredientsCopy();
                    _extrasSortedOnce = data.ExtrasSortedOnce;
                    // Καταγράφεται και η επιτυχία: χωρίς αυτό, μια αναφορά «βλέπω λάθος κατάλογο» δεν
                    // ξεχωρίζει από «δεν άνοιξε καν η εφαρμογή» — δεν υπάρχει τίποτα στο αρχείο.
                    Trace($"φορτώθηκε: {Categories.Count} κατηγορίες, {Extras.Count} έξτρα " +
                        $"(πρώτη: {Categories[0].Name})");
                    return;
                }

                // Παλιά μορφή: το αρχείο ήταν απλά μια λίστα κατηγοριών, χωρίς έξτρα — αναβάθμισέ το
                var oldCategories = JsonSerializer.Deserialize<List<MenuCategory>>(text);
                if (oldCategories is { Count: > 0 })
                {
                    Categories = oldCategories;
                    Extras = SeedExtrasCopy();
                    DoublePitaPrices = SeedDoublePitaPrices();
                    Ingredients = SeedIngredientsCopy();
                    SaveToDisk();
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("menu", $"χαλασμένο menu.json: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Υπάρχει αρχείο αλλά δεν βγάλαμε άκρη. Δείχνουμε τον εργοστασιακό για να δουλέψει το ταμείο,
        // ΧΩΡΙΣ όμως να τον γράψουμε πουθενά — ο πραγματικός κατάλογος μένει ανέπαφος στον δίσκο.
        LoadFailed = true;
        Trace("ΠΡΟΣΟΧΗ: ο κατάλογος δεν φορτώθηκε· εμφανίζεται ο εργοστασιακός και οι αλλαγές δεν αποθηκεύονται");
        Categories = SeedCopy();
        Extras = SeedExtrasCopy();
        DoublePitaPrices = SeedDoublePitaPrices();
        Ingredients = SeedIngredientsCopy();
        StartRecoveryRetries();
    }

    /// <summary>
    /// Ξαναδοκιμάζει το φόρτωμα στο παρασκήνιο, κάθε 3 δευτερόλεπτα για ένα λεπτό. Ό,τι κρατούσε το
    /// αρχείο κλειδωμένο (σάρωση antivirus, συγχρονισμός) τελειώνει σε δευτερόλεπτα — δεν έχει νόημα
    /// να μείνει το ταμείο με λάθος κατάλογο μέχρι να το κλείσει και να το ξανανοίξει κάποιος.
    /// Μόλις πετύχει, οι οθόνες ανανεώνονται μόνες τους μέσω του Changed.
    /// </summary>
    private void StartRecoveryRetries()
    {
        var attempts = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            attempts++;
            if (attempts > 20)
            {
                timer.Stop();
                return;
            }

            string text;
            try { text = File.ReadAllText(_path); }
            catch (Exception) { return; } // ακόμη κλειδωμένο — ξαναδοκιμάζουμε στον επόμενο χτύπο

            MenuData? data;
            try { data = JsonSerializer.Deserialize<MenuData>(text); }
            catch (JsonException) { return; }
            if (data is null || data.Categories.Count == 0)
                return;

            timer.Stop();
            Categories = data.Categories;
            Extras = data.Extras.Count > 0 ? data.Extras : SeedExtrasCopy();
            DoublePitaPrices = data.DoublePitaPrices.Count > 0 ? data.DoublePitaPrices : SeedDoublePitaPrices();
            Ingredients = data.Ingredients.Count > 0 ? data.Ingredients : SeedIngredientsCopy();
            _extrasSortedOnce = data.ExtrasSortedOnce;
            LoadFailed = false;
            AppLog.Write("menu", $"ο κατάλογος ανακτήθηκε με την {attempts}η προσπάθεια: {Categories.Count} κατηγορίες");
            Changed?.Invoke();
        };
        timer.Start();
    }

    /// <summary>
    /// Βαθύ αντίγραφο του αρχικού μενού ώστε οι αλλαγές να μην αγγίζουν το seed.
    ///
    /// Αντιγράφονται ΟΛΑ τα πεδία. Πριν έμεναν έξω τα βασικά υλικά του προϊόντος, το όνομα εκτύπωσης,
    /// τα επιτρεπτά έξτρα και οι τρεις ιδιότητες της κατηγορίας — δηλαδή όσο σωστά κι αν ήταν γραμμένα
    /// στον MenuSeed, κάθε ΝΕΑ εγκατάσταση ξεκινούσε με όλα τα έξτρα σε όλα τα προϊόντα (κόκα-κόλα με
    /// μπέικον) και χωρίς υλικά ανά προϊόν. Οι ιδιότητες κατηγορίας γέμιζαν μετά από το
    /// <see cref="MigrateCategoryFlags"/> μαντεύοντάς τες από το ΟΝΟΜΑ, που έτυχε να συμφωνεί — μέχρι
    /// την πρώτη μετονομασία κατηγορίας μέσα στον βασικό κατάλογο.
    /// </summary>
    private static List<MenuCategory> SeedCopy() =>
        MenuSeed.Categories.Select(c => new MenuCategory
        {
            Id = c.Id,
            Name = c.Name,
            HasBread = c.HasBread,
            FuseBreadIntoName = c.FuseBreadIntoName,
            SupportsDoublePita = c.SupportsDoublePita,
            Products = c.Products.Select(p => new Product
            {
                Id = p.Id, Name = p.Name, NameEn = p.NameEn, PrintName = p.PrintName,
                Description = p.Description, Price = p.Price, DeliveryPrice = p.DeliveryPrice,
                Customizable = p.Customizable,
                // ΝΕΕΣ λίστες, όχι οι ίδιες αναφορές: ο MenuSeed είναι στατικός και ζει όσο η εφαρμογή,
                // οπότε μια διαγραφή έξτρα/υλικού από τη Διαχείριση θα τον πείραζε μόνιμα.
                ExtraNames = p.ExtraNames is null ? null : [.. p.ExtraNames],
                Ingredients = p.Ingredients is null ? null : [.. p.Ingredients],
            }).ToList(),
        }).ToList();

    private static List<string> SeedIngredientsCopy() => [.. MenuSeed.IncludedIngredients];

    /// <summary>Τα υλικά που ισχύουν για ένα προϊόν: τα δικά του αν έχει δηλώσει, αλλιώς ο κοινός
    /// κατάλογος — έτσι όσα προϊόντα δεν ρυθμίστηκαν ποτέ δουλεύουν ακριβώς όπως πριν.
    /// Άδεια λίστα σημαίνει «κανένα υλικό» και το σέβεται: πριν έπεφτε κι αυτή στον κοινό κατάλογο,
    /// οπότε ένα προϊόν που ξετσεκάριζες όλα του τα υλικά τα ξανάβγαζε όλα.</summary>
    public IReadOnlyList<string> IngredientsFor(Product product) =>
        product.Ingredients ?? Ingredients;

    /// <summary>
    /// Ανοίγει το προϊόν υλικά όταν το πατήσεις; <b>Ναι αν έχει γραμμένα δικά του βασικά υλικά</b> —
    /// ακόμα κι αν δεν είναι τσεκαρισμένο το «τροποποιήσιμο» στη Διαχείριση.
    ///
    /// <para>Ο ταμίας το είπε καθαρά: «στον κατάλογο τα βγάζει σωστά, μετά δεν μου τα δείχνει». Ήταν
    /// δύο διαφορετικοί διακόπτες για το ίδιο πράγμα — οι ΣΑΛΑΤΕΣ είχαν όλα τους τα υλικά γραμμένα,
    /// σωστά και ξεχωριστά για την καθεμία, αλλά κανένα δεν άνοιγε ποτέ επειδή ήταν σβηστό ένα κουτάκι
    /// που δεν φαίνεται πουθενά στην παραγγελιοληψία.</para>
    ///
    /// <para>Δεν πιάνει τα ποτά: υλικά έχουν ΜΟΝΟ όσα φαγητά τους έχουν γραμμένα ρητά (τα αναψυκτικά,
    /// οι μπύρες, τα κρασιά και οι σκέτες μερίδες έχουν κενή λίστα, ελεγμένο πάνω στον πραγματικό
    /// κατάλογο του μαγαζιού). Το «τροποποιήσιμο» παραμένει για ό,τι θέλει ψωμί/έξτρα χωρίς να έχει
    /// βασικά υλικά.</para>
    /// </summary>
    public static bool OpensIngredients(Product product) =>
        product.Customizable || product.Ingredients is { Count: > 0 };

    private static List<ExtraItem> SeedExtrasCopy() =>
        MenuSeed.Extras.Select(e => new ExtraItem { Name = e.Name, Price = e.Price }).ToList();

    /// <summary>Οι χρεώσεις διπλής πίτας του καταλόγου-βάσης. Τα κλειδιά είναι τα ονόματα κατηγοριών
    /// του MenuSeed, οπότε ταιριάζουν εξ ορισμού σε μια καθαρή εγκατάσταση. Σε υπάρχον κατάστημα που
    /// έχει μετονομάσει κατηγορία, το κλειδί απλώς δεν βρίσκεται και η χρέωση μένει 0 μέχρι να την
    /// ορίσει ο χρήστης — ίδια συμπεριφορά με πριν, καμία λάθος χρέωση.</summary>
    private static Dictionary<string, decimal> SeedDoublePitaPrices() =>
        new(MenuSeed.DoublePitaPrices);

    /// <summary>Τα ΔΥΟ μοναδικά κλειδιά χρέωσης διπλής πίτας. Το μαγαζί έχει δύο πίτες — μικρή και
    /// μεγάλη — όχι μία ανά κατηγορία (βλ. <see cref="MenuCategory.DoublePitaLarge"/>).</summary>
    public const string SmallPitaKey = "ΜΙΚΡΗ";
    public const string LargePitaKey = "ΜΕΓΑΛΗ";

    /// <summary>Είναι μεγάλη η πίτα αυτής της κατηγορίας; Fallback στο όνομα μόνο για κατηγορία που
    /// δεν βρέθηκε καθόλου (π.χ. παραγγελία από κινητό με κατηγορία που μόλις διαγράφηκε).</summary>
    public bool IsLargePita(string categoryLabel) =>
        FindCategory(categoryLabel)?.DoublePitaLarge ?? MenuSeed.GuessLargePita(categoryLabel);

    /// <summary>Το κλειδί χρέωσης της κατηγορίας — μικρή ή μεγάλη.</summary>
    public string PitaSizeKeyFor(string categoryLabel) =>
        IsLargePita(categoryLabel) ? LargePitaKey : SmallPitaKey;

    /// <summary>Χρέωση διπλής πίτας για μια κατηγορία (0 αν δεν έχει ρυθμιστεί ή δεν υποστηρίζεται).
    /// Η υπογραφή μένει «ανά κατηγορία» επίτηδες: έτσι ούτε ο customizer ούτε το κινητό χρειάστηκε να
    /// αλλάξουν — μόνο το τι κρύβεται από πίσω.</summary>
    public decimal DoublePitaPriceFor(string categoryLabel) =>
        SupportsDoublePita(categoryLabel) ? DoublePitaPrices.GetValueOrDefault(PitaSizeKeyFor(categoryLabel)) : 0m;

    /// <summary>Αποθήκευση + ειδοποίηση όλων των οθονών.</summary>
    public void Save()
    {
        if (RemoteSync.IsClient)
        {
            // Δεύτερη δικλείδα, στην πηγή: το δεύτερο ταμείο δεν στέλνει ΠΟΤΕ κατάλογο που δεν έχει.
            // Μέχρι να απαντήσει το κύριο, οι κατηγορίες εδώ είναι άδειες — μια αλλαγή στη Διαχείριση
            // Καταλόγου εκείνη τη στιγμή θα έστελνε το τίποτα και θα έσβηνε τον πραγματικό κατάλογο.
            if (Categories.Count == 0)
            {
                AppLog.Write("menu", "η αποστολή καταλόγου αγνοήθηκε: δεν έχει έρθει ακόμα ο κατάλογος από το κύριο ταμείο");
                return;
            }
            _ = SyncThenRefreshAsync();
            return;
        }
        SaveToDisk();
        Changed?.Invoke();
    }

    /// <summary>Δεύτερο ταμείο (client) — στέλνει ολόκληρο το επεξεργασμένο μενού στο host (κατηγορίες,
    /// έξτρα, χρεώσεις διπλής πίτας), μετά ξαναδιαβάζει. Πριν έστελνε μόνο τις κατηγορίες, οπότε κάθε
    /// αλλαγή σε έξτρα ή χρέωση διπλής πίτας από το δεύτερο ταμείο χανόταν σιωπηλά.</summary>
    private async Task SyncThenRefreshAsync()
    {
        await RemoteSync.PostAsync("/api/sync/menu", BuildSyncDto());
        await RefreshFromHostAsync();
    }

    private void SaveToDisk()
    {
        // Δικλείδα: αν το φόρτωμα απέτυχε, στη μνήμη κάθεται ο εργοστασιακός κατάλογος. Οποιαδήποτε
        // εγγραφή εδώ θα τον έγραφε πάνω στον πραγματικό — ακριβώς η καταστροφή που αποφεύγουμε.
        if (LoadFailed)
        {
            AppLog.Write("menu", "η αποθήκευση αγνοήθηκε: ο κατάλογος δεν είχε φορτωθεί σωστά");
            return;
        }

        try
        {
            var data = new MenuData
            {
                Categories = Categories,
                Extras = Extras,
                DoublePitaPrices = DoublePitaPrices,
                Ingredients = Ingredients,
                ExtrasSortedOnce = _extrasSortedOnce,
            };
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(data, JsonOpts));
        }
        catch (Exception ex)
        {
            // Δεν μπλοκάρει το ταμείο — αλλά ΓΡΑΦΕΤΑΙ. Μια αποτυχία εγγραφής (γεμάτος δίσκος,
            // κλείδωμα από antivirus, χαλασμένος δίσκος) σήμαινε ότι τα δεδομένα ζούσαν πια μόνο
            // στη μνήμη και θα χάνονταν στο επόμενο κλείσιμο — χωρίς κανένα ίχνος πουθενά.
            AppLog.Write("save", $"Δεν γράφτηκε το «{Path.GetFileName(_path)}»: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static string NewId() => "u" + DateTime.UtcNow.Ticks.ToString("x");

    /// <summary>Χρησιμοποιείται από το endpoint συγχρονισμού (host) όταν το δεύτερο ταμείο στέλνει
    /// ολόκληρο το επεξεργασμένο μενού — κατηγορίες, έξτρα και χρεώσεις διπλής πίτας μαζί.</summary>
    public void ReplaceAll(MenuSyncDto dto)
    {
        ApplySyncDto(dto);
        Save();
    }

    // Δεν υπάρχουν πια AddIngredient/RemoveIngredient εδώ, ΕΠΙΤΗΔΕΣ: τα βασικά υλικά είναι ξεχωριστά
    // για κάθε προϊόν και γράφονται μόνο στο Product.Ingredients (MenuManagerViewModel). Οι παλιές
    // μέθοδοι πείραζαν τον κοινό κατάλογο ΚΑΙ όλα τα προϊόντα μαζί — μια διαγραφή υλικού σε ένα
    // προϊόν το έσβηνε από όλα. Το MenuStore.Ingredients μένει μόνο ως λίστα έτοιμων επιλογών
    // (και ως προεπιλογή για προϊόν που δεν ρυθμίστηκε ποτέ, βλ. IngredientsFor).

    /// <summary>Προσθήκη νέου έξτρα στον κοινό κατάλογο (Διαχείριση Καταλόγου). Τα έξτρα ΟΝΤΩΣ είναι
    /// κοινός κατάλογος (έχουν τιμή), αλλά προϊόν με null ExtraNames σημαίνει «όλα του καταλόγου»,
    /// οπότε ένα σκέτο Add θα εμφάνιζε το νέο έξτρα ταυτόχρονα σε ΟΛΑ τα προϊόντα. Κάθε τέτοιο προϊόν
    /// κλειδώνει πρώτα ρητά τα έξτρα που έχει τώρα, ώστε το νέο να μπαίνει μόνο όπου το τσεκάρεις.</summary>
    public void AddExtra(string name, decimal price)
    {
        foreach (var product in Categories.SelectMany(c => c.Products))
            product.ExtraNames ??= [.. Extras.Select(e => e.Name)];
        Extras.Add(new ExtraItem { Name = name, Price = price });
        Save();
    }

    /// <summary>
    /// Βάζει το έξτρα <paramref name="draggedName"/> στη θέση του <paramref name="targetName"/>, όπως
    /// το σύρσιμο στη λίστα προϊόντων. Η σειρά της λίστας είναι και η σειρά που βλέπει ο ταμίας στον
    /// customizer, οπότε το μαγαζί θέλει τα πολυχρησιμοποιημένα πρώτα — με 21 έξτρα, το να τα
    /// μετακινείς ένα-ένα με βελάκια ήταν ανεφάρμοστο.
    /// </summary>
    public void MoveExtraTo(string draggedName, string targetName)
    {
        var from = Extras.FindIndex(e => e.Name == draggedName);
        var to = Extras.FindIndex(e => e.Name == targetName);
        if (from < 0 || to < 0 || from == to)
            return;

        var item = Extras[from];
        Extras.RemoveAt(from);
        Extras.Insert(to, item);
        Save();
    }

    /// <summary>
    /// Βάζει ΟΛΑ τα έξτρα σε αλφαβητική σειρά — και τον κοινό κατάλογο και τη σειρά μέσα σε κάθε
    /// προϊόν που έχει δικά του (<see cref="Product.ExtraNames"/>), αλλιώς η σειρά θα άλλαζε μόνο
    /// στα μισά προϊόντα. Σημείο εκκίνησης για να τα ξαναδιατάξει μετά ο ταμίας με σύρσιμο.
    /// <para>Ελληνικό αλφάβητο ρητά (el-GR): με άλλη γλώσσα συστήματος τα τονούμενα και το «ς»
    /// έμπαιναν σε λάθος θέση.</para>
    /// </summary>
    public void SortExtrasAlphabetically()
    {
        var alphabet = StringComparer.Create(CultureInfo.GetCultureInfo("el-GR"), ignoreCase: true);
        Extras.Sort((a, b) => alphabet.Compare(a.Name, b.Name));
        foreach (var product in Categories.SelectMany(c => c.Products))
            product.ExtraNames?.Sort(alphabet);
        Save();
    }

    /// <summary>
    /// Μετονομασία έξτρα. Το όνομα είναι και το ΚΛΕΙΔΙ με το οποίο κάθε προϊόν δηλώνει ποια έξτρα
    /// δέχεται (<see cref="Product.ExtraNames"/>), οπότε αλλάζει και εκεί — αλλιώς το έξτρα θα
    /// εξαφανιζόταν σιωπηλά από όσα προϊόντα το είχαν ρητά επιλεγμένο.
    /// <para>Οι ΠΑΛΙΕΣ παραγγελίες δεν πειράζονται επίτηδες: το ιστορικό πρέπει να δείχνει τι
    /// γράφτηκε τότε, όχι πώς λέγεται σήμερα το υλικό.</para>
    /// </summary>
    public void RenameExtra(string oldName, string newName)
    {
        var extra = Extras.FirstOrDefault(e => e.Name == oldName);
        if (extra is null || oldName == newName)
            return;

        // Το ExtraItem κρατά το όνομα ως init-only (είναι κλειδί): μπαίνει νέο στη ΘΕΣΗ του παλιού,
        // ώστε να μη χαλάσει η σειρά που έχει φτιάξει ο ταμίας με σύρσιμο.
        Extras[Extras.IndexOf(extra)] = new ExtraItem { Name = newName, Price = extra.Price };
        foreach (var product in Categories.SelectMany(c => c.Products))
        {
            if (product.ExtraNames is not { } names)
                continue;
            for (var i = 0; i < names.Count; i++)
                if (names[i] == oldName)
                    names[i] = newName;
        }
        Save();
    }

    /// <summary>Αλλαγή τιμής υπάρχοντος έξτρα — δεν αγγίζει ποια προϊόντα το επιτρέπουν.</summary>
    public void UpdateExtraPrice(string name, decimal price)
    {
        var extra = Extras.FirstOrDefault(e => e.Name == name);
        if (extra is null)
            return;
        extra.Price = price;
        Save();
    }

    /// <summary>Αλλαγή χρέωσης διπλής πίτας για ένα ΜΕΓΕΘΟΣ (<see cref="SmallPitaKey"/>/<see cref="LargePitaKey"/>) —
    /// πιάνει μονομιάς όλες τις κατηγορίες εκείνου του μεγέθους.</summary>
    public void UpdateDoublePitaPrice(string sizeKey, decimal price)
    {
        DoublePitaPrices[sizeKey] = price;
        Save();
    }

    /// <summary>
    /// Μετονομασία κατηγορίας. Η χρέωση διπλής πίτας ΔΕΝ χρειάζεται πια μεταφορά: κλειδώνει στο
    /// μέγεθος (ΜΙΚΡΗ/ΜΕΓΑΛΗ) και όχι στο όνομα της κατηγορίας — παλιότερα μια μετονομασία άφηνε τη
    /// χρέωση ορφανή στο παλιό όνομα και η διπλή πίτα άρχιζε σιωπηλά να χρεώνεται 0€.
    /// </summary>
    public void RenameCategory(MenuCategory category, string newName)
    {
        category.Name = newName;
        Save();
    }

    /// <summary>Διαγραφή έξτρα από τον κοινό κατάλογο — βγαίνει και από τη λίστα επιτρεπτών κάθε
    /// προϊόντος που το είχε ρητά επιλεγμένο, ώστε να μη μείνει «ορφανό» όνομα σε παλιά προϊόντα.</summary>
    public void RemoveExtra(string name)
    {
        Extras.RemoveAll(e => e.Name == name);
        foreach (var product in Categories.SelectMany(c => c.Products))
            product.ExtraNames?.Remove(name);
        Save();
    }
}
