using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.App.Services;
using PittaPos.App.Views;

namespace PittaPos.App.ViewModels;

/// <summary>Κουμπί «πέρασέ την σε» ή στατιστικό καναλιού.</summary>
public class ChannelButtonViewModel
{
    public required string Name { get; init; }
    public required Brush Brush { get; init; }
    public required IRelayCommand Command { get; init; }
    public string CountSumLabel { get; init; } = "";
}

/// <summary>Γραμμή παραγγελίας μέσα σε προβολή καναλιού, με επιλογές αλλαγής.</summary>
public partial class ChannelOrderViewModel : ObservableObject
{
    public required BoardOrder Order { get; init; }
    public required IReadOnlyList<ChannelButtonViewModel> ReassignOptions { get; init; }
    public required IRelayCommand RevertCommand { get; init; }

    /// <summary>Οι επιλογές αλλαγής κρύβονται μέχρι να πατηθεί «ΑΛΛΑΓΗ ΣΕ» — προστασία από κατά λάθος κλικ.</summary>
    [ObservableProperty]
    private bool _showReassignOptions;

    [RelayCommand]
    private void ToggleReassignOptions() => ShowReassignOptions = !ShowReassignOptions;
}

/// <summary>Παράμετρος για το CancelOrderCommand — ποια παραγγελία και ποιος την ακυρώνει.</summary>
public record CancelBoardOrderRequest(BoardOrder Order, string CancelledBy);

/// <summary>Ο πίνακας ΖΩΝΤΑΝΕΣ ΠΑΡΑΓΓΕΛΙΕΣ.</summary>
public partial class LiveOrdersViewModel : ObservableObject
{
    private readonly OrderBoardService _board = OrderBoardService.Instance;
    private readonly DispatcherTimer _timer;

