using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.App.Services;
using PittaPos.Core.Models;

namespace PittaPos.App.ViewModels;

/// <summary>Μία γραμμή παραγγελίας μέσα σε ένα τραπέζι — κλικ για επιλογή, μετά «ΠΛΗΡΩΜΕΝΟ» για εξόφληση.</summary>
public partial class TableLineViewModel : ObservableObject
{
    public required int OrderNumber { get; init; }
    public required int LineIndex { get; init; }

    /// <summary>Ποιο τεμάχιο της γραμμής είναι (0-based). Μια γραμμή με ποσότητα 3 εμφανίζεται σαν ΤΡΕΙΣ
    /// ξεχωριστές σειρές, ώστε να μπορεί ο ένας από την παρέα να πληρώσει μόνο τη δική του — με μία
    /// σειρά «3 × ΠΙΤΤΑ» η εξόφληση θα σήμαινε αναγκαστικά και τα τρία.</summary>
    public required int Unit { get; init; }

    public required string QtyNameLabel { get; init; }
    public required string RevenueLabel { get; init; }
    public required decimal Revenue { get; init; }
    public required string Details { get; init; }
    public bool HasDetails => Details.Length > 0;

    [ObservableProperty]
    private bool _isSettled;

    /// <summary>Επιλεγμένη με κλικ — μπορούν να είναι επιλεγμένα πολλά προϊόντα μαζί (π.χ. όλα όσα πήρε μία παρέα).</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Παράμετρος για το CancelRoundCommand — ποια παραγγελία και ποιος την ακυρώνει.</summary>
public record CancelRoundRequest(int OrderNumber, string CancelledBy);

/// <summary>Ένας γύρος (μία υποβληθείσα παραγγελία) μέσα στο τραπέζι.</summary>
public class TableRoundViewModel
{
    public required int OrderNumber { get; init; }
    public required string TimeLabel { get; init; }
    public required ObservableCollection<TableLineViewModel> Lines { get; init; }
    public string Note { get; init; } = "";
    public bool HasNote => Note.Length > 0;
}

/// <summary>
/// Λεπτομέρειες ανοιχτού τραπεζιού στο ταμείο — τι έχει παραγγελθεί ανά γύρο, με δυνατότητα να
/// σημειωθεί ξεχωριστά πληρωμένο κάθε προϊόν (π.χ. πλήρωσε μόνο ένας από την παρέα), ίδια λογική
/// με την οθόνη του κινητού σερβιτόρου (βλ. TableSettlementService, WaiterApiService).
/// </summary>
public partial class TableDetailViewModel : ObservableObject
{
    private readonly int _table;
    private readonly SalesStatsService _stats = SalesStatsService.Instance;
    private readonly TableSettlementService _settlement = TableSettlementService.Instance;

    public TableDetailViewModel(int table)
    {
        _table = table;
        _stats.Changed += Refresh;
        TableStatusService.Instance.Changed += Refresh;
        // Και οι εξοφλήσεις: στο ΔΕΥΤΕΡΟ ταμείο το Settle φεύγει ασύγχρονα στο κύριο και η αληθινή
        // κατάσταση γυρίζει λίγο αργότερα — χωρίς αυτή τη σύνδεση, το προϊόν δεν φαινόταν πληρωμένο
        // μέχρι να τύχει κάποια άλλη ανανέωση.
        _settlement.Changed += Refresh;
        Refresh();
    }

    /// <summary>Αποσύνδεση από τα services όταν κλείσει το παράθυρο.</summary>
    public void Detach()
    {
        _stats.Changed -= Refresh;
        TableStatusService.Instance.Changed -= Refresh;
        _settlement.Changed -= Refresh;
    }

    public int TableNumber => _table;
    public string TitleLabel => "ΤΡΑΠΕΖΙ " + _table;

