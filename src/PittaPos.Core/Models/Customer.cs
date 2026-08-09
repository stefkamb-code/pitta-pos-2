namespace PittaPos.Core.Models;

/// <summary>Μια πρόσθετη αποθηκευμένη διεύθυνση πελάτη πέρα από την κύρια (π.χ. δουλειά, φίλος) —
/// βλ. Customer.OtherAddresses. Ξεχωριστό όνομα ανά πεδίο από το Customer ώστε να μπορεί να
/// αντιγραφεί κατευθείαν στα ίδια πεδία φόρμας παραγγελίας.</summary>
public class CustomerAddress
{
    /// <summary>Σύντομη περιγραφή για το picker — π.χ. η ίδια η περιοχή, ή κάτι σαν «Δουλειά».</summary>
    public string Label { get; set; } = "";
    public string Address { get; set; } = "";
    public string StreetNumber { get; set; } = "";
    public string Area { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Floor { get; set; } = "";
}

public class Customer
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    /// <summary>Αριθμός οδού — ξεχωριστό πεδίο από τη Διεύθυνση, ώστε ο Χάρτης Διανομής να εντοπίζει ακριβώς το σημείο.</summary>
    public string StreetNumber { get; set; } = "";
    public string Area { get; set; } = "";
    /// <summary>Ταχυδρομικός κώδικας — βοηθάει τη γεωκωδικοποίηση όταν η περιοχή έχει κοινό όνομα δρόμου.</summary>
    public string PostalCode { get; set; } = "";
    public string Floor { get; set; } = "";
    /// <summary>Πρόσθετες διευθύνσεις πέρα από την παραπάνω κύρια (π.χ. δουλειά, προσωρινή σε φίλο) —
    /// δεν αντικαθιστούν ποτέ αυτόματα την κύρια, μόνο προστίθενται όταν εμφανιστεί νέα διεύθυνση σε
    /// παραγγελία (βλ. CustomerStore.FindOrCreate). Ο ταμίας διαλέγει ανάμεσά τους στο Βήμα 2.</summary>
    public List<CustomerAddress> OtherAddresses { get; set; } = [];
    /// <summary>Σχόλια διανομής — π.χ. «θέλει αναπάντητη».</summary>
    public string Notes { get; set; } = "";
    /// <summary>Μόνιμη υπενθύμιση που εμφανίζεται όταν ο πελάτης περνάει παραγγελία
    /// (π.χ. «του χρωστάμε ένα γεύμα δώρο»).</summary>
    public string Memo { get; set; } = "";

    // ---- Ιστορικό ζωής (μόνιμο, επιβιώνει του κλεισίματος ημέρας) ----
    /// <summary>Σύνολο ολοκληρωμένων παραγγελιών.</summary>
    public int OrderCount { get; set; }
    /// <summary>Συνολικός τζίρος από τον πελάτη.</summary>
    public decimal TotalRevenue { get; set; }
    /// <summary>Προϊόν → πόσες φορές το έχει παραγγείλει.</summary>
    public Dictionary<string, int> ProductCounts { get; set; } = [];
    public DateTime? FirstOrderAt { get; set; }
    public DateTime? LastOrderAt { get; set; }
}
