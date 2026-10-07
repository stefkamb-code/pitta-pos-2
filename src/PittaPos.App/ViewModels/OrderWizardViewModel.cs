using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.App.Services;
using PittaPos.Core.Data;
using PittaPos.Core.Models;

namespace PittaPos.App.ViewModels;

/// <summary>Βήμα στο stepper.</summary>
public class StepItemViewModel
{
    public int Number { get; init; }
    public required string Label { get; init; }
    public bool IsActive { get; init; }
    public bool IsReachable { get; init; }
    public bool ShowArrow { get; init; }
}

/// <summary>Κάρτα τύπου παραγγελίας στο Βήμα 1.</summary>
public partial class OrderTypeOptionViewModel : ObservableObject
{
    public required OrderType Key { get; init; }
    public required string Label { get; init; }
    public required string Sub { get; init; }

    /// <summary>Οι πλατφόρμες με τα χρώματά τους — μόνο η κάρτα ΕΦΑΡΜΟΓΕΣ τις έχει. Όπου είναι null
    /// γράφεται το απλό γκρίζο <see cref="Sub"/>, όπως και πριν.</summary>
    public IReadOnlyList<AppMethodViewModel>? Platforms { get; init; }
    public bool HasPlatforms => Platforms is { Count: > 0 };

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Κάρτα τραπεζιού στο Βήμα 1 — ελεύθερο, ή ανοιχτό με τρέχον σύνολο.</summary>
public partial class TableOptionViewModel : ObservableObject
{
    public int Number { get; init; }
    public bool IsOpen { get; init; }
    public decimal Total { get; init; }
    public int RoundCount { get; init; }
    public DateTime? LastOrderAt { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Θέση στην κάτοψη — ο χρήστης τη σέρνει ελεύθερα (βλ. TableLayoutService).</summary>
    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    public string TotalLabel => Order.FormatPrice(Total);
    public string RoundCountLabel => RoundCount == 1 ? "1 παραγγελία" : RoundCount + " παραγγελίες";
    public string LastOrderLabel => LastOrderAt is { } t ? "τελευταία " + t.ToString("HH:mm") : "";
}

/// <summary>Πλατφόρμα εφαρμογών (Βήμα 4) με το χρώμα της.</summary>
public partial class AppMethodViewModel : ObservableObject
{
    public required string Name { get; init; }
    public required Brush Color { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}

public partial class PickupTimeViewModel : ObservableObject
{
    public required string Label { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}

public class CustomerMatchViewModel
{
    public required Customer Customer { get; init; }
    public string Name => Customer.Name;
    public string Detail => string.Join(" · ",
        new[] { Customer.Phone, Customer.Address, Customer.Area }.Where(s => s.Length > 0))
        + (Customer.OtherAddresses.Count > 0 ? "  ·  +" + Customer.OtherAddresses.Count + " ακόμη διεύθυνση" : "");
}

/// <summary>Μία επιλογή στο picker αποθηκευμένων διευθύνσεων πελάτη (Βήμα 2) — η κύρια διεύθυνση του
/// Customer μαζί με όσες άλλες έχει (βλ. Customer.OtherAddresses).</summary>
public class CustomerAddressOptionViewModel
{
    public required string Label { get; init; }
    public required string Address { get; init; }
    public required string StreetNumber { get; init; }
    public required string Area { get; init; }
    public required string PostalCode { get; init; }
    public required string Floor { get; init; }
    /// <summary>Αν διαγραφεί, η κύρια διεύθυνση χρειάζεται διαφορετικό χειρισμό (προαγωγή επόμενης, βλ.
    /// CustomerStore.RemoveMainAddress) από τις άλλες (CustomerStore.RemoveOtherAddress).</summary>
    public required bool IsMain { get; init; }
}

/// <summary>Το 5-βημα wizard παραγγελίας — κατέχει την κατάσταση και τη ροή.</summary>
/// <summary>Ένα άτομο που έχει ήδη περάσει στο τρέχον τραπέζι — μόνο για εμφάνιση στη δεξιά στήλη της
/// παραγγελιοληψίας. Η παραγγελία του έχει ήδη καταχωρηθεί και δεν πειράζεται από εκεί.</summary>
public sealed class SentPersonViewModel
{
    /// <summary>Ποιο άτομο είναι (0-based) — πατώντας το, ο ταμίας γυρνά σε αυτό για να προσθέσει κι
    /// άλλα, όταν κάποιος αλλάξει γνώμη.</summary>
    public required int Person { get; init; }
    public required string Header { get; init; }
    /// <summary>Το σύνολο ΟΛΩΝ όσων έχει πάρει το άτομο — και από δεύτερη/τρίτη προσθήκη.</summary>
    public required decimal Total { get; init; }
    public required List<SentPersonLineViewModel> Lines { get; init; }

    public string TotalLabel => Order.FormatPrice(Total);

    /// <summary>Άτομο που φαίνεται μόνο για να πατηθεί (έχει παραγγείλει σε προηγούμενο γύρο, δεν του
    /// γράφτηκε τίποτα τώρα): δεν δείχνει «0,00 €» — θα διαβαζόταν σαν να μην πήρε ποτέ τίποτα.</summary>
    public bool ShowTotal => Lines.Count > 0;
}

/// <summary>Μία γραμμή ενός ήδη περασμένου ατόμου.</summary>
public sealed record SentPersonLineViewModel(string Name, string PriceLabel);

/// <summary>
/// Το καλάθι ενός ατόμου όσο γράφεται το τραπέζι. ΤΙΠΟΤΑ δεν έχει καταχωρηθεί ακόμα: μένει εδώ, στη
/// μνήμη, μέχρι να κλείσει το τελευταίο άτομο — τότε καταχωρούνται όλα μαζί (βλ. CommitTablePersons).
/// Έτσι ο ταμίας μπαίνει ξανά σε όποιον θέλει και τον ΕΠΕΞΕΡΓΑΖΕΤΑΙ, χωρίς ακυρώσεις.
/// </summary>
internal sealed class PersonDraftViewModel
{
    /// <summary>Οι ίδιες γραμμές που δείχνει η δεξιά στήλη — μπαινοβγαίνουν στο Products.Cart αυτούσιες,
    /// γι' αυτό και ξαναγίνονται ζωντανά επεξεργάσιμες (ποσότητα, έξτρα, σβήσιμο).</summary>
    public required List<CartLineViewModel> Lines { get; init; }
    public required decimal Total { get; init; }
}

public partial class OrderWizardViewModel : ObservableObject
{
    public OrderWizardViewModel()
    {
        SettingsStore.Instance.TouchLayoutChanged += () => OnPropertyChanged(nameof(IsTouchLayout));
        Products = new ProductsViewModel
        {
            ContinueRequested = ContinueStep3,
            BackRequested = BackStep3,
        };

        // Σειρά κατά συχνότητα χρήσης στο μαγαζί: όρθιος πελάτης πρώτος, διανομή τελευταία.
        OrderTypeOptions =
        [
            new() { Key = Core.Models.OrderType.Pickup, Label = "ΟΡΘΙΟΣ", Sub = "Από το κατάστημα" },
            new() { Key = Core.Models.OrderType.Table, Label = "ΤΡΑΠΕΖΙ", Sub = "Επί τόπου" },
            // Οι τρεις πλατφόρμες με τα χρώματά τους αντί για μια γκρίζα γραμμή κειμένου: το μάτι τις
            // πιάνει αμέσως, και είναι τα ίδια χρώματα με το Βήμα 2 και τα κανάλια των Ζωντανών.
            // Ξεχωριστά αντικείμενα από το AppMethods παρακάτω — κοινά, το πάτημα στο Βήμα 2 θα
            // «άναβε» και την κάρτα της αρχικής (κοινό IsSelected).
            new() { Key = Core.Models.OrderType.Apps, Label = "ΕΦΑΡΜΟΓΕΣ", Sub = "e-food · Wolt · BOX",
                    Platforms = NewAppMethods() },
            new() { Key = Core.Models.OrderType.Delivery, Label = "ΔΙΑΝΟΜΗ", Sub = "Ίδιος διανομέας" },
        ];
        TableNumbers = [];
        RebuildTableNumbers();
        SettingsStore.Instance.Changed += RebuildTableNumbers;
        SettingsStore.Instance.Changed += () => OnPropertyChanged(nameof(IsEveningShift));
        SalesStatsService.Instance.Changed += RebuildTableNumbers;
        TableStatusService.Instance.Changed += RebuildTableNumbers;
        TableSettlementService.Instance.Changed += RebuildTableNumbers;
        TableLayoutService.Instance.Changed += RebuildTableNumbers;
        AppMethods = [.. NewAppMethods()];
        // Καθαρή σειρά αντικειμένων κάθε φορά: το IsSelected είναι κατάσταση της ΟΘΟΝΗΣ, οπότε δύο
        // σημεία που δείχνουν τις ίδιες πλατφόρμες δεν πρέπει να μοιράζονται τα ίδια αντικείμενα.
        static List<AppMethodViewModel> NewAppMethods() =>
        [
            new() { Name = "e-food", Color = new SolidColorBrush(Color.FromRgb(0xd3, 0x2f, 0x2f)) },
            new() { Name = "Wolt", Color = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xc0)) },
            new() { Name = "BOX", Color = new SolidColorBrush(Color.FromRgb(0xb8, 0x86, 0x0b)) },
        ];
        PickupTimes = new ObservableCollection<PickupTimeViewModel>(
            MenuSeed.PickupTimes.Select(t => new PickupTimeViewModel { Label = t }));

        Products.OrderNumber = NextDisplayNumber();
    }

    /// <summary>Τρέχουσα βάρδια — κοινός χειροκίνητος διακόπτης (Αρχική/Ζωντανές Παραγγελίες), ποτέ αυτόματος.</summary>
    public bool IsEveningShift => SettingsStore.Instance.Settings.IsEveningShift;

    // Η αλλαγή βάρδιας ΡΩΤΑΕΙ πρώτα. Δεν είναι διακοσμητικός διακόπτης: σφραγίζει κάθε επόμενη
    // παραγγελία, χωρίζει τα στατιστικά της ημέρας και φιλτράρει τις Ζωντανές Παραγγελίες — ένα κατά
    // λάθος πάτημα στη μέση της βάρδιας στέλνει τις παραγγελίες στη λάθος μεριά της ημέρας, και
    // φαίνεται μόνο στο κλείσιμο, όταν πια δεν διορθώνεται.

    /// <summary>Σε ποια βάρδια ρωτάμε να αλλάξουμε (null = δεν ρωτάμε τώρα) — ανάβει το banner.</summary>
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

    /// <summary>Πάτημα στη βάρδια που ΗΔΗ τρέχει δεν ρωτάει τίποτα — δεν αλλάζει τίποτα.</summary>
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

    /// <summary>
    /// Ξαναφτιάχνει τη λίστα τραπεζιών από τις ρυθμίσεις — ελεύθερο/ανοιχτό, τρέχον σύνολο,
    /// κρατά επιλεγμένο ό,τι υπάρχει ακόμα.
    /// </summary>
    private void RebuildTableNumbers()
    {
        var selected = TableNumber;
        var orders = SalesStatsService.Instance.Orders.Where(o => o.Type == Core.Models.OrderType.Table).ToList();
        var openSince = TableStatusService.Instance.OpenSince;

        TableNumbers.Clear();
        // Ένα ΑΝΟΙΧΤΟ τραπέζι φαίνεται πάντα, ακόμα κι αν ο αριθμός τραπεζιών μειώθηκε στο μεταξύ από
        // τις Ρυθμίσεις. Αλλιώς έβγαινε εκτός λίστας με τα λεφτά του μέσα: ο ταμίας δεν μπορούσε ούτε
        // να το ανοίξει, ούτε να το εξοφλήσει, ούτε να το κλείσει — και το ποσό έμενε «ανεξόφλητο»
        // στην αναφορά ημέρας χωρίς πουθενά να φαίνεται από πού κρέμεται.
        var numbers = Enumerable.Range(1, SettingsStore.Instance.Settings.TableCount)
            .Concat(openSince.Keys)
            .Distinct()
            .Order();
        foreach (var n in numbers)
        {
            var isOpen = openSince.TryGetValue(n, out var since);
            // Μόνο οι γύροι από το τρέχον άνοιγμα του τραπεζιού — όχι παλιότερος πελάτης της ίδιας μέρας
            var mine = isOpen
                ? orders.Where(o => o.Who == "Τραπέζι " + n && o.PlacedAt >= since).ToList()
                : [];
            var pos = TableLayoutService.Instance.GetPosition(n);
            TableNumbers.Add(new TableOptionViewModel
            {
                Number = n,
                IsSelected = n == selected,
                IsOpen = isOpen,
                Total = OutstandingTotal(n, mine),
                RoundCount = mine.Count,
                LastOrderAt = mine.Count > 0 ? mine.Max(o => o.PlacedAt) : null,
                X = pos.X,
                Y = pos.Y,
            });
        }
    }

    /// <summary>
    /// Άθροισμα ό,τι δεν έχει εξοφληθεί ακόμα — όχι το αρχικό σύνολο. Ανά <b>ΤΕΜΑΧΙΟ</b> και όχι ανά
    /// γραμμή: οι εξοφλήσεις γράφονται πάντα ανά τεμάχιο (βλ. TableSettlementService.Settle), οπότε ο
    /// παλιός έλεγχος «ανά γραμμή» δεν τις έβλεπε ΠΟΤΕ και η κάτοψη κρατούσε το τραπέζι στο πλήρες ποσό
    /// ακόμα κι αφού είχε πληρώσει ένα άτομο. Ίδιος υπολογισμός με το κινητό (WaiterApiService).
    /// </summary>
    private static decimal OutstandingTotal(int table, List<CompletedOrder> orders) => orders.Sum(o =>
        o.Lines.Select((l, i) => (l, i)).Sum(x =>
        {
            var units = Math.Max(1, x.l.Quantity);
            var unpaid = units - TableSettlementService.Instance.SettledUnits(table, o.OrderNumber, x.i, units);
            return x.l.Revenue / units * unpaid;
        }));

    /// <summary>Λειτουργία «σύρε τα τραπέζια όπου θέλεις» στην κάτοψη, αντί για επιλογή τραπεζιού.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArrangeButtonLabel))]
    private bool _isArrangingTables;

