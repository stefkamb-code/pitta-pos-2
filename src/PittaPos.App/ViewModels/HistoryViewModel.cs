using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.App.Services;
using PittaPos.Core.Models;

namespace PittaPos.App.ViewModels;

/// <summary>Ποια λίστα δείχνει το ιστορικό αυτή τη στιγμή.</summary>
public enum HistoryTab { Orders, Cancelled }

/// <summary>
/// Μία γραμμή της λίστας του Ιστορικού. Για τα ΤΡΑΠΕΖΙΑ είναι ΟΛΟΣ ο λογαριασμός της παρέας — όλα τα
/// άτομα μαζί, σαν μία παραγγελία — και τα άτομα φαίνονται μέσα, όταν την επιλέξεις. Για τα υπόλοιπα
/// κανάλια είναι μία παραγγελία, όπως πάντα.
/// <para>Χωρίς αυτό, ένα τραπέζι τεσσάρων γέμιζε το ιστορικό με τέσσερις σχεδόν ίδιες γραμμές
/// «Τραπέζι 5» — η παρέα είναι ΕΝΑΣ λογαριασμός, απλώς κόβει τέσσερις αποδείξεις.</para>
/// </summary>
public sealed class HistoryEntryViewModel
{
    /// <summary>Ένα άτομο = μία παραγγελία = μία απόδειξη. Ταξινομημένα κατά άτομο.</summary>
    public required IReadOnlyList<CompletedOrder> Orders { get; init; }

    public CompletedOrder First => Orders[0];
    /// <summary>Λογαριασμός τραπεζιού: ξεδιπλώνεται «μέσα» — σε άτομα αν πληρώνουν χωριστά, αλλιώς
    /// στους γύρους του (η παρέα που πληρώνει μαζί κόβει ΜΙΑ απόδειξη, όσους γύρους κι αν έκανε).</summary>
    public bool IsTableBill => First.Type == OrderType.Table && (Orders.Count > 1 || First.HasPerson);

    /// <summary>Πληρώνουν χωριστά — ένα άτομο, μία απόδειξη ταμειακής.</summary>
    public bool HasPersons => Orders.Any(o => o.HasPerson);

    public string TypeLabel => First.TypeLabel;
    public string WhoLabel => First.WhoLabel;

    /// <summary>Στο τραπέζι το «Τραπέζι 2» από κάτω λέει ακριβώς ό,τι λέει ήδη το «#2 · ΤΡΑΠΕΖΙ» από
    /// πάνω — δύο φορές το ίδιο. Στα υπόλοιπα κανάλια από κάτω είναι το ΟΝΟΜΑ ΤΟΥ ΠΕΛΑΤΗ, που δεν
    /// υπάρχει πουθενά αλλού, οπότε μένει. Κριτήριο είναι το αν όντως δείχνεται αριθμός τραπεζιού και
    /// όχι σκέτα ο τύπος: μια παραγγελία διορθωμένη σε ΤΡΑΠΕΖΙ κρατά όνομα πελάτη στο Who, και χωρίς
    /// αυτό θα έμενε γραμμή χωρίς κανένα αναγνωριστικό.</summary>
    public bool ShowWhoLabel => First.TableNumberLabel.Length == 0;
    public decimal Total => Orders.Sum(o => o.Total);
    public string TotalLabel => Order.FormatPrice(Total);
    /// <summary>Ώρα του λογαριασμού = της πρώτης παραγγελίας του.</summary>
    public DateTime PlacedAt => Orders.Min(o => o.PlacedAt);
    public string DateTimeLabel => PlacedAt.ToString("dd/MM/yyyy · HH:mm");

    /// <summary>
    /// «#03», «#322» (e-food), «#5» (τραπέζι) — πάντα ο αριθμός που ξέρει ο ταμίας, ποτέ ο εσωτερικός
    /// (βλ. CompletedOrder.DisplayNumber). Ο λογαριασμός ενός τραπεζιού είναι πολλές παραγγελίες με τον
    /// ΙΔΙΟ αριθμό τραπεζιού, οπότε βγαίνει ένα σκέτο «#5» αντί για εύρος εσωτερικών αριθμών: το
    /// «#1001–#1003» δεν έλεγε τίποτα σε κανέναν και η παραγγελία δεν βρισκόταν με τίποτα.
    /// </summary>
    public string NumberLabel
    {
        get
        {
            var numbers = Orders.OrderBy(o => o.OrderNumber).Select(o => o.DisplayNumber).Distinct().ToList();
            return numbers.Count == 1 ? "#" + numbers[0] : $"#{numbers[0]}–#{numbers[^1]}";
        }
    }

    /// <summary>«3 άτομα · 3 αποδείξεις» — το ζητούμενο του μαγαζιού με μια ματιά. Όταν πληρώνουν μαζί
    /// δεν υπάρχουν άτομα: είναι γύροι του ίδιου λογαριασμού και η απόδειξη είναι μία.</summary>
    public string SubLabel
    {
        get
        {
            if (!IsTableBill)
                return "";
            if (HasPersons)
                return $"{Orders.Count} {(Orders.Count == 1 ? "άτομο" : "άτομα")} · " +
                       $"{Orders.Count} {(Orders.Count == 1 ? "απόδειξη" : "αποδείξεις")}";
            return $"{Orders.Count} γύροι · μία απόδειξη";
        }
    }

