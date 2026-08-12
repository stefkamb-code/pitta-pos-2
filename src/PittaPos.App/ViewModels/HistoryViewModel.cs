using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.App.Services;
using PittaPos.Core.Models;

namespace PittaPos.App.ViewModels;

/// <summary>Ποια λίστα δείχνει το ιστορικό αυτή τη στιγμή.</summary>
public enum HistoryTab { Orders, Cancelled, Discounts }

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
    public decimal Total => Orders.Sum(o => o.Total);
    public string TotalLabel => Order.FormatPrice(Total);
    /// <summary>Ώρα του λογαριασμού = της πρώτης παραγγελίας του.</summary>
    public DateTime PlacedAt => Orders.Min(o => o.PlacedAt);
    public string DateTimeLabel => PlacedAt.ToString("dd/MM/yyyy · HH:mm");

    /// <summary>«#42» ή «#42–#45» για τον λογαριασμό ολόκληρης παρέας.</summary>
    public string NumberLabel
    {
        get
        {
            var min = Orders.Min(o => o.OrderNumber);
            var max = Orders.Max(o => o.OrderNumber);
            return min == max ? "#" + min : $"#{min}–#{max}";
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

    public string TimeLabel => CancelledAt.ToString("HH:mm");
    public string CancelledByLabel => Lines[0].CancelledByLabel;
    public decimal Revenue => Lines.Sum(l => l.Revenue);
    public string RevenueLabel => Order.FormatPrice(Revenue);
    /// <summary>Πάνω από μία γραμμή — ολόκληρη παραγγελία ακυρώθηκε μαζί, όχι μεμονωμένο προϊόν.</summary>
    public bool IsWholeOrder => Lines.Count > 1;
    public string SummaryLabel => Lines.Count + " προϊόντα";
}

/// <summary>Μία γραμμή έκπτωσης (ανά προϊόν ή ανά ολόκληρη παραγγελία) για τη λίστα «ΕΚΠΤΩΣΕΙΣ».</summary>
public class DiscountEntryViewModel
{
    public required int OrderNumber { get; init; }
    public required string TimeLabel { get; init; }
    public required string Description { get; init; }
    public required int DiscountPct { get; init; }
    public required decimal Amount { get; init; }
    public string AmountLabel => Order.FormatPrice(Amount);
    public string DiscountLabel => "-" + DiscountPct + "%";
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
        Refresh();
    }

    /// <summary>Αποσύνδεση από τα services όταν κλείσει το παράθυρο.</summary>
    public void Detach()
    {
        _stats.Changed -= Refresh;
        _cancellations.Changed -= Refresh;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOrdersTab))]
    [NotifyPropertyChangedFor(nameof(ShowCancelledTab))]
    [NotifyPropertyChangedFor(nameof(ShowDiscountsTab))]
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
    public bool ShowDiscountsTab => ActiveTab == HistoryTab.Discounts;

    [RelayCommand] private void SelectOrdersTab() => ActiveTab = HistoryTab.Orders;
    [RelayCommand] private void SelectCancelledTab() => ActiveTab = HistoryTab.Cancelled;
    [RelayCommand] private void SelectDiscountsTab() => ActiveTab = HistoryTab.Discounts;

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
    public IReadOnlyList<DiscountEntryViewModel> DiscountEntries { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelected))]
    [NotifyPropertyChangedFor(nameof(NothingSelected))]
    private CompletedOrder? _selectedOrder;

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
    public bool NoDiscountEntries { get; private set; } = true;

    [RelayCommand]
    private void SelectOrder(CompletedOrder order) => SelectedOrder = order;

    /// <summary>
    /// Πραγματική διαγραφή της επιλεγμένης παραγγελίας — αφαιρείται από τον τζίρο, καταγράφεται στα
    /// ακυρωμένα. Σε τραπέζι με άτομα σβήνει **ΜΟΝΟ ΤΟ ΕΠΙΛΕΓΜΕΝΟ ΑΤΟΜΟ** (μία παραγγελία = ένα άτομο):
    /// αν ένας από την παρέα δεν πλήρωσε ή έγινε λάθος στη δική του, δεν ακυρώνεται όλο το τραπέζι.
    /// </summary>
    [RelayCommand]
    private void DeleteOrder(string cancelledBy)
    {
        if (SelectedOrder is null)
            return;

        var order = SelectedOrder;
        _stats.RemoveOrder(order.OrderNumber, cancelledBy);
        if (order.Type == OrderType.Table)
        {
            // Μαζί του φεύγει και η είσπραξή του, αλλιώς το ποσό θα έμενε στα μετρητά/κάρτα της ημέρας
            // ενώ ο τζίρος του αφαιρέθηκε — και ο χωρισμός του τραπεζιού, για να μη μείνουν ορφανά.
            TablePaymentsService.Instance.RemoveFor(order.OrderNumber);
            if (TableNumberOf(order) is { } table)
                TablePersonsService.Instance.ClearOrder(table, order.OrderNumber);
        }
        SelectedOrder = null;
    }

    /// <summary>«Τραπέζι 5» → 5. Το τραπέζι δεν αποθηκεύεται σαν αριθμός πάνω στην παραγγελία.</summary>
    private static int? TableNumberOf(CompletedOrder order) =>
        int.TryParse(order.Who.Replace("Τραπέζι", "").Trim(), out var n) ? n : null;

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
            // Το ΠΟΣΟ μετακινείται από τα μετρητά στην κάρτα (ή ανάποδα) στην αναφορά ημέρας. Δεν
            // αγγίζουμε το PaymentMethod της παραγγελίας: για τραπέζι θα διπλομετρούσε τον τζίρο του
            // στον διαχωρισμό (βλ. DayReportService — τα τραπέζια μετρώνται μόνο από τις εισπράξεις).
            var moved = TablePaymentsService.Instance.SwitchMethod(SelectedOrder.OrderNumber, method);
            if (moved == 0 && TablePaymentsService.Instance.AmountFor(SelectedOrder.OrderNumber) == 0)
                MessageBox.Show(
                    "Αυτό το άτομο δεν έχει πληρώσει ακόμα (το τραπέζι δεν έχει εξοφληθεί από την οθόνη " +
                    "τραπεζιού), οπότε δεν υπάρχει πληρωμή για διόρθωση.\n\nΕξόφλησέ το πρώτα από το τραπέζι.",
                    "Τρόπος πληρωμής", MessageBoxButton.OK, MessageBoxImage.Information);
            // Και πάνω στην παραγγελία, ώστε η διόρθωση να μείνει και μετά το κλείσιμο της ημέρας.
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

    private static IEnumerable<DiscountEntryViewModel> BuildDiscountEntries(CompletedOrder o)
    {
        foreach (var l in o.Lines.Where(l => l.HasDiscount))
            yield return new DiscountEntryViewModel
            {
                OrderNumber = o.OrderNumber,
                TimeLabel = o.TimeLabel,
                Description = l.QtyNameLabel,
                DiscountPct = l.DiscountPct,
                Amount = l.Revenue,
            };
        if (o.OrderDiscountPct > 0)
            yield return new DiscountEntryViewModel
            {
                OrderNumber = o.OrderNumber,
                TimeLabel = o.TimeLabel,
                Description = "Όλη η παραγγελία" + (o.WhoLabel != "—" ? " · " + o.WhoLabel : ""),
                DiscountPct = o.OrderDiscountPct,
                Amount = o.Total,
            };
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
        var from = FromDate ?? DateTime.MinValue;
        var to = ToDate ?? DateTime.MaxValue;
        var today = SalesStatsService.BusinessDay(DateTime.Now);
        var includesToday = from.Date <= today && today <= to.Date;

        var archivedOrders = HistoryArchiveService.LoadOrders(from, to)
            .Where(o => SalesStatsService.BusinessDay(o.PlacedAt) != today);
        var liveOrders = includesToday ? _stats.Orders : Enumerable.Empty<CompletedOrder>();
        Orders = ApplyChannelFilter(archivedOrders.Concat(liveOrders))
            .OrderByDescending(o => o.PlacedAt).ToList();
        Entries = BuildEntries(Orders);
        NoOrders = Entries.Count == 0;
        if (SelectedOrder is not null && !Orders.Contains(SelectedOrder))
            SelectedOrder = Orders.FirstOrDefault(o => o.OrderNumber == SelectedOrder.OrderNumber);
        SyncSelectedEntry();

        var archivedCancellations = HistoryArchiveService.LoadCancellations(from, to)
            .Where(c => SalesStatsService.BusinessDay(c.CancelledAt) != today);
        var liveCancellations = includesToday ? _cancellations.Entries : Enumerable.Empty<CancelledLine>();
        // Ομαδοποίηση ανά (παραγγελία, ακριβές instant) — γραμμές που ακυρώθηκαν μαζί (π.χ. ολόκληρος
        // γύρος) γίνονται μία κάρτα αντί να εμφανίζονται σαν ξεχωριστά προϊόντα.
        CancelledEntries = archivedCancellations.Concat(liveCancellations)
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

        DiscountEntries = Orders.SelectMany(BuildDiscountEntries).ToList();
        NoDiscountEntries = DiscountEntries.Count == 0;

        OnPropertyChanged(nameof(Orders));
        OnPropertyChanged(nameof(NoOrders));
        OnPropertyChanged(nameof(NothingSelected));
        OnPropertyChanged(nameof(CancelledEntries));
        OnPropertyChanged(nameof(NoCancelledEntries));
        OnPropertyChanged(nameof(DiscountEntries));
        OnPropertyChanged(nameof(NoDiscountEntries));
    }
}