    public string ArrangeButtonLabel => IsArrangingTables ? "✓ ΤΕΛΟΣ ΔΙΑΤΑΞΗΣ" : "✥ ΔΙΑΤΑΞΗ ΤΡΑΠΕΖΙΩΝ";

    [RelayCommand]
    private void ToggleArrangeTables() => IsArrangingTables = !IsArrangingTables;

    /// <summary>Καλείται από το MainWindow όταν ο χρήστης αφήνει ένα σερμένο τραπέζι — αποθηκεύει τη νέα θέση.</summary>
    public void SaveTablePosition(TableOptionViewModel table) =>
        TableLayoutService.Instance.SetPosition(table.Number, table.X, table.Y);

    public ProductsViewModel Products { get; }
    public OrderBoardService Board => OrderBoardService.Instance;
    public IncomingCallService IncomingCall => IncomingCallService.Instance;

    /// <summary>Ξεκινά καθαρή παραγγελία ΔΙΑΝΟΜΗΣ προσυμπληρωμένη από μια ειδοποίηση κλήσης — ο ταμίας
    /// βλέπει/διορθώνει τα στοιχεία στο Βήμα 2 πριν προχωρήσει (π.χ. αν άλλαξε διεύθυνση). Δουλεύει
    /// το ίδιο σε host και client — το CustomerStore είναι ήδη συγχρονισμένο και στα δύο.</summary>
    [RelayCommand]
    private void StartOrderFromIncomingCall(IncomingCallItem call)
    {
        var phone = call.Phone;
        var customer = CustomerStore.Instance.FindByPhone(phone);
        IncomingCallService.Instance.Dismiss(call.Id);

        ResetForm();
        var option = OrderTypeOptions.First(o => o.Key == Core.Models.OrderType.Delivery);
        SelectOrderType(option);

        CustomerPhone = phone;
        if (customer is not null)
        {
            // Συμπλήρωση από αποθηκευμένα στοιχεία πελάτη, όχι πληκτρολόγηση — δεν πρέπει να ανοίξουν οι
            // προτάσεις (ίδιο πρόβλημα με SelectCustomer/SelectCustomerAddressOption).
            _suppressSuggestions = true;
            CustomerName = customer.Name;
            CustomerAddress = customer.Address;
            CustomerArea = customer.Area;
            _suppressSuggestions = false;
            CustomerStreetNumber = customer.StreetNumber;
            CustomerPostalCode = customer.PostalCode;
            CustomerFloor = customer.Floor;
            CustomerNotes = customer.Notes;
            CustomerMemo = customer.Memo;
            LoadCustomerAddressOptions(customer);
        }
    }

    [RelayCommand]
    private void DismissIncomingCall(IncomingCallItem call) => IncomingCallService.Instance.Dismiss(call.Id);

    public ObservableCollection<OrderTypeOptionViewModel> OrderTypeOptions { get; }
    public ObservableCollection<TableOptionViewModel> TableNumbers { get; }
    public ObservableCollection<AppMethodViewModel> AppMethods { get; }
    public ObservableCollection<PickupTimeViewModel> PickupTimes { get; }

