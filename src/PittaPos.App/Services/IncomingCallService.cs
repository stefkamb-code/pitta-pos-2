using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>
/// Μία κλήση στην ουρά αναμονής, όπως τη βλέπει η οθόνη — το πλήρες Customer (διεύθυνση, ιστορικό
/// κ.λπ.) αναζητείται τοπικά μέσω <see cref="CustomerStore"/> από το τηλέφωνο, όχι μέσω δικτύου, γιατί
/// ο CustomerStore είναι ήδη συγχρονισμένος στο δεύτερο ταμείο (client) με τον ίδιο μηχανισμό.
/// Το <see cref="IsMinimized"/> είναι καθαρά τοπική προτίμηση αυτής της οθόνης — δεν συγχρονίζεται
/// στο άλλο ταμείο, ώστε κάθε ταμίας να ελαχιστοποιεί ό,τι τον εμποδίζει χωρίς να πειράζει τον άλλον.
/// </summary>
public partial class IncomingCallItem : ObservableObject
{
    public required int Id { get; init; }
    public required string Phone { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionLabel))]
    [NotifyPropertyChangedFor(nameof(HasQueuePosition))]
    private int _position;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpanded))]
    private bool _isMinimized;

    public bool IsExpanded => !IsMinimized;

    [RelayCommand]
    private void ToggleMinimized() => IsMinimized = !IsMinimized;

    public bool HasQueuePosition => Position > 1;

    private Customer? Customer => CustomerStore.Instance.FindByPhone(Phone);

    public string DisplayName => Customer?.Name ?? "Άγνωστος αριθμός";

    public string DisplayDetail => Customer is { } c
        ? string.Join(" · ", new[] { c.Address, c.Area }.Where(s => s.Length > 0))
            + (c.OtherAddresses.Count > 0 ? "  ·  +" + c.OtherAddresses.Count + " ακόμη" : "")
        : Phone;

    public string PositionLabel => Position <= 1 ? "" : $"#{Position} ΣΤΗ ΣΕΙΡΑ";
}

/// <summary>
/// Ουρά εισερχόμενων κλήσεων, στέλνεται από το τηλεφωνικό κέντρο (Grandstream UCM, Event
/// Notification) μέσω του endpoint <c>/api/incoming-call</c> στο WaiterApiService — πάντα στο
/// κύριο ταμείο (host), αφού εκεί δείχνει η ρύθμιση του UCM. Αν χτυπήσουν πάνω από μία γραμμές πριν
/// προλάβει ο ταμίας να διαβάσει την προηγούμενη ειδοποίηση, καμία δεν χάνεται — μπαίνουν σε ουρά
/// FIFO με αριθμό σειράς. Στο δεύτερο ταμείο (client) η ίδια ουρά φαίνεται μέσω <see cref="RemoteSync"/>
/// (ίδιο μοτίβο με τραπέζια/παραγγελίες/πελάτες) — όποιο ταμείο κι αν κλείσει/αναλάβει μια κλήση,
/// εξαφανίζεται και από τα δύο.
/// </summary>
public partial class IncomingCallService : ObservableObject
{
    public static IncomingCallService Instance { get; } = new();

    /// <summary>Το UCM στέλνει συχνά πάνω από ένα event ανά κλήση (ringing/answer) — αγνόησε τα διπλά.</summary>
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromSeconds(45);
    private readonly List<(string Digits, DateTime At)> _recentlySeen = [];

    /// <summary>Η πραγματική ουρά — έχει νόημα μόνο στο host· το client τη βλέπει μέσω <see cref="Calls"/>.</summary>
    /// <summary>Πόσες κλήσεις κρατάει η λίστα της αρχικής. Δώδεκα κάρτες είναι ήδη περισσότερες απ' όσες
    /// κοιτάει κανείς· πιο πάνω απλώς σκεπάζουν την οθόνη.</summary>
    private const int MaxQueue = 12;