    public LiveOrdersViewModel()
    {
        _board.Changed += Rebuild;
        SettingsStore.Instance.Changed += OnSettingsChanged;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            foreach (var o in _board.Orders)
                o.Tick();
            NowLabel = DateTime.Now.ToString("HH:mm");
        };
        _timer.Start();
        NowLabel = DateTime.Now.ToString("HH:mm");
        Rebuild();
    }

    /// <summary>Τρέχουσα βάρδια — κοινός χειροκίνητος διακόπτης (Αρχική/Ζωντανές Παραγγελίες), ποτέ αυτόματος.</summary>
    public bool IsEveningShift => SettingsStore.Instance.Settings.IsEveningShift;

    private void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(IsEveningShift));
        Rebuild();
    }

    // Ίδια προστασία με την Αρχική (βλ. OrderWizardViewModel): η βάρδια ρωτάει πριν αλλάξει, γιατί
    // σφραγίζει κάθε επόμενη παραγγελία και χωρίζει τα στατιστικά της ημέρας.

    /// <summary>Σε ποια βάρδια ρωτάμε να αλλάξουμε (null = δεν ρωτάμε τώρα).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowShiftPrompt))]
    [NotifyPropertyChangedFor(nameof(ShiftPromptText))]
    private bool? _pendingShift;

    public bool ShowShiftPrompt => PendingShift is not null;

    public string ShiftPromptText => PendingShift is true
        ? "Αλλαγή σε ΒΡΑΔΙΝΗ βάρδια;"
        : "Αλλαγή σε ΠΡΩΙΝΗ βάρδια;";

    [RelayCommand]
    private void SelectMorningShift() => AskShift(false);

    [RelayCommand]
    private void SelectEveningShift() => AskShift(true);

    private void AskShift(bool evening)
    {
        if (IsEveningShift == evening)
            return;
        PendingShift = evening;
    }

    [RelayCommand]
    private void ConfirmShift()
    {
        if (PendingShift is { } evening)
            SettingsStore.Instance.SetShift(evening);
        PendingShift = null;
    }

    [RelayCommand]
    private void CancelShift() => PendingShift = null;

    /// <summary>Καλείται όταν κλείνει το παράθυρο.</summary>
    public void Shutdown()
    {
        _timer.Stop();
        _board.Changed -= Rebuild;
        SettingsStore.Instance.Changed -= OnSettingsChanged;
    }

    [ObservableProperty]
    private string _nowLabel = "";

    [ObservableProperty]
    private BoardOrder? _selectedOrder;

    [ObservableProperty]
    private string? _selectedChannel;

    public IReadOnlyList<BoardOrder> PendingOrders { get; private set; } = [];
    public bool HasPending => PendingOrders.Count > 0;
    public bool NoPending => PendingOrders.Count == 0;

    /// <summary>Πόσα μετρητά πρέπει να έχει πάνω του ο διανομέας αυτή τη στιγμή — άθροισμα των εκκρεμών
    /// ΔΙΑΝΟΜΗ/BOX παραγγελιών με PaymentMethod == Cash (βλ. OrderWizardViewModel.ShowCustomerForm).
    /// Οι παραγγελίες με κάρτα δεν προσθέτουν τίποτα εδώ — τις πληρώνει ο πελάτης απευθείας.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCashOnDelivery))]
    private string _cashOnDeliveryLabel = "";

    public bool HasCashOnDelivery => CashOnDeliveryLabel.Length > 0;

    public bool HasSelectedOrder => SelectedOrder is not null;
    public bool HasSelectedChannel => SelectedChannel is not null;
    public bool NothingSelected => SelectedOrder is null && SelectedChannel is null;

    public IReadOnlyList<ChannelButtonViewModel> DispatchOptions { get; private set; } = [];
    public IReadOnlyList<ChannelButtonViewModel> ChannelStats { get; private set; } = [];
    public IReadOnlyList<ChannelOrderViewModel> ChannelOrders { get; private set; } = [];
    public bool HasChannelOrders => ChannelOrders.Count > 0;
    public bool NoChannelOrders => ChannelOrders.Count == 0;

    [RelayCommand]
    private void SelectOrder(BoardOrder order)
    {
        SelectedOrder = order;
        SelectedChannel = null;
        Rebuild();
    }

    [RelayCommand]
    private void ClearChannel()
    {
        SelectedChannel = null;
        Rebuild();
    }

    /// <summary>Χάρτης διανομής — διαβάζει μόνες της τις παραγγελίες περασμένες σε «Διανομέα»
    /// (βλ. DeliveryMapWindow), ανεξάρτητα ποιο κανάλι είναι επιλεγμένο εδώ.</summary>
    [RelayCommand]
    private static void OpenMap() => new DeliveryMapWindow().Show();

    /// <summary>Τυπώνει την αναφορά διανομέα (βλ. OrderBoardService.BuildDriverReport) — όλες οι
    /// παραγγελίες που πέρασαν σήμερα σε κανάλι διανομέα, μία-μία, με σύνολο τζίρου.</summary>
    [RelayCommand]
    private void PrintDriverReport() => ReceiptPrinter.PrintDriverReport(_board.BuildDriverReport());

    /// <summary>Πραγματική ακύρωση (διαγραφή) μιας ζωντανής παραγγελίας — π.χ. ακύρωσε ο πελάτης. Αφαιρείται
    /// και από τον τζίρο (SalesStatsService, ίδια λογική με το CancelRound στο τραπέζι) και από τον πίνακα.
    /// Μόνο με ονομαστικό κωδικό (βλ. code-behind, StaffPinDialog) — δεν είναι bindable κατευθείαν σε
    /// Command στο XAML, το κουμπί καλεί πρώτα το PIN dialog (βλ. LiveOrdersWindow.xaml.cs).</summary>
    [RelayCommand]
    private void CancelOrder(CancelBoardOrderRequest request)
    {
        SalesStatsService.Instance.RemoveOrder(request.Order.OrderNumber, request.CancelledBy);
        _board.Cancel(request.Order);
        if (SelectedOrder == request.Order)
            SelectedOrder = null;
    }

    private void SelectChannel(string name)
    {
        SelectedChannel = name;
        SelectedOrder = null;
        Rebuild();
    }

    /// <summary>Ποια κανάλια έχει νόημα να προσφέρει το «ΠΕΡΑΣΕ ΤΗΝ ΣΕ»/«ΑΛΛΑΓΗ ΣΕ» για μια παραγγελία —
    /// ακριβώς ΕΝΑ, αυτό που ταιριάζει με τον τύπο/πλατφόρμα ΚΑΙ τον ήδη επιλεγμένο τρόπο πληρωμής (βλ.
    /// BoardOrder.PaymentMethod): ΔΙΑΝΟΜΗ/BOX μετρητά → Διανομέας/BOX Μετρητά, ΔΙΑΝΟΜΗ/BOX κάρτα → Κάρτα
    /// Διανομέα/BOX Κάρτα. e-food/Wolt → μόνο το ίδιο το κανάλι τους, καμία παραλλαγή (τα πληρώνει ο
    /// πελάτης στην πλατφόρμα). Αν λείπει ο τρόπος πληρωμής (παλιά παραγγελία, πριν το feature), δείχνει
    /// και τις δύο παραλλαγές μαζί — δεν μπορούμε να μαντέψουμε ποια είναι η σωστή. Σημείωση: το switch
    /// εδώ κλειδώνει πάνω στο order.Channel (η πλατφόρμα προέλευσης — "BOX" όπως επιλέχθηκε στο βήμα 2
    /// του wizard), ενώ οι τιμές που επιστρέφει είναι τα ονόματα καναλιού αποστολής (ChannelInfo.All) —
    /// γι' αυτό η ετικέτα μετρητών είναι "BOX Μετρητά" ενώ το κλειδί του switch μένει "BOX".</summary>
    private static IReadOnlyList<ChannelInfo> AllowedChannels(BoardOrder order)
    {
        var isApps = order.Type == Core.Models.OrderType.Apps;
        var isCard = order.PaymentMethod == Core.Models.PaymentMethod.Card;
        var unknownPayment = order.PaymentMethod is null;
        var names = (isApps ? order.Channel : null) switch
        {
            "BOX" => unknownPayment ? ["BOX Μετρητά", "BOX Κάρτα"] : [isCard ? "BOX Κάρτα" : "BOX Μετρητά"],
            "e-food" => new[] { "e-food" },
            "Wolt" => new[] { "Wolt" },
            _ => unknownPayment ? ["Διανομέας", "Κάρτα Διανομέα"] : [isCard ? "Κάρτα Διανομέα" : "Διανομέας"],
        };
        return ChannelInfo.All.Where(c => names.Contains(c.Name)).ToList();
    }

    private void Rebuild()
    {
        PendingOrders = _board.Orders.Where(o => o.IsPending).ToList();
        if (SelectedOrder is not null && !SelectedOrder.IsPending)
            SelectedOrder = null;

        var cashTotal = PendingOrders
            .Where(o => o.PaymentMethod == Core.Models.PaymentMethod.Cash)
            .Sum(o => o.Total);
        CashOnDeliveryLabel = cashTotal > 0 ? "💶 Διανομέας: " + Core.Models.Order.FormatPrice(cashTotal) : "";

        DispatchOptions = SelectedOrder is null
            ? []
            : AllowedChannels(SelectedOrder).Select(c => new ChannelButtonViewModel
            {
                Name = c.Name, Brush = c.Brush,
                Command = new RelayCommand(() =>
                {
                    if (SelectedOrder is not null)
                        _board.Dispatch(SelectedOrder, c.Name);
                    SelectedOrder = null;
                }),
            }).ToList();

        ChannelStats = ChannelInfo.All.Select(c =>
        {
            var matchList = _board.Orders
                .Where(o => o.SentVia == c.Name && o.IsEveningShift == IsEveningShift)
                .ToList();
            return new ChannelButtonViewModel
            {
                Name = c.Name, Brush = c.Brush,
                CountSumLabel = matchList.Count + " · " + Core.Models.Order.FormatPrice(matchList.Sum(o => o.Total)),
                Command = new RelayCommand(() => SelectChannel(c.Name)),
            };
        }).ToList();

        ChannelOrders = SelectedChannel is null
            ? []
            : _board.Orders.Where(o => o.SentVia == SelectedChannel)
                .OrderByDescending(o => o.SentAt ?? DateTime.MinValue)
                .Select(o => new ChannelOrderViewModel
                {
                    Order = o,
                    ReassignOptions = AllowedChannels(o).Where(c => c.Name != o.SentVia)
                        .Select(c => new ChannelButtonViewModel
                        {
                            Name = c.Name, Brush = c.Brush,
                            Command = new RelayCommand(() => _board.Reassign(o, c.Name)),
                        }).ToList(),
                    RevertCommand = new RelayCommand(() => _board.RevertToPending(o)),
                }).ToList();

        OnPropertyChanged(nameof(PendingOrders));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(NoPending));
        OnPropertyChanged(nameof(HasSelectedOrder));
        OnPropertyChanged(nameof(HasSelectedChannel));
        OnPropertyChanged(nameof(NothingSelected));
        OnPropertyChanged(nameof(DispatchOptions));
        OnPropertyChanged(nameof(ChannelStats));
        OnPropertyChanged(nameof(ChannelOrders));
        OnPropertyChanged(nameof(HasChannelOrders));
        OnPropertyChanged(nameof(NoChannelOrders));
    }

    partial void OnSelectedOrderChanged(BoardOrder? value) => OnPropertyChanged(nameof(HasSelectedOrder));
    partial void OnSelectedChannelChanged(string? value) => OnPropertyChanged(nameof(HasSelectedChannel));
}