    /// <summary>
    /// Ο αριθμός που ΔΕΙΧΝΕΙ η οθόνη όσο γράφεται η παραγγελία. Υπολογίζεται κάθε φορά από τη μία
    /// πηγή αλήθειας (SalesStatsService.NextOrderNumber) αντί για δικό του μετρητή που αυξανόταν
    /// τυφλά: έτσι δεν αποκλίνει από την πραγματικότητα μέσα στη βάρδια (π.χ. όταν έρθουν στο ενδιάμεσο
    /// παραγγελίες από το κινητό ή το δεύτερο ταμείο). Ο ΟΡΙΣΤΙΚΟΣ αριθμός δεσμεύεται πάντα ξανά, την
    /// τελευταία στιγμή, στο ContinueStep3 — αυτός εδώ είναι μόνο για εμφάνιση.
    /// </summary>
    /// <remarks>Πριν διαλεγεί τύπος, δείχνουμε τη σειρά της βάρδιας (ΟΡΘΙΟΣ) — είναι η πιο συχνή
    /// περίπτωση και ο αριθμός ξαναδεσμεύεται σωστά μόλις πατηθεί ο τύπος (βλ. SelectOrderType).</remarks>
    /// <remarks>Ίδιο κριτήριο με το CompletedOrder.HasOwnNumber — το BOX χωρίς κωδικό μπαίνει κι αυτό
    /// στη σειρά της βάρδιας. Ο οριστικός αριθμός δεσμεύεται στο ContinueStep3, δηλαδή ΜΕΤΑ το Βήμα 2
    /// όπου πληκτρολογείται ο κωδικός, οπότε εκεί το κριτήριο είναι ήδη σωστό.</remarks>
    private int NextDisplayNumber() => SalesStatsService.Instance.NextOrderNumber(
        OrderType == Core.Models.OrderType.Table
        || (OrderType == Core.Models.OrderType.Apps && AppOrderRef.Trim().Length > 0));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(IsStep4))]
    [NotifyPropertyChangedFor(nameof(IsStep5))]
    [NotifyPropertyChangedFor(nameof(StepItems))]
    [NotifyPropertyChangedFor(nameof(ShowHeader))]
    [NotifyPropertyChangedFor(nameof(OrderContextLabel))]
    private int _step = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepItems))]
    private int _maxStep = 1;

    /// <summary>Καταχωρεί/ενημερώνει την παραγγελία στα στατιστικά όταν ολοκληρώνεται.</summary>
    /// <summary>Καταγράφει την παραγγελία και επιστρέφει ΤΗΝ ΤΕΛΙΚΗ μορφή της — με τον αριθμό που
    /// όντως πήρε. Ο καλών τη χρειάζεται για να χρεώσει τα τεμάχια σε άτομα και για το δελτίο κουζίνας
    /// (βλ. CompleteOrder, τραπέζι με πολλά άτομα).</summary>
    private CompletedOrder RecordStats()
    {
        _orderCompleted = true;

        // Καταγραφή στο μόνιμο ιστορικό του πελάτη (διανομή/εφαρμογές) — και ενημέρωση προφίλ
        if (OrderType is Core.Models.OrderType.Delivery or Core.Models.OrderType.Apps
            && CustomerName.Trim().Length > 0)
        {
            CustomerStore.Instance.RecordOrder(CustomerName.Trim(), CustomerPhone.Trim(), CustomerAddress.Trim(),
                CustomerStreetNumber.Trim(), CustomerArea.Trim(), CustomerPostalCode.Trim(), CustomerFloor.Trim(),
                CustomerNotes.Trim(), Products.Total, Products.Cart.Select(l => (l.Name, l.Quantity)).ToList());
        }

        // Πρέπει να ανοίξει ΠΡΙΝ καταγραφεί η παραγγελία — αλλιώς η ώρα ανοίγματος μπορεί να βγει
        // (λόγω I/O) ελάχιστα μετά το PlacedAt της ίδιας της παραγγελίας και να μη μετρήσει στο σύνολο.
        if (OrderType == Core.Models.OrderType.Table && TableNumber is { } table)
            TableStatusService.Instance.MarkOpen(table);

        var order = new CompletedOrder
        {
            OrderNumber = Products.OrderNumber,
            Type = OrderType ?? Core.Models.OrderType.Delivery,
            Channel = OrderType == Core.Models.OrderType.Apps ? AppPlatform : null,
            AppOrderRef = OrderType == Core.Models.OrderType.Apps && AppOrderRef.Trim().Length > 0 ? AppOrderRef.Trim() : null,
            PaymentMethod = NeedsPaymentMethod ? SelectedPaymentMethod : null,
            Who = OrderType == Core.Models.OrderType.Table
                ? "Τραπέζι " + TableNumber
                : CustomerName.Trim(),
            // Ποιανού είναι η απόδειξη — μόνο σε τραπέζι που πληρώνει χωριστά. Το Ιστορικό το χρειάζεται
            // για να διορθώνει μετρητά/κάρτα ΕΝΟΣ ατόμου αφού κλείσει το τραπέζι (βλ. CompletedOrder.TablePerson).
            TablePerson = OrderType == Core.Models.OrderType.Table && IsSplittingTable ? TablePerson : null,
            // Σε ποιο άνοιγμα του τραπεζιού ανήκει — το MarkOpen από πάνω το έχει ήδη εξασφαλίσει.
            TableOpenedAt = OrderType == Core.Models.OrderType.Table && TableNumber is { } t
                && TableStatusService.Instance.OpenSince.TryGetValue(t, out var openedAt)
                    ? openedAt
                    : null,
            Phone = ShowCustomerForm ? CustomerPhone.Trim() : "",
            // Ίδια λογική με το PushBoardOrder — μόνο ΔΙΑΝΟΜΗ/BOX έχουν δικά μας στοιχεία παράδοσης
            // (Wolt/e-food τα έχει ήδη η πλατφόρμα, ΤΡΑΠΕΖΙ/ΠΑΡΑΛΑΒΗ δεν έχουν καν πεδία διεύθυνσης).
            // Ξεχωριστά πεδία (όχι μία σύνθετη πρόταση) — η απόδειξη τα τυπώνει σε ξεχωριστές γραμμές.
            DeliveryAddress = ShowCustomerForm ? ComposeAddressLine() : "",
            DeliveryFloor = ShowCustomerForm ? CustomerFloor.Trim() : "",
            // Το CustomerNotes ΔΕΝ περιορίζεται σε ΔΙΑΝΟΜΗ/BOX σαν τα παραπάνω — το ίδιο πεδίο γεμίζει και
            // από το κουμπί «💬 ΣΧΟΛΙΑ ΠΑΡΑΓΓΕΛΙΑΣ» στα Προϊόντα για ΤΡΑΠΕΖΙ/ΠΑΡΑΛΑΒΗ/ΕΦΑΡΜΟΓΕΣ (βλ.
            // ShowOrderNoteButton, ProductsView.xaml) — πριν αυτό το fix, εκείνα τα σχόλια καταχωρούνταν
            // αλλά δεν τυπώνονταν ποτέ στην απόδειξη.
            DeliveryNotes = CustomerNotes.Trim(),
            Total = Products.Total,
            Lines = Products.Cart
                .Select(l => new SoldLine(l.Name, l.Quantity, l.Total,
                    string.Join("\n", new[] { l.DescLine1, l.DescLine2 }.Where(s => s.Length > 0)),
                    l.ProductId, l.Customization, l.PrintName))
                .ToList(),
            IsEveningShift = SettingsStore.Instance.Settings.IsEveningShift,
        };

        // Ο αριθμός μπορεί να αλλάξει αν στο μεταξύ τον πήρε το κινητό του σερβιτόρου.
        var number = SalesStatsService.Instance.Record(order);
        return number == order.OrderNumber ? order : SalesStatsService.WithOrderNumber(order, number);
    }

    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;

    /// <summary>Η οθόνη παραγγελίας για οθόνη αφής (βλ. AppSettings.TouchLayout) αντί για τη λίστα του ποντικιού.</summary>
    public bool IsTouchLayout => SettingsStore.Instance.Settings.TouchLayout;
    public bool IsStep4 => Step == 4;
    public bool IsStep5 => Step == 5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTableGrid))]
    [NotifyPropertyChangedFor(nameof(ShowOrderTypeCards))]
    [NotifyPropertyChangedFor(nameof(ShowHeader))]
    [NotifyPropertyChangedFor(nameof(ShowAddressField))]
    [NotifyPropertyChangedFor(nameof(OrderContextLabel))]
    [NotifyPropertyChangedFor(nameof(IsPickupOrder))]
    [NotifyPropertyChangedFor(nameof(Step4Title))]
    [NotifyPropertyChangedFor(nameof(Step4Sub))]
    [NotifyPropertyChangedFor(nameof(ReceiptTypeLabel))]
    [NotifyPropertyChangedFor(nameof(ReceiptWhoLabel))]
    [NotifyPropertyChangedFor(nameof(ReceiptWho))]
    [NotifyPropertyChangedFor(nameof(StepItems))]
    [NotifyPropertyChangedFor(nameof(ShowOrderNoteButton))]
    [NotifyPropertyChangedFor(nameof(ShowCustomerForm))]
    [NotifyPropertyChangedFor(nameof(ShowAppPlatformPicker))]
    [NotifyPropertyChangedFor(nameof(ShowStep2ContinueButton))]
    [NotifyPropertyChangedFor(nameof(Step2ContinueEnabled))]
    [NotifyPropertyChangedFor(nameof(Step2Header))]
    [NotifyPropertyChangedFor(nameof(Step2Sub))]
    [NotifyPropertyChangedFor(nameof(DisplayOrderNumber))]
    private OrderType? _orderType;

    /// <summary>Το κουμπί «💬 ΣΧΟΛΙΑ ΠΑΡΑΓΓΕΛΙΑΣ» μέσα στα Προϊόντα υπάρχει σε <b>ΚΑΘΕ</b> είδος
    /// παραγγελίας (ζητήθηκε ρητά — πριν το είχαν μόνο όσα δεν έχουν Βήμα 2, δηλαδή
    /// ΤΡΑΠΕΖΙ/ΟΡΘΙΟΣ/ΕΦΑΡΜΟΓΕΣ). Γράφει στο ίδιο <see cref="CustomerNotes"/> με το Βήμα 2, οπότε σε
    /// ΔΙΑΝΟΜΗ/BOX βλέπεις εκεί ό,τι έχει ήδη γραφτεί και μπορείς να το συμπληρώσεις — δεν υπάρχουν
    /// δύο διαφορετικά σχόλια.</summary>
    public bool ShowOrderNoteButton => true;

    /// <summary>Βήμα 2 για ΔΙΑΝΟΜΗ, και για BOX (βλ. SelectAppMethod) — οι δύο περιπτώσεις που πραγματικά
    /// χρειάζονται στοιχεία πελάτη (όνομα/διεύθυνση), γιατί τις παραδίδει δικός μας διανομέας. Ο τρόπος
    /// πληρωμής πλέον ρωτιέται στο Βήμα 3 (Προϊόντα, βλ. ProductsView) — όχι εδώ.</summary>
    public bool ShowCustomerForm => OrderType == Core.Models.OrderType.Delivery
        || (OrderType == Core.Models.OrderType.Apps && AppPlatform == "BOX");

    /// <summary>
    /// Ποιοι τύποι παραγγελίας ρωτάνε τρόπο πληρωμής στο τέλος (βλ. ShowPaymentPrompt/ContinueStep3):
    /// <b>μόνο ΔΙΑΝΟΜΗ και BOX</b> (βλ. ShowCustomerForm) — εκεί ο διανομέας πρέπει να ξέρει τι θα
    /// εισπράξει, και το ποσό κρέμεται πάνω του μέχρι να γυρίσει.
    ///
    /// <para>Ο <b>ΟΡΘΙΟΣ βγήκε</b> (ζητήθηκε ρητά): ο πελάτης είναι μπροστά στο ταμείο και πληρώνει
    /// εκείνη τη στιγμή — η ερώτηση δεν έλυνε κανένα πρόβλημα, απλώς πρόσθετε ένα πάτημα σε κάθε
    /// παραγγελία της ουράς. Τα λεφτά του παίρνουν πλέον δική τους γραμμή στην αναφορά ημέρας (βλ.
    /// DayReportService.AppendPrintSummary), ώστε να μη μετρηθούν αυθαίρετα σαν μετρητά ούτε να
    /// εμφανιστούν σαν «ανεξόφλητα».</para>
    ///
    /// <para>ΤΡΑΠΕΖΙ/e-food/Wolt δεν το χρειάζονται ούτως ή άλλως.</para>
    /// </summary>
    public bool NeedsPaymentMethod => ShowCustomerForm;

    /// <summary>Βήμα 2 για ΕΦΑΡΜΟΓΕΣ — αντί για στοιχεία πελάτη (τα έχει ήδη η ίδια η εφαρμογή), επιλογή
    /// πλατφόρμας πρώτα-πρώτα, πριν τα προϊόντα (βλ. SelectAppMethod). Το πεδίο αριθμού παραγγελίας
    /// (βλ. AppOrderRef) φαίνεται εδώ για όλες τις πλατφόρμες — υποχρεωτικό για Wolt/e-food, προαιρετικό
    /// για BOX (βλ. Step2ContinueEnabled).</summary>
    public bool ShowAppPlatformPicker => OrderType == Core.Models.OrderType.Apps;

    /// <summary>Το κουμπί «ΣΥΝΕΧΕΙΑ» χρειάζεται πλέον και οι ΕΦΑΡΜΟΓΕΣ — πριν, το Wolt/e-food προχωρούσε
    /// αυτόματα με το πάτημα της πλατφόρμας· τώρα ο αριθμός παραγγελίας είναι υποχρεωτικός γι' αυτά (βλ.
    /// Step2ContinueEnabled), άρα ο ταμίας πρέπει πρώτα να τον γράψει και μετά να πατήσει ΣΥΝΕΧΕΙΑ.</summary>
    public bool ShowStep2ContinueButton => OrderType is Core.Models.OrderType.Delivery or Core.Models.OrderType.Apps;

    /// <summary>Ο αριθμός που δείχνει η οθόνη Προϊόντων πάνω δεξιά (και η απόδειξη) — για ΕΦΑΡΜΟΓΕΣ με
    /// δηλωμένο αριθμό πλατφόρμας, αυτός είναι ο κύριος, ίδια λογική με BoardOrder.DisplayNumber, από
    /// πριν καν καταχωρηθεί η παραγγελία στον πίνακα· διαφορετικά ο εσωτερικός μας μετρητής, όπως πάντα.</summary>
    public string DisplayOrderNumber => OrderType == Core.Models.OrderType.Apps && AppOrderRef.Trim().Length > 0
        ? AppOrderRef.Trim()
        // ΤΡΑΠΕΖΙ: ο αριθμός τραπεζιού, ίδιος με ό,τι θα τυπώσει η απόδειξη και θα δείξει το Ιστορικό
        // (βλ. CompletedOrder.DisplayNumber). Χωρίς αυτό η κεφαλίδα έγραφε τον εσωτερικό «#1001».
        : OrderType == Core.Models.OrderType.Table && TableNumber is { } table
            ? table.ToString()
            : Products.OrderNumber < SalesStatsService.ExternalBandStart
                ? Products.OrderNumber.ToString("00")
                : Products.OrderNumber.ToString();

    public string Step2Header => ShowAppPlatformPicker ? "Ποια εφαρμογή;" : "Στοιχεία πελάτη";
    public string Step2Sub => ShowAppPlatformPicker
        ? "Επίλεξε την πλατφόρμα παράδοσης"
        : "Αναζήτησε με τηλέφωνο ή όνομα, ή συμπλήρωσε νέα στοιχεία";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrderContextLabel))]
    [NotifyPropertyChangedFor(nameof(ReceiptTypeLabel))]
    [NotifyPropertyChangedFor(nameof(ReceiptWho))]
    [NotifyPropertyChangedFor(nameof(DisplayOrderNumber))]
    private int? _tableNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomerMatches))]
    [NotifyPropertyChangedFor(nameof(HasCustomerMatches))]
    private string _customerSearch = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Step2ContinueEnabled))]
    [NotifyPropertyChangedFor(nameof(OrderContextLabel))]
    [NotifyPropertyChangedFor(nameof(ReceiptWho))]
    private string _customerName = "";

    /// <summary>
    /// Το ονοματεπώνυμο σε δύο κελιά, μόνο για την οθόνη. Από κάτω παραμένει ΕΝΑ πεδίο
    /// (<see cref="CustomerName"/>): έτσι αποθηκεύεται στους πελάτες, ψάχνεται στην αναζήτηση,
    /// τυπώνεται στην απόδειξη και φαίνεται στον πίνακα παραγγελιών — τίποτα από όσα δουλεύουν ήδη
    /// δεν χρειάστηκε να αλλάξει.
    ///
    /// Ο χωρισμός γίνεται στο πρώτο κενό: ό,τι είναι πριν είναι το όνομα, ό,τι μετά το επώνυμο. Έτσι
    /// ένας παλιός πελάτης «Γιώργος Παπαδόπουλος» ανοίγει σωστά μοιρασμένος στα δύο κελιά.
    /// </summary>
    public string CustomerFirstName
    {
        get
        {
            var s = CustomerName.Trim();
            var i = s.IndexOf(' ');
            return i < 0 ? s : s[..i];
        }
        set
        {
            _editingNameCell = true;
            SetFullName(value, CustomerLastName);
            _editingNameCell = false;
            // Οι προτάσεις βγαίνουν από ΤΟ ΚΕΛΙ που γράφεται, όχι από το ενιαίο όνομα από κάτω — αλλιώς
            // γράφοντας «ΣΤΕΦΑΝΟΣ» στο Όνομα προτεινόταν ολόκληρο το «ΣΤΕΦΑΝΟΣ ΚΑΜΠΟΥΡΗΣ» και το επώνυμο
            // κατέληγε μέσα στο κελί του ονόματος.
            RefreshFirstNameSuggestions(value);
        }
    }

    public string CustomerLastName
    {
        get
        {
            var s = CustomerName.Trim();
            var i = s.IndexOf(' ');
            return i < 0 ? "" : s[(i + 1)..].TrimStart();
        }
        set
        {
            _editingNameCell = true;
            SetFullName(CustomerFirstName, value);
            _editingNameCell = false;
            RefreshLastNameSuggestions(value);
        }
    }

    private void SetFullName(string first, string last) =>
        CustomerName = string.Join(" ", new[] { first.Trim(), last.Trim() }.Where(s => s.Length > 0));

    /// <summary>Σηκωμένο ΜΟΝΟ όσο γράφει ο ταμίας σε ένα από τα δύο κελιά του ονόματος. Τότε το ενιαίο
    /// CustomerName αλλάζει από την ίδια την πληκτρολόγηση, και οι προτάσεις που μόλις άνοιξαν δεν
    /// πρέπει να σβηστούν από κάτω τους.</summary>
    private bool _editingNameCell;

    /// <summary>Όταν αλλάζει το ενιαίο όνομα (π.χ. επιλογή πελάτη από την αναζήτηση), ξαναδιαβάζονται
    /// και τα δύο κελιά.</summary>
    partial void OnCustomerNameChanged(string value)
    {
        if (ForceUpper(value, v => CustomerName = v))
            return;
        OnPropertyChanged(nameof(CustomerFirstName));
        OnPropertyChanged(nameof(CustomerLastName));
        // Το όνομα άλλαξε από αλλού — καθάρισμα φόρμας, νέα παραγγελία, επιλογή πελάτη, αναγνώριση
        // κλήσης. Τα δύο κελιά είναι υπολογιζόμενα πάνω στο CustomerName, οπότε κανένας setter δεν
        // τρέχει και οι προτάσεις της προηγούμενης πληκτρολόγησης έμεναν ανοιχτές πάνω από άδεια
        // πεδία, ακόμα κι αν έβγαινες και ξαναέμπαινες στο βήμα.
        if (!_editingNameCell)
        {
            FillSuggestions(FirstNameSuggestions, [], nameof(HasFirstNameSuggestions));
            FillSuggestions(LastNameSuggestions, [], nameof(HasLastNameSuggestions));
        }
        NotifyStep2Validation();
    }

    partial void OnCustomerPhoneChanged(string value) => NotifyStep2Validation();
    partial void OnCustomerFloorChanged(string value) => NotifyStep2Validation();

    /// <summary>
    /// Τα στοιχεία πελάτη γράφονται ΠΑΝΤΑ κεφαλαία (ζητήθηκε 15/8/2026): ίδια εικόνα σε οθόνη και
    /// απόδειξη, και ο ίδιος δρόμος δεν διχάζεται σε «Μαγνησίας»/«μαγνησίας» στις προτάσεις.
    /// Γίνεται εδώ, στο ViewModel, και όχι μόνο με CharacterCasing στα κουτιά, ώστε να ισχύει και για
    /// ό,τι μπαίνει χωρίς πληκτρολόγηση (αναγνώριση κλήσης, επιλογή αποθηκευμένης διεύθυνσης).
    /// </summary>
    /// <returns>true αν χρειάστηκε διόρθωση — τότε ο καλών σταματά, γιατί η ανάθεση ξανακαλεί τον ίδιο
    /// handler με το κεφαλαίο κείμενο και η δουλειά γίνεται εκεί (χωρίς αυτό, όλα θα γίνονταν δύο φορές).</returns>
    private static bool ForceUpper(string value, Action<string> assign)
    {
        var upper = GreekText.Upper(value);
        if (upper == value)
            return false;
        assign(upper);
        return true;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Step2ContinueEnabled))]
    private string _customerPhone = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrderContextLabel))]
    [NotifyPropertyChangedFor(nameof(Step2ContinueEnabled))]
    private string _customerAddress = "";

    /// <summary>Αριθμός οδού — ξεχωριστό πεδίο, ώστε ο Χάρτης Διανομής να εντοπίζει ακριβώς το σημείο.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Step2ContinueEnabled))]
    private string _customerStreetNumber = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Step2ContinueEnabled))]
    private string _customerArea = "";

    /// <summary>Ταχυδρομικός κώδικας — προαιρετικός (βλ. Step2Fields), δεν εμποδίζει το ΣΥΝΕΧΕΙΑ.</summary>
    [ObservableProperty]
    private string _customerPostalCode = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Step2ContinueEnabled))]
    private string _customerFloor = "";

    /// <summary>Επιλογές για το dropdown ορόφου — σταθερή λίστα, ώστε να μη γράφεται ελεύθερο κείμενο
    /// που μπερδεύει τη γεωκωδικοποίηση (βλ. DeliveryMapWindow.CleanAddressForGeocoding).</summary>
    public static IReadOnlyList<string> FloorOptions { get; } =
        // "ος" σκόπιμα με μικρά — κεφαλαίο "10ΟΣ" διαβάζεται σαν "100" (Ο δίπλα σε 1,0).
        ["ΥΠΟΓΕΙΟ", "ΙΣΟΓΕΙΟ", "1ος", "2ος", "3ος", "4ος", "5ος", "6ος", "7ος", "8ος", "9ος", "10ος"];

    /// <summary>Σχόλια διανομής — π.χ. «θέλει αναπάντητη».</summary>
    [ObservableProperty]
    private string _customerNotes = "";

    /// <summary>Μόνιμη υπενθύμιση πελάτη (π.χ. «του χρωστάμε ένα γεύμα»).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomerMemo))]
    private string _customerMemo = "";

    public bool HasCustomerMemo => CustomerMemo.Trim().Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrderContextLabel))]
    [NotifyPropertyChangedFor(nameof(ReceiptTypeLabel))]
    [NotifyPropertyChangedFor(nameof(ShowCustomerForm))]
    [NotifyPropertyChangedFor(nameof(Step2ContinueEnabled))]
    private string? _appPlatform;

    /// <summary>Ο αριθμός παραγγελίας που δίνει η ίδια η πλατφόρμα (Wolt/e-food/BOX) — ώστε να αντιστοιχεί
    /// η παραγγελία μας με αυτήν της εφαρμογής (π.χ. σε τηλεφώνημα διαφωνίας) και να φαίνεται σαν κύριος
    /// αριθμός στις Ζωντανές Παραγγελίες (βλ. BoardOrder.DisplayNumber). Υποχρεωτικό για Wolt/e-food,
    /// προαιρετικό για BOX — βλ. Step2ContinueEnabled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Step2ContinueEnabled))]
    [NotifyPropertyChangedFor(nameof(DisplayOrderNumber))]
    private string _appOrderRef = "";

    /// <summary>Μετρητά ή κάρτα — μόνο για ΔΙΑΝΟΜΗ/BOX (βλ. ShowCustomerForm), ώστε ο διανομέας να ξέρει
    /// πόσα μετρητά να έχει πάνω του. Ρωτιέται στο τέλος, με το banner (βλ. ShowPaymentPrompt), όχι στο
    /// Βήμα 2 — ο ταμίας το επιλέγει αφού έχει ήδη δει το τελικό σύνολο, με λιγότερα λάθη.</summary>
    [ObservableProperty]
    private Core.Models.PaymentMethod? _selectedPaymentMethod;

    /// <summary>Banner στη μέση της οθόνης, πάνω από όλα (βλ. MainWindow.xaml) — εμφανίζεται όταν ο ταμίας
    /// πατήσει το τελικό «ΣΥΝΕΧΕΙΑ»/ΟΛΟΚΛΗΡΩΣΗ» για ΔΙΑΝΟΜΗ/BOX χωρίς να έχει διαλέξει ακόμα τρόπο
    /// πληρωμής (βλ. ContinueStep3) — επιλογή εκεί ολοκληρώνει κατευθείαν την παραγγελία.</summary>
    [ObservableProperty]
    private bool _showPaymentPrompt;

    [RelayCommand] private void SelectCashPayment() => ChoosePaymentMethod(Core.Models.PaymentMethod.Cash);
    [RelayCommand] private void SelectCardPayment() => ChoosePaymentMethod(Core.Models.PaymentMethod.Card);

    /// <summary>Κλείνει το banner τρόπου πληρωμής και επιστρέφει στην παραγγελία, χωρίς να καταχωρηθεί
    /// τίποτα. Αρχικά δεν υπήρχε διέξοδος (η παραγγελία θεωρούνταν έτοιμη), αλλά στην πράξη ο ταμίας
    /// θυμάται συχνά κάτι ξεχασμένο ακριβώς εκείνη τη στιγμή — χωρίς αυτό ήταν αναγκασμένος να
    /// ολοκληρώσει και μετά να διορθώσει (ή να ακυρώσει) ολόκληρη παραγγελία.</summary>
    [RelayCommand]
    private void CancelPaymentPrompt() => ShowPaymentPrompt = false;


    private void ChoosePaymentMethod(Core.Models.PaymentMethod method)
    {
        SelectedPaymentMethod = method;
        if (!ShowPaymentPrompt)
            return;
        ShowPaymentPrompt = false;
        ContinueStep3();
    }

    [ObservableProperty]
    private string? _pickupTime;

    // ---- header ----

    public string OrderContextLabel
    {
        get
        {
            if (Step <= 1 || OrderType is null)
                return "";
            // Βήμα 2: τίποτα. Ό,τι θα έγραφε εδώ (όνομα, διεύθυνση) το βλέπει ήδη ο ταμίας μέσα στη
            // φόρμα που συμπληρώνει — η γκρίζα επανάληψη πάνω-πάνω ήταν σκέτος θόρυβος. Από το Βήμα 3
            // και μετά η φόρμα δεν φαίνεται πια, οπότε εκεί η υπενθύμιση έχει νόημα.
            if (Step == 2)
                return "";
            var type = TypeLabel();
            return OrderType == Core.Models.OrderType.Table
                ? type
                : string.Join(" · ", new[] { type, CustomerName, CustomerAddress }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }

    private string TypeLabel()
    {
        var label = OrderType switch
        {
            Core.Models.OrderType.Delivery => "ΔΙΑΝΟΜΗ",
            Core.Models.OrderType.Apps => "ΕΦΑΡΜΟΓΕΣ",
            Core.Models.OrderType.Pickup => "ΟΡΘΙΟΣ",
            Core.Models.OrderType.Table => "ΤΡΑΠΕΖΙ " + TableNumber,
            _ => "",
        };
        if (OrderType == Core.Models.OrderType.Apps && AppPlatform is not null)
            label += " · " + AppPlatform;
        return label;
    }

    // ---- stepper ----

    /// <summary>Η ετικέτα του Βήματος 2 αλλάζει για ΕΦΑΡΜΟΓΕΣ — δείχνει πλατφόρμα, όχι πελάτη.</summary>
    private string[] StepLabels => ["1 ΤΥΠΟΣ", ShowAppPlatformPicker ? "2 ΕΦΑΡΜΟΓΗ" : "2 ΠΕΛΑΤΗΣ", "3 ΠΡΟΪΟΝΤΑ", "4 ΔΙΑΝΟΜΗ"];

    /// <summary>ΤΡΑΠΕΖΙ/ΠΑΡΑΛΑΒΗ δεν έχουν Βήμα 2 (στοιχεία πελάτη) — να μη γίνεται προσβάσιμο, μπερδεύει.
    /// ΕΦΑΡΜΟΓΕΣ επίσης δεν έχουν στοιχεία πελάτη πια (το Βήμα 2 δείχνει την επιλογή πλατφόρμας αντ' αυτού,
    /// βλ. ShowAppPlatformPicker) — ο πελάτης είναι δικός της εφαρμογής, όχι δικός μας.</summary>
    private bool SkipsStep2 => OrderType is Core.Models.OrderType.Table or Core.Models.OrderType.Pickup
        or Core.Models.OrderType.Apps;

    public IReadOnlyList<StepItemViewModel> StepItems =>
        StepLabels.Select((label, i) => new StepItemViewModel
        {
            Number = i + 1,
            Label = label,
            IsActive = i + 1 == Step,
            IsReachable = i + 1 <= MaxStep && !(i + 1 == 2 && SkipsStep2),
            ShowArrow = i < 3,
        }).ToList();

    /// <summary>Η παραγγελία έφτασε στην εκτύπωση — επιστροφή στην αρχική σημαίνει νέα παραγγελία.</summary>
    private bool _orderCompleted;

    [RelayCommand]
    private void GoToStep(int step)
    {
        // Επιστροφή στην αρχική = πάντα καθαρή φόρμα, μην κρατάει τα προηγούμενα
        if (step == 1)
        {
            if (_orderCompleted)
                NewOrder();
            else
                ResetForm();
            return;
        }
        if (step == 2 && SkipsStep2)
            return;
        if (step <= MaxStep)
            Step = step;
    }

    private void AdvanceTo(int step)
    {
        MaxStep = Math.Max(MaxStep, step);
        Step = step;
    }

    // ---- βήμα 1 ----

    public bool ShowTableGrid => OrderType == Core.Models.OrderType.Table;

    /// <summary>Κρύβει τις κάρτες τύπου παραγγελίας όσο διαλέγεις τραπέζι — μόνο τραπέζι ή πίσω.</summary>
    public bool ShowOrderTypeCards => !ShowTableGrid;

    /// <summary>Ενόσω διαλέγεις τραπέζι, η κάτοψη γίνεται όλη η οθόνη — κρύβεται και το header.</summary>
    public bool ShowHeader => !(Step == 1 && ShowTableGrid);

    [RelayCommand]
    private void SelectOrderType(OrderTypeOptionViewModel option)
    {
        OrderType = option.Key;
        // Ο αριθμός εξαρτάται πλέον από τον τύπο (σειρά βάρδιας μόνο σε ΟΡΘΙΟ/ΔΙΑΝΟΜΗ) — ξαναϋπολογίζεται
        // ώστε η οθόνη να μη δείχνει αριθμό άλλης ζώνης όσο γράφεται η παραγγελία.
        Products.OrderNumber = NextDisplayNumber();
        TableNumber = null;
        foreach (var o in OrderTypeOptions)
            o.IsSelected = o == option;
        foreach (var t in TableNumbers)
            t.IsSelected = false;
        Products.ContinueLabel = option.Key == Core.Models.OrderType.Apps
            ? "ΣΥΝΕΧΕΙΑ" : "ΣΥΝΕΧΕΙΑ · ΕΚΤΥΠΩΣΗ";
        // Μόνο οι εφαρμογές (e-food/Wolt/BOX) έχουν δικό τους τιμοκατάλογο
        Products.UseDeliveryPrices = option.Key == Core.Models.OrderType.Apps;

        if (option.Key == Core.Models.OrderType.Table)
            return; // περιμένει αριθμό τραπεζιού
        AdvanceTo(option.Key == Core.Models.OrderType.Pickup ? 3 : 2);
    }

    [RelayCommand]
    private void SelectTable(TableOptionViewModel table)
    {
        if (IsArrangingTables)
            return;
        if (table.IsOpen)
        {
            TableDetailRequested?.Invoke(table.Number);
            return;
        }
        TableNumber = table.Number;
        foreach (var t in TableNumbers)
            t.IsSelected = t == table;
        // Νέα παρέα: «πόσα άτομα;» ΤΩΡΑ, όσο ο ταμίας έχει μπροστά του το τραπέζι — ένα άτομο = μία
        // απόδειξη στην ταμειακή. Δεν ρωτιέται στο StartNewRoundForTable: εκεί το τραπέζι είναι ήδη
        // ανοιχτό και τα άτομα δηλωμένα, θα ρωτούσε σε κάθε γύρο.
        PersonsAskRequested?.Invoke(table.Number);
        BeginPersonSequence(table.Number);
        AdvanceTo(3);
    }

    /// <summary>Ζητά προβολή λεπτομερειών ανοιχτού τραπεζιού (τι έχει παραγγελθεί, εξόφληση ανά προϊόν).</summary>
    public event Action<int>? TableDetailRequested;

    /// <summary>Ζητά την ερώτηση «πόσα άτομα;» για τραπέζι που μόλις ανοίγει (βλ. Views/PersonsDialog).</summary>
    public event Action<int>? PersonsAskRequested;

    // ── Παραγγελία ανά άτομο ─────────────────────────────────────────────────────────────────────
    // Το μαγαζί κόβει ΜΙΑ ΑΠΟΔΕΙΞΗ ΑΝΑ ΑΤΟΜΟ σε ξεχωριστή ταμειακή. Ο ταμίας γράφει του Α, πατάει
    // ΟΛΟΚΛΗΡΩΣΗ, και η ίδια οθόνη τον ξαναβάζει στα προϊόντα για τον Β — μέχρι να τελειώσουν τα άτομα
    // που δηλώθηκαν στο άνοιγμα. Κάθε άτομο γίνεται δικός του γύρος (άρα δική του απόδειξη), αλλά η
    // ΚΟΥΖΙΝΑ παίρνει ΕΝΑ δελτίο στο τέλος με όλο το τραπέζι — ίδια λογική με το κινητό του σερβιτόρου.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSplittingTable))]
    [NotifyPropertyChangedFor(nameof(PersonProgressLabel))]
    private int _tablePersonCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PersonProgressLabel))]
    private int _tablePerson;

    /// <summary>Το τραπέζι πληρώνει χωριστά — μόνο τότε εμφανίζεται οτιδήποτε σχετικό με άτομα.</summary>
    public bool IsSplittingTable => TablePersonCount > 1;

    /// <summary>«ΑΤΟΜΟ Β · 2 από 4» — μπροστά στα μάτια του ταμία όσο γράφει.</summary>
    public string PersonProgressLabel =>
        IsSplittingTable ? $"ΑΤΟΜΟ {TablePersonsService.Label(TablePerson)} · {TablePerson + 1} από {TablePersonCount}" : "";

    /// <summary>Οι γύροι που έχουν ήδη καταχωρηθεί για αυτό το τραπέζι, ένας ανά άτομο — κρατιούνται
    /// μόνο για να τυπωθεί ΕΝΑ μαζεμένο δελτίο στην κουζίνα όταν κλείσει το τελευταίο άτομο.</summary>
    private readonly List<CompletedOrder> _tableRounds = [];

    /// <summary>Τα άλλα άτομα του τραπεζιού, με τα προϊόντα τους — μένουν στη δεξιά στήλη ώστε ο ταμίας
    /// να βλέπει ΟΛΟ το τραπέζι όσο γράφει, όχι μόνο το άτομο της στιγμής.
    /// <para>Χωρισμένα σε ΠΑΝΩ/ΚΑΤΩ από το άτομο που γράφεται: το τρέχον άτομο φαίνεται ζωντανό στο
    /// καλάθι, ανάμεσά τους, οπότε η σειρά μένει πάντα Α, Β, Γ, Δ και κανένα άτομο δεν «κατεβαίνει»
    /// στο τέλος όταν το επιλέγεις.</para></summary>
    public ObservableCollection<SentPersonViewModel> SentPersonsAbove { get; } = [];
    public ObservableCollection<SentPersonViewModel> SentPersonsBelow { get; } = [];

    /// <summary>Ποια άτομα έχουν ήδη περάσει παραγγελία — ώστε το «επόμενο» να μην ξαναπάει σε κάποιον
    /// που τελείωσε, ακόμα κι αν ο ταμίας γύρισε ενδιάμεσα πίσω σε αυτόν.</summary>
    private readonly HashSet<int> _personsDone = [];

    /// <summary>Το καλάθι κάθε ατόμου του τρέχοντος γύρου, ΑΚΑΤΑΧΩΡΗΤΟ. Ο ταμίας πηγαινοέρχεται
    /// ελεύθερα ανάμεσά τους· καταχωρούνται όλα μαζί στο τέλος (βλ. CommitTablePersons).</summary>
    private readonly Dictionary<int, PersonDraftViewModel> _personDrafts = [];

    /// <summary>Τραπέζι με άτομα: εδώ ΔΕΝ καταχωρείται τίποτα με το «ΟΛΟΚΛΗΡΩΣΗ» — μόνο κλείνει το άτομο.</summary>
    private bool IsDeferredPersonRound => OrderType == Core.Models.OrderType.Table && IsSplittingTable;

    /// <summary>Είναι ο τελευταίος που μένει; Τότε το κουμπί καταχωρεί ΟΛΟ το τραπέζι.</summary>
    private bool IsLastPendingPerson => TablePersonCount > 0 && Enumerable
        .Range(0, TablePersonCount)
        .All(p => p == TablePerson || _personsDone.Contains(p));

    /// <summary>
    /// Κλικ σε άτομο του τραπεζιού: το καλάθι που γράφεις τώρα μπαίνει στην άκρη ΟΠΩΣ ΕΙΝΑΙ, και στη
    /// θέση του ανοίγει το δικό του — ζωντανό και επεξεργάσιμο (ποσότητες, έξτρα, σβήσιμο γραμμής).
    /// <para>Καμία ακύρωση και καμία διπλή παραγγελία: τίποτα δεν έχει καταχωρηθεί ακόμα, οπότε ό,τι
    /// αλλάξεις εδώ είναι απλώς η παραγγελία του — όπως θα βγει και η απόδειξή του στο τέλος.</para>
    /// </summary>
    [RelayCommand]
    private void SelectSentPerson(SentPersonViewModel sent)
    {
        if (sent.Person == TablePerson)
            return;

        StashCurrentPerson();
        LoadPerson(sent.Person);
    }

    /// <summary>Φυλάει στην άκρη το καλάθι του ατόμου που γράφεται τώρα, χωρίς να το καταχωρεί.
    /// Άδειο καλάθι = το άτομο δεν πήρε τίποτα, οπότε φεύγει και από τη λίστα.</summary>
    private void StashCurrentPerson()
    {
        if (!IsDeferredPersonRound)
            return;

        if (Products.Cart.Count == 0)
            _personDrafts.Remove(TablePerson);
        else
            _personDrafts[TablePerson] = new PersonDraftViewModel
            {
                Lines = [.. Products.Cart],
                Total = Products.Total,
            };
    }

    /// <summary>Ανοίγει ένα άτομο στη δεξιά στήλη: το καλάθι του γίνεται ΤΟ καλάθι της οθόνης.</summary>
    private void LoadPerson(int person)
    {
        TablePerson = person;
        Products.Cart.Clear();
        if (_personDrafts.TryGetValue(person, out var draft))
            foreach (var line in draft.Lines)
                Products.Cart.Add(line);
        Products.Customizer = null;
        Products.OnCartChanged();
        RefreshPersonBlocks();
    }

    /// <summary>Ξαναχτίζει τα μπλοκ των ΑΛΛΩΝ ατόμων (το τρέχον φαίνεται ήδη ζωντανό στο καλάθι) και
    /// ρυθμίζει το κουμπί: «ΟΛΟΚΛΗΡΩΣΗ ΑΤΟΜΟΥ Β» ή, στον τελευταίο, «ΚΑΤΑΧΩΡΗΣΗ ΟΛΩΝ».</summary>
    private void RefreshPersonBlocks()
    {
        SentPersonsAbove.Clear();
        SentPersonsBelow.Clear();
        if (IsDeferredPersonRound)
        {
            // Και τα άτομα που είχαν παραγγείλει σε προηγούμενο γύρο: χωρίς αυτά, σε τραπέζι που έχει
            // ήδη παραγγείλει η στήλη ήταν άδεια και δεν υπήρχε τρόπος να πας στον Β όταν ζητήσει κάτι
            // ακόμα — έγραφες αναγκαστικά σε όποιον άνοιγε η οθόνη.
            var shown = _personDrafts.Keys.Concat(_personsOrderedBefore)
                .Where(p => p != TablePerson)
                .Distinct()
                .Order();
            foreach (var person in shown)
            {
                var draft = _personDrafts.GetValueOrDefault(person);
                (person < TablePerson ? SentPersonsAbove : SentPersonsBelow).Add(new SentPersonViewModel
                {
                    Person = person,
                    // ✓ = τελειωμένος, ✎ = τον άφησες στη μέση, + = έχει ήδη παραγγελία και δέχεται κι
                    // άλλα. Και τα τρία πατιούνται και ανοίγουν.
                    Header = (draft is null ? "+ ΑΤΟΜΟ "
                        : _personsDone.Contains(person) ? "✓ ΑΤΟΜΟ "
                        : "✎ ΑΤΟΜΟ ") + TablePersonsService.Label(person),
                    Total = draft?.Total ?? 0m,
                    Lines = draft is null
                        ? []
                        : draft.Lines
                            .Select(l => new SentPersonLineViewModel(
                                (l.Quantity > 1 ? l.Quantity + "× " : "") + l.Name, Order.FormatPrice(l.Total)))
                            .ToList(),
                });
            }

            Products.ContinueLabel = IsLastPendingPerson
                ? "ΚΑΤΑΧΩΡΗΣΗ ΟΛΩΝ · ΕΚΤΥΠΩΣΗ"
                : "ΟΛΟΚΛΗΡΩΣΗ ΑΤΟΜΟΥ " + TablePersonsService.Label(TablePerson);
            // Σε σειρά ατόμων το κουμπί δουλεύει ΚΑΙ με άδειο καλάθι: σημαίνει «αυτός δεν πήρε τίποτα»
            // και τον προσπερνά (βλ. CommitTablePersons — το τραπέζι κρατά τελικά μόνο όσους πήραν).
            Products.AllowEmptyContinue = true;
        }
        else
        {
            Products.AllowEmptyContinue = false;
        }
    }

    /// <summary>Ξεκινά καθαρή σειρά ατόμων για το τραπέζι (καλείται μόλις επιλεγεί).</summary>
    /// <param name="startPerson">Σε ποιον γράφεται· -1 = στον πρώτο που δεν έχει παραγγείλει ακόμα.</param>
    private void BeginPersonSequence(int table, int startPerson = -1)
    {
        TablePersonCount = TablePersonsService.Instance.CountFor(table);
        _tableRounds.Clear();
        SentPersonsAbove.Clear();
        SentPersonsBelow.Clear();
        _personsDone.Clear();
        _personDrafts.Clear();
        // Όσοι έχουν ΗΔΗ παραγγείλει (προηγούμενος γύρος, ή παραγγελία από το κινητό) δεν ξαναρωτιούνται:
        // ξεκινάμε από τον πρώτο που δεν έχει τίποτα — δηλαδή και από το άτομο που μόλις προστέθηκε.
        _personsOrderedBefore.Clear();
        foreach (var p in TablePersonsService.Instance.PersonsWithItems(table))
        {
            _personsDone.Add(p);
            _personsOrderedBefore.Add(p);
        }
        _isAddOnRound = _personsOrderedBefore.Count > 0;
        TablePerson = startPerson >= 0
            ? startPerson
            : Enumerable.Range(0, Math.Max(1, TablePersonCount))
                .FirstOrDefault(p => !_personsDone.Contains(p), 0);
        RefreshPersonBlocks();
    }

    /// <summary>Άτομα που είχαν ήδη παραγγείλει πριν ανοίξει αυτός ο γύρος — φαίνονται στη στήλη ακόμα
    /// κι όταν δεν τους έχει γραφτεί τίποτα τώρα, ώστε να πατηθεί όποιος ζητήσει κάτι επιπλέον.</summary>
    private readonly HashSet<int> _personsOrderedBefore = [];

    /// <summary>Είναι ΠΡΟΣΘΗΚΗ σε τραπέζι που έχει ήδη παραγγείλει (κάποιος ζήτησε κάτι ακόμα), όχι η
    /// πρώτη παραγγελία της παρέας. Αλλάζει ΜΟΝΟ το χαρτί — βλ. PrintTableRounds.</summary>
    private bool _isAddOnRound;

    /// <summary>Καλείται από την οθόνη λεπτομερειών τραπεζιού όταν πατηθεί «+ ΠΡΟΣΘΗΚΗ».</summary>
    /// <param name="person">Σε ποιο άτομο γράφεται· -1 = στον πρώτο που δεν έχει παραγγείλει ακόμα.</param>
    public void StartNewRoundForTable(int number, int person = -1)
    {
        // Ο τύπος δηλώνεται ΡΗΤΑ: η οθόνη τραπεζιού είναι ξεχωριστό παράθυρο και μπορεί να πατηθεί το
        // «+ Νέα παραγγελία» αφού ο ταμίας έχει ήδη ξεδιαλέξει το ΤΡΑΠΕΖΙ πίσω στην κάτοψη. Τότε ο
        // τύπος ήταν null και η παραγγελία καταγραφόταν ως ΔΙΑΝΟΜΗ χωρίς όνομα (βλ. RecordStats).
        OrderType = Core.Models.OrderType.Table;
        // Και ΡΗΤΑ οι τιμές του μαγαζιού: αυτός ο δρόμος παρακάμπτει το Βήμα 1 (SelectOrderType), που
        // είναι το μόνο σημείο που ορίζει τιμοκατάλογο. Μετά από μια παραγγελία ΕΦΑΡΜΟΓΩΝ ο διακόπτης
        // έμενε αναμμένος και το τραπέζι χρεωνόταν σιωπηλά με τιμές e-food/Wolt/BOX — 88 από τα 141
        // προϊόντα του καταλόγου έχουν διαφορετική τιμή εκεί.
        Products.UseDeliveryPrices = false;
        Products.ContinueLabel = "ΣΥΝΕΧΕΙΑ · ΕΚΤΥΠΩΣΗ";
        TableNumber = number;
        foreach (var t in TableNumbers)
            t.IsSelected = t.Number == number;
        // Και στον δεύτερο γύρο ξαναπερνάει από τα άτομα: η παρέα ξαναπαραγγέλνει με τη σειρά, και το
        // καθένα προσθέτει στη ΔΙΚΗ ΤΟΥ απόδειξη (τα σύνολα βγαίνουν ανά άτομο, όχι ανά γύρο).
        BeginPersonSequence(number, person);
        AdvanceTo(3);
    }

    /// <summary>Κλικ στο κενό φόντο του Βήματος 1 κάνει deselect.</summary>
    [RelayCommand]
    private void DeselectOrderType()
    {
        OrderType = null;
        TableNumber = null;
        foreach (var o in OrderTypeOptions)
            o.IsSelected = false;
        foreach (var t in TableNumbers)
            t.IsSelected = false;
    }

    // ---- βήμα 2 ----

    public bool ShowAddressField => OrderType is Core.Models.OrderType.Delivery or Core.Models.OrderType.Apps;

    /// <summary>
    /// Τα πεδία που πρέπει να έχουν συμπληρωθεί για να προχωρήσει η ΔΙΑΝΟΜΗ (και το BOX, που το
    /// παραδίδει επίσης δικός μας διανομέας) — όνομα, επώνυμο, τηλέφωνο και ολόκληρη η διεύθυνση.
    /// Ζητήθηκε 15/8/2026: μισοσυμπληρωμένα στοιχεία σήμαιναν διανομέα που ψάχνει όροφο ή τηλέφωνο
    /// που δεν υπάρχει. Ο Τ.Κ. μένει ΕΞΩ επίτηδες («δεν μας πειράζει»), όπως και τα σχόλια.
    /// </summary>
    private IReadOnlyList<(string Label, bool Filled)> Step2Fields =>
    [
        ("Όνομα", CustomerFirstName.Trim().Length > 0),
        ("Επώνυμο", CustomerLastName.Trim().Length > 0),
        ("Τηλέφωνο", CustomerPhone.Trim().Length > 0),
        ("Διεύθυνση", CustomerAddress.Trim().Length > 0),
        ("Αριθμός", CustomerStreetNumber.Trim().Length > 0),
        ("Περιοχή", CustomerArea.Trim().Length > 0),
        ("Όροφος", CustomerFloor.Trim().Length > 0),
    ];

    /// <summary>
    /// Άναψε ο έλεγχος; Γίνεται true μόνο όταν ο ταμίας πατήσει ΣΥΝΕΧΕΙΑ με κάτι κενό — τότε και μόνο
    /// τότε κοκκινίζουν τα άδεια κουτιά. Μέχρι εκείνη τη στιγμή η φόρμα είναι καθαρή: κόκκινα σε πεδία
    /// που απλώς δεν έχει προλάβει να συμπληρώσει ήταν σκέτος θόρυβος ενώ γράφει.
    /// </summary>
    [ObservableProperty]
    private bool _step2Validated;

    // Ποια κουτιά κοκκινίζουν. Ξεχωριστά ανά πεδίο, ώστε να δείχνει ακριβώς ΠΟΙΟ λείπει.
    public bool MissingFirstName => Step2Validated && CustomerFirstName.Trim().Length == 0;
    public bool MissingLastName => Step2Validated && CustomerLastName.Trim().Length == 0;
    public bool MissingPhone => Step2Validated && CustomerPhone.Trim().Length == 0;
    public bool MissingAddress => Step2Validated && CustomerAddress.Trim().Length == 0;
    public bool MissingStreetNumber => Step2Validated && CustomerStreetNumber.Trim().Length == 0;
    public bool MissingArea => Step2Validated && CustomerArea.Trim().Length == 0;
    public bool MissingFloor => Step2Validated && CustomerFloor.Trim().Length == 0;

    /// <summary>ΕΦΑΡΜΟΓΕΣ (Wolt/e-food): εκεί δεν φαίνεται καθόλου φόρμα πελάτη, οπότε το μόνο που
    /// μπορεί να λείπει — και το μόνο που έχει νόημα να κοκκινίσει — είναι ο αριθμός της πλατφόρμας.</summary>
    public bool MissingAppOrderRef => Step2Validated && ShowAppPlatformPicker
        && AppPlatform is not null and not "BOX" && AppOrderRef.Trim().Length == 0;

    /// <summary>Ξαναδιαβάζονται όλα μαζί σε κάθε πληκτρολόγηση — έτσι ένα κόκκινο κουτί ξεκοκκινίζει τη
    /// στιγμή που γράφεται, χωρίς να ξαναπατηθεί το ΣΥΝΕΧΕΙΑ.</summary>
    private void NotifyStep2Validation()
    {
        OnPropertyChanged(nameof(Step2ContinueEnabled));
        OnPropertyChanged(nameof(MissingFirstName));
        OnPropertyChanged(nameof(MissingLastName));
        OnPropertyChanged(nameof(MissingPhone));
        OnPropertyChanged(nameof(MissingAddress));
        OnPropertyChanged(nameof(MissingStreetNumber));
        OnPropertyChanged(nameof(MissingArea));
        OnPropertyChanged(nameof(MissingFloor));
        OnPropertyChanged(nameof(MissingAppOrderRef));
    }

    /// <summary>
    /// Ο κωδικός της πλατφόρμας: <b>Wolt και e-food δέχονται ΜΟΝΟ ψηφία</b> — ό,τι άλλο πληκτρολογηθεί
    /// απλώς δεν γράφεται. Το <b>BOX δέχεται και γράμματα</b>, γιατί οι δικοί του κωδικοί δεν είναι
    /// σκέτοι αριθμοί.
    /// <para>Γι' αυτό ακριβώς η αναζήτηση του Ιστορικού δέχεται πλέον κι εκείνη γράμματα (βλ.
    /// HistoryViewModel.OnSearchNumberChanged): όσο έψαχνε μόνο με ψηφία, ένας κωδικός με γράμμα
    /// έφτιαχνε παραγγελία που μετά ΔΕΝ βρισκόταν ποτέ με αναζήτηση.</para>
    /// </summary>
    partial void OnAppOrderRefChanged(string value)
    {
        var clean = FilterOrderRef(value);
        if (clean != value)
        {
            AppOrderRef = clean; // ξαναμπαίνει εδώ, καθαρό
            return;
        }
        NotifyStep2Validation();
    }

    /// <summary>Το BOX κρατά ό,τι γράφτηκε· οι υπόλοιπες πλατφόρμες κρατούν μόνο τα ψηφία.</summary>
    private string FilterOrderRef(string value) =>
        AppPlatform == "BOX" ? value : new string(value.Where(char.IsDigit).ToArray());

    partial void OnAppPlatformChanged(string? value)
    {
        // Αλλαγή BOX → Wolt/e-food με γράμματα ήδη γραμμένα: καθαρίζουν εδώ, αλλιώς θα περνούσε
        // κωδικός με γράμματα σε πλατφόρμα που δεν τα δέχεται.
        AppOrderRef = FilterOrderRef(AppOrderRef);
        NotifyStep2Validation();
    }

    partial void OnStep2ValidatedChanged(bool value) => NotifyStep2Validation();

    /// <summary>ΔΙΑΝΟΜΗ/BOX: όλα τα στοιχεία πελάτη εκτός Τ.Κ. (βλ. Step2Fields). ΕΦΑΡΜΟΓΕΣ (Wolt/e-food):
    /// πλατφόρμα επιλεγμένη και αριθμός παραγγελίας — στοιχεία πελάτη δεν έχουμε, τα κρατά η εφαρμογή.</summary>
    public bool Step2ContinueEnabled
    {
        get
        {
            if (OrderType == Core.Models.OrderType.Apps && AppPlatform != "BOX")
                return !string.IsNullOrWhiteSpace(AppPlatform) && AppOrderRef.Trim().Length > 0;
            return Step2Fields.All(f => f.Filled);
        }
    }

    public IReadOnlyList<CustomerMatchViewModel> CustomerMatches
    {
        get
        {
            // Μόνο το ειδικό πεδίο αναζήτησης ενεργοποιεί προτάσεις — τα κανονικά πεδία Τηλέφωνο/
            // Διεύθυνση (όταν ο ταμίας απλώς καταχωρεί έναν νέο/υπάρχοντα πελάτη) δεν πρέπει να πετάνε
            // απροειδοποίητα λίστα πελατών πάνω από τη φόρμα.
            var q = CustomerSearch.Trim();
            if (q.Length < 2)
                return [];
            // Ο ήδη επιλεγμένος πελάτης πετιέται ΜΕΣΑ στην αναζήτηση, όχι μετά — αλλιώς έπιανε μία από
            // τις έξι θέσεις και ο ταμίας έβλεπε πέντε προτάσεις χωρίς λόγο.
            return CustomerStore.Instance
                .Search(q, c => c.Name == CustomerName && c.Phone == CustomerPhone && c.Address == CustomerAddress)
                .Select(c => new CustomerMatchViewModel { Customer = c })
                .ToList();
        }
    }

    public bool HasCustomerMatches => CustomerMatches.Count > 0;

    /// <summary>Επιλογές αποθηκευμένων διευθύνσεων του τρέχοντος πελάτη (κύρια + τυχόν άλλες, βλ.
    /// Customer.OtherAddresses) — γεμίζει όταν φορτωθεί πελάτης (αναζήτηση ή εισερχόμενη κλήση), άδειο
    /// για νέο/άγνωστο πελάτη (βλ. ShowAddressPicker για το πότε φαίνεται η γραμμή).</summary>
    public ObservableCollection<CustomerAddressOptionViewModel> CustomerAddressOptions { get; } = [];

    /// <summary>Ο πελάτης που φόρτωσε τελευταία το picker διευθύνσεων — κρατιέται εδώ ώστε το
    /// DeleteCustomerAddressOption να ξέρει σε ποιον να αφαιρέσει τη διεύθυνση (βλ. CustomerStore).</summary>
    private Customer? _addressOptionsCustomer;

    private void LoadCustomerAddressOptions(Customer customer)
    {
        _addressOptionsCustomer = customer;
        CustomerAddressOptions.Clear();
        if (customer.Address.Length > 0)
        {
            CustomerAddressOptions.Add(new CustomerAddressOptionViewModel
            {
                Label = "Κύρια" + (customer.Address.Length > 0 ? " · " + customer.Address : ""),
                Address = customer.Address, StreetNumber = customer.StreetNumber, Area = customer.Area,
                PostalCode = customer.PostalCode, Floor = customer.Floor, IsMain = true,
            });
        }
        foreach (var other in customer.OtherAddresses)
        {
            // Πάντα από οδό+αριθμό, όχι από το αποθηκευμένο other.Label — αυτό μπορεί να έχει μείνει από
            // παλιότερη λογική που έβαζε την περιοχή όταν έλειπε η οδός, ή απλά να είναι η περιοχή σε
            // παλιά δεδομένα· ο ταμίας θέλει να βλέπει διεύθυνση στο κουμπί, όχι περιοχή.
            var display = other.StreetNumber.Length > 0 ? other.Address + " " + other.StreetNumber : other.Address;
            CustomerAddressOptions.Add(new CustomerAddressOptionViewModel
            {
                Label = display.Length > 0 ? display : (other.Area.Length > 0 ? other.Area : "Άλλη διεύθυνση"),
                Address = other.Address, StreetNumber = other.StreetNumber, Area = other.Area,
                PostalCode = other.PostalCode, Floor = other.Floor, IsMain = false,
            });
        }
        OnPropertyChanged(nameof(ShowAddressPicker));
    }

    /// <summary>Ο ταμίας πάτησε το × πάνω σε μια αποθηκευμένη διεύθυνση, κατευθείαν στο Βήμα 2 — χωρίς να
    /// χρειάζεται να ανοίξει το παράθυρο ΠΕΛΑΤΕΣ. Ρωτάει πρώτα (ώστε ένα κατά λάθος πάτημα να μην πετάξει
    /// αμέσως μια διεύθυνση) — αν διαγραφεί η κύρια, προάγεται αυτόματα η επόμενη αποθηκευμένη σε κύρια
    /// (βλ. CustomerStore.RemoveMainAddress), δεν μένει ποτέ κενή αν υπάρχει άλλη επιλογή.</summary>
    [RelayCommand]
    private void DeleteCustomerAddressOption(CustomerAddressOptionViewModel option)
    {
        if (_addressOptionsCustomer is not { } customer)
            return;

        var message = option.IsMain
            ? "Διαγραφή της κύριας διεύθυνσης «" + option.Label + "»;"
                + (customer.OtherAddresses.Count > 0 ? "\n\nΘα γίνει κύρια η επόμενη αποθηκευμένη διεύθυνση." : "")
            : "Διαγραφή της διεύθυνσης «" + option.Label + "»;";
        var answer = MessageBox.Show(message, "Διαγραφή διεύθυνσης", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        if (option.IsMain)
        {
            CustomerStore.Instance.RemoveMainAddress(customer);
        }
        else
        {
            var address = new CustomerAddress
            {
                Address = option.Address, StreetNumber = option.StreetNumber, Area = option.Area,
            };
            CustomerStore.Instance.RemoveOtherAddress(customer, address);
        }
        LoadCustomerAddressOptions(customer);
    }

    private void ClearCustomerAddressOptions()
    {
        _addressOptionsCustomer = null;
        CustomerAddressOptions.Clear();
        OnPropertyChanged(nameof(ShowAddressPicker));
    }

    /// <summary>Η γραμμή διευθύνσεων (αποθηκευμένες + «Νέα διεύθυνση») φαίνεται για κάθε ήδη γνωστό
    /// πελάτη — ακόμα κι αν έχει μία μόνο διεύθυνση, γιατί ακριβώς τότε χρειάζεται το κουμπί για τη
    /// δεύτερη, και το κουμπάκι της κύριας είναι το «πίσω» αν πατηθεί κατά λάθος.</summary>
    public bool ShowAddressPicker => _addressOptionsCustomer is not null;

    /// <summary>«Νέα διεύθυνση»: αδειάζει ΤΑ ΙΔΙΑ πεδία της παραγγελίας για να γραφτεί η καινούρια από
    /// την αρχή — δεν ανοίγει δεύτερη φόρμα.
    ///
    /// <para><b>Γιατί όχι ξεχωριστή φόρμα, όπως πριν:</b> είχε δικά της κουτιά κάτω από τα κουτιά της
    /// παραγγελίας και δικό της κουμπί Αποθήκευσης. Όποιος έγραφε εκεί τη διεύθυνση και πατούσε
    /// κατευθείαν ΣΥΝΕΧΕΙΑ — ή έχανε ένα από τα υποχρεωτικά της κουτιά, οπότε η Αποθήκευση δεν περνούσε
    /// τίποτα — έστελνε την παραγγελία με ό,τι ήταν ακόμα πάνω, δηλαδή την ΚΥΡΙΑ διεύθυνση, και αυτή
    /// τυπωνόταν. Τώρα υπάρχει ΕΝΑ σετ πεδίων: ό,τι γράφεται εκεί είναι η διεύθυνση της παραγγελίας
    /// και αυτή τυπώνεται. Αποθηκεύεται μόνη της στον πελάτη με το ΣΥΝΕΧΕΙΑ, σαν ΠΡΟΣΘΕΤΗ διεύθυνση
    /// (βλ. SaveCustomer → CustomerStore.FindOrCreate, που δεν αγγίζει ποτέ την κύρια).</para>
    ///
    /// <para>Αδειάζουν ΟΛΑ τα πεδία της διεύθυνσης μαζί: μισοαδειασμένα κουτιά θα άφηναν την περιοχή ή
    /// τον όροφο της προηγούμενης διεύθυνσης πάνω στη νέα.</para></summary>
    [RelayCommand]
    private void StartNewAddress()
    {
        _suppressSuggestions = true;
        CustomerAddress = "";
        CustomerArea = "";
        _suppressSuggestions = false;
        CustomerStreetNumber = "";
        CustomerPostalCode = "";
        CustomerFloor = "";
    }

    /// <summary>Ο ταμίας διάλεξε άλλη αποθηκευμένη διεύθυνση από το picker (Βήμα 2).</summary>
    [RelayCommand]
    private void SelectCustomerAddressOption(CustomerAddressOptionViewModel option)
    {
        _suppressSuggestions = true;
        CustomerAddress = option.Address;
        CustomerArea = option.Area;
        _suppressSuggestions = false;
        CustomerStreetNumber = option.StreetNumber;
        CustomerPostalCode = option.PostalCode;
        CustomerFloor = option.Floor;
    }

    [RelayCommand]
    private void SelectCustomer(CustomerMatchViewModel match)
    {
        // Συμπλήρωση από αποθηκευμένα στοιχεία πελάτη, όχι πληκτρολόγηση — δεν πρέπει να ανοίξουν οι
        // προτάσεις (ίδιο πρόβλημα με το Τηλέφωνο/Διεύθυνση, βλ. CustomerMatches παραπάνω).
        _suppressSuggestions = true;
        CustomerName = match.Customer.Name;
        CustomerAddress = match.Customer.Address;
        CustomerArea = match.Customer.Area;
        _suppressSuggestions = false;
        CustomerPhone = match.Customer.Phone;
        CustomerStreetNumber = match.Customer.StreetNumber;
        CustomerPostalCode = match.Customer.PostalCode;
        CustomerFloor = match.Customer.Floor;
        CustomerNotes = match.Customer.Notes;
        CustomerMemo = match.Customer.Memo;
        CustomerSearch = "";
        LoadCustomerAddressOptions(match.Customer);
    }

    // ---- προτάσεις από ό,τι έχει ήδη περαστεί στο μαγαζί, ενόσω πληκτρολογεί (βλ. CustomerStore.Suggest) ----
    //
    // Πριν, οι προτάσεις διεύθυνσης έρχονταν από τον χάρτη (Nominatim/Google): πρότειναν δρόμους όλης της
    // χώρας, συχνά σε λάθος περιοχή, με καθυστέρηση δικτύου και μόνο εφόσον υπήρχε internet. Τώρα η πηγή
    // είναι οι ίδιοι μας οι πελάτες — ονόματα, οδοί και περιοχές που έχουν ήδη γραφτεί εδώ. Είναι τοπικό
    // και ακαριαίο, οπότε δεν χρειάζεται ούτε debounce ούτε ακύρωση αιτημάτων όπως πριν.

    /// <summary>Όσο συμπληρώνονται πεδία από αποθηκευμένα στοιχεία (επιλογή πελάτη, αναγνώριση κλήσης,
    /// επιλογή αποθηκευμένης διεύθυνσης) δεν ανοίγουν προτάσεις — δεν πληκτρολογεί ο ταμίας.</summary>
    private bool _suppressSuggestions;

    public ObservableCollection<string> FirstNameSuggestions { get; } = [];
    public bool HasFirstNameSuggestions => FirstNameSuggestions.Count > 0;

    public ObservableCollection<string> LastNameSuggestions { get; } = [];
    public bool HasLastNameSuggestions => LastNameSuggestions.Count > 0;

    public ObservableCollection<string> AddressSuggestions { get; } = [];
    public bool HasAddressSuggestions => AddressSuggestions.Count > 0;

    public ObservableCollection<string> AreaSuggestions { get; } = [];
    public bool HasAreaSuggestions => AreaSuggestions.Count > 0;

    private void FillSuggestions(ObservableCollection<string> target, IReadOnlyList<string> values, string hasAnyProperty)
    {
        target.Clear();
        foreach (var v in values)
            target.Add(v);
        OnPropertyChanged(hasAnyProperty);
    }

    private void RefreshFirstNameSuggestions(string value) =>
        FillSuggestions(FirstNameSuggestions,
            _suppressSuggestions || !ShowCustomerForm ? [] : CustomerStore.Instance.SuggestFirstNames(value),
            nameof(HasFirstNameSuggestions));

    private void RefreshLastNameSuggestions(string value) =>
        FillSuggestions(LastNameSuggestions,
            _suppressSuggestions || !ShowCustomerForm ? [] : CustomerStore.Instance.SuggestLastNames(value),
            nameof(HasLastNameSuggestions));

    partial void OnCustomerAddressChanged(string value)
    {
        if (ForceUpper(value, v => CustomerAddress = v))
            return;
        NotifyStep2Validation();
        FillSuggestions(AddressSuggestions,
            _suppressSuggestions || !ShowAddressField ? [] : CustomerStore.Instance.SuggestStreets(value),
            nameof(HasAddressSuggestions));
    }

    partial void OnCustomerAreaChanged(string value)
    {
        if (ForceUpper(value, v => CustomerArea = v))
            return;
        NotifyStep2Validation();
        FillSuggestions(AreaSuggestions,
            _suppressSuggestions || !ShowAddressField ? [] : CustomerStore.Instance.SuggestAreas(value),
            nameof(HasAreaSuggestions));
    }

    partial void OnCustomerStreetNumberChanged(string value)
    {
        if (ForceUpper(value, v => CustomerStreetNumber = v))
            return;
        NotifyStep2Validation();
    }
    partial void OnCustomerNotesChanged(string value) => ForceUpper(value, v => CustomerNotes = v);

    /// <summary>Ο ταμίας πάτησε ένα όνομα από τις προτάσεις — μπαίνει ΜΟΝΟ στο κελί του ονόματος, το
    /// επώνυμο μένει όπως είναι. Τα υπόλοιπα στοιχεία δεν γεμίζουν από εδώ: γι' αυτό υπάρχει η αναζήτηση
    /// πελάτη από πάνω (βλ. SelectCustomer), που ξέρει ποιον ακριβώς πελάτη διάλεξε ο ταμίας — δύο
    /// πελάτες μπορεί κάλλιστα να λέγονται το ίδιο.</summary>
    [RelayCommand]
    private void SelectFirstNameSuggestion(string firstName)
    {
        _suppressSuggestions = true;
        CustomerFirstName = firstName;
        _suppressSuggestions = false;
        FillSuggestions(FirstNameSuggestions, [], nameof(HasFirstNameSuggestions));
    }

    [RelayCommand]
    private void SelectLastNameSuggestion(string lastName)
    {
        _suppressSuggestions = true;
        CustomerLastName = lastName;
        _suppressSuggestions = false;
        FillSuggestions(LastNameSuggestions, [], nameof(HasLastNameSuggestions));
    }

    [RelayCommand]
    private void SelectAddressSuggestion(string street)
    {
        // Αν ο ταμίας έγραψε οδό ΚΑΙ αριθμό μαζί στο ίδιο κουτί (π.χ. «Μαγνησίας 12»), ο αριθμός δεν
        // πρέπει να χαθεί όταν το κουτί ξαναγραφεί με μόνο το όνομα του δρόμου — πάει στο δικό του κουτί.
        var typedNumber = Regex.Match(CustomerAddress, @"\d+\s*[Α-Ωα-ωA-Za-z]?\s*$").Value.Trim();

        _suppressSuggestions = true;
        CustomerAddress = street;
        _suppressSuggestions = false;
        FillSuggestions(AddressSuggestions, [], nameof(HasAddressSuggestions));

        if (typedNumber.Length > 0 && CustomerStreetNumber.Trim().Length == 0)
            CustomerStreetNumber = typedNumber;
    }

    [RelayCommand]
    private void SelectAreaSuggestion(string area)
    {
        _suppressSuggestions = true;
        CustomerArea = area;
        _suppressSuggestions = false;
        FillSuggestions(AreaSuggestions, [], nameof(HasAreaSuggestions));
    }

    [RelayCommand] private void BackStep2() => ResetForm();

    [RelayCommand]
    private void ContinueStep2()
    {
        // Λείπει κάτι: δεν προχωράμε, αλλά ούτε βγάζουμε μήνυμα — κοκκινίζουν τα ίδια τα άδεια κουτιά,
        // οπότε φαίνεται με μια ματιά ΠΟΥ πρέπει να γράψει ο ταμίας (βλ. Step2Validated/Missing*).
        if (!Step2ContinueEnabled)
        {
            Step2Validated = true;
            // Η φόρμα του Βήματος 2 είναι μακρύτερη από την οθόνη: το κόκκινο κουτί μπορεί να είναι
            // κύλισμα πιο πάνω και ο ταμίας να βλέπει μόνο ένα κουμπί που δεν κάνει τίποτα. Το φέρνουμε
            // μπροστά του και βάζουμε μέσα τον κέρσορα (βλ. MainWindow.FocusFirstMissingStep2Field).
            MissingFieldFocusRequested?.Invoke();
            return;
        }

        Step2Validated = false;
        SaveCustomer();
        // Ο ίδιος ο πελάτης λέει πότε παρήγγειλε τελευταία φορά — έτσι ανοίγεται μία μέρα αρχείου
        // αντί για έξι μήνες (βλ. FindLastOrderLines).
        var known = CustomerStore.Instance.FindByPhone(CustomerPhone);
        Products.SetRepeatableOrder(FindLastOrderLines(CustomerName, known?.LastOrderAt));
        // Σχόλιο γραμμένο ήδη στο Βήμα 2 (ή σταθερό σχόλιο του πελάτη): το κουτί ανοίγει μόνο του στα
        // Προϊόντα, αλλιώς το κουμπί σχολίων θα έδειχνε κλειστό ενώ από κάτω υπάρχει κείμενο.
        Products.ShowNoteField = CustomerNotes.Trim().Length > 0;
        AdvanceTo(3);
    }

    /// <summary>
    /// Ψάχνει την πιο πρόσφατη ολοκληρωμένη παραγγελία αυτού του πελάτη, για το κουμπί «μία από τα
    /// ίδια» στα προϊόντα. Ταίριασμα με το όνομα, γιατί το αρχειοθετημένο ιστορικό δεν κρατάει
    /// τηλέφωνο ανά παραγγελία.
    ///
    /// <para><b>ΔΙΑΒΑΖΕΙ ΤΟ ΠΟΛΥ ΕΝΑ ΑΡΧΕΙΟ.</b> Πριν φόρτωνε ολόκληρο το αρχείο <b>180 ημερών</b> και
    /// το αποκωδικοποιούσε στο νήμα της οθόνης — σε ΚΑΘΕ παραγγελία διανομής, μόνο και μόνο για να
    /// βρει μία παραγγελία. Με λίγες μέρες αρχείου δεν φαινόταν· με έξι μήνες γίνονται δεκάδες MB
    /// JSON ανά παραγγελία, δηλαδή το ταμείο θα «κόλλαγε» όλο και περισσότερο χωρίς να αλλάξει
    /// τίποτα. Τώρα ο ίδιος ο πελάτης λέει πότε παρήγγειλε τελευταία φορά
    /// (<see cref="Customer.LastOrderAt"/>) και ανοίγεται μόνο εκείνη η μέρα.</para>
    /// </summary>
    /// <param name="lastOrderAt">Πότε παρήγγειλε τελευταία φορά· null = δεν έχει ξαναπαραγγείλει από
    /// αυτό το ταμείο, οπότε δεν υπάρχει τίποτα να ψάξουμε στο αρχείο.</param>
    private static IReadOnlyList<SoldLine>? FindLastOrderLines(string customerName, DateTime? lastOrderAt)
    {
        var name = customerName.Trim();
        if (name.Length == 0)
            return null;

        static IReadOnlyList<SoldLine>? Newest(IEnumerable<CompletedOrder> orders, string who) => orders
            .Where(o => string.Equals(o.Who.Trim(), who, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.PlacedAt)
            .FirstOrDefault()?.Lines;

        // Πρώτα η σημερινή μέρα: είναι ήδη στη μνήμη και είναι και η πιο πρόσφατη.
        if (Newest(SalesStatsService.Instance.Orders, name) is { } todayLines)
            return todayLines;

        if (lastOrderAt is not { } when)
            return null;

        var day = SalesStatsService.BusinessDay(when);
        return Newest(HistoryArchiveService.LoadOrders(day, day), name);
    }

    /// <summary>Αποθηκεύει/ενημερώνει τον πελάτη ώστε να βρίσκεται στην αναζήτηση από εδώ και πέρα.</summary>
    private void SaveCustomer()
    {
        if (OrderType is Core.Models.OrderType.Delivery or Core.Models.OrderType.Apps
            && CustomerName.Trim().Length > 0)
        {
            CustomerStore.Instance.Upsert(CustomerName.Trim(), CustomerPhone.Trim(), CustomerAddress.Trim(),
                CustomerStreetNumber.Trim(), CustomerArea.Trim(), CustomerPostalCode.Trim(), CustomerFloor.Trim(),
                CustomerNotes.Trim());
        }
    }

    // ---- βήμα 3 (μέσα στο ProductsViewModel) ----

    private void BackStep3()
    {
        if (OrderType is Core.Models.OrderType.Delivery or Core.Models.OrderType.Apps)
            Step = 2;
        else if (OrderType == Core.Models.OrderType.Table)
        {
            // Μέσα σε σειρά ατόμων, το «πίσω» πάει ΕΝΑ ΑΤΟΜΟ πίσω (Β → Α) με το καλάθι του ανοιχτό
            // για διόρθωση — ίδια συμπεριφορά με το κινητό του σερβιτόρου. Από το πρώτο άτομο και
            // μόνο βγαίνεις από το τραπέζι (και τότε ρωτάει, γιατί χάνονται όλα).
            if (IsDeferredPersonRound && TablePerson > 0)
            {
                StashCurrentPerson();
                LoadPerson(TablePerson - 1);
                return;
            }
            BackToTableGrid();
        }
        else
            ResetForm();
    }

    /// <summary>Πίσω από τα προϊόντα ενός τραπεζιού — γυρνά στην κάτοψη τραπεζιών, όχι στη γενική επιλογή τύπου.</summary>
    private void BackToTableGrid()
    {
        // Καμία ερώτηση: το «πίσω» από το πρώτο άτομο σημαίνει «η παραγγελία δεν ισχύει» και είναι
        // συνειδητή κίνηση — τίποτα δεν έχει καταχωρηθεί ούτε τυπωθεί, οπότε δεν χάνεται πώληση.
        // (Ο χρήστης ζήτησε ρητά να μη ρωτάει πουθενά, 12/8/2026.)
        Step = 1;
        MaxStep = 1;
        TableNumber = null;
        _boardPushed = false;
        _orderCompleted = false;
        _personDrafts.Clear();
        _personsDone.Clear();
        SentPersonsAbove.Clear();
        SentPersonsBelow.Clear();
        _tableRounds.Clear();
        Products.AllowEmptyContinue = false;
        foreach (var t in TableNumbers) t.IsSelected = false;
        Products.Reset(NextDisplayNumber());
    }

    /// <summary>
    /// Σηκώνεται όταν ολοκληρώνεται παραγγελία — τυπώνει αυτόματα την απόδειξη. Κουβαλάει ΤΗΝ ΙΔΙΑ την
    /// παραγγελία που μόλις καταχωρήθηκε.
    ///
    /// Πριν δεν κουβαλούσε τίποτα και ο εκτυπωτής έψαχνε μόνος του «την πιο πρόσφατη παραγγελία της
    /// ημέρας». Στο κύριο ταμείο τύχαινε να είναι η σωστή· στο ΔΕΥΤΕΡΟ όχι: εκεί η λίστα παραγγελιών
    /// είναι αντίγραφο του κύριου που ανανεώνεται κάθε 3 δευτερόλεπτα, οπότε η μόλις καταχωρημένη δεν
    /// είχε προλάβει να επιστρέψει και τυπωνόταν Η ΠΡΟΗΓΟΥΜΕΝΗ ΠΑΡΑΓΓΕΛΙΑ — λάθος απόδειξη στον πελάτη.
    /// </summary>
    /// <summary>Το ΣΥΝΕΧΕΙΑ του Βήματος 2 σταμάτησε σε κενό πεδίο — το παράθυρο κυλάει σε αυτό.</summary>
    public event Action? MissingFieldFocusRequested;

    public event Action<CompletedOrder>? AutoPrintRequested;

    private void ContinueStep3()
    {
        // Τρόπος πληρωμής (Μετρητά/Κάρτα) ρωτιέται εδώ, στο τέλος — όχι στο Βήμα 2 — για να το επιλέγει
        // ο ταμίας αφού έχει ήδη δει το τελικό σύνολο, με λιγότερα λάθη (βλ. SelectedPaymentMethod). Μόνο
        // ΔΙΑΝΟΜΗ/BOX/ΠΑΡΑΛΑΒΗ το χρειάζονται (βλ. NeedsPaymentMethod). Δείχνει banner στη μέση της οθόνης
        // (βλ. ShowPaymentPrompt, MainWindow.xaml) — η επιλογή εκεί ξαναπερνάει από εδώ (βλ.
        // ChoosePaymentMethod) και ολοκληρώνει κατευθείαν την παραγγελία.
        if (NeedsPaymentMethod && SelectedPaymentMethod is null)
        {
            ShowPaymentPrompt = true;
            return;
        }

        // Τραπέζι με άτομα: εδώ δεν κλείνει παραγγελία, κλείνει ΑΤΟΜΟ. Δεν δεσμεύεται αριθμός και δεν
        // ελέγχεται βάρδια — γίνονται μαζί, στο τέλος, για όλο το τραπέζι (βλ. CommitTablePersons).
        if (IsDeferredPersonRound)
        {
            CompleteOrder();
            return;
        }

        // Ο έλεγχος βάρδιας ΠΡΙΝ δεσμευτεί αριθμός: δείχνει modal παράθυρο, και όσο αυτό είναι ανοιχτό το
        // UI thread συνεχίζει να εξυπηρετεί άλλες εργασίες — π.χ. παραγγελία από το κινητό του σερβιτόρου.
        // Αν ο αριθμός είχε ήδη δεσμευτεί, εκείνη θα προλάβαινε να πάρει τον ίδιο όσο περίμενε ο ταμίας.
        if (!ConfirmShiftMatchesTime())
            return;

        // Φρέσκος αριθμός ΤΩΡΑ, όχι αυτός που δείχνει η οθόνη από νωρίτερα — βλ. SalesStatsService.NextOrderNumber
        // για το γιατί (αποφυγή σιωπηλής απώλειας παραγγελίας σε σύγκρουση με το κινητό σερβιτόρου). Πρέπει
        // να γίνει ΠΡΙΝ το PushBoardOrder, ώστε BoardOrder/CompletedOrder να μοιράζονται τον ίδιο αριθμό.
        Products.OrderNumber = NextDisplayNumber();

        // ΕΦΑΡΜΟΓΕΣ έχει ήδη διαλέξει πλατφόρμα στο Βήμα 2 (βλ. SelectAppMethod) — ολοκληρώνεται
        // εδώ μαζί με τη ΔΙΑΝΟΜΗ, και τα δύο περνάνε από τον πίνακα ζωντανών παραγγελιών.
        if (OrderType is Core.Models.OrderType.Delivery or Core.Models.OrderType.Apps)
            PushBoardOrder();
        CompleteOrder();
    }

    /// <summary>Κλείνει την παραγγελία: καταγραφή, αυτόματη εκτύπωση και επιστροφή στην αρχική.
    /// Ο έλεγχος βάρδιας έχει ήδη γίνει στο ContinueStep3 (βλ. εκεί γιατί πρέπει να προηγείται).</summary>
    private void CompleteOrder()
    {
        if (IsDeferredPersonRound && TableNumber is { } table)
        {
            // Το άτομο απλώς «κλείνει»: το καλάθι του πάει στην άκρη, ΤΙΠΟΤΑ δεν καταχωρείται ακόμα.
            StashCurrentPerson();
            _personsDone.Add(TablePerson);

            // Επόμενο άτομο = το πρώτο που δεν έχει κλείσει ακόμα. Δεν είναι απλώς «+1», γιατί ο ταμίας
            // μπορεί να γύρισε πίσω σε κάποιον που είχε ήδη παραγγείλει (άλλαξε γνώμη) — τότε συνεχίζουμε
            // από εκεί που είχαμε μείνει, χωρίς να ξαναρωτηθεί κάποιος που έχει ήδη τελειώσει.
            var next = Enumerable.Range(0, TablePersonCount).FirstOrDefault(p => !_personsDone.Contains(p), -1);
            if (next >= 0)
            {
                LoadPerson(next);
                return;
            }

            CommitTablePersons(table);
            return;
        }

        // Τυπώνεται ΑΥΤΗ η παραγγελία, όχι «ό,τι βρεθεί τελευταίο στη λίστα» (βλ. AutoPrintRequested).
        var order = RecordStats();
        AutoPrintRequested?.Invoke(order);
        NewOrder();
    }

    /// <summary>
    /// Έκλεισε το ΤΕΛΕΥΤΑΙΟ άτομο — τώρα καταχωρούνται όλα μαζί: μία παραγγελία ανά άτομο (άρα μία
    /// απόδειξη ο καθένας στην ταμειακή) και ΕΝΑ δελτίο στην κουζίνα.
    /// <para>Μέχρι εδώ δεν είχε δεσμευτεί ούτε αριθμός παραγγελίας: παίρνονται τώρα, όλοι μαζί, ώστε να
    /// μη μείνουν «τρύπες» αν ο ταμίας γύριζε πίσω στη μέση.</para>
    /// </summary>
    private void CommitTablePersons(int table)
    {
        // Ο έλεγχος βάρδιας γίνεται ΕΔΩ, μία φορά για όλο το τραπέζι — όχι σε κάθε άτομο (βλ. ContinueStep3).
        if (!ConfirmShiftMatchesTime())
            return;

        foreach (var person in _personDrafts.Keys.Order().ToList())
        {
            LoadPerson(person);
            Products.OrderNumber = NextDisplayNumber();
            var order = RecordStats();

            // Ο γύρος ΕΙΝΑΙ το άτομο: όλα του τα τεμάχια χρεώνονται στον ίδιο.
            for (var i = 0; i < order.Lines.Count; i++)
                for (var u = 0; u < Math.Max(1, order.Lines[i].Quantity); u++)
                    TablePersonsService.Instance.Assign(table, order.OrderNumber, i, u, person);
            _tableRounds.Add(order);
        }

        _personDrafts.Clear();
        SentPersonsAbove.Clear();
        SentPersonsBelow.Clear();
        // Όποιος τελικά δεν πήρε τίποτα δεν είναι άτομο του τραπεζιού (βλ. TablePersonsService).
        TablePersonsService.Instance.ShrinkToWhoOrdered(table);
        PrintTableRounds();
        NewOrder();
    }

    /// <summary>
    /// ΕΝΑ δελτίο κουζίνας για όλο το τραπέζι, στο τέλος. Οι παραγγελίες έχουν ήδη καταχωρηθεί μία ανά
    /// άτομο (μία απόδειξη ο καθένας) — αλλά ο ψήστης πρέπει να πάρει ένα χαρτί, όχι τέσσερα για το
    /// ίδιο τραπέζι. Το δελτίο βγαίνει ακριβώς όπως πάντα, χωρίς καμία αναφορά σε άτομα.
    /// </summary>
    private void PrintTableRounds()
    {
        if (_tableRounds.Count == 0)
            return;

        // ΠΡΟΣΘΗΚΗ σε τραπέζι που έχει ήδη παραγγείλει: ένα χαρτί ανά άτομο, με το άτομο γραμμένο
        // πάνω («ΤΡΑΠΕΖΙ #5 · ΑΤΟΜΟ Β»). Εδώ ο σερβιτόρος πρέπει να ξέρει σε ΠΟΙΟΝ πάει η κόκα κόλα —
        // ενώ στην πρώτη παραγγελία της παρέας το τραπέζι σερβίρεται μαζί και τα άτομα είναι θόρυβος.
        if (_isAddOnRound)
        {
            foreach (var round in _tableRounds)
                ReceiptPrinter.PrintOrder(round);
            _tableRounds.Clear();
            return;
        }

        var last = _tableRounds[^1];
        var ticket = _tableRounds.Count == 1
            ? last
            : new CompletedOrder
            {
                OrderNumber = last.OrderNumber,
                Type = last.Type,
                Who = last.Who,
                Total = _tableRounds.Sum(o => o.Total),
                Lines = _tableRounds.SelectMany(o => o.Lines).ToList(),
                DeliveryNotes = string.Join(" · ",
                    _tableRounds.Select(o => o.DeliveryNotes).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct()),
                IsEveningShift = last.IsEveningShift,
            };
        _tableRounds.Clear();
        ReceiptPrinter.PrintOrder(ticket);
    }

    /// <summary>
    /// Προειδοποίηση αν η επιλεγμένη βάρδια δεν ταιριάζει με την ώρα (πιθανό ξεχασμένος διακόπτης) —
    /// βραδινή επιλεγμένη το πρωί/μεσημέρι (6:00–18:59), ή πρωινή επιλεγμένη μετά τις 19:00.
    /// </summary>
    private static bool ConfirmShiftMatchesTime()
    {
        var hour = DateTime.Now.Hour;
        var evening = SettingsStore.Instance.Settings.IsEveningShift;
        var mismatch = evening ? hour is >= 6 and < 19 : hour >= 19;
        if (!mismatch)
            return true;

        var shiftLabel = evening ? "ΒΡΑΔΙΝΗ" : "ΠΡΩΙΝΗ";
        var answer = MessageBox.Show(
            $"Έχεις επιλεγμένη {shiftLabel} βάρδια, αλλά δεν ταιριάζει με την ώρα ({DateTime.Now:HH:mm}).\n\nΝα καταχωρηθεί έτσι η παραγγελία;",
            "Έλεγχος βάρδιας", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return answer == MessageBoxResult.Yes;
    }

    private bool _boardPushed;

    /// <summary>Διεύθυνση + αριθμός + περιοχή + Τ.Κ. + όροφος + σχόλια σε μία γραμμή για τον πίνακα
    /// διανομής. Σειρά σκόπιμη: οδός+αριθμός/περιοχή/Τ.Κ. πρώτα, χωρισμένα με κόμμα (ό,τι χρειάζεται
    /// ο Χάρτης Διανομής για γεωκωδικοποίηση — βλ. DeliveryMapWindow.CleanAddressForGeocoding), μετά
    /// ο όροφος με «·» και οι ελεύθερες σημειώσεις με «—» (δεν έχουν νόημα σε μια αναζήτηση χάρτη).</summary>
    private string ComposeDeliveryInfo()
    {
        var info = ComposeAddressLine();

        var floor = CustomerFloor.Trim();
        if (floor.Length > 0)
            info = info.Length > 0 ? info + " · " + floor : floor;

        var notes = CustomerNotes.Trim();
        if (notes.Length > 0)
            info = info.Length > 0 ? info + " — " + notes : notes;
        return info;
    }

    /// <summary>Μόνο οδός+αριθμός/περιοχή/Τ.Κ. — χωρίς όροφο/σχόλια (βλ. ComposeDeliveryInfo παραπάνω,
    /// που τα προσθέτει για τον πίνακα διανομής). Χρησιμοποιείται στο CompletedOrder.DeliveryAddress, όπου
    /// όροφος/σχόλια θέλουν να τυπωθούν σε ξεχωριστές γραμμές στην απόδειξη, όχι όλα μαζί σε μία πρόταση.</summary>
    private string ComposeAddressLine()
    {
        var street = (CustomerAddress.Trim() + " " + CustomerStreetNumber.Trim()).Trim();
        var parts = new[] { street, CustomerArea.Trim(), CustomerPostalCode.Trim() }.Where(s => s.Length > 0);
        return string.Join(", ", parts);
    }

    /// <summary>Καταχωρεί την παραγγελία στον πίνακα ζωντανών παραγγελιών (μία φορά ανά παραγγελία).</summary>
    private void PushBoardOrder()
    {
        if (_boardPushed)
            return;
        _boardPushed = true;
        Board.Add(new BoardOrder
        {
            OrderNumber = Products.OrderNumber,
            Name = OrderType == Core.Models.OrderType.Table
                ? "Τραπέζι " + TableNumber
                : (string.IsNullOrWhiteSpace(CustomerName) ? "—" : CustomerName),
            // BOX πλέον το παραδίδει δικός μας διανομέας (βλ. ShowCustomerForm) — θέλει την πραγματική
            // διεύθυνση παράδοσης σαν τη ΔΙΑΝΟΜΗ. Wolt/e-food δεν έχουν δική μας διεύθυνση — ο αριθμός
            // παραγγελίας της πλατφόρμας πάει πλέον στο δικό του πεδίο (βλ. AppOrderRef παρακάτω), όχι
            // εδώ, ώστε να είναι ο κύριος αριθμός στις Ζωντανές Παραγγελίες (βλ. BoardOrder.DisplayNumber).
            Address = OrderType == Core.Models.OrderType.Apps && AppPlatform != "BOX" ? "" : ComposeDeliveryInfo(),
            Type = OrderType ?? Core.Models.OrderType.Delivery,
            Channel = OrderType == Core.Models.OrderType.Apps ? AppPlatform : null,
            AppOrderRef = OrderType == Core.Models.OrderType.Apps && AppOrderRef.Trim().Length > 0 ? AppOrderRef.Trim() : null,
            PaymentMethod = ShowCustomerForm ? SelectedPaymentMethod : null,
            Total = Products.Total,
            IsEveningShift = SettingsStore.Instance.Settings.IsEveningShift,
        });
    }

    // ---- βήμα 4 ----

    public bool IsPickupOrder => OrderType == Core.Models.OrderType.Pickup;
    public string Step4Title => "Ώρα παραλαβής";
    public string Step4Sub => "Πότε θα παραλάβει ο πελάτης";

    /// <summary>Ο ταμίας διαλέγει πλατφόρμα στο Βήμα 2, πριν τα προϊόντα — μόνο επιλογή/highlight εδώ,
    /// ΔΕΝ προχωράει αυτόματα πια (πριν, Wolt/e-food προχωρούσαν αμέσως) — ο αριθμός παραγγελίας είναι
    /// πλέον υποχρεωτικός για Wolt/e-food (βλ. Step2ContinueEnabled), οπότε ο ταμίας πρέπει να πατήσει
    /// «ΣΥΝΕΧΕΙΑ» μόνος του αφού τον γράψει, ίδια λογική με τη ΔΙΑΝΟΜΗ/BOX.</summary>
    [RelayCommand]
    private void SelectAppMethod(AppMethodViewModel method)
    {
        AppPlatform = method.Name;
        foreach (var m in AppMethods)
            m.IsSelected = m == method;
    }

    [RelayCommand]
    private void SelectPickupTime(PickupTimeViewModel time)
    {
        PickupTime = time.Label;
        foreach (var t in PickupTimes)
            t.IsSelected = t == time;
        AdvanceTo(5);
    }

    [RelayCommand] private void BackStep4() => Step = 3;

    // ---- βήμα 5 ----

    public string ReceiptTypeLabel => TypeLabel();
    public string ReceiptWhoLabel => OrderType == Core.Models.OrderType.Table ? "Τραπέζι" : "Πελάτης";
    public string ReceiptWho => OrderType == Core.Models.OrderType.Table
        ? TableNumber?.ToString() ?? "—"
        : (string.IsNullOrWhiteSpace(CustomerName) ? "—" : CustomerName);

    [RelayCommand]
    private void BackStep5() =>
        Step = OrderType == Core.Models.OrderType.Apps ? 4 : 3;

    [RelayCommand]
    private void NewOrder()
    {
        ResetForm();
    }

    /// <summary>Καθαρίζει όλη τη φόρμα — κρατάει τον ίδιο αριθμό αν η παραγγελία δεν ολοκληρώθηκε.</summary>
    private void ResetForm()
    {
        Step = 1;
        MaxStep = 1;
        OrderType = null;
        TableNumber = null;
        CustomerSearch = CustomerName = CustomerPhone = CustomerAddress = "";
        CustomerStreetNumber = CustomerArea = CustomerPostalCode = "";
        CustomerFloor = CustomerNotes = CustomerMemo = "";
        // Καθαρή φόρμα στην επόμενη παραγγελία: χωρίς αυτό, τα κόκκινα κουτιά της προηγούμενης θα
        // υποδέχονταν τον ταμία πριν προλάβει να γράψει οτιδήποτε.
        Step2Validated = false;
        FillSuggestions(FirstNameSuggestions, [], nameof(HasFirstNameSuggestions));
        FillSuggestions(LastNameSuggestions, [], nameof(HasLastNameSuggestions));
        FillSuggestions(AddressSuggestions, [], nameof(HasAddressSuggestions));
        FillSuggestions(AreaSuggestions, [], nameof(HasAreaSuggestions));
        ClearCustomerAddressOptions();
        AppPlatform = null;
        AppOrderRef = "";
        SelectedPaymentMethod = null;
        ShowPaymentPrompt = false;
        PickupTime = null;
        _boardPushed = false;
        _orderCompleted = false;
        IsArrangingTables = false;
        _personDrafts.Clear();
        _personsDone.Clear();
        SentPersonsAbove.Clear();
        SentPersonsBelow.Clear();
        Products.AllowEmptyContinue = false;
        // Πίσω στις τιμές του μαγαζιού. Ο τιμοκατάλογος εφαρμογών ανάβει ΜΟΝΟ όταν επιλεγεί ΕΦΑΡΜΟΓΕΣ
        // στο Βήμα 1· αν έμενε αναμμένος από την προηγούμενη παραγγελία, η επόμενη ξεκινούσε με λάθος
        // τιμές χωρίς τίποτα να το δείχνει στην οθόνη.
        Products.UseDeliveryPrices = false;
        foreach (var o in OrderTypeOptions) o.IsSelected = false;
        foreach (var t in TableNumbers) t.IsSelected = false;
        foreach (var m in AppMethods) m.IsSelected = false;
        foreach (var t in PickupTimes) t.IsSelected = false;
        Products.Reset(NextDisplayNumber());
    }
}