    private readonly List<(int Id, string Phone)> _queue = [];
    private int _nextId = 1;

    [ObservableProperty]
    private bool _isActive;

    public ObservableCollection<IncomingCallItem> Calls { get; } = [];

    private IncomingCallService()
    {
        if (RemoteSync.IsClient)
            RemoteSync.StartPolling(TimeSpan.FromSeconds(2), RefreshFromHostAsync);
    }

    /// <summary>Καλείται όταν χτυπάει το τηλέφωνο — μόνο στο κύριο ταμείο φτάνει ποτέ αυτό το request.</summary>
    public void ReportRinging(string number)
    {
        if (RemoteSync.IsClient)
            return;

        var digits = new string(number.Where(char.IsDigit).ToArray());
        if (digits.Length < 6)
            return;

        var now = DateTime.Now;
        _recentlySeen.RemoveAll(r => now - r.At > DedupeWindow);
        if (_recentlySeen.Any(r => r.Digits == digits))
            return;
        _recentlySeen.Add((digits, now));

        _queue.Add((_nextId++, digits));
        // Η ουρά δεν αδειάζει μόνη της: μια κάρτα φεύγει μόνο όταν πατηθεί, ή όταν η κλήση γίνει
        // παραγγελία. Λάθος νούμερα, κλεισίματα και «πόσο κάνει η πίτα;» μένουν εκεί όλη τη βραδιά και
        // στοιβάζονται στην αρχική. Κρατάμε τις πιο πρόσφατες — αυτές έχει νόημα να πάρει κανείς πίσω.
        while (_queue.Count > MaxQueue)
        {
            AppLog.Write("calls", $"Η λίστα κλήσεων γέμισε ({MaxQueue}) — έφυγε η παλαιότερη: {_queue[0].Phone}");
            _queue.RemoveAt(0);
        }
        ApplyEntries(Snapshot());
    }

    /// <summary>Κλείνει μια συγκεκριμένη κλήση από την ουρά — από όποιο ταμείο κι αν πατήθηκε.</summary>
    public void Dismiss(int id)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync(id);
            return;
        }
        _queue.RemoveAll(c => c.Id == id);
        ApplyEntries(Snapshot());
    }

    /// <summary>Στιγμιότυπο της ουράς για το API sync — καλείται πάνω στο host.</summary>
    public List<IncomingCallEntry> Snapshot() =>
        _queue.Select((c, i) => new IncomingCallEntry(c.Id, i + 1, c.Phone)).ToList();

    private async Task SyncThenRefreshAsync(int id)
    {
        await RemoteSync.PostAsync("/api/sync/incoming-calls/dismiss", new { Id = id });
        await RefreshFromHostAsync();
    }

    private async Task RefreshFromHostAsync()
    {
        var data = await RemoteSync.GetAsync<List<IncomingCallEntry>>("/api/sync/incoming-calls");
        if (data is not null)
            ApplyEntries(data);
    }

    /// <summary>Ενημερώνει τη λίστα εμφάνισης χωρίς να ξαναφτιάχνει (και άρα να ξανα-animate-άρει)
    /// κάρτες που ήδη υπάρχουν — μόνο θέση/σειρά ενημερώνεται σε αυτές.</summary>
    private void ApplyEntries(List<IncomingCallEntry> entries)
    {
        var incomingIds = entries.Select(e => e.Id).ToHashSet();
        for (var i = Calls.Count - 1; i >= 0; i--)
            if (!incomingIds.Contains(Calls[i].Id))
                Calls.RemoveAt(i);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var existing = Calls.FirstOrDefault(c => c.Id == entry.Id);
            if (existing is null)
                Calls.Insert(Math.Min(i, Calls.Count), new IncomingCallItem { Id = entry.Id, Phone = entry.Phone, Position = entry.Position });
            else
                existing.Position = entry.Position;
        }

        IsActive = Calls.Count > 0;
    }
}
