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

/// <summary>
/// Ένα άτομο του τραπεζιού με ό,τι πήρε — <b>μία ομάδα = μία απόδειξη</b> στην ταμειακή του μαγαζιού.
/// Το «ΑΧΡΕΩΤΑ» (Person = -1) μαζεύει ό,τι δεν χρεώθηκε σε κανέναν: παραγγελία γραμμένη από το ταμείο
/// πριν μπουν τα άτομα, ή κοινό πιάτο. Φαίνεται ξεχωριστά ώστε να μην ξεφύγει από την καταμέτρηση.
/// </summary>
public partial class TablePersonGroupViewModel : ObservableObject
{
    public required int Person { get; init; }
    public required string Label { get; init; }
    public required ObservableCollection<TableLineViewModel> Lines { get; init; }
    /// <summary>Πόσα οφείλει ακόμα — αυτό είναι και το ποσό που θα πληκτρολογηθεί στην ταμειακή.</summary>
    public required decimal Outstanding { get; init; }

    /// <summary>Διαλεγμένος με κλικ πάνω στην κάρτα του — μαρκάρει ΟΛΑ του τα απλήρωτα προϊόντα, ώστε η
    /// κάτω μπάρα να δείχνει «Ν ΕΠΙΛΕΓΜΕΝΑ» και να πληρωθούν μαζί. Ίδια χειρονομία με το κινητό.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public string OutstandingLabel => Order.FormatPrice(Outstanding);
    public bool CanSettle => Outstanding > 0;
    /// <summary>Τα «ΑΧΡΕΩΤΑ» δεν είναι άτομο — δεν έχει πού να προστεθεί κάτι.</summary>
    public bool CanAddMore => Person >= 0;
    /// <summary>Η απόδειξή του έχει ήδη κοπεί (όλα του πληρωμένα).</summary>
    public bool IsPaid => Outstanding == 0 && Lines.Count > 0;
}

/// <summary>Παράμετρος για το CancelRoundCommand — ποια παραγγελία και ποιος την ακυρώνει.</summary>
public record CancelRoundRequest(int OrderNumber, string CancelledBy);

