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

    /// <summary>Μορφή αποθήκευσης στο δίσκο — μαζί κατηγορίες, κοινά έξτρα και χρέωση διπλής πίτας.</summary>
    private sealed class MenuData
    {
        public List<MenuCategory> Categories { get; set; } = [];
        public List<ExtraItem> Extras { get; set; } = [];
        public Dictionary<string, decimal> DoublePitaPrices { get; set; } = [];
    }

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
        }
        if (changed)
            SaveToDisk();
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

    /// <summary>Ολόκληρο το μενού για συγχρονισμό — κατηγορίες, κοινά έξτρα ΚΑΙ χρεώσεις διπλής πίτας.</summary>
    public MenuSyncDto BuildSyncDto() => new(Categories, Extras, DoublePitaPrices);

    /// <summary>Εφαρμόζει μενού που ήρθε από το δίκτυο (host που δέχεται από client, ή client που
    /// τραβάει από host). Κενές λίστες αγνοούνται αντί να σβήσουν ό,τι υπάρχει — ένα αίτημα από
    /// παλιότερη έκδοση, που δεν στέλνει έξτρα, δεν πρέπει να μηδενίσει τον κατάλογο έξτρα.</summary>
    public void ApplySyncDto(MenuSyncDto dto)
    {
        Categories = dto.Categories;
        if (dto.Extras.Count > 0)
            Extras = dto.Extras;
        if (dto.DoublePitaPrices.Count > 0)
            DoublePitaPrices = dto.DoublePitaPrices;
    }

    /// <summary>Δεύτερο ταμείο (client) — αντικαθιστά το τοπικό μενού με αυτό του host, ΜΑΖΙ με τα
    /// κοινά έξτρα και τις χρεώσεις διπλής πίτας (πριν έρχονταν μόνο οι κατηγορίες, οπότε τα έξτρα
    /// στο δεύτερο ταμείο έμεναν στις αρχικές τιμές του seed — λάθος τιμές στις παραγγελίες).</summary>
    private async Task RefreshFromHostAsync()
    {
        var dto = await RemoteSync.GetAsync<MenuSyncDto>("/api/sync/menu");
        if (dto is null)
            return;
        ApplySyncDto(dto);
        if (Extras.Count == 0)
            Extras = SeedExtrasCopy();
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
            LoadFailed = false;
            AppLog.Write("menu", $"ο κατάλογος ανακτήθηκε με την {attempts}η προσπάθεια: {Categories.Count} κατηγορίες");
            Changed?.Invoke();
        };
        timer.Start();
    }

    /// <summary>Βαθύ αντίγραφο του αρχικού μενού ώστε οι αλλαγές να μην αγγίζουν το seed.</summary>
    private static List<MenuCategory> SeedCopy() =>
        MenuSeed.Categories.Select(c => new MenuCategory
        {
            Id = c.Id,
            Name = c.Name,
            Products = c.Products.Select(p => new Product
            {
                Id = p.Id, Name = p.Name, NameEn = p.NameEn,
                Description = p.Description, Price = p.Price, DeliveryPrice = p.DeliveryPrice,
                Customizable = p.Customizable,
            }).ToList(),
        }).ToList();

    private static List<ExtraItem> SeedExtrasCopy() =>
        MenuSeed.Extras.Select(e => new ExtraItem { Name = e.Name, Price = e.Price }).ToList();

    /// <summary>Οι χρεώσεις διπλής πίτας του καταλόγου-βάσης. Τα κλειδιά είναι τα ονόματα κατηγοριών
    /// του MenuSeed, οπότε ταιριάζουν εξ ορισμού σε μια καθαρή εγκατάσταση. Σε υπάρχον κατάστημα που
    /// έχει μετονομάσει κατηγορία, το κλειδί απλώς δεν βρίσκεται και η χρέωση μένει 0 μέχρι να την
    /// ορίσει ο χρήστης — ίδια συμπεριφορά με πριν, καμία λάθος χρέωση.</summary>
    private static Dictionary<string, decimal> SeedDoublePitaPrices() =>
        new(MenuSeed.DoublePitaPrices);

    /// <summary>Χρέωση διπλής πίτας για μια κατηγορία (0 αν δεν έχει ρυθμιστεί ή δεν υποστηρίζεται).</summary>
    public decimal DoublePitaPriceFor(string categoryLabel) => DoublePitaPrices.GetValueOrDefault(categoryLabel);

    /// <summary>Αποθήκευση + ειδοποίηση όλων των οθονών.</summary>
    public void Save()
    {
        if (RemoteSync.IsClient)
        {
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
            var data = new MenuData { Categories = Categories, Extras = Extras, DoublePitaPrices = DoublePitaPrices };
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(data, JsonOpts));
        }
        catch (Exception)
        {
            // Αποτυχία εγγραφής δεν πρέπει να μπλοκάρει το ταμείο
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

    /// <summary>Προσθήκη νέου έξτρα στον κοινό κατάλογο (Διαχείριση Καταλόγου).</summary>
    public void AddExtra(string name, decimal price)
    {
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

    /// <summary>Αλλαγή τιμής υπάρχοντος έξτρα — δεν αγγίζει ποια προϊόντα το επιτρέπουν.</summary>
    public void UpdateExtraPrice(string name, decimal price)
    {
        var extra = Extras.FirstOrDefault(e => e.Name == name);
        if (extra is null)
            return;
        extra.Price = price;
        Save();
    }

    /// <summary>Αλλαγή χρέωσης διπλής πίτας για μία κατηγορία.</summary>
    public void UpdateDoublePitaPrice(string categoryLabel, decimal price)
    {
        DoublePitaPrices[categoryLabel] = price;
        Save();
    }

    /// <summary>
    /// Μετονομασία κατηγορίας — μεταφέρει ΜΑΖΙ και τη χρέωση διπλής πίτας, που είναι αποθηκευμένη με
    /// κλειδί το όνομα. Χωρίς αυτό, μια απλή μετονομασία άφηνε τη χρέωση «ορφανή» στο παλιό όνομα και
    /// η διπλή πίτα άρχιζε σιωπηλά να χρεώνεται 0€ — χωρίς κανένα μήνυμα ούτε ορατή αλλαγή.
    /// </summary>
    public void RenameCategory(MenuCategory category, string newName)
    {
        var oldName = category.Name;
        category.Name = newName;
        if (oldName != newName && DoublePitaPrices.Remove(oldName, out var price))
            DoublePitaPrices[newName] = price;
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
