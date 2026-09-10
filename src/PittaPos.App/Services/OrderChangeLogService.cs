using System.IO;
using System.Text.Json;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>
/// Μία διόρθωση από το Ιστορικό πάνω σε ήδη χτυπημένη παραγγελία — τρόπος πληρωμής ή κανάλι. Γράφεται
/// με το όνομα του υπαλλήλου που την έκανε (ονομαστικός κωδικός, ο ίδιος με της ακύρωσης) και βγαίνει
/// στην αναφορά ημέρας κάτω από τις ακυρώσεις (βλ. DayReportService.AppendChanges).
///
/// <para>Γιατί υπάρχει: ένα «μετρητά → κάρτα» βγάζει λεφτά από το συρτάρι χωρίς να αλλάξει ο τζίρος, και
/// ένα «ΔΙΑΝΟΜΗ → e-food» τα βγάζει εντελώς από τα μετρητά/κάρτα. Πριν γινόταν από όποιον είχε ανοίξει το
/// Ιστορικό και δεν άφηνε κανένα ίχνος.</para>
/// </summary>
/// <param name="Label">Η παραγγελία όπως τη λέει το μαγαζί: «#12 ΔΙΑΝΟΜΗ», «ΤΡΑΠΕΖΙ 4 · ΑΤΟΜΟ Β».
/// Γράφεται τη στιγμή της αλλαγής — μετά, μια παραγγελία που άλλαξε κανάλι λέγεται αλλιώς.</param>
/// <param name="From">Τι ήταν: «Μετρητά», «ΟΡΘΙΟΣ», «e-food», «—» όταν δεν είχε τίποτα.</param>
/// <param name="To">Τι έγινε.</param>
/// <param name="Amount">Πόσα λεφτά άλλαξαν θέση: το σύνολο της παραγγελίας, και για άτομο τραπεζιού η
/// είσπραξή του.</param>
public sealed record OrderChange(int OrderNumber, string Label, string From, string To, decimal Amount,
    DateTime ChangedAt, string ChangedBy)
{
    public string ChangedByLabel => ChangedBy.Length > 0 ? ChangedBy : "—";

    /// <summary>«21:05 · #12 ΔΙΑΝΟΜΗ · Μετρητά → Κάρτα · €15,50» — μία γραμμή της αναφοράς.</summary>
    public string Text => $"{ChangedAt:HH:mm} · {Label} · {From} → {To} · {Order.FormatPrice(Amount)}";
}

/// <summary>
/// Οι διορθώσεις της ημέρας από το Ιστορικό. Ίδια συμπεριφορά με το <see cref="CancellationLogService"/>:
/// γράφει ΜΟΝΟ το κύριο ταμείο, το δεύτερο τις διαβάζει από εκεί, και καθαρίζουν στο κλείσιμο ημέρας.
/// </summary>
public class OrderChangeLogService
{
    public static OrderChangeLogService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly List<OrderChange> _entries = [];

    /// <summary>Με τη σειρά που έγιναν.</summary>
    public IReadOnlyList<OrderChange> Entries => _entries;

    private OrderChangeLogService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "order-changes.json");
        if (RemoteSync.IsClient)
            RemoteSync.StartPolling(TimeSpan.FromSeconds(5), RefreshFromHostAsync);
        else
            Load();
    }

    /// <summary>Δεύτερο ταμείο: οι αλλαγές γράφονται στο κύριο (βλ. SalesStatsService.CorrectPaymentMethod),
    /// οπότε η αναφορά που φτιάχνεται εδώ τις παίρνει από εκεί.</summary>
    private async Task RefreshFromHostAsync()
    {
        var entries = await RemoteSync.GetAsync<List<OrderChange>>("/api/sync/order-changes");
        if (entries is null)
            return;
        _entries.Clear();
        _entries.AddRange(entries);
    }

    public void Log(OrderChange change)
    {
        _entries.Add(change);
        Save();
    }

    /// <summary>Σβήνει τις αλλαγές ΜΙΑΣ μέρας — όταν αρχειοθετείται μόνη της μια παλιά μέρα μέσα στη
    /// βάρδια (βλ. DayReportService.ArchiveStaleDaysOnly), ίδια λογική με τις ακυρώσεις.</summary>
    public void RemoveForDay(DateTime businessDay)
    {
        if (_entries.RemoveAll(c => SalesStatsService.BusinessDay(c.ChangedAt) == businessDay) > 0)
            Save();
    }

    /// <summary>Καθαρίζει με το κλείσιμο ημέρας, αφού μπουν στην αναφορά.</summary>
    public void Clear()
    {
        if (_entries.Count == 0)
            return;
        _entries.Clear();
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            _entries.AddRange(JsonSerializer.Deserialize<List<OrderChange>>(File.ReadAllText(_path)) ?? []);
        }
        catch (Exception)
        {
            // Χαλασμένο αρχείο — ξεκίνα άδειο αντί να ρίξεις την εφαρμογή
        }
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_entries, JsonOpts));
        }
        catch (Exception ex)
        {
            AppLog.Write("save", $"Δεν γράφτηκε το «{Path.GetFileName(_path)}»: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
