using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.App.Services;
using PittaPos.Core.Models;

namespace PittaPos.App.ViewModels;

/// <summary>Ποια λίστα δείχνει το ιστορικό αυτή τη στιγμή.</summary>
public enum HistoryTab { Orders, Cancelled, Discounts }

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

    /// <summary>Πραγματική διαγραφή της επιλεγμένης παραγγελίας — αφαιρείται από τον τζίρο, καταγράφεται στα ακυρωμένα.</summary>
    [RelayCommand]
    private void DeleteOrder(string cancelledBy)
    {
        if (SelectedOrder is null)
            return;
        _stats.RemoveOrder(SelectedOrder.OrderNumber, cancelledBy);
        SelectedOrder = null;
    }

    /// <summary>Εμφανίζει/κρύβει τις επιλογές τρόπου πληρωμής.</summary>
    [RelayCommand]
    private void TogglePaymentOptions() => ShowPaymentOptions = !ShowPaymentOptions;

    /// <summary>Διόρθωση τρόπου πληρωμής — π.χ. ο ταμίας πάτησε κατά λάθος Μετρητά αντί για Κάρτα στην ώρα
    /// της παραγγελίας, ή δεν είχε καταχωρηθεί καθόλου. Μόνο σημερινές παραγγελίες αλλάζουν πραγματικά
    /// (βλ. SalesStatsService.UpdatePaymentMethod).</summary>
    [RelayCommand]
    private void SetPaymentMethodCash()
    {
        if (SelectedOrder is not null)
            _stats.UpdatePaymentMethod(SelectedOrder.OrderNumber, Core.Models.PaymentMethod.Cash);
        ShowPaymentOptions = false;
    }

    [RelayCommand]
    private void SetPaymentMethodCard()
    {
        if (SelectedOrder is not null)
            _stats.UpdatePaymentMethod(SelectedOrder.OrderNumber, Core.Models.PaymentMethod.Card);
        ShowPaymentOptions = false;
    }

    /// <summary>Εμφανίζει/κρύβει τις επιλογές διόρθωσης καναλιού.</summary>
    [RelayCommand]
    private void ToggleChannelOptions() => ShowChannelOptions = !ShowChannelOptions;

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

    private void Refresh()
    {
        var from = FromDate ?? DateTime.MinValue;
        var to = ToDate ?? DateTime.MaxValue;
        var today = SalesStatsService.BusinessDay(DateTime.Now);
        var includesToday = from.Date <= today && today <= to.Date;

        var archivedOrders = HistoryArchiveService.LoadOrders(from, to)
            .Where(o => SalesStatsService.BusinessDay(o.PlacedAt) != today);
        var liveOrders = includesToday ? _stats.Orders : Enumerable.Empty<CompletedOrder>();
        Orders = archivedOrders.Concat(liveOrders).OrderByDescending(o => o.PlacedAt).ToList();
        NoOrders = Orders.Count == 0;
        if (SelectedOrder is not null && !Orders.Contains(SelectedOrder))
            SelectedOrder = Orders.FirstOrDefault(o => o.OrderNumber == SelectedOrder.OrderNumber);

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