    public bool ShowSubLabel => SubLabel.Length > 0;
}

/// <summary>Ένα άτομο μέσα στον λογαριασμό τραπεζιού, όπως φαίνεται στο δεξί μέρος του Ιστορικού.</summary>
public sealed class HistoryPersonViewModel
{
    public required CompletedOrder Order { get; init; }
    public required bool IsSelected { get; init; }
    /// <summary>Η θέση του μέσα στον λογαριασμό (1-based) — για τραπέζι που πληρώνει μαζί.</summary>
    public required int Index { get; init; }
    /// <summary>«ΑΤΟΜΟ Β» όταν πληρώνουν χωριστά· «ΓΥΡΟΣ 2 · #43» όταν πληρώνουν μαζί.</summary>
    public string Label => Order.HasPerson ? Order.PersonLabel : $"ΓΥΡΟΣ {Index} · #{Order.OrderNumber}";
    public string TotalLabel => Order.TotalLabel;
    /// <summary>
    /// 💶/💳 όπως εισπράχθηκε. Όσο το τραπέζι είναι ανοιχτό και το άτομο δεν έχει πληρώσει, γράφει
    /// «εκκρεμεί» — έτσι ο λογαριασμός φαίνεται στο ιστορικό από την πρώτη στιγμή, χωρίς πληρωμή.
    /// <para>Πρώτα οι σημερινές εισπράξεις (ακριβείς, ξέρουν και τα μισά-μισά), αλλιώς ό,τι έμεινε
    /// γραμμένο πάνω στην παραγγελία — αυτό επιβιώνει και στο αρχείο των προηγούμενων ημερών.</para>
    /// </summary>
    public string PaymentIcon =>
        (TablePaymentsService.Instance.MethodFor(Order.OrderNumber) ?? Order.PaymentMethod) switch
        {
            PaymentMethod.Cash => "💶",
            PaymentMethod.Card => "💳",
            _ => "εκκρεμεί",
        };
}

/// <summary>Ένα κουμπί φίλτρου καναλιού στο Ιστορικό (ΟΛΑ / ΤΡΑΠΕΖΙ / Wolt / …).</summary>
public partial class HistoryFilterOption(string key, string label) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Μία «ενέργεια ακύρωσης» για τη λίστα «ΑΚΥΡΩΜΕΝΑ» — μαζεύει όλες τις γραμμές που ακυρώθηκαν μαζί
/// (ίδια παραγγελία, ίδιο instant) σε μία κάρτα, αντί να τις δείχνει σαν ξεχωριστά προϊόντα.
/// </summary>
public class CancelledEntryViewModel
{
    public required int OrderNumber { get; init; }
    public required string Who { get; init; }
    public required DateTime CancelledAt { get; init; }
    public required IReadOnlyList<CancelledLine> Lines { get; init; }

    /// <summary>Ίδιος κανόνας με το <see cref="CompletedOrder.DisplayNumber"/>, όσο το επιτρέπει η
    /// ακύρωση: κρατά μόνο αριθμό και «ποιος», οπότε ο αριθμός τραπεζιού βγαίνει από το Who. Ο κωδικός
    /// πλατφόρμας δεν αποθηκεύεται στην ακύρωση — εκεί μένει ο εσωτερικός αριθμός.</summary>
    public string DisplayNumber
    {
        get
        {
            var digits = new string(Who.Where(char.IsDigit).ToArray());
            return Who.StartsWith("Τραπέζι", StringComparison.Ordinal) && digits.Length > 0
                ? digits
                : OrderNumber < SalesStatsService.ExternalBandStart
                    ? OrderNumber.ToString("00")
                    : OrderNumber.ToString();
        }
    }

    public string TimeLabel => CancelledAt.ToString("HH:mm");
    public string CancelledByLabel => Lines[0].CancelledByLabel;
    public decimal Revenue => Lines.Sum(l => l.Revenue);
    public string RevenueLabel => Order.FormatPrice(Revenue);
    /// <summary>Πάνω από μία γραμμή — ολόκληρη παραγγελία ακυρώθηκε μαζί, όχι μεμονωμένο προϊόν.</summary>
    public bool IsWholeOrder => Lines.Count > 1;
    public string SummaryLabel => Lines.Count + " προϊόντα";
}

