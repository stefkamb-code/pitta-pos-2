using System.IO;
using System.Text.Json;
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
            RemoteSync.StartPolling(TimeSpan.FromSeconds(5), RefreshFromHostAsync);
            return;
        }
        Load();
    }

    /// <summary>Δεύτερο ταμείο (client) — αντικαθιστά την τοπική λίστα με τους πελάτες του host.
    /// Χωρίς αναζήτηση δικτύου ανά πληκτρολόγηση: η αναζήτηση δουλεύει πάνω σε αυτό το τοπικό αντίγραφο.</summary>
    private async Task RefreshFromHostAsync()
    {
        var customers = await RemoteSync.GetAsync<List<Customer>>("/api/sync/customers");
        if (customers is null)
            return;
        _customers = customers;
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
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_customers, JsonOpts));
        }
        catch (Exception)
        {
            // Αποτυχία εγγραφής δεν πρέπει να μπλοκάρει το ταμείο
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

    /// <summary>Βρίσκει (με τηλέφωνο, αλλιώς όνομα+διεύθυνση) ή δημιουργεί πελάτη και ενημερώνει το προφίλ.</summary>
    private Customer FindOrCreate(string name, string phone, string address, string streetNumber,
        string area, string postalCode, string floor, string notes)
    {
        var phoneDigits = DigitsOnly(phone);
        var existing = phoneDigits.Length > 0
            ? _customers.FirstOrDefault(c => DigitsOnly(c.Phone) == phoneDigits)
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
    public static bool Matches(Customer c, string query)
    {
        var q = query.Trim().ToLowerInvariant();
        if (q.Length == 0)
            return true;
        var qDigits = DigitsOnly(q);
        return c.Name.ToLowerInvariant().Contains(q)
            || c.Address.ToLowerInvariant().Contains(q)
            || c.Area.ToLowerInvariant().Contains(q)
            || (qDigits.Length >= 3 && DigitsOnly(c.Phone).Contains(qDigits));
    }

    /// <summary>Αναζήτηση για το autocomplete στις παραγγελίες (μέχρι 6 αποτελέσματα).</summary>
    public IReadOnlyList<Customer> Search(string query)
    {
        if (query.Trim().Length == 0)
            return [];
        return _customers.Where(c => Matches(c, query)).Take(6).ToList();
    }

    /// <summary>Χρησιμοποιείται από το endpoint συγχρονισμού μνήμης (host) όταν το δεύτερο ταμείο
    /// στέλνει υπενθύμιση πελάτη — ίδιο ταίριασμα με το FindOrCreate (τηλέφωνο, αλλιώς όνομα+διεύθυνση).</summary>
    public Customer? Find(string name, string phone, string address)
    {
        var phoneDigits = DigitsOnly(phone);
        return phoneDigits.Length > 0
            ? _customers.FirstOrDefault(c => DigitsOnly(c.Phone) == phoneDigits)
            : _customers.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Address, address, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Βρίσκει πελάτη με ακριβές τηλέφωνο — για την αναγνώριση κλήσεων (βλ. IncomingCallService).
    /// Συγκρίνει τα τελευταία 10 ψηφία, ώστε να ταιριάζει είτε το τηλεφωνικό κέντρο στέλνει τον
    /// αριθμό με 0, με κωδικό χώρας (+30) ή χωρίς.
    /// </summary>
    public Customer? FindByPhone(string phone)
    {
        var digits = DigitsOnly(phone);
        if (digits.Length < 6)
            return null;
        var tail = digits.Length > 10 ? digits[^10..] : digits;
        return _customers.FirstOrDefault(c =>
        {
            var cDigits = DigitsOnly(c.Phone);
            if (cDigits.Length == 0)
                return false;
            var cTail = cDigits.Length > 10 ? cDigits[^10..] : cDigits;
            return cTail == tail;
        });
    }
}