/// <summary>Ποια είσπραξη περιμένει το «μετρητά ή κάρτα» (βλ. TableDetailViewModel.PendingPayment).</summary>
public enum PendingPaymentKind
{
    /// <summary>Όσα προϊόντα έχει επιλέξει ο ταμίας — ένα-ένα ή όλο το άτομο με κλικ στην κάρτα του.</summary>
    Selected,
    /// <summary>Ό,τι έχει μείνει ανεξόφλητο, με κλείσιμο του τραπεζιού.</summary>
    CloseTable,
}

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

    /// <summary>Το τραπέζι χωρισμένο σε άτομα — μία ομάδα ανά απόδειξη. Άδειο όταν πληρώνουν μαζί.</summary>
    public ObservableCollection<TablePersonGroupViewModel> Persons { get; } = [];

    public bool HasPersons => Persons.Count > 0;

    /// <summary>Η παλιά λίστα ανά γύρο εμφανίζεται ΜΟΝΟ όταν δεν υπάρχει χωρισμός σε άτομα — αλλιώς
    /// τα ίδια προϊόντα θα φαίνονταν δύο φορές στην ίδια οθόνη.</summary>
    public bool ShowRounds => !HasPersons;

    /// <summary>«ΘΑ ΚΟΠΟΥΝ 4 ΑΠΟΔΕΙΞΕΙΣ · 2 κομμένες» — το ζητούμενο με μία ματιά.</summary>
    public string ReceiptsHeader
    {
        get
        {
            var withItems = Persons.Where(p => p.Lines.Count > 0).ToList();
            var paid = withItems.Count(p => p.IsPaid);
            var word = withItems.Count == 1 ? "ΑΠΟΔΕΙΞΗ" : "ΑΠΟΔΕΙΞΕΙΣ";
            return paid == 0
                ? $"ΘΑ ΚΟΠΟΥΝ {withItems.Count} {word}"
                : $"{withItems.Count} {word} · {paid} κομμένες";
        }
    }

    /// <summary>
    /// Ποια είσπραξη περιμένει να δηλωθεί «μετρητά ή κάρτα» — <c>null</c> όσο δεν ρωτάμε τίποτα.
    ///
    /// Ένα κουμπί «πληρωμή» και μετά η ερώτηση, αντί για ζευγάρι 💶/💳 σε κάθε άτομο και σε κάθε
    /// ενέργεια: με τέσσερα άτομα στο τραπέζι η οθόνη γέμιζε κουμπιά και το μάτι δεν έβρισκε τίποτα.
    /// Ίδια ροή με το κινητό (βλ. TableDetailScreen.PaymentBar), ώστε να μαθαίνεται μία φορά.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosingPayment))]
    [NotifyPropertyChangedFor(nameof(PendingPaymentTitle))]
    [NotifyPropertyChangedFor(nameof(PendingPaymentAmountLabel))]
    private PendingPaymentKind? _pendingPayment;

    public bool IsChoosingPayment => PendingPayment is not null;

    public string PendingPaymentTitle => PendingPayment == PendingPaymentKind.CloseTable
        ? "ΠΩΣ ΠΛΗΡΩΘΗΚΕ ΤΟ ΥΠΟΛΟΙΠΟ;"
        : "ΠΩΣ ΠΛΗΡΩΘΗΚΕ;";

    public string PendingPaymentAmountLabel => Order.FormatPrice(
        PendingPayment == PendingPaymentKind.CloseTable ? OutstandingTotal : SelectedTotal);

    [RelayCommand]
    private void RequestSettleSelected() => PendingPayment = PendingPaymentKind.Selected;

    [RelayCommand]
    private void RequestCloseTable() => PendingPayment = PendingPaymentKind.CloseTable;

    [RelayCommand]
    private void CancelPayment() => PendingPayment = null;

    [RelayCommand]
    private void PayCash() => Pay(PaymentMethod.Cash);

    [RelayCommand]
    private void PayCard() => Pay(PaymentMethod.Card);

    /// <summary>Η απάντηση στο «πώς πληρώθηκε;» — εκτελεί την είσπραξη που περίμενε.</summary>
    private void Pay(PaymentMethod method)
    {
        var kind = PendingPayment;
        // Καθαρίζει ΠΡΩΤΑ: η ίδια η είσπραξη ξαναχτίζει τις λίστες (Refresh) και μπορεί να κλείσει το
        // τραπέζι, οπότε δεν πρέπει να μείνει η ερώτηση κρεμασμένη σε ό,τι έχει ήδη πληρωθεί.
        PendingPayment = null;
        switch (kind)
        {
            case PendingPaymentKind.Selected:
                ConfirmSettleSelected(method);
                break;
            case PendingPaymentKind.CloseTable:
                CloseTableWith(method);
                break;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutstandingTotalLabel))]
    private decimal _outstandingTotal;

    public string OutstandingTotalLabel => Order.FormatPrice(OutstandingTotal);
    public bool NoRounds => Rounds.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(NoSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedCountLabel))]
    [NotifyPropertyChangedFor(nameof(SettleSelectedLabel))]
    private int _selectedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTotalLabel))]
    private decimal _selectedTotal;

    public string SelectedTotalLabel => Order.FormatPrice(SelectedTotal);
    public bool HasSelection => SelectedCount > 0;
    public bool NoSelection => SelectedCount == 0;

    // Ενικός στο ένα προϊόν: «ΠΛΗΡΩΜΕΝΑ» με ένα επιλεγμένο διαβάζεται σαν να πληρώνονται πολλά.
    public string SelectedCountLabel => SelectedCount + (SelectedCount == 1 ? " ΕΠΙΛΕΓΜΕΝΟ" : " ΕΠΙΛΕΓΜΕΝΑ");
    public string SettleSelectedLabel => SelectedCount == 1 ? "✓ ΠΛΗΡΩΜΕΝΟ" : "✓ ΠΛΗΡΩΜΕΝΑ";

    /// <summary>Ζητά από το παράθυρο να ξεκινήσει νέο γύρο παραγγελίας γι' αυτό το τραπέζι. Η παράμετρος
    /// είναι σε ΠΟΙΟΝ γράφεται (0-based)· -1 = «όποιος δεν έχει παραγγείλει ακόμα», όπως πάντα.</summary>
    public event Action<int>? NewRoundRequested;

    /// <summary>Το τραπέζι έκλεισε (ελευθερώθηκε) — το παράθυρο πρέπει να κλείσει.</summary>
    public event Action? TableClosed;

    /// <summary>Σε τραπέζι χωρισμένο σε άτομα, η προσθήκη σημαίνει «ήρθε κι άλλος».</summary>
    public string NewRoundLabel => HasPersons ? "+ ΠΡΟΣΘΗΚΗ ΑΤΟΜΟΥ" : "+ ΠΡΟΣΘΗΚΗ";

    [RelayCommand]
    private void RequestNewRound()
    {
        // Νέο άτομο στην παρέα: παίρνει το επόμενο γράμμα και δική του απόδειξη. Η παραγγελιοληψία
        // ξεκινά μόνη της από αυτόν, γιατί είναι ο πρώτος που δεν έχει παραγγείλει ακόμα.
        if (HasPersons)
            TablePersonsService.Instance.SetCount(_table, TablePersonsService.Instance.CountFor(_table) + 1);
        NewRoundRequested?.Invoke(-1);
    }

    /// <summary>
    /// «Το ΑΤΟΜΟ Β θέλει και μια κόκα κόλα»: ανοίγει την παραγγελιοληψία κατευθείαν πάνω σε ΑΥΤΟ το
    /// άτομο, ώστε ό,τι γραφτεί να μπει στη δική του απόδειξη. Χωρίς αυτό, η μόνη προσθήκη σε
    /// ανοιχτό τραπέζι ήταν «+ ΠΡΟΣΘΗΚΗ ΑΤΟΜΟΥ», που έφτιαχνε ΝΕΟ άτομο και δεύτερη απόδειξη.
    /// </summary>
    [RelayCommand]
    private void AddToPerson(TablePersonGroupViewModel person) => NewRoundRequested?.Invoke(person.Person);

    /// <summary>
    /// Κλικ πάνω στην κάρτα ενός ατόμου — διαλέγει ΟΛΑ του τα απλήρωτα προϊόντα (δεύτερο κλικ τα
    /// ξεδιαλέγει). Από κει και πέρα η ροή είναι μία για όλα: «✓ ΠΛΗΡΩΜΕΝΟ» κάτω και μετά το
    /// παραθυράκι μετρητά/κάρτα — ακριβώς όπως στο κινητό, χωρίς ξεχωριστό κουμπί μέσα σε κάθε άτομο.
    /// </summary>
    [RelayCommand]
    private void TogglePerson(TablePersonGroupViewModel person)
    {
        var unpaid = person.Lines.Where(l => !l.IsSettled).ToList();
        if (unpaid.Count == 0)
            return;
        var select = unpaid.Any(l => !l.IsSelected);
        foreach (var l in unpaid)
            l.IsSelected = select;
        RecomputeSelection();
    }

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
            // Και οι χρεώσεις ανά άτομο μετακινούνται μαζί — αλλιώς «του Β» θα κολλούσε σε άλλο προϊόν.
            TablePersonsService.Instance.ShiftAfterRemoval(_table, orderNumber, lineIndex);
            _stats.RemoveLine(orderNumber, lineIndex, cancelledBy, cancelledAt);
        }
        AutoCloseIfNothingOwed();
    }

    /// <summary>Πραγματική ακύρωση (διαγραφή) ολόκληρου γύρου — π.χ. λάθος παραγγελία. Μόνο με ονομαστικό κωδικό.</summary>
    [RelayCommand]
    private void CancelRound(CancelRoundRequest request)
    {
        _settlement.ClearOrder(_table, request.OrderNumber);
        TablePersonsService.Instance.ClearOrder(_table, request.OrderNumber);
        _stats.RemoveOrder(request.OrderNumber, request.CancelledBy);
        AutoCloseIfNothingOwed();
    }

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
        // Η κάρτα του ατόμου ανάβει όταν είναι διαλεγμένα ΟΛΑ του τα απλήρωτα — και όταν ο ταμίας τα
        // διάλεξε ένα-ένα, όχι μόνο με κλικ πάνω στην κάρτα.
        foreach (var p in Persons)
        {
            var unpaid = p.Lines.Where(l => !l.IsSettled).ToList();
            p.IsSelected = unpaid.Count > 0 && unpaid.All(l => l.IsSelected);
        }
    }

    /// <summary>
    /// Ενώνει ΟΛΑ όσα έχουν παραγγελθεί σε αυτό το τραπέζι σε ένα δελτίο, για επανεκτύπωση — ίδια λογική
    /// με το χαρτί που βγαίνει όταν κλείνει η σειρά των ατόμων (βλ. WaiterApiService.MergeForPrinting):
    /// η κουζίνα θέλει ΕΝΑ χαρτί με όλο το τραπέζι, όχι ένα ανά γύρο.
    ///
    /// Επιστρέφει null όταν το τραπέζι είναι άδειο ή κλειστό.
    /// </summary>
    public CompletedOrder? BuildReprintTicket()
    {
        if (!TableStatusService.Instance.OpenSince.TryGetValue(_table, out var since))
            return null;

        var orders = _stats.Orders
            .Where(o => o.Type == OrderType.Table && o.Who == "Τραπέζι " + _table && o.PlacedAt >= since)
            .OrderBy(o => o.PlacedAt)
            .ToList();
        if (orders.Count == 0)
            return null;

        var last = orders[^1];
        return new CompletedOrder
        {
            OrderNumber = last.OrderNumber,
            Type = last.Type,
            Who = last.Who,
            Total = orders.Sum(o => o.Total),
            Lines = orders.SelectMany(o => o.Lines).ToList(),
            Note = string.Join(" · ", orders.Select(o => o.Note).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct()),
            IsEveningShift = last.IsEveningShift,
        };
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
        BuildPersonGroups();
        OnPropertyChanged(nameof(NoRounds));
        RecomputeSelection();
    }

    /// <summary>
    /// Ξαναχτίζει το «ΑΤΟΜΟ Α αυτά, ΑΤΟΜΟ Β αυτά». Οι ΙΔΙΕΣ γραμμές με τους γύρους — δεν αντιγράφονται,
    /// ώστε επιλογή και κατάσταση πληρωμής να είναι κοινές όπου κι αν τις δει ο ταμίας.
    /// </summary>
    private void BuildPersonGroups()
    {
        Persons.Clear();
        var svc = TablePersonsService.Instance;
        var all = Rounds.SelectMany(r => r.Lines).ToList();

        if (svc.IsSplit(_table) && all.Count > 0)
        {
            for (var p = 0; p < svc.CountFor(_table); p++)
            {
                var mine = all.Where(l => svc.PersonFor(_table, l.OrderNumber, l.LineIndex, l.Unit) == p).ToList();
                if (mine.Count == 0)
                    continue; // άτομο που δεν πήρε τίποτα δεν κόβει απόδειξη
                Persons.Add(new TablePersonGroupViewModel
                {
                    Person = p,
                    Label = "ΑΤΟΜΟ " + TablePersonsService.Label(p),
                    Lines = new ObservableCollection<TableLineViewModel>(mine),
                    Outstanding = mine.Where(l => !l.IsSettled).Sum(l => l.Revenue),
                });
            }

            var orphans = all.Where(l => svc.PersonFor(_table, l.OrderNumber, l.LineIndex, l.Unit) is null).ToList();
            if (orphans.Count > 0)
            {
                Persons.Add(new TablePersonGroupViewModel
                {
                    Person = -1,
                    Label = "ΑΧΡΕΩΤΑ",
                    Lines = new ObservableCollection<TableLineViewModel>(orphans),
                    Outstanding = orphans.Where(l => !l.IsSettled).Sum(l => l.Revenue),
                });
            }
        }

        OnPropertyChanged(nameof(HasPersons));
        OnPropertyChanged(nameof(ShowRounds));
        OnPropertyChanged(nameof(NewRoundLabel));
        OnPropertyChanged(nameof(ReceiptsHeader));
    }
}