/// <summary>Ιστορικό παραγγελιών ημέρας — προβολή, διόρθωση πληρωμής, ακύρωση/διαγραφή, επανεκτύπωση.</summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly SalesStatsService _stats = SalesStatsService.Instance;
    private readonly CancellationLogService _cancellations = CancellationLogService.Instance;

    public HistoryViewModel()
    {
        _stats.Changed += Refresh;
        _cancellations.Changed += Refresh;
        // Δεύτερο ταμείο: όταν κατέβει το αρχείο παλιότερων ημερών, να φανεί χωρίς να ξανανοίξει το
        // παράθυρο (βλ. HistoryArchiveService.StartClientMirror).
        HistoryArchiveService.Changed += Refresh;
        Refresh();
    }

    /// <summary>Αποσύνδεση από τα services όταν κλείσει το παράθυρο.</summary>
    public void Detach()
    {
        _stats.Changed -= Refresh;
        _cancellations.Changed -= Refresh;
        HistoryArchiveService.Changed -= Refresh;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOrdersTab))]
    [NotifyPropertyChangedFor(nameof(ShowCancelledTab))]
    private HistoryTab _activeTab = HistoryTab.Orders;

    /// <summary>Εύρος ημερών που δείχνει το ιστορικό — προεπιλογή μόνο η σημερινή ημέρα-επιχείρησης.</summary>
    [ObservableProperty]
    private DateTime? _fromDate = SalesStatsService.BusinessDay(DateTime.Now);

    [ObservableProperty]
    private DateTime? _toDate = SalesStatsService.BusinessDay(DateTime.Now);

    partial void OnFromDateChanged(DateTime? value) => Refresh();
    partial void OnToDateChanged(DateTime? value) => Refresh();

    /// <summary>Επαναφορά στη σημερινή ημέρα-επιχείρησης μόνο.</summary>
    [RelayCommand]
    private void SelectToday()
    {
        var today = SalesStatsService.BusinessDay(DateTime.Now);
        FromDate = today;
        ToDate = today;
    }

    public bool ShowOrdersTab => ActiveTab == HistoryTab.Orders;
    public bool ShowCancelledTab => ActiveTab == HistoryTab.Cancelled;

    [RelayCommand] private void SelectOrdersTab() => ActiveTab = HistoryTab.Orders;
    [RelayCommand] private void SelectCancelledTab() => ActiveTab = HistoryTab.Cancelled;

    public IReadOnlyList<CompletedOrder> Orders { get; private set; } = [];

    /// <summary>Ό,τι δείχνει η λίστα: ένα τραπέζι = ΜΙΑ γραμμή με όλα του τα άτομα μέσα.</summary>
    public IReadOnlyList<HistoryEntryViewModel> Entries { get; private set; } = [];

    /// <summary>Ο λογαριασμός που είναι ανοιχτός δεξιά (για τραπέζι: όλη η παρέα).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPersons))]
    [NotifyPropertyChangedFor(nameof(Persons))]
    [NotifyPropertyChangedFor(nameof(PersonsHeader))]
    private HistoryEntryViewModel? _selectedEntry;

    /// <summary>Τα άτομα του επιλεγμένου λογαριασμού — φαίνονται πάνω από την απόδειξη και πατιούνται.</summary>
    public IReadOnlyList<HistoryPersonViewModel> Persons => SelectedEntry is null || !SelectedEntry.IsTableBill
        ? []
        : SelectedEntry.Orders
            .Select((o, i) => new HistoryPersonViewModel
            {
                Order = o,
                Index = i + 1,
                IsSelected = ReferenceEquals(o, SelectedOrder),
            })
            .ToList();

    public bool ShowPersons => Persons.Count > 0;

    public string PersonsHeader => SelectedEntry is null
        ? ""
        : $"{SelectedEntry.WhoLabel} · {SelectedEntry.SubLabel} · {SelectedEntry.TotalLabel}";

    /// <summary>
    /// Μαζεύει τα άτομα του ίδιου τραπεζιού σε μία εγγραφή. Κλειδί: το τραπέζι ΚΑΙ το άνοιγμά του
    /// (βλ. CompletedOrder.TableOpenedAt) — δύο παρέες που κάθισαν στο ίδιο τραπέζι την ίδια μέρα
    /// είναι δύο λογαριασμοί. Παλιές παραγγελίες δεν έχουν άνοιγμα: πέφτουν πίσω στην ημέρα-επιχείρηση,
    /// που είναι ό,τι καλύτερο μπορεί να ξέρει κανείς γι' αυτές.
    /// </summary>
    private static List<HistoryEntryViewModel> BuildEntries(IReadOnlyList<CompletedOrder> orders)
    {
        var entries = new List<HistoryEntryViewModel>();
        foreach (var group in orders.GroupBy(o => o.Type == OrderType.Table
            ? (Table: o.Who, Session: o.TableOpenedAt ?? SalesStatsService.BusinessDay(o.PlacedAt))
            : (Table: "#" + o.OrderNumber, Session: o.PlacedAt)))
        {
            entries.Add(new HistoryEntryViewModel
            {
                // Με τη σειρά των ατόμων (Α, Β, Γ) — όχι της ώρας: έτσι διαβάζεται σαν λογαριασμός.
                Orders = [.. group.OrderBy(o => o.TablePerson ?? int.MaxValue).ThenBy(o => o.OrderNumber)],
            });
        }
        return [.. entries.OrderByDescending(e => e.PlacedAt)];
    }

    /// <summary>Κρατά τη δεξιά στήλη συμβατή με τη λίστα μετά από κάθε ανανέωση.</summary>
    private void SyncSelectedEntry()
    {
        if (SelectedOrder is null)
        {
            SelectedEntry = null;
            return;
        }
        SelectedEntry = Entries.FirstOrDefault(e => e.Orders.Any(o => o.OrderNumber == SelectedOrder.OrderNumber));
        OnPropertyChanged(nameof(Persons));
    }

    /// <summary>Κλικ σε γραμμή της λίστας — ανοίγει ο λογαριασμός, με το ΠΡΩΤΟ άτομο επιλεγμένο.</summary>
    [RelayCommand]
    private void SelectEntry(HistoryEntryViewModel entry)
    {
        SelectedEntry = entry;
        SelectedOrder = entry.First;
        OnPropertyChanged(nameof(Persons));
    }

    /// <summary>Κλικ σε άτομο μέσα στον λογαριασμό — δείχνει τη ΔΙΚΗ ΤΟΥ απόδειξη και τα κουμπιά της.</summary>
    [RelayCommand]
    private void SelectPerson(HistoryPersonViewModel person)
    {
        SelectedOrder = person.Order;
        OnPropertyChanged(nameof(Persons));
    }
    public IReadOnlyList<CancelledEntryViewModel> CancelledEntries { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelected))]
    [NotifyPropertyChangedFor(nameof(NothingSelected))]
    [NotifyPropertyChangedFor(nameof(CanChangeToTable))]
    [NotifyPropertyChangedFor(nameof(CanChangeToPickup))]
    [NotifyPropertyChangedFor(nameof(CanChangeToDelivery))]
    [NotifyPropertyChangedFor(nameof(ChannelOptionColumns))]
    private CompletedOrder? _selectedOrder;

    // ΟΡΘΙΟΣ και ΤΡΑΠΕΖΙ αλλάζουν ΜΟΝΟ μεταξύ τους: είναι το ίδιο μαγαζί με τον πελάτη μπροστά σου, και
    // το ένα περνιέται κατά λάθος αντί για το άλλο. Κανάλι διανομής δεν έχει νόημα εκεί — δεν υπάρχει
    // διεύθυνση ούτε κωδικός πλατφόρμας. Το ανάποδο μένει ανοιχτό ΜΟΝΟ προς ΟΡΘΙΟ (μια διανομή μπορεί
    // να γίνει όρθιος), γιατί εκεί το λάθος όντως συμβαίνει και πρέπει να διορθώνεται — προς ΤΡΑΠΕΖΙ όχι,
    // βλ. CanChangeToTable.
    private bool SelectedIsPickup => SelectedOrder?.Type == OrderType.Pickup;

    /// <summary>Σε ΤΡΑΠΕΖΙ γυρίζει ΜΟΝΟ ο ΟΡΘΙΟΣ. Μια διανομή (ή παραγγελία εφαρμογής) δεν έχει νόημα να
    /// γίνει τραπέζι — και δεν είναι μόνο θέμα λογικής: η διορθωμένη παραγγελία κρατά το όνομα του πελάτη,
    /// όχι αριθμό τραπεζιού, οπότε δεν κολλάει σε κανένα πραγματικό τραπέζι για να εξοφληθεί. Τα λεφτά της
    /// έβγαιναν από τον διαχωρισμό μετρητά/κάρτα (τα τραπέζια μετρώνται μόνο από τις εισπράξεις τους) και
    /// δεν ξαναέμπαιναν ποτέ, ενώ ο τζίρος τα κρατούσε.</summary>
    public bool CanChangeToTable => SelectedIsPickup;
    public bool CanChangeToPickup => SelectedOrder is not null && !SelectedIsPickup;
    public bool CanChangeToDelivery => SelectedOrder is not null && !SelectedIsTable && !SelectedIsPickup;

    /// <summary>Σε ΟΡΘΙΟ/ΤΡΑΠΕΖΙ μένει ένα μόνο κουμπί — να πιάνει όλο το πλάτος αντί για το 1/3 ενός
    /// πλέγματος τριών στηλών που έχει άδειες θέσεις.</summary>
    public int ChannelOptionColumns => CanChangeToDelivery ? 3 : 1;

    /// <summary>Οι επιλογές διόρθωσης καναλιού κρύβονται μέχρι να πατηθεί το κουμπί «ΔΙΟΡΘΩΣΗ ΚΑΝΑΛΙΟΥ» —
    /// προστασία από κατά λάθος κλικ, ίδια λογική με το ΑΛΛΑΓΗ ΣΕ στις Ζωντανές Παραγγελίες.</summary>
    [ObservableProperty]
    private bool _showChannelOptions;

    /// <summary>Ίδια λογική για το «ΤΡΟΠΟΣ ΠΛΗΡΩΜΗΣ» — πλέον διαθέσιμο σε ΟΛΕΣ τις παραγγελίες (όχι μόνο
    /// ΔΙΑΝΟΜΗ/BOX), ώστε να μπορεί να προστεθεί/διορθωθεί τρόπος πληρωμής ακόμα κι όπου δεν υπήρχε πριν.</summary>
    [ObservableProperty]
    private bool _showPaymentOptions;

    partial void OnSelectedOrderChanged(CompletedOrder? value)
    {
        ShowChannelOptions = false;
        ShowPaymentOptions = false;
        RefreshPaymentLabels();
    }

    /// <summary>
    /// ΤΡΑΠΕΖΙ: ο τρόπος πληρωμής ΔΕΝ ζει πάνω στην παραγγελία — το τραπέζι εισπράττεται τμηματικά και
    /// κάθε είσπραξη γράφεται στο <see cref="TablePaymentsService"/>, από όπου βγαίνει ο διαχωρισμός
    /// μετρητά/κάρτα της ημέρας. Με μία παραγγελία ανά άτομο, η είσπραξη αυτής της παραγγελίας είναι
    /// ακριβώς η απόδειξη ΕΝΟΣ ατόμου — άρα εδώ διορθώνεται το άτομο που πέρασε λάθος.
    /// </summary>
    private bool SelectedIsTable => SelectedOrder?.Type == OrderType.Table;

    /// <summary>Τι δείχνει η γραμμή «Πληρωμή»: για τραπέζι από τις εισπράξεις, αλλιώς από την παραγγελία.</summary>
    public string? SelectedPaymentIcon => SelectedOrder is null
        ? null
        : SelectedIsTable
            ? (TablePaymentsService.Instance.MethodFor(SelectedOrder.OrderNumber) ?? SelectedOrder.PaymentMethod) switch
            {
                Core.Models.PaymentMethod.Cash => "💶",
                Core.Models.PaymentMethod.Card => "💳",
                _ => null,
            }
            : SelectedOrder.PaymentIcon;

    public bool ShowPaymentRow => SelectedPaymentIcon is not null;

    /// <summary>«ΤΡΟΠΟΣ ΠΛΗΡΩΜΗΣ · ΑΤΟΜΟ Β» — ώστε να ξέρει ο ταμίας ποιανού απόδειξη διορθώνει.</summary>
    public string PaymentSectionLabel =>
        SelectedOrder?.HasPerson == true ? "ΤΡΟΠΟΣ ΠΛΗΡΩΜΗΣ · " + SelectedOrder.PersonLabel : "ΤΡΟΠΟΣ ΠΛΗΡΩΜΗΣ";

    private void RefreshPaymentLabels()
    {
        OnPropertyChanged(nameof(Persons)); // τα 💶/💳 δίπλα στα άτομα
        OnPropertyChanged(nameof(SelectedPaymentIcon));
        OnPropertyChanged(nameof(ShowPaymentRow));
        OnPropertyChanged(nameof(PaymentSectionLabel));
        OnPropertyChanged(nameof(DeleteSectionLabel));
        OnPropertyChanged(nameof(DeleteTargetLabel));
    }

    /// <summary>Ρύθμιση απόδειξης — εμφάνιση λεπτομερειών προϊόντων (ψωμί/έξτρα/χωρίς).</summary>
    public bool ShowLineDetails => SettingsStore.Instance.Settings.ReceiptShowDetails;

    public bool HasSelected => SelectedOrder is not null;
    public bool NothingSelected => SelectedOrder is null && Orders.Count > 0;
    public bool NoOrders { get; private set; } = true;
    public bool NoCancelledEntries { get; private set; } = true;

    [RelayCommand]
    private void SelectOrder(CompletedOrder order) => SelectedOrder = order;

    /// <summary>
    /// Πραγματική διαγραφή της επιλεγμένης παραγγελίας — φεύγει από τον τζίρο και πηγαίνει στις
    /// ακυρωμένες (βλ. OrderCancellationService, ίδια συμπεριφορά με τραπέζι και Ζωντανές Παραγγελίες).
    /// Σε τραπέζι με άτομα σβήνει **ΜΟΝΟ ΤΟ ΕΠΙΛΕΓΜΕΝΟ ΑΤΟΜΟ** (μία παραγγελία = ένα άτομο):
    /// αν ένας από την παρέα δεν πλήρωσε ή έγινε λάθος στη δική του, δεν ακυρώνεται όλο το τραπέζι.
    /// </summary>
    [RelayCommand]
    private void DeleteOrder(string cancelledBy)
    {
        if (SelectedOrder is null)
            return;

        OrderCancellationService.CancelOrder(SelectedOrder.OrderNumber, cancelledBy);
        SelectedOrder = null;
    }

    /// <summary>Τι ακριβώς θα σβηστεί — μπαίνει στην ερώτηση επιβεβαίωσης ώστε να μην ακυρωθεί λάθος
    /// άτομο ή, χειρότερα, να νομίζει ο ταμίας ότι σβήνει όλο το τραπέζι.</summary>
    public string DeleteTargetLabel => SelectedOrder is null
        ? ""
        : SelectedOrder.HasPerson
            ? $"την παραγγελία του {SelectedOrder.PersonLabel} ({SelectedOrder.WhoLabel}) — {SelectedOrder.TotalLabel}"
            : $"την παραγγελία #{SelectedOrder.OrderNumber} ({SelectedOrder.WhoLabel}) — {SelectedOrder.TotalLabel}";

    /// <summary>«ΔΙΑΓΡΑΦΗ · ΑΤΟΜΟ Β» — το κουμπί λέει ποιον αφορά.</summary>
    public string DeleteSectionLabel =>
        SelectedOrder?.HasPerson == true ? "ΔΙΑΓΡΑΦΗ · " + SelectedOrder.PersonLabel : "ΔΙΑΓΡΑΦΗ";

    /// <summary>Εμφανίζει/κρύβει τις επιλογές τρόπου πληρωμής. Ανοίγει ΜΙΑ ομάδα επιλογών τη φορά —
    /// αλλιώς μαζεύονταν όλες ανοιχτές η μία κάτω από την άλλη και η οθόνη γινόταν κατάλογος κουμπιών.</summary>
    [RelayCommand]
    private void TogglePaymentOptions()
    {
        ShowPaymentOptions = !ShowPaymentOptions;
        ShowChannelOptions = false;
    }

    /// <summary>Διόρθωση τρόπου πληρωμής — π.χ. ο ταμίας πάτησε κατά λάθος Μετρητά αντί για Κάρτα στην ώρα
    /// της παραγγελίας, ή δεν είχε καταχωρηθεί καθόλου. Μόνο σημερινές παραγγελίες αλλάζουν πραγματικά
    /// (βλ. SalesStatsService.UpdatePaymentMethod).</summary>
    [RelayCommand]
    private void SetPaymentMethodCash() => ApplyPaymentMethod(Core.Models.PaymentMethod.Cash);

    [RelayCommand]
    private void SetPaymentMethodCard() => ApplyPaymentMethod(Core.Models.PaymentMethod.Card);

    private void ApplyPaymentMethod(PaymentMethod method)
    {
        if (SelectedOrder is null)
            return;

        if (SelectedIsTable)
        {
            // ΔΕΝ σημειώνουμε τρόπο πληρωμής σε άτομο που δεν έχει πληρώσει: θα έδειχνε 💶 ενώ στο
            // ταμείο δεν μπήκε ευρώ. Η πληρωμή γίνεται από την οθόνη τραπεζιού· εδώ μόνο διορθώνεται.
            if (TablePaymentsService.Instance.AmountFor(SelectedOrder.OrderNumber) == 0)
            {
                MessageBox.Show(
                    "Αυτό το άτομο δεν έχει πληρώσει ακόμα (δεν έχει εξοφληθεί από την οθόνη τραπεζιού), " +
                    "οπότε δεν υπάρχει πληρωμή για διόρθωση.\n\nΕξόφλησέ το πρώτα από το τραπέζι.",
                    "Τρόπος πληρωμής", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowPaymentOptions = false;
                return;
            }

            // Το ΠΟΣΟ μετακινείται από τα μετρητά στην κάρτα (ή ανάποδα) στην αναφορά ημέρας, και ο
            // τρόπος γράφεται και πάνω στην παραγγελία ώστε η διόρθωση να επιβιώσει στο αρχείο. Δεν
            // διπλομετράει: η αναφορά ημέρας για τα τραπέζια μετράει μόνο τις εισπράξεις.
            TablePaymentsService.Instance.SwitchMethod(SelectedOrder.OrderNumber, method);
            _stats.UpdatePaymentMethod(SelectedOrder.OrderNumber, method);
            RefreshPaymentLabels();
        }
        else
        {
            _stats.UpdatePaymentMethod(SelectedOrder.OrderNumber, method);
        }
        ShowPaymentOptions = false;
    }

    /// <summary>Εμφανίζει/κρύβει τις επιλογές διόρθωσης καναλιού (μία ομάδα ανοιχτή τη φορά).</summary>
    [RelayCommand]
    private void ToggleChannelOptions()
    {
        ShowChannelOptions = !ShowChannelOptions;
        ShowPaymentOptions = false;
    }

    /// <summary>Διόρθωση καναλιού — π.χ. μια παραγγελία πέρασε κατά λάθος ως e-food ενώ ήταν κάτι άλλο.
    /// Ίδιος περιορισμός με το τρόπο πληρωμής (βλ. SalesStatsService.UpdateChannel) — μόνο σημερινές.</summary>
    [RelayCommand]
    private void SetChannel(string channelKey)
    {
        if (SelectedOrder is null)
            return;
        var (type, channel) = channelKey switch
        {
            "ΔΙΑΝΟΜΗ" => (Core.Models.OrderType.Delivery, (string?)null),
            "ΤΡΑΠΕΖΙ" => (Core.Models.OrderType.Table, null),
            "ΟΡΘΙΟΣ" => (Core.Models.OrderType.Pickup, null),
            _ => (Core.Models.OrderType.Apps, channelKey),
        };
        _stats.UpdateChannel(SelectedOrder.OrderNumber, type, channel);
        ShowChannelOptions = false;
    }

    /// <summary>
    /// Φίλτρο καναλιού στο Ιστορικό. Κενό = όλα. Οι τιμές είναι ή τύπος παραγγελίας (ΤΡΑΠΕΖΙ,
    /// ΔΙΑΝΟΜΗ, ΠΑΡΑΛΑΒΗ) ή πλατφόρμα εφαρμογών (e-food, Wolt, BOX) — ο ταμίας ψάχνει «τι πήγε σε
    /// Wolt χθες», όχι με ποιον τεχνικό τύπο είναι αποθηκευμένο.
    /// </summary>
    [ObservableProperty]
    private string _channelFilter = "";

    partial void OnChannelFilterChanged(string value)
    {
        foreach (var o in ChannelFilters)
            o.IsSelected = o.Key == value;
        OnPropertyChanged(nameof(ChannelFilters));
        OnPropertyChanged(nameof(ChannelFilterButtonLabel));
        OnPropertyChanged(nameof(HasChannelFilter));
        Refresh();
    }

    /// <summary>Ανοιχτό/κλειστό το μενού του φίλτρου — ένα κουμπί, οι επιλογές από μέσα.</summary>
    [ObservableProperty]
    private bool _showChannelMenu;

    [RelayCommand]
    private void ToggleChannelMenu() => ShowChannelMenu = !ShowChannelMenu;

    /// <summary>Το κουμπί λέει πάντα τι βλέπεις — αλλιώς ξεχνιέται ενεργό φίλτρο και λείπουν παραγγελίες.</summary>
    public string ChannelFilterButtonLabel =>
        "ΦΙΛΤΡΟ: " + (ChannelFilters.FirstOrDefault(o => o.Key == ChannelFilter)?.Label ?? "ΟΛΑ") + " ▾";

    public bool HasChannelFilter => ChannelFilter.Length > 0;

    public IReadOnlyList<HistoryFilterOption> ChannelFilters { get; } =
    [
        new("", "ΟΛΑ") { IsSelected = true },
        new("ΤΡΑΠΕΖΙ", "ΤΡΑΠΕΖΙ"),
        new("ΔΙΑΝΟΜΗ", "ΔΙΑΝΟΜΗ"),
        new("ΠΑΡΑΛΑΒΗ", "ΠΑΡΑΛΑΒΗ"),
        new("e-food", "e-food"),
        new("Wolt", "Wolt"),
        new("BOX", "BOX"),
    ];

    [RelayCommand]
    private void SetChannelFilter(string key)
    {
        ChannelFilter = key ?? "";
        ShowChannelMenu = false;
    }

    /// <summary>
    /// Αναζήτηση παραγγελίας από τον αριθμό της. Μόνο ψηφία — ό,τι άλλο πληκτρολογηθεί αγνοείται,
    /// ώστε να μη βγάζει «κανένα αποτέλεσμα» επειδή ξέφυγε ένα γράμμα.
    /// </summary>
    [ObservableProperty]
    private string _searchNumber = "";

    /// <summary>Δέχεται και γράμματα: οι κωδικοί του BOX δεν είναι σκέτοι αριθμοί (βλ.
    /// OrderWizardViewModel.FilterOrderRef). Όσο εδώ κρατιόνταν μόνο τα ψηφία, μια BOX παραγγελία με
    /// γράμμα στον κωδικό δεν βρισκόταν ποτέ.</summary>
    partial void OnSearchNumberChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(NoOrdersLabel));
        Refresh();
    }

    public bool IsSearching => SearchNumber.Length > 0;

    [RelayCommand]
    private void ClearSearch() => SearchNumber = "";

    /// <summary>Τι λέει η οθόνη όταν η λίστα είναι άδεια — αλλιώς «Καμία παραγγελία σήμερα» σε αναζήτηση
    /// παλιάς παραγγελίας διαβάζεται σαν να χάθηκε.</summary>
    public string NoOrdersLabel => IsSearching
        ? $"Δεν βρέθηκε παραγγελία #{SearchNumber} " +
          (FromDate is null && ToDate is null ? "σε όλο το ιστορικό" : "στις ημερομηνίες που έχεις επιλέξει")
        : "Καμία παραγγελία";

    /// <summary>Ταιριάζει ό,τι ΠΕΡΙΕΧΕΙ τα ψηφία: «14» φέρνει #14, #142, #514 — ο ταμίας συχνά θυμάται
    /// μόνο τα τελευταία νούμερα από το δελτίο.</summary>
    private static IEnumerable<CompletedOrder> Matching(IEnumerable<CompletedOrder> orders, string search) =>
        // Ψάχνει τον αριθμό ΠΟΥ ΒΛΕΠΕΙ ο ταμίας (DisplayNumber): τον κωδικό της πλατφόρμας σε
        // e-food/Wolt/BOX, τον αριθμό τραπεζιού στα τραπέζια, τη σειρά βάρδιας στον ΟΡΘΙΟ/ΔΙΑΝΟΜΗ.
        // Με τον εσωτερικό αριθμό, το «322» της e-food δεν έβγαζε τίποτα — ακριβώς ο λόγος που
        // ζητήθηκε η αλλαγή αρίθμησης.
        // Χωρίς διάκριση πεζών/κεφαλαίων — ο κωδικός του BOX μπορεί να έχει γράμματα και κανείς
        // δεν θυμάται αν τα είχε γράψει κεφαλαία.
        orders.Where(o => o.DisplayNumber.Contains(search, StringComparison.OrdinalIgnoreCase));

    /// <summary>Περνάει το φίλτρο καναλιού πάνω στις παραγγελίες της περιόδου.</summary>
    private IEnumerable<CompletedOrder> ApplyChannelFilter(IEnumerable<CompletedOrder> orders) =>
        ChannelFilter switch
        {
            "" => orders,
            "ΤΡΑΠΕΖΙ" => orders.Where(o => o.Type == OrderType.Table),
            "ΔΙΑΝΟΜΗ" => orders.Where(o => o.Type == OrderType.Delivery),
            "ΠΑΡΑΛΑΒΗ" => orders.Where(o => o.Type == OrderType.Pickup),
            // Οι πλατφόρμες ζουν στο Channel των ΕΦΑΡΜΟΓΩΝ.
            var platform => orders.Where(o => o.Type == OrderType.Apps
                && string.Equals(o.Channel, platform, StringComparison.OrdinalIgnoreCase)),
        };

    private void Refresh()
    {
        // Αναζήτηση με αριθμό: ψάχνει ΟΛΟ το ιστορικό και αγνοεί ημερομηνίες και φίλτρο καναλιού —
        // όποιος ψάχνει «την #142» δεν ξέρει ποια μέρα ήταν, ούτε από πού είχε έρθει.
        var search = SearchNumber.Trim();
        var searching = search.Length > 0;

        // Η αναζήτηση σέβεται το εύρος ημερομηνιών: «μόνο σήμερα» ψάχνει μόνο σήμερα, μεγαλύτερο εύρος
        // ψάχνει όλο το εύρος. Παλιότερα αγνοούσε τις ημερομηνίες και σάρωνε ΟΛΟ το ιστορικό — δούλευε
        // όσο ο αριθμός ήταν ένας συνεχής μετρητής και άρα μοναδικός. Τώρα η σειρά της βάρδιας ξεκινά
        // από #01 κάθε μέρα, οπότε το «01» θα έφερνε μια παραγγελία από κάθε ημέρα του αρχείου.
        var from = FromDate ?? DateTime.MinValue;
        var to = ToDate ?? DateTime.MaxValue;
        var today = SalesStatsService.BusinessDay(DateTime.Now);

        // Οι ΖΩΝΤΑΝΕΣ φιλτράρονται με τον ΙΔΙΟ κανόνα ημέρας-επιχείρησης που φιλτράρονται και οι
        // αρχειοθετημένες. Πριν έμπαιναν όλες όποτε το εύρος περιλάμβανε τη σημερινή: συνήθως σωστό,
        // γιατί ζωντανές είναι μόνο οι σημερινές — αλλά όχι πάντα. Μια παραγγελία προηγούμενης μέρας
        // μπορεί να κάθεται ακόμα εδώ (έφτασε καθυστερημένα από την ουρά του δεύτερου ταμείου, ή ο
        // υπολογιστής κοιμήθηκε και προσπέρασε την ώρα κλεισίματος) και εμφανιζόταν σαν ΣΗΜΕΡΙΝΗ.
        static bool InRange(DateTime day, DateTime from, DateTime to) => day >= from.Date && day <= to.Date;

        var archivedOrders = HistoryArchiveService.LoadOrders(from, to)
            .Where(o => SalesStatsService.BusinessDay(o.PlacedAt) != today);
        var liveOrders = _stats.Orders
            .Where(o => InRange(SalesStatsService.BusinessDay(o.PlacedAt), from, to));
        var allOrders = archivedOrders.Concat(liveOrders);
        Orders = (searching ? Matching(allOrders, search) : ApplyChannelFilter(allOrders))
            .OrderByDescending(o => o.PlacedAt).ToList();
        Entries = BuildEntries(Orders);
        NoOrders = Entries.Count == 0;
        if (SelectedOrder is not null && !Orders.Contains(SelectedOrder))
            SelectedOrder = Orders.FirstOrDefault(o => o.OrderNumber == SelectedOrder.OrderNumber);
        SyncSelectedEntry();

        var archivedCancellations = HistoryArchiveService.LoadCancellations(from, to)
            .Where(c => SalesStatsService.BusinessDay(c.CancelledAt) != today);
        var liveCancellations = _cancellations.Entries
            .Where(c => InRange(SalesStatsService.BusinessDay(c.CancelledAt), from, to));
        var allCancellations = archivedCancellations.Concat(liveCancellations);
        // Η αναζήτηση πιάνει και τα ΑΚΥΡΩΜΕΝΑ: «πού πήγε η #142» έχει απάντηση και όταν ακυρώθηκε.
        if (searching)
            allCancellations = allCancellations.Where(c =>
                c.OrderNumber.ToString().Contains(search)
                // Και με τον αριθμό τραπεζιού: αυτόν βλέπει πια ο ταμίας στη λίστα, οπότε αυτόν θα
                // πληκτρολογήσει ψάχνοντας «τι ακυρώθηκε στο 5».
                || (c.Who.StartsWith("Τραπέζι", StringComparison.Ordinal)
                    && new string(c.Who.Where(char.IsDigit).ToArray()).Contains(search)));
        // Ομαδοποίηση ανά (παραγγελία, ακριβές instant) — γραμμές που ακυρώθηκαν μαζί (π.χ. ολόκληρος
        // γύρος) γίνονται μία κάρτα αντί να εμφανίζονται σαν ξεχωριστά προϊόντα.
        CancelledEntries = allCancellations
            .GroupBy(c => (c.OrderNumber, c.CancelledAt))
            .Select(g => new CancelledEntryViewModel
            {
                OrderNumber = g.Key.OrderNumber,
                Who = g.First().Who,
                CancelledAt = g.Key.CancelledAt,
                Lines = g.ToList(),
            })
            .OrderByDescending(c => c.CancelledAt)
            .ToList();
        NoCancelledEntries = CancelledEntries.Count == 0;

        OnPropertyChanged(nameof(Orders));
        // ΧΩΡΙΣ ΑΥΤΟ η λίστα δεν ξαναζωγραφίζεται ποτέ: δένεται στο Entries, όχι στο Orders — γι' αυτό
        // «δεν δούλευε το φίλτρο» (ούτε η αλλαγή ημερομηνίας ούτε οι νέες παραγγελίες φαίνονταν).
        OnPropertyChanged(nameof(Entries));
        OnPropertyChanged(nameof(NoOrders));
        OnPropertyChanged(nameof(NoOrdersLabel));
        OnPropertyChanged(nameof(NothingSelected));
        OnPropertyChanged(nameof(CancelledEntries));
        OnPropertyChanged(nameof(NoCancelledEntries));
    }
}
