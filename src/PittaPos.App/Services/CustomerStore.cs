using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>
/// Αποθηκευμένοι πελάτες διανομής. Κάθε πελάτης που περνά μία φορά μένει,
/// ώστε την επόμενη να βρίσκεται αυτόματα με το κινητό, τη διεύθυνση ή το όνομα.
/// JSON στο %AppData%\PittaPos — μέχρι να μπει η SQLite βάση.
/// </summary>
public class CustomerStore
{
    public static CustomerStore Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private List<Customer> _customers = [];

    /// <summary>Σηκώνεται σε κάθε αλλαγή πελατών (τοπικά ή από sync με το host) — ώστε ανοιχτά
    /// παράθυρα (π.χ. Πελάτες) να ανανεώνονται μόνα τους αντί να μένουν με παλιά εικόνα.</summary>
    public event Action? Changed;

    private CustomerStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "customers.json");
        if (RemoteSync.IsClient)
        {
            // ΑΡΑΙΑ επίτηδες. Ο πελατολόγιος είναι ΟΛΟΚΛΗΡΟΣ σε κάθε λήψη — με δεκάδες χιλιάδες
            // πελάτες αυτό είναι δεκάδες MB, και στα 5 δευτερόλεπτα που ήταν πριν το κύριο ταμείο
            // δεν προλάβαινε καν να τελειώσει τη μία αποστολή πριν ζητηθεί η επόμενη. Πλέον ρωτάμε
            // πρώτα «άλλαξε τίποτα;» (βλ. Version) και κατεβάζουμε μόνο τότε.
            RemoteSync.StartPolling(TimeSpan.FromSeconds(20), RefreshFromHostIfChangedAsync);
            return;
        }
        Load();
    }

    /// <summary>
    /// Αύξοντας αριθμός έκδοσης — αλλάζει σε κάθε μεταβολή πελατών. Το δεύτερο ταμείο τον ρωτάει
    /// (φθηνό, ένας αριθμός) και κατεβάζει ολόκληρη τη λίστα ΜΟΝΟ όταν έχει όντως αλλάξει.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Η «σφραγίδα» που συγκρίνει το δεύτερο ταμείο. Μαζί με τον αριθμό των πελατών, ώστε μια
    /// επανεκκίνηση του κυρίου (που μηδενίζει την έκδοση) να μη μοιάζει κατά λάθος «ίδια» με ό,τι έχει
    /// ήδη κατεβασμένο το δεύτερο.</summary>
    public string Stamp => Version + ":" + _customers.Count;

    /// <summary>
    /// Κάθε μεταβολή περνά από εδώ: ανεβάζει την έκδοση και φροντίζει τους βοηθητικούς πίνακες.
    ///
    /// <para><b>ΔΕΝ τους πετάει πια.</b> Πριν μηδενίζονταν όλοι, και ο επόμενος που θα τους ζητούσε
    /// πλήρωνε την ανακατασκευή ΠΑΝΩ ΣΤΗΝ ΟΘΟΝΗ: με 135.000 πελάτες αυτό είναι κοντά στο ένα
    /// δευτερόλεπτο. Και επειδή κάθε ολοκληρωμένη διανομή είναι μεταβολή, ο ταμίας το έτρωγε ξανά και
    /// ξανά — «περνάει ώρα, πάω να γράψω πελάτη και κολλάει μέχρι να φορτώσει».</para>
    ///
    /// <para>Πλέον οι πίνακες προτάσεων ξαναχτίζονται στο παρασκήνιο και ΩΣ ΤΟΤΕ σερβίρεται ο
    /// προηγούμενος: μια πρόταση που λείπει για ένα δευτερόλεπτο δεν τη βλέπει κανείς, ένα κολλημένο
    /// ταμείο το βλέπουν όλοι. Εξαίρεση το ευρετήριο τηλεφώνου, που δεν είναι υπόδειξη αλλά ΑΚΡΙΒΕΙΑ
    /// (πάνω του κρίνεται αν ο πελάτης είναι ο ίδιος) — αυτό ακυρώνεται αμέσως.</para>
    /// </summary>
    private void Touch()
    {
        Version++;
        _byPhone = null;
        ScheduleIndexRebuild();
    }

    /// <summary>Τελευταία σφραγίδα που κατέβασε το δεύτερο ταμείο — αν δεν άλλαξε, δεν ξανακατεβάζουμε.</summary>
    private string _syncedStamp = "";

    /// <summary>Δεύτερο ταμείο (client) — ρωτάει πρώτα τη σφραγίδα (λίγα bytes) και κατεβάζει
    /// ολόκληρο τον πελατολόγιο μόνο όταν έχει αλλάξει κάτι στο κύριο ταμείο.</summary>
    private async Task RefreshFromHostIfChangedAsync()
    {
        var stamp = await RemoteSync.GetAsync<string>("/api/sync/customers/version");
        if (stamp is not null && stamp == _syncedStamp)
            return;
        await RefreshFromHostAsync();
        if (stamp is not null)
            _syncedStamp = stamp;
    }

    /// <summary>Δεύτερο ταμείο (client) — αντικαθιστά την τοπική λίστα με τους πελάτες του host.
    /// Χωρίς αναζήτηση δικτύου ανά πληκτρολόγηση: η αναζήτηση δουλεύει πάνω σε αυτό το τοπικό αντίγραφο.</summary>
    private async Task RefreshFromHostAsync()
    {
        var customers = await RemoteSync.GetAsync<List<Customer>>("/api/sync/customers");
        if (customers is null)
            return;
        _customers = customers;
        Touch();
        Changed?.Invoke();
    }

    /// <summary>Δεύτερο ταμείο (client) — στέλνει τη μεταβολή στο host, μετά ξαναδιαβάζει την αληθινή κατάσταση.</summary>
    private async Task SyncThenRefreshAsync(string path, object body)
    {
        await RemoteSync.PostAsync(path, body);
        await RefreshFromHostAsync();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
                _customers = JsonSerializer.Deserialize<List<Customer>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception)
        {
            // Χαλασμένο αρχείο — ξεκίνα με κενή λίστα αντί να ρίξεις την εφαρμογή
            _customers = [];
        }
        ImportSeedIfPresent();
        // Πρώτο χτίσιμο αμέσως, στο παρασκήνιο: μέχρι να ανοίξει ο ταμίας παραγγελία είναι έτοιμο,
        // και δεν πληρώνεται ποτέ πάνω στο πρώτο πληκτρολόγημα.
        ScheduleIndexRebuild();
    }

    /// <summary>Τα τελευταία 10 ψηφία — έτσι ταιριάζει το ίδιο νούμερο γραμμένο με 0, με +30 ή σκέτο.</summary>
    private static string PhoneKey(string phone)
    {
        var d = DigitsOnly(phone);
        return d.Length > 10 ? d[^10..] : d;
    }

    /// <summary>
    /// Πελατολόγιο που ήρθε ΜΑΖΙ ΜΕ ΤΗΝ ΕΓΚΑΤΑΣΤΑΣΗ (αρχείο «pelates.json» δίπλα στο exe).
    ///
    /// <para><b>ΠΡΟΣΘΕΤΕΙ, ΔΕΝ ΑΝΤΙΚΑΘΙΣΤΑ.</b> Όποιος υπάρχει ήδη μένει ακριβώς όπως είναι — κρατάει το
    /// ιστορικό του (παραγγελίες, τζίρος, υπενθύμιση), που το εισαγόμενο αρχείο δεν έχει. Μπαίνουν μόνο
    /// όσοι λείπουν, με κλειδί το τηλέφωνο.</para>
    ///
    /// <para>Γίνεται ΜΙΑ φορά ανά αρχείο: η σφραγίδα (μέγεθος + ώρα) γράφεται δίπλα στα δεδομένα, οπότε
    /// τα επόμενα setup με τον ΙΔΙΟ πελατολόγιο δεν ξαναπερνούν τίποτα. Αν σταλεί ανανεωμένος, η
    /// σφραγίδα αλλάζει και μπαίνουν οι νέοι — πάλι χωρίς να πειραχτεί κανείς υπάρχων.</para>
    ///
    /// <para><b>Γίνεται στο ΠΑΡΑΣΚΗΝΙΟ.</b> Με δεκάδες χιλιάδες πελάτες, το διάβασμα ενός αρχείου
    /// σαράντα MB κρατούσε το πρώτο άνοιγμα κολλημένο σε μαύρο παράθυρο — και μοιάζει με κρέμασμα.
    /// Πλέον το ταμείο ανοίγει αμέσως και οι πελάτες εμφανίζονται μόλις τελειώσει, μόνοι τους.</para>
    /// </summary>
    private void ImportSeedIfPresent()
    {
        try
        {
            var seed = Path.Combine(AppContext.BaseDirectory, "pelates.json");
            if (!File.Exists(seed))
                return;

            var info = new FileInfo(seed);
            var stamp = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            var marker = Path.Combine(Path.GetDirectoryName(_path)!, "customers-import.txt");
            if (File.Exists(marker) && File.ReadAllText(marker).Trim() == stamp)
                return;   // μπήκε ήδη — ούτε διάβασμα, ούτε νήμα, τίποτα

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                MergeSeed(ReadSeed(seed), stamp, marker);   // χωρίς UI (π.χ. δοκιμές): επιτόπου
                return;
            }

            // ΠΑΡΑΣΚΗΝΙΟ. Το διάβασμα και η αποκωδικοποίηση δεκάδων MB είναι το ακριβό κομμάτι και
            // γίνεται εκτός οθόνης: το ταμείο ανοίγει και δουλεύει κανονικά όσο τρέχει. Το σμίξιμο
            // επιστρέφει στο νήμα της οθόνης, γιατί εκεί ζει η λίστα των πελατών.
            _ = Task.Run(() =>
            {
                try
                {
                    var prepared = ReadSeed(seed);
                    dispatcher.BeginInvoke(() => MergeSeed(prepared, stamp, marker));
                }
                catch (Exception ex)
                {
                    AppLog.Write("customers", $"Η ανάγνωση πελατολογίου απέτυχε: {ex.GetType().Name}: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            // Αποτυχία εισαγωγής δεν πρέπει να εμποδίσει το ταμείο να ανοίξει — αλλά πρέπει να φαίνεται.
            AppLog.Write("customers", $"Η εισαγωγή πελατολογίου απέτυχε: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Το ακριβό μισό, εκτός νήματος οθόνης: διάβασμα, αποκωδικοποίηση και υπολογισμός του
    /// κλειδιού τηλεφώνου για τον καθένα. Όσοι δεν έχουν τηλέφωνο μετριούνται και μένουν έξω — δεν
    /// μπορούν ούτε να ταιριάξουν με υπάρχοντα ούτε να βρεθούν αργότερα στην αναζήτηση.</summary>
    private static (List<(string Key, Customer Customer)> Ready, int Total, int NoPhone) ReadSeed(string seed)
    {
        var incoming = JsonSerializer.Deserialize<List<Customer>>(File.ReadAllText(seed)) ?? [];
        var ready = new List<(string, Customer)>(incoming.Count);
        var noPhone = 0;
        foreach (var c in incoming)
        {
            var key = PhoneKey(c.Phone);
            if (key.Length == 0)
                noPhone++;
            else
                ready.Add((key, c));
        }
        return (ready, incoming.Count, noPhone);
    }

    /// <summary>Το φθηνό μισό, στο νήμα της οθόνης: κρατάει όσους ΛΕΙΠΟΥΝ. Η αποθήκευση στον δίσκο
    /// φεύγει από το Save (αναβάλλεται και γράφεται στο παρασκήνιο) — ούτε εδώ περιμένει η οθόνη.</summary>
    private void MergeSeed((List<(string Key, Customer Customer)> Ready, int Total, int NoPhone) seed, string stamp, string marker)
    {
        try
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in _customers)
            {
                var key = PhoneKey(c.Phone);
                if (key.Length > 0)
                    known.Add(key);
            }

            var had = _customers.Count;
            var added = 0;
            var already = 0;
            foreach (var (key, customer) in seed.Ready)
            {
                if (!known.Add(key))
                {
                    already++;   // υπάρχει ήδη — ο ΥΠΑΡΧΩΝ κερδίζει, έχει ιστορικό
                    continue;
                }
                _customers.Add(customer);
                added++;
            }

            if (added > 0)
            {
                Save();              // Touch + γράψιμο στο παρασκήνιο
                Changed?.Invoke();   // ανοιχτά παράθυρα (π.χ. Πελάτες) δείχνουν τους νέους αμέσως
            }
            File.WriteAllText(marker, stamp);
            // Χωριστά νούμερα: το «πετάχτηκαν» έκρυβε δύο πολύ διαφορετικά πράγματα, και μόνο το ένα
            // είναι φυσιολογικό. Πολλοί ΧΩΡΙΣ ΤΗΛΕΦΩΝΟ σημαίνει ότι το αρχείο ήρθε λειψό.
            AppLog.Write("customers",
                $"Πελατολόγιο εγκατάστασης: είχε {had}, ήρθαν {seed.Total}, προστέθηκαν {added}, " +
                $"υπήρχαν ήδη {already}, χωρίς τηλέφωνο (δεν μπήκαν) {seed.NoPhone}. Σύνολο τώρα {_customers.Count}.");
        }
        catch (Exception ex)
        {
            AppLog.Write("customers", $"Η εισαγωγή πελατολογίου απέτυχε: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- Αποθήκευση: αναβάλλεται λίγο και γράφεται στο ΠΑΡΑΣΚΗΝΙΟ ----
    //
    // Πριν, κάθε αλλαγή πελάτη έγραφε ΟΛΟΚΛΗΡΟ τον πελατολόγιο πάνω στο νήμα της οθόνης. Με λίγους
    // πελάτες ήταν αστραπή· με δεκάδες χιλιάδες κρατούσε πάνω από ένα δευτερόλεπτο — και συμβαίνει
    // σε ΚΑΘΕ παραγγελία διανομής (βλ. RecordOrder), δηλαδή το ταμείο «κόλλαγε» σε κάθε πελάτη.
    //
    // Τώρα: μαζεύουμε τις αλλαγές για λίγο (πολλές αλλαγές = ΜΙΑ εγγραφή) και μετά γράφουμε από
    // αντίγραφο, σε νήμα παρασκηνίου. Η οθόνη δεν περιμένει ποτέ τον δίσκο.
    private DispatcherTimer? _saveTimer;

    /// <summary>Δύο εγγραφές μαζί θα πατούσαν το ίδιο προσωρινό αρχείο (βλ. AtomicFile) — σειριοποιούνται.</summary>
    private readonly object _saveGate = new();

    private void Save()
    {
        // ΕΝΑ σημείο για κάθε μεταβολή: όλες οι αλλαγές πελατών καταλήγουν εδώ, οπότε εδώ ανεβαίνει
        // η έκδοση και πετιούνται οι πίνακες αναζήτησης.
        Touch();

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            WriteToDisk([.. _customers]);   // χωρίς UI (π.χ. δοκιμές): γράψε κατευθείαν
            return;
        }
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Save);
            return;
        }

        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _saveTimer.Tick += (_, _) => FlushPendingSave(background: true);
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Γράφει ΤΩΡΑ ό,τι εκκρεμεί — καλείται και στο κλείσιμο της εφαρμογής (βλ. App.OnExit),
    /// ώστε μια αλλαγή των τελευταίων δευτερολέπτων να μη χαθεί.</summary>
    /// <param name="background">true στη συνηθισμένη ροή (η οθόνη δεν περιμένει). ΣΤΟ ΚΛΕΙΣΙΜΟ πρέπει
    /// να είναι false: μια εργασία παρασκηνίου δεν προλαβαίνει να τελειώσει όταν η εφαρμογή σβήνει, και
    /// η τελευταία αλλαγή θα χανόταν σιωπηλά.</param>
    public void FlushPendingSave(bool background = false)
    {
        _saveTimer?.Stop();
        var snapshot = _customers.ToList();
        if (background)
            _ = Task.Run(() => WriteToDisk(snapshot));
        else
            WriteToDisk(snapshot);
    }

    private void WriteToDisk(List<Customer> snapshot)
    {
        lock (_saveGate)
        {
            try
            {
                AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(snapshot, JsonOpts));
            }
            catch (Exception)
            {
                // Αποτυχία εγγραφής δεν πρέπει να μπλοκάρει το ταμείο
            }
        }
    }

    private static string DigitsOnly(string s) => new([.. s.Where(char.IsDigit)]);

    /// <summary>«Ίδια» διεύθυνση για σκοπούς ταιριάσματος — δρόμος + περιοχή, χωρίς να μετράει αριθμός/
    /// όροφος (αυτά συχνά διορθώνονται σε επόμενη παραγγελία στην ΙΔΙΑ διεύθυνση, δεν είναι νέα διεύθυνση).</summary>
    private static bool SameAddress(string street1, string area1, string street2, string area2) =>
        string.Equals(street1.Trim(), street2.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(area1.Trim(), area2.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Πόσες πρόσθετες διευθύνσεις κρατάμε ανά πελάτη πριν αρχίσουμε να πετάμε την παλιότερη —
    /// αρκετό για «δουλειά»/«φίλος»/τα σπάνια περιστασιακά, χωρίς να μεγαλώνει επ' άπειρον από τυπογραφικά.</summary>
    private const int MaxOtherAddresses = 4;

    /// <summary>Όλοι οι πελάτες, με τους καλύτερους πρώτους — μεγαλύτερος τζίρος, μετά περισσότερες παραγγελίες.</summary>
    public IReadOnlyList<Customer> All =>
        _customers.OrderByDescending(c => c.TotalRevenue).ThenByDescending(c => c.OrderCount).ToList();

    /// <summary>Αποθηκεύει τη μόνιμη υπενθύμιση του πελάτη.</summary>
    public void SetMemo(Customer customer, string memo)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/customers/memo",
                new { customer.Name, customer.Phone, customer.Address, Memo = memo.Trim() });
            return;
        }
        customer.Memo = memo.Trim();
        Save();
        Changed?.Invoke();
    }

    /// <summary>Διαγραφή μιας πρόσθετης αποθηκευμένης διεύθυνσης (π.χ. μετακόμισε/δεν ισχύει πια) —
    /// δεν αγγίζει ποτέ την κύρια διεύθυνση, μόνο εγγραφές στο OtherAddresses. Το ταίριασμα εδώ πρέπει να
    /// είναι ΑΚΡΙΒΕΣ (μαζί με αριθμό) — το SameAddress παραπάνω αγνοεί επίτηδες τον αριθμό για το
    /// FindOrCreate (βλ. σχόλιό του), αλλά εδώ θα διέγραφε ΚΑΙ τις δύο αν ο πελάτης έχει δύο άλλες
    /// διευθύνσεις στον ίδιο δρόμο/περιοχή με διαφορετικό αριθμό.</summary>
    public void RemoveOtherAddress(Customer customer, CustomerAddress address)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/customers/remove-address", new
            {
                customer.Name, customer.Phone, customer.Address,
                OtherAddress = address.Address, OtherNumber = address.StreetNumber, OtherArea = address.Area,
            });
            return;
        }
        customer.OtherAddresses.RemoveAll(a => ExactSameAddress(a, address));
        Save();
        Changed?.Invoke();
    }

    /// <summary>Διαγραφή της κύριας διεύθυνσης — ο ταμίας μπορεί να διαγράψει οποιαδήποτε διεύθυνση θέλει,
    /// ακόμα και την κύρια. Προάγει την πρώτη αποθηκευμένη «άλλη» διεύθυνση σε κύρια αν υπάρχει, αλλιώς
    /// αδειάζει εντελώς τα στοιχεία κύριας διεύθυνσης.</summary>
    public void RemoveMainAddress(Customer customer)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/customers/remove-main-address",
                new { customer.Name, customer.Phone, customer.Address });
            return;
        }
        var promoted = customer.OtherAddresses.FirstOrDefault();
        if (promoted is not null)
        {
            customer.Address = promoted.Address;
            customer.StreetNumber = promoted.StreetNumber;
            customer.Area = promoted.Area;
            customer.PostalCode = promoted.PostalCode;
            customer.Floor = promoted.Floor;
            customer.OtherAddresses.Remove(promoted);
        }
        else
        {
            customer.Address = "";
            customer.StreetNumber = "";
            customer.Area = "";
            customer.PostalCode = "";
            customer.Floor = "";
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>Προσθήκη μιας διεύθυνσης στον πελάτη χωρίς να χρειάζεται να περάσει παραγγελία πρώτα —
    /// ο ταμίας τη γράφει κατευθείαν στα πεδία του Βήματος 2 και πατάει «Προσθήκη». Ίδια λογική
    /// ταιριάσματος με το FindOrCreate (βλ. εκεί): αν είναι ίδια με την κύρια ή με ήδη αποθηκευμένη
    /// «άλλη» διεύθυνση, ενημερώνει επιτόπου αντί να δημιουργήσει διπλότυπο.</summary>
    public void AddOtherAddress(Customer customer, string address, string streetNumber, string area,
        string postalCode, string floor)
    {
        if (address.Trim().Length == 0)
            return;
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/customers/add-address", new
            {
                customer.Name, customer.Phone, customer.Address,
                NewAddress = address, NewNumber = streetNumber, NewArea = area, NewPostalCode = postalCode, NewFloor = floor,
            });
            return;
        }

        if (customer.Address.Length == 0 || SameAddress(customer.Address, customer.Area, address, area))
        {
            customer.Address = address;
            if (streetNumber.Length > 0) customer.StreetNumber = streetNumber;
            if (area.Length > 0) customer.Area = area;
            if (postalCode.Length > 0) customer.PostalCode = postalCode;
            if (floor.Length > 0) customer.Floor = floor;
            Save();
            Changed?.Invoke();
            return;
        }

        var match = customer.OtherAddresses.FirstOrDefault(a => SameAddress(a.Address, a.Area, address, area));
        if (match is not null)
        {
            match.Address = address;
            match.Label = address;
            if (streetNumber.Length > 0) match.StreetNumber = streetNumber;
            if (area.Length > 0) match.Area = area;
            if (postalCode.Length > 0) match.PostalCode = postalCode;
            if (floor.Length > 0) match.Floor = floor;
        }
        else
        {
            customer.OtherAddresses.Add(new CustomerAddress
            {
                Label = address, Address = address, StreetNumber = streetNumber, Area = area,
                PostalCode = postalCode, Floor = floor,
            });
            while (customer.OtherAddresses.Count > MaxOtherAddresses)
                customer.OtherAddresses.RemoveAt(0);
        }
        Save();
        Changed?.Invoke();
    }

    private static bool ExactSameAddress(CustomerAddress a, CustomerAddress b) =>
        string.Equals(a.Address.Trim(), b.Address.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.StreetNumber.Trim(), b.StreetNumber.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Area.Trim(), b.Area.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Βρίσκει (με τηλέφωνο, αλλιώς όνομα+διεύθυνση) ή δημιουργεί πελάτη και ενημερώνει το προφίλ.
    /// <para>Το ταίριασμα γίνεται με το ΙΔΙΟ κλειδί που χρησιμοποιεί η αναγνώριση κλήσης και η εισαγωγή
    /// πελατολογίου (PhoneKey, τα τελευταία 10 ψηφία). Πριν συγκρίνονταν όλα τα ψηφία ολόκληρα, οπότε ο
    /// ίδιος άνθρωπος γραμμένος «+30 210…» τη μία και «210…» την άλλη έφτιαχνε ΔΕΥΤΕΡΗ καρτέλα — ενώ το
    /// τηλέφωνο που χτυπούσε τον έβρισκε κανονικά. Με εισαγόμενο πελατολόγιο από αλλού, όπου τα νούμερα
    /// είναι γραμμένα όπως τύχαινε, αυτό είναι ο κανόνας και όχι η εξαίρεση.</para>
    /// <para>Και είναι ευρετήριο αντί για σάρωση: το FindOrCreate τρέχει σε ΚΑΘΕ παραγγελία διανομής, και
    /// η σάρωση γεννούσε μια συμβολοσειρά ανά πελάτη — δεκάδες χιλιάδες, δύο φορές, σε κάθε παραγγελία.</para></summary>
    private Customer FindOrCreate(string name, string phone, string address, string streetNumber,
        string area, string postalCode, string floor, string notes)
    {
        var phoneKey = PhoneKey(phone);
        var existing = phoneKey.Length > 0
            ? PhoneIndex.GetValueOrDefault(phoneKey)
            : _customers.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Address, address, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = new Customer
            {
                Name = name, Phone = phone, Address = address, StreetNumber = streetNumber,
                Area = area, PostalCode = postalCode, Floor = floor, Notes = notes,
            };
            _customers.Add(existing);
        }
        else
        {
            existing.Name = name;
            if (phone.Length > 0) existing.Phone = phone;
            if (notes.Length > 0) existing.Notes = notes;

            if (address.Length > 0)
            {
                if (existing.Address.Length == 0 || SameAddress(existing.Address, existing.Area, address, area))
                {
                    // Ίδια διεύθυνση με την κύρια (ή δεν υπήρχε ακόμα καμία) — ενημέρωσε επιτόπου, δεν
                    // είναι νέα διεύθυνση, μάλλον διόρθωση αριθμού/ορόφου/Τ.Κ.
                    existing.Address = address;
                    if (streetNumber.Length > 0) existing.StreetNumber = streetNumber;
                    if (area.Length > 0) existing.Area = area;
                    if (postalCode.Length > 0) existing.PostalCode = postalCode;
                    if (floor.Length > 0) existing.Floor = floor;
                }
                else
                {
                    // Διαφορετική διεύθυνση από την κύρια — ΔΕΝ την αντικαθιστά (π.χ. μια περιστασιακή
                    // παραγγελία από φίλο δεν πρέπει να σβήσει το σπίτι του πελάτη). Ταιριάζει με ήδη
                    // αποθηκευμένη «άλλη» διεύθυνση αν υπάρχει, αλλιώς προστίθεται σαν νέα.
                    var match = existing.OtherAddresses.FirstOrDefault(a => SameAddress(a.Address, a.Area, address, area));
                    if (match is not null)
                    {
                        match.Address = address;
                        match.Label = address.Length > 0 ? address : area;
                        if (streetNumber.Length > 0) match.StreetNumber = streetNumber;
                        if (area.Length > 0) match.Area = area;
                        if (postalCode.Length > 0) match.PostalCode = postalCode;
                        if (floor.Length > 0) match.Floor = floor;
                    }
                    else
                    {
                        existing.OtherAddresses.Add(new CustomerAddress
                        {
                            // Η οδός ξεχωρίζει καλύτερα από την περιοχή — δύο διευθύνσεις στην ίδια
                            // περιοχή (π.χ. και οι δύο «Βύρωνας») θα έδειχναν ταυτόσημες ετικέτες.
                            Label = address.Length > 0 ? address : area,
                            Address = address, StreetNumber = streetNumber, Area = area,
                            PostalCode = postalCode, Floor = floor,
                        });
                        while (existing.OtherAddresses.Count > MaxOtherAddresses)
                            existing.OtherAddresses.RemoveAt(0);
                    }
                }
            }
        }
        return existing;
    }

    /// <summary>Καταχώρηση/ενημέρωση πελάτη — ταίριασμα με το τηλέφωνο, αλλιώς με όνομα+διεύθυνση.</summary>
    public void Upsert(string name, string phone, string address, string streetNumber = "",
        string area = "", string postalCode = "", string floor = "", string notes = "")
    {
        if (name.Length == 0)
            return;
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/customers/upsert", new CustomerUpsertRequest(
                name, phone, address, streetNumber, area, postalCode, floor, notes));
            return;
        }
        FindOrCreate(name, phone, address, streetNumber, area, postalCode, floor, notes);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Καταγράφει ολοκληρωμένη παραγγελία στο μόνιμο ιστορικό του πελάτη.</summary>
    public void RecordOrder(string name, string phone, string address, string streetNumber, string area,
        string postalCode, string floor, string notes, decimal total, IReadOnlyList<(string Name, int Quantity)> lines)
    {
        if (name.Length == 0)
            return;
        if (RemoteSync.IsClient)
        {
            var dto = lines.Select(l => new CustomerOrderLineDto(l.Name, l.Quantity)).ToList();
            _ = SyncThenRefreshAsync("/api/sync/customers/record-order", new CustomerRecordOrderRequest(
                name, phone, address, streetNumber, area, postalCode, floor, notes, total, dto));
            return;
        }
        var c = FindOrCreate(name, phone, address, streetNumber, area, postalCode, floor, notes);
        c.OrderCount++;
        c.TotalRevenue += total;
        c.FirstOrderAt ??= DateTime.Now;
        c.LastOrderAt = DateTime.Now;
        foreach (var (lineName, qty) in lines)
            c.ProductCounts[lineName] = c.ProductCounts.GetValueOrDefault(lineName) + qty;
        // Η υπενθύμιση «καταναλώνεται» με την παραγγελία — δεν ξαναβγαίνει την επόμενη φορά
        c.Memo = "";
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Κοινή λογική αναζήτησης πελάτη — όνομα, διεύθυνση, περιοχή ή τηλέφωνο.
    /// Χρησιμοποιείται και στις παραγγελίες και στο παράθυρο Πελάτες.
    /// </summary>
    public static bool Matches(Customer c, string query) => Matches(c, query.Trim(), DigitsOnly(query));

    /// <summary>Ίδιο με το παραπάνω, με τα ψηφία του ερωτήματος ΕΤΟΙΜΑ — για σάρωση δεκάδων χιλιάδων
    /// πελατών σε κάθε πλήκτρο, όπου ο υπολογισμός τους ανά πελάτη ήταν σκέτη σπατάλη.</summary>
    private static bool Matches(Customer c, string q, string qDigits) =>
        Matches(c, q, qDigits, DigitsOnly(c.Phone));

    private static bool Matches(Customer c, string q, string qDigits, string cDigits)
    {
        if (q.Length == 0)
            return true;

        // Σύγκριση ΧΩΡΙΣ να φτιάχνονται καινούριες συμβολοσειρές. Πριν, κάθε πελάτης γεννούσε τρία
        // πεζογραμμένα αντίγραφα ΣΕ ΚΑΘΕ ΠΛΗΚΤΡΟ — με δεκάδες χιλιάδες πελάτες, εκατοντάδες χιλιάδες
        // περιττές συμβολοσειρές ανά γράμμα. (Και πιάνει σωστά το τελικό «ς»: το OrdinalIgnoreCase
        // βλέπει «ς» και «σ» ως ίδιο γράμμα, ενώ το πεζογράμμισμα όχι.)
        if (c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || c.Address.Contains(q, StringComparison.OrdinalIgnoreCase)
            || c.Area.Contains(q, StringComparison.OrdinalIgnoreCase))
            return true;

        // Το τηλέφωνο μετράει ΜΟΝΟ αν όντως ψάχνει με αριθμό.
        return qDigits.Length >= 3 && cDigits.Contains(qDigits);
    }

    // ---- Αυτόματη συμπλήρωση από ό,τι έχει ήδη περαστεί ----
    // Οι προτάσεις διεύθυνσης έρχονταν παλιά από τον χάρτη (Nominatim/Google, βλ. DeliveryRouteService):
    // πρότεινε δρόμους όλης της Ελλάδας, συχνά λάθος περιοχή, και εξαρτιόταν από το internet. Τώρα οι
    // προτάσεις βγαίνουν από τους ίδιους μας τους πελάτες — ό,τι έχει ξαναγραφτεί στο μαγαζί, τίποτα άλλο.

    /// <summary>
    /// Μικρά ονόματα που έχουν ήδη περαστεί — ΜΟΝΟ το πρώτο κομμάτι του ονοματεπώνυμου.
    ///
    /// Κάθε κελί προτείνει αυστηρά ό,τι γράφεται σε ΕΚΕΙΝΟ το κελί: γράφοντας «ΣΤΕΦΑΝΟΣ» στο Όνομα, η
    /// πρόταση δεν πρέπει να φέρνει μαζί και το επώνυμο κάποιου άλλου πελάτη — που κατέληγε να γράφεται
    /// ολόκληρο μέσα στο κελί του ονόματος.
    /// </summary>
    // Οι τέσσερις πίνακες προτάσεων. Χτίζονται ΜΙΑ φορά και ζουν μέχρι να αλλάξει κάτι στους πελάτες
    // (βλ. Touch). Πριν, κάθε πάτημα πλήκτρου σάρωνε ΟΛΟΥΣ τους πελάτες και έφτιαχνε λεξικό συχνοτήτων
    // από την αρχή — με δεκάδες χιλιάδες πελάτες αυτό είναι αισθητό κόλλημα σε κάθε γράμμα.
    private List<string>? _firstNames;
    private List<string>? _lastNames;
    private List<string>? _streets;
    private List<string>? _areas;

    public IReadOnlyList<string> SuggestFirstNames(string typed) =>
        Pick(_firstNames, typed);

    /// <summary>Επώνυμα που έχουν ήδη περαστεί — ό,τι ακολουθεί το πρώτο κενό.</summary>
    public IReadOnlyList<string> SuggestLastNames(string typed) =>
        Pick(_lastNames, typed);

    /// <summary>Ο χωρισμός γίνεται στο πρώτο κενό, ίδια λογική με τα δύο κελιά της φόρμας
    /// (βλ. OrderWizardViewModel.CustomerFirstName/CustomerLastName) — από κάτω παραμένει ΕΝΑ πεδίο.</summary>
    private static string FirstNameOf(string name)
    {
        var s = name.Trim();
        var i = s.IndexOf(' ');
        return i < 0 ? s : s[..i];
    }

    private static string LastNameOf(string name)
    {
        var s = name.Trim();
        var i = s.IndexOf(' ');
        return i < 0 ? "" : s[(i + 1)..].TrimStart();
    }

    /// <summary>Οδοί που έχουν ήδη περαστεί — και οι κύριες και οι πρόσθετες διευθύνσεις.</summary>
    public IReadOnlyList<string> SuggestStreets(string typed) =>
        Pick(_streets, typed);

    /// <summary>Περιοχές που έχουν ήδη περαστεί.</summary>
    public IReadOnlyList<string> SuggestAreas(string typed) =>
        Pick(_areas, typed);

    /// <summary>
    /// Όσα ταιριάζουν με ό,τι πληκτρολογείται, χωρίς διπλότυπα. Πρώτα αυτά που ΑΡΧΙΖΟΥΝ από το
    /// γραμμένο κείμενο (αυτό περιμένει ο ταμίας όταν γράφει τα πρώτα γράμματα) και μετά όσα απλώς το
    /// περιέχουν· μέσα σε κάθε ομάδα, πρώτα τα πιο συχνά — έτσι οι καθημερινές περιοχές/δρόμοι του
    /// μαγαζιού ανεβαίνουν από μόνες τους στην κορυφή.
    /// </summary>
    private static List<string> BuildIndex(IEnumerable<string> values)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            // Κεφαλαία και εδώ: οι παλιοί πελάτες είναι γραμμένοι όπως τύχαινε, και χωρίς αυτό ο ίδιος
            // δρόμος εμφανιζόταν δύο φορές στη λίστα («Μαγνησίας» και «ΜΑΓΝΗΣΙΑΣ»).
            var v = GreekText.Upper(value).Trim();
            if (v.Length == 0)
                continue;
            counts[v] = counts.TryGetValue(v, out var n) ? n + 1 : 1;
        }

        // Η σειρά κρίνεται ΜΙΑ φορά, εδώ: πρώτα οι πιο συχνές τιμές του μαγαζιού.
        return counts
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.Key)
            .ToList();
    }

    /// <summary>
    /// Διαλέγει μέχρι 6 προτάσεις από τον έτοιμο πίνακα. Ένα πέρασμα, χωρίς ταξινόμηση και χωρίς
    /// καινούριες συμβολοσειρές — ο πίνακας είναι ήδη με τις πιο συχνές πρώτες, οπότε κρατάμε τη σειρά
    /// του και απλώς βάζουμε μπροστά όσες ΑΡΧΙΖΟΥΝ από το γραμμένο κείμενο.
    /// </summary>
    private static IReadOnlyList<string> Pick(List<string>? index, string typed)
    {
        var q = typed.Trim();
        // Άδειος πίνακας = δεν έχει προλάβει να χτιστεί ακόμα (τα πρώτα δευτερόλεπτα μετά το άνοιγμα).
        // Καμία πρόταση, καμία αναμονή.
        if (index is null || q.Length < 2)
            return [];

        var starts = new List<string>(6);
        var contains = new List<string>(6);
        foreach (var v in index)
        {
            // Ό,τι έχει ήδη γραφτεί ολόκληρο δεν είναι πρόταση — αλλιώς η λίστα έμενε ανοιχτή από κάτω
            // ακόμα και αφού ο ταμίας διάλεγε από αυτήν.
            if (string.Equals(v, q, StringComparison.OrdinalIgnoreCase))
                continue;
            if (v.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            {
                starts.Add(v);
                if (starts.Count == 6)
                    break;   // γέμισε με τις καλύτερες, δεν χρειάζεται να δούμε τις υπόλοιπες
            }
            else if (contains.Count < 6 && v.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                contains.Add(v);
            }
        }

        if (starts.Count >= 6)
            return starts;
        starts.AddRange(contains.Take(6 - starts.Count));
        return starts;
    }

    /// <summary>
    /// Αναζήτηση για το autocomplete στις παραγγελίες — μέχρι 6, με τους πιθανότερους ΠΡΩΤΟΥΣ.
    ///
    /// <para>Πριν κρατούσε απλώς τους 6 πρώτους της λίστας που ταίριαζαν, με σειρά καταχώρησης. Και
    /// επειδή το ταίριασμα είναι «περιέχει», το «211» ενός σταθερού έπιανε και κάθε κινητό που τύχαινε
    /// να έχει 211 στη μέση: με δεκάδες χιλιάδες πελάτες η λίστα γέμιζε άσχετους και ο ζητούμενος δεν
    /// φαινόταν ΠΟΤΕ. Τώρα προηγούνται όσοι <b>ΑΡΧΙΖΟΥΝ</b> από ό,τι γράφτηκε — τηλέφωνο, όνομα ή
    /// διεύθυνση — και οι υπόλοιποι μπαίνουν από κάτω για να γεμίσουν τις 6 θέσεις.</para>
    ///
    /// <para>Ένα πέρασμα, με έξοδο μόλις γεμίσουν οι 6 «αρχίζει από»: στη συνηθισμένη περίπτωση (γράφει
    /// τηλέφωνο από την αρχή) σταματά αμέσως, χωρίς να δει ολόκληρο τον πελατολόγιο.</para>
    /// </summary>
    public IReadOnlyList<Customer> Search(string query)
    {
        var q = query.Trim();
        if (q.Length == 0)
            return [];

        var qDigits = DigitsOnly(q);
        var digits = PhoneDigits;
        var starts = new List<Customer>(6);
        var contains = new List<Customer>(6);
        for (var i = 0; i < _customers.Count; i++)
        {
            var c = _customers[i];
            if (!Matches(c, q, qDigits, digits[i]))
                continue;
            if (StartsWithQuery(c, q, qDigits, digits[i]))
            {
                starts.Add(c);
                if (starts.Count == 6)
                    return starts;
            }
            else if (contains.Count < 6)
            {
                contains.Add(c);
            }
        }
        starts.AddRange(contains.Take(6 - starts.Count));
        return starts;
    }

    /// <summary>Ταιριάζει από την ΑΡΧΗ — αυτό περιμένει ο ταμίας όταν πληκτρολογεί τα πρώτα ψηφία ή
    /// γράμματα. Υπολογίζεται μόνο για τους λίγους που πέρασαν ήδη το φίλτρο.</summary>
    private static bool StartsWithQuery(Customer c, string q, string qDigits, string cDigits) =>
        (qDigits.Length >= 3 && cDigits.StartsWith(qDigits, StringComparison.Ordinal))
        || c.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase)
        || c.Address.StartsWith(q, StringComparison.OrdinalIgnoreCase);

    /// <summary>Χρησιμοποιείται από το endpoint συγχρονισμού μνήμης (host) όταν το δεύτερο ταμείο
    /// στέλνει υπενθύμιση πελάτη — ίδιο ταίριασμα με το FindOrCreate (τηλέφωνο, αλλιώς όνομα+διεύθυνση).</summary>
    public Customer? Find(string name, string phone, string address)
    {
        var phoneKey = PhoneKey(phone);
        return phoneKey.Length > 0
            ? PhoneIndex.GetValueOrDefault(phoneKey)
            : _customers.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Address, address, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Βρίσκει πελάτη με ακριβές τηλέφωνο — για την αναγνώριση κλήσεων (βλ. IncomingCallService).
    /// Συγκρίνει τα τελευταία 10 ψηφία, ώστε να ταιριάζει είτε το τηλεφωνικό κέντρο στέλνει τον
    /// αριθμό με 0, με κωδικό χώρας (+30) ή χωρίς.
    /// </summary>
    /// <summary>
    /// Τα ψηφία του τηλεφώνου κάθε πελάτη, στη ΣΕΙΡΑ του _customers.
    ///
    /// <para>Η αναζήτηση με αριθμό είναι «περιέχει», άρα δεν γίνεται ευρετήριο — πρέπει να δει έναν
    /// έναν τους πελάτες. Χωρίς αυτόν τον πίνακα, κάθε πλήκτρο γεννούσε μια συμβολοσειρά ΑΝΑ ΠΕΛΑΤΗ:
    /// με 135.000 πελάτες, εκατόν τριάντα πέντε χιλιάδες σκουπίδια σε κάθε γράμμα που πληκτρολογεί ο
    /// ταμίας. Τώρα τα ψηφία υπολογίζονται μία φορά και ζουν μέχρι να αλλάξει κάτι (βλ. Touch).</para>
    /// </summary>
    private string[]? _phoneDigits;

    private string[] PhoneDigits
    {
        get
        {
            // Οι πελάτες μόνο ΠΡΟΣΤΙΘΕΝΤΑΙ στο τέλος, ποτέ δεν αφαιρούνται: όταν ο πίνακας έχει μείνει
            // πίσω, αρκεί να συμπληρωθεί η ουρά — δεν ξαναϋπολογίζονται 135.000 τηλέφωνα επειδή
            // μπήκε ένας πελάτης. (Αλλαγή τηλεφώνου σε υπάρχοντα διορθώνεται με την ανακατασκευή
            // παρασκηνίου λίγο μετά· η αναζήτηση είναι υπόδειξη, όχι ταμειακή απόδειξη.)
            if (_phoneDigits is null || _phoneDigits.Length > _customers.Count)
                _phoneDigits = [.. _customers.Select(c => DigitsOnly(c.Phone))];
            else if (_phoneDigits.Length < _customers.Count)
                _phoneDigits = [.. _phoneDigits, .. _customers.Skip(_phoneDigits.Length).Select(c => DigitsOnly(c.Phone))];
            return _phoneDigits;
        }
    }

    // ---- Ανακατασκευή πινάκων στο παρασκήνιο ----

    /// <summary>Μαζεύει τις αλλαγές για λίγο (μια βάρδια με παραγγελίες πίσω από πίσω θα έριχνε
    /// αλλιώς μια ανακατασκευή ανά παραγγελία) και μετά χτίζει ΜΙΑ φορά, εκτός οθόνης.</summary>
    private DispatcherTimer? _indexTimer;
    private bool _rebuildingIndexes;

    private void ScheduleIndexRebuild()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            ApplyIndexes(BuildIndexes([.. _customers]));   // χωρίς UI (π.χ. δοκιμές): επιτόπου
            return;
        }
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(ScheduleIndexRebuild);
            return;
        }

        if (_indexTimer is null)
        {
            _indexTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _indexTimer.Tick += (_, _) => StartIndexRebuild();
        }
        _indexTimer.Stop();
        _indexTimer.Start();
    }

    private void StartIndexRebuild()
    {
        _indexTimer?.Stop();
        if (_rebuildingIndexes)
        {
            // Τρέχει ήδη μία με παλιότερα δεδομένα — ξαναπρογραμμάτισε ώστε να πιαστούν και οι νέες
            // αλλαγές, αντί για δύο ανακατασκευές μαζί πάνω στον ίδιο επεξεργαστή.
            ScheduleIndexRebuild();
            return;
        }

        _rebuildingIndexes = true;
        // Κρατάμε ΚΑΙ τη λίστα από την οποία βγήκε το αντίγραφο. Το δεύτερο ταμείο αντικαθιστά ολόκληρη
        // τη λίστα σε κάθε συγχρονισμό (RefreshFromHostAsync): αν αυτό συμβεί όσο χτίζουμε, ο πίνακας
        // ψηφίων θα αντιστοιχούσε σε ΑΛΛΗ λίστα — και αν τύχαινε ίδιο πλήθος, θα ταίριαζε σιωπηλά λάθος
        // τηλέφωνα με λάθος πελάτες. Οι προτάσεις είναι υποδείξεις και δεν πειράζει να είναι λίγο παλιές·
        // ο πίνακας ψηφίων όμως δείχνει με τη ΘΕΣΗ, οπότε ή ταιριάζει στη λίστα ή πετιέται.
        var source = _customers;
        var snapshot = source.ToList();
        var dispatcher = System.Windows.Application.Current!.Dispatcher;
        _ = Task.Run(() =>
        {
            var built = BuildIndexes(snapshot);
            dispatcher.BeginInvoke(() =>
            {
                _rebuildingIndexes = false;
                ApplyIndexes(built, ReferenceEquals(source, _customers));
            });
        });
    }

    private sealed record Indexes(List<string> FirstNames, List<string> LastNames, List<string> Streets,
        List<string> Areas, string[] PhoneDigits);

    /// <summary>Το ακριβό κομμάτι, πάντα πάνω σε ΑΝΤΙΓΡΑΦΟ της λίστας ώστε να μπορεί να τρέξει έξω από
    /// το νήμα της οθόνης όσο το ταμείο δουλεύει.</summary>
    private static Indexes BuildIndexes(List<Customer> customers)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var built = new Indexes(
            BuildIndex(customers.Select(c => FirstNameOf(c.Name))),
            BuildIndex(customers.Select(c => LastNameOf(c.Name))),
            BuildIndex(customers.SelectMany(c => c.OtherAddresses.Select(a => a.Address).Prepend(c.Address))),
            BuildIndex(customers.SelectMany(c => c.OtherAddresses.Select(a => a.Area).Prepend(c.Area))),
            [.. customers.Select(c => DigitsOnly(c.Phone))]);
        // Μετρημένο 18/8/2026 με 135.004 πελάτες: 811 ms. Είναι ΚΑΝΟΝΙΚΟ και γι αυτό τρέχει στο
        // παρασκήνιο· καταγράφεται μόνο αν ξεφύγει πολύ πάνω από αυτό, χωρίς να γεμίζει το αρχείο
        // γραμμές σε κάθε παραγγελία.
        if (watch.ElapsedMilliseconds > 2000)
            AppLog.Write("customers", $"Πίνακες αναζήτησης: {customers.Count} πελάτες σε {watch.ElapsedMilliseconds} ms (παρασκήνιο).");
        return built;
    }

    private void ApplyIndexes(Indexes built, bool sameList = true)
    {
        _firstNames = built.FirstNames;
        _lastNames = built.LastNames;
        _streets = built.Streets;
        _areas = built.Areas;
        // Ίδια λίστα: ό,τι μπήκε στο μεταξύ μπήκε στο ΤΕΛΟΣ, οπότε ο πίνακας είναι σωστός ως εκεί που
        // φτάνει και ο getter συμπληρώνει την ουρά. Αλλιώς τον πετάμε και ξαναχτίζεται με την πρώτη
        // αναζήτηση.
        _phoneDigits = sameList ? built.PhoneDigits : null;
    }

    /// <summary>Τηλέφωνο (τα τελευταία 10 ψηφία, βλ. PhoneKey) → πελάτης. Χτίζεται μία φορά και ζει
    /// μέχρι να αλλάξει κάτι στους πελάτες (βλ. Touch). Το χρησιμοποιούν και η αναγνώριση κλήσης και το
    /// FindOrCreate κάθε παραγγελίας.</summary>
    private Dictionary<string, Customer>? _byPhone;

    private Dictionary<string, Customer> PhoneIndex
    {
        get
        {
            if (_byPhone is not null)
                return _byPhone;
            var map = new Dictionary<string, Customer>(StringComparer.Ordinal);
            foreach (var c in _customers)
            {
                var key = PhoneKey(c.Phone);
                if (key.Length == 0)
                    continue;
                // Πρώτος κερδίζει — ίδια συμπεριφορά με το FirstOrDefault που υπήρχε πριν.
                map.TryAdd(key, c);
            }
            return _byPhone = map;
        }
    }

    public Customer? FindByPhone(string phone)
    {
        var key = PhoneKey(phone);
        // Λιγότερα από 6 ψηφία δεν είναι τηλέφωνο — το τηλεφωνικό κέντρο στέλνει και σκουπίδια.
        if (key.Length < 6)
            return null;
        return PhoneIndex.GetValueOrDefault(key);
    }
}