    public ObservableCollection<TableRoundViewModel> Rounds { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutstandingTotalLabel))]
    private decimal _outstandingTotal;

    public string OutstandingTotalLabel => Order.FormatPrice(OutstandingTotal);
    public bool NoRounds => Rounds.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(NoSelection))]
    private int _selectedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTotalLabel))]
    private decimal _selectedTotal;

    public string SelectedTotalLabel => Order.FormatPrice(SelectedTotal);
    public bool HasSelection => SelectedCount > 0;
    public bool NoSelection => SelectedCount == 0;

    /// <summary>Ζητά από το παράθυρο να ξεκινήσει νέο γύρο παραγγελίας γι' αυτό το τραπέζι.</summary>
    public event Action? NewRoundRequested;

    /// <summary>Το τραπέζι έκλεισε (ελευθερώθηκε) — το παράθυρο πρέπει να κλείσει.</summary>
    public event Action? TableClosed;

    [RelayCommand]
    private void RequestNewRound() => NewRoundRequested?.Invoke();

    /// <summary>Κλικ πάνω σε προϊόν — toggle επιλογής· μπορούν να μείνουν επιλεγμένα πολλά μαζί.</summary>
    [RelayCommand]
    private void SelectLine(TableLineViewModel line)
    {
        if (line.IsSettled)
            return;
        line.IsSelected = !line.IsSelected;
        RecomputeSelection();
    }

    /// <summary>Εξοφλεί μαζί όλα τα επιλεγμένα προϊόντα (π.χ. ό,τι πήρε ένας από την παρέα), σημειώνοντας
    /// και τον τρόπο πληρωμής — ώστε να μετρήσει στον διαχωρισμό μετρητά/κάρτα της αναφοράς ημέρας.</summary>
    [RelayCommand]
    private void SettleSelectedCash() => ConfirmSettleSelected(PaymentMethod.Cash);

    [RelayCommand]
    private void SettleSelectedCard() => ConfirmSettleSelected(PaymentMethod.Card);

    private void ConfirmSettleSelected(PaymentMethod method)
    {
        // ToList ΠΡΙΝ τον βρόχο: το Settle σηκώνει Changed, που ξαναχτίζει τα Rounds (βλ. Refresh) —
        // χωρίς στιγμιότυπο, ο βρόχος διέτρεχε λίστα που άλλαζε από κάτω του και έσκαγε
        // («Collection was modified») στο δεύτερο επιλεγμένο προϊόν.
        foreach (var l in Rounds.SelectMany(r => r.Lines).Where(l => l.IsSelected).ToList())
            _settlement.Settle(_table, l.OrderNumber, l.LineIndex, l.Unit, method, l.Revenue);
        Refresh();
        AutoCloseIfNothingOwed();
    }

    /// <summary>
    /// Πραγματική ακύρωση (διαγραφή) των επιλεγμένων προϊόντων — αφαιρούνται και από τον τζίρο,
    /// καταγράφονται στο ιστορικό ακυρωμένων. Μόνο με ονομαστικό κωδικό (βλ. code-behind του παραθύρου).
    /// </summary>
    [RelayCommand]
    private void CancelSelected(string cancelledBy)
    {
        // Ίδιο instant για όλες — ώστε το Ιστορικό να τις ομαδοποιεί σαν μία ενέργεια ακύρωσης.
        var cancelledAt = DateTime.Now;
        // Φθίνουσα σειρά ανά γύρο ώστε η διαγραφή μιας γραμμής να μην αλλάξει τον δείκτη των επόμενων προς διαγραφή.
        // ToList για τον ίδιο λόγο με το ConfirmSettleSelected: το RemoveLine ξαναχτίζει τα Rounds.
        // (Το OrderByDescending τυχαίνει να κάνει ήδη buffer, αλλά δεν θέλουμε να βασιζόμαστε σε αυτό.)
        // Distinct ανά (παραγγελία, γραμμή): οι σειρές είναι πλέον ΤΕΜΑΧΙΑ, αλλά η ακύρωση αφαιρεί
        // ολόκληρη τη γραμμή. Χωρίς αυτό, επιλέγοντας 3 τεμάχια της ίδιας γραμμής θα καλούνταν τρεις
        // φορές η διαγραφή και θα έσβηνε ΤΡΕΙΣ ΔΙΑΦΟΡΕΤΙΚΕΣ γραμμές του γύρου.
        var toRemove = Rounds.SelectMany(r => r.Lines)
            .Where(l => l.IsSelected)
            .Select(l => (l.OrderNumber, l.LineIndex))
            .Distinct()
            .OrderByDescending(x => x.LineIndex)
            .ToList();
        foreach (var (orderNumber, lineIndex) in toRemove)
        {
            _settlement.ShiftAfterRemoval(_table, orderNumber, lineIndex);
            _stats.RemoveLine(orderNumber, lineIndex, cancelledBy, cancelledAt);
        }
        AutoCloseIfNothingOwed();
    }

    /// <summary>Πραγματική ακύρωση (διαγραφή) ολόκληρου γύρου — π.χ. λάθος παραγγελία. Μόνο με ονομαστικό κωδικό.</summary>
    [RelayCommand]
    private void CancelRound(CancelRoundRequest request)
    {
        _settlement.ClearOrder(_table, request.OrderNumber);
        _stats.RemoveOrder(request.OrderNumber, request.CancelledBy);
        AutoCloseIfNothingOwed();
    }

    [RelayCommand]
    private void CloseTableCash() => CloseTableWith(PaymentMethod.Cash);

    [RelayCommand]
    private void CloseTableCard() => CloseTableWith(PaymentMethod.Card);

    /// <summary>
    /// Κλείνει το τραπέζι σημειώνοντας ό,τι έχει μείνει ανεξόφλητο ως πληρωμένο με τον δοσμένο τρόπο.
    /// Οι ήδη εξοφλημένες γραμμές δεν ξαναχρεώνονται — κρατούν τον δικό τους τρόπο πληρωμής (π.χ. ένας
    /// πλήρωσε νωρίτερα με κάρτα, οι υπόλοιποι στο τέλος μετρητά).
    /// </summary>
    private void CloseTableWith(PaymentMethod method)
    {
        foreach (var l in Rounds.SelectMany(r => r.Lines).Where(l => !l.IsSettled).ToList())
            _settlement.Settle(_table, l.OrderNumber, l.LineIndex, l.Unit, method, l.Revenue);
        TableStatusService.Instance.MarkClosed(_table);
    }

    /// <summary>
    /// Αν μηδενίστηκε το οφειλόμενο (όλα εξοφλήθηκαν ή ακυρώθηκαν), κλείνει αυτόματα το τραπέζι —
    /// αλλιώς έμενε «ανοιχτό» με €0,00, κάτι που μοιάζει σαν σφάλμα στην κάτοψη.
    /// </summary>
    private void AutoCloseIfNothingOwed()
    {
        if (OutstandingTotal == 0 && TableStatusService.Instance.OpenSince.ContainsKey(_table))
            TableStatusService.Instance.MarkClosed(_table);
    }

    private void RecomputeSelection()
    {
        var selected = Rounds.SelectMany(r => r.Lines).Where(l => l.IsSelected).ToList();
        SelectedCount = selected.Count;
        SelectedTotal = selected.Sum(l => l.Revenue);
    }

    private void Refresh()
    {
        // Ό,τι αλλαγή στις παραγγελίες (έστω και αλλού) ξαναχτίζει τις γραμμές από την αρχή —
        // κρατάμε ποιες ήταν επιλεγμένες πριν, ώστε να μη χάνεται η επιλογή του χρήστη ενδιάμεσα.
        var selectedKeys = Rounds.SelectMany(r => r.Lines)
            .Where(l => l.IsSelected)
            .Select(l => (l.OrderNumber, l.LineIndex, l.Unit))
            .ToHashSet();

        if (!TableStatusService.Instance.OpenSince.TryGetValue(_table, out var since))
        {
            Rounds.Clear();
            OutstandingTotal = 0;
            OnPropertyChanged(nameof(NoRounds));
            RecomputeSelection();
            TableClosed?.Invoke();
            return;
        }

        var orders = _stats.Orders
            .Where(o => o.Type == OrderType.Table && o.Who == "Τραπέζι " + _table && o.PlacedAt >= since)
            .OrderBy(o => o.PlacedAt)
            .ToList();

        Rounds.Clear();
        var outstanding = 0m;
        foreach (var o in orders)
        {
            var lines = new ObservableCollection<TableLineViewModel>();
            for (var i = 0; i < o.Lines.Count; i++)
            {
                var l = o.Lines[i];
                // Ένα τεμάχιο = μία σειρά. Έτσι μπορεί ο καθένας από την παρέα να πληρώσει ακριβώς ό,τι
                // πήρε, ακόμα κι όταν πάρθηκαν πολλά ίδια μαζί.
                var units = Math.Max(1, l.Quantity);
                var unitPrice = l.Revenue / units;
                for (var u = 0; u < units; u++)
                {
                    var settled = _settlement.IsSettled(_table, o.OrderNumber, i, u);
                    if (!settled)
                        outstanding += unitPrice;
                    lines.Add(new TableLineViewModel
                    {
                        OrderNumber = o.OrderNumber,
                        LineIndex = i,
                        Unit = u,
                        QtyNameLabel = l.Name,
                        RevenueLabel = Order.FormatPrice(unitPrice),
                        Revenue = unitPrice,
                        Details = l.Details,
                        IsSettled = settled,
                        IsSelected = !settled && selectedKeys.Contains((o.OrderNumber, i, u)),
                    });
                }
            }
            Rounds.Add(new TableRoundViewModel { OrderNumber = o.OrderNumber, TimeLabel = o.TimeLabel, Lines = lines, Note = o.Note });
        }
        OutstandingTotal = outstanding;
        OnPropertyChanged(nameof(NoRounds));
        RecomputeSelection();
    }
}
