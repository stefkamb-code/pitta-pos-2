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

/// <summary>Πρόταση διεύθυνσης από τον χάρτη, καθώς πληκτρολογεί ο ταμίας (βλ. DeliveryRouteService).</summary>
public class AddressSuggestionViewModel
{
    public required DeliveryRouteService.AddressSuggestion Suggestion { get; init; }
    public string Display => Suggestion.Display;

    /// <summary>Οδός/αριθμός — πρώτο κομμάτι πριν το πρώτο κόμμα, σε έντονα. Το Nominatim επιστρέφει
    /// ολόκληρη διεύθυνση σε ένα string («Λεωφ. Χ 12, Δήμος, Περιφέρεια, Τ.Κ., Ελλάδα») — ο χωρισμός
    /// δείχνει πρώτα το πιο χρήσιμο κομμάτι, σαν προτάσεις του Google Maps.</summary>
    public string Primary => Display.Split(',')[0].Trim();

    /// <summary>Ό,τι απομένει μετά το πρώτο κόμμα — περιοχή/πόλη, σε μικρότερα/πιο αχνά γράμματα.</summary>
    public string Secondary => Display.Contains(',') ? Display[(Display.IndexOf(',') + 1)..].Trim() : "";
    public bool HasSecondary => Secondary.Length > 0;
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
    public required int DiscountPct { get; init; }
    public required decimal Total { get; init; }
}

public partial class OrderWizardViewModel : ObservableObject
{
    public OrderWizardViewModel()
    {
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
            new() { Key = Core.Models.OrderType.Apps, Label = "ΕΦΑΡΜΟΓΕΣ", Sub = "e-food · Wolt · BOX" },
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
        AppMethods =
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

    [RelayCommand]
    private void SelectMorningShift() => SettingsStore.Instance.SetShift(false);

    [RelayCommand]
    private void SelectEveningShift() => SettingsStore.Instance.SetShift(true);

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
        foreach (var n in Enumerable.Range(1, SettingsStore.Instance.Settings.TableCount))
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
            // Με την έκπτωση παραγγελίας μέσα — ίδιος υπολογισμός με την οθόνη τραπεζιού.
            return x.l.Revenue / units * unpaid * (1 - o.OrderDiscountPct / 100m);
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
            CustomerName = customer.Name;
            // Συμπλήρωση από αποθηκευμένα στοιχεία πελάτη, όχι πληκτρολόγηση — δεν πρέπει να ανοίξει τις
            // προτάσεις διεύθυνσης (ίδιο πρόβλημα με SelectCustomer/SelectCustomerAddressOption).
            _suppressAddressAutocomplete = true;
            CustomerAddress = customer.Address;
            _suppressAddressAutocomplete = false;
            CustomerStreetNumber = customer.StreetNumber;
            CustomerArea = customer.Area;
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
                    string.Join("\n", new[] { l.DescLine1, l.DescLine2 }.Where(s => s.Length > 0)), l.DiscountPct,
                    l.ProductId, l.Customization, l.PrintName))
                .ToList(),
            OrderDiscountPct = Products.OrderDiscountPct,
            IsEveningShift = SettingsStore.Instance.Settings.IsEveningShift,
        };

        // Ο αριθμός μπορεί να αλλάξει αν στο μεταξύ τον πήρε το κινητό του σερβιτόρου.
        var number = SalesStatsService.Instance.Record(order);
        return number == order.OrderNumber ? order : SalesStatsService.WithOrderNumber(order, number);
    }

    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;
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
    [NotifyPropertyChangedFor(nameof(Step2Header))]
    [NotifyPropertyChangedFor(nameof(Step2Sub))]
    [NotifyPropertyChangedFor(nameof(DisplayOrderNumber))]
    private OrderType? _orderType;

    /// <summary>ΤΡΑΠΕΖΙ/ΠΑΡΑΛΑΒΗ/ΕΦΑΡΜΟΓΕΣ δεν έχουν σχόλια πελάτη στο Βήμα 2 — δίνε κουμπί σχολίων μέσα στα Προϊόντα.</summary>
    public bool ShowOrderNoteButton => SkipsStep2;

    /// <summary>Βήμα 2 για ΔΙΑΝΟΜΗ, και για BOX (βλ. SelectAppMethod) — οι δύο περιπτώσεις που πραγματικά
    /// χρειάζονται στοιχεία πελάτη (όνομα/διεύθυνση), γιατί τις παραδίδει δικός μας διανομέας. Ο τρόπος
    /// πληρωμής πλέον ρωτιέται στο Βήμα 3 (Προϊόντα, βλ. ProductsView) — όχι εδώ.</summary>
    public bool ShowCustomerForm => OrderType == Core.Models.OrderType.Delivery
        || (OrderType == Core.Models.OrderType.Apps && AppPlatform == "BOX");

    /// <summary>Ποιοι τύποι παραγγελίας ρωτάνε τρόπο πληρωμής στο τέλος (βλ. ShowPaymentPrompt/
    /// ContinueStep3) — ΔΙΑΝΟΜΗ/BOX (βλ. ShowCustomerForm, τα παραδίδει δικός μας διανομέας) ΚΑΙ ΠΑΡΑΛΑΒΗ
    /// (ο πελάτης πληρώνει στο ταμείο όταν παραλαμβάνει). ΤΡΑΠΕΖΙ/e-food/Wolt δεν το χρειάζονται.</summary>
    public bool NeedsPaymentMethod => ShowCustomerForm || OrderType == Core.Models.OrderType.Pickup;

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
        set => SetFullName(value, CustomerLastName);
    }

    public string CustomerLastName
    {
        get
        {
            var s = CustomerName.Trim();
            var i = s.IndexOf(' ');
            return i < 0 ? "" : s[(i + 1)..].TrimStart();
        }
        set => SetFullName(CustomerFirstName, value);
    }

    private void SetFullName(string first, string last) =>
        CustomerName = string.Join(" ", new[] { first.Trim(), last.Trim() }.Where(s => s.Length > 0));

    /// <summary>Όταν αλλάζει το ενιαίο όνομα (π.χ. επιλογή πελάτη από την αναζήτηση), ξαναδιαβάζονται
    /// και τα δύο κελιά.</summary>
    partial void OnCustomerNameChanged(string value)
    {
        OnPropertyChanged(nameof(CustomerFirstName));
        OnPropertyChanged(nameof(CustomerLastName));
    }

    [ObservableProperty]
    private string _customerPhone = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrderContextLabel))]
    private string _customerAddress = "";

    /// <summary>Αριθμός οδού — ξεχωριστό πεδίο, ώστε ο Χάρτης Διανομής να εντοπίζει ακριβώς το σημείο.</summary>
    [ObservableProperty]
    private string _customerStreetNumber = "";

    [ObservableProperty]
    private string _customerArea = "";

    /// <summary>Ταχυδρομικός κώδικας — βοηθάει τη γεωκωδικοποίηση όταν η περιοχή έχει κοινό όνομα δρόμου.</summary>
    [ObservableProperty]
    private string _customerPostalCode = "";

    [ObservableProperty]
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
                DiscountPct = Products.OrderDiscountPct,
                Total = Products.Total,
            };
    }

    /// <summary>Ανοίγει ένα άτομο στη δεξιά στήλη: το καλάθι του γίνεται ΤΟ καλάθι της οθόνης.</summary>
    private void LoadPerson(int person)
    {
        TablePerson = person;
        Products.Cart.Clear();
        if (_personDrafts.TryGetValue(person, out var draft))
        {
            foreach (var line in draft.Lines)
                Products.Cart.Add(line);
            Products.OrderDiscountPct = draft.DiscountPct;
        }
        else
        {
            Products.OrderDiscountPct = 0;
        }
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
    /// <summary>ΔΙΑΝΟΜΗ: όνομα. ΕΦΑΡΜΟΓΕΣ: πλατφόρμα επιλεγμένη, και αριθμός παραγγελίας — υποχρεωτικός
    /// για Wolt/e-food, προαιρετικός για BOX (που έχει ήδη το πιο σημαντικό: όνομα/διεύθυνση πελάτη).</summary>
    public bool Step2ContinueEnabled
    {
        get
        {
            if (OrderType == Core.Models.OrderType.Apps)
            {
                if (string.IsNullOrWhiteSpace(AppPlatform))
                    return false;
                return AppPlatform == "BOX"
                    ? !string.IsNullOrWhiteSpace(CustomerName)
                    : AppOrderRef.Trim().Length > 0;
            }
            return !string.IsNullOrWhiteSpace(CustomerName);
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
            return CustomerStore.Instance.Search(q)
                // Μην ξαναδείχνεις τον ήδη επιλεγμένο πελάτη
                .Where(c => c.Name != CustomerName || c.Phone != CustomerPhone || c.Address != CustomerAddress)
                .Select(c => new CustomerMatchViewModel { Customer = c })
                .ToList();
        }
    }

    public bool HasCustomerMatches => CustomerMatches.Count > 0;

    /// <summary>Επιλογές αποθηκευμένων διευθύνσεων του τρέχοντος πελάτη (κύρια + τυχόν άλλες, βλ.
    /// Customer.OtherAddresses) — γεμίζει όταν φορτωθεί πελάτης (αναζήτηση ή εισερχόμενη κλήση), άδειο
    /// για νέο/άγνωστο πελάτη. Το picker φαίνεται μόνο όταν υπάρχει πάνω από μία επιλογή.</summary>
    public ObservableCollection<CustomerAddressOptionViewModel> CustomerAddressOptions { get; } = [];
    public bool HasMultipleCustomerAddresses => CustomerAddressOptions.Count > 1;

    /// <summary>Ο πελάτης που φόρτωσε τελευταία το picker διευθύνσεων — κρατιέται εδώ ώστε το
    /// DeleteCustomerAddressOption να ξέρει σε ποιον να αφαιρέσει τη διεύθυνση (βλ. CustomerStore).</summary>
    private Customer? _addressOptionsCustomer;

    private void LoadCustomerAddressOptions(Customer customer)
    {
        _addressOptionsCustomer = customer;
        IsAddingCustomerAddress = false;
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
        OnPropertyChanged(nameof(HasMultipleCustomerAddresses));
        OnPropertyChanged(nameof(CanAddCustomerAddress));
        OnPropertyChanged(nameof(ShowAddCustomerAddressButton));
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
        IsAddingCustomerAddress = false;
        OnPropertyChanged(nameof(HasMultipleCustomerAddresses));
        OnPropertyChanged(nameof(CanAddCustomerAddress));
        OnPropertyChanged(nameof(ShowAddCustomerAddressButton));
    }

    /// <summary>Το κουμπί «+ Νέα διεύθυνση» φαίνεται μόνο για ήδη γνωστό πελάτη (βρέθηκε με τηλέφωνο/
    /// όνομα) — για εντελώς νέο πελάτη δεν υπάρχει ακόμα Customer object να προστεθεί η διεύθυνση,
    /// δημιουργείται μόνο όταν ολοκληρωθεί η παραγγελία (βλ. CustomerStore.Upsert).</summary>
    public bool CanAddCustomerAddress => _addressOptionsCustomer is not null;
    /// <summary>Το κουμπί κρύβεται όσο είναι ήδη ανοιχτή η φόρμα προσθήκης (βλ. παρακάτω).</summary>
    public bool ShowAddCustomerAddressButton => CanAddCustomerAddress && !IsAddingCustomerAddress;

    // ---- μικρή, ξεχωριστή φόρμα «νέα διεύθυνση πελάτη» (Βήμα 2) — δικά της πεδία, ΟΧΙ τα πεδία της
    // τρέχουσας παραγγελίας, ώστε να μην μπερδεύεται ο ταμίας για το ποια διεύθυνση επεξεργάζεται. ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAddCustomerAddressButton))]
    private bool _isAddingCustomerAddress;
    [ObservableProperty]
    private string _newAddressStreet = "";
    [ObservableProperty]
    private string _newAddressNumber = "";
    [ObservableProperty]
    private string _newAddressArea = "";
    [ObservableProperty]
    private string _newAddressPostalCode = "";
    [ObservableProperty]
    private string _newAddressFloor = "";

    [RelayCommand]
    private void ShowAddCustomerAddressForm()
    {
        NewAddressStreet = NewAddressNumber = NewAddressArea = NewAddressPostalCode = NewAddressFloor = "";
        IsAddingCustomerAddress = true;
    }

    [RelayCommand]
    private void CancelAddCustomerAddress() => IsAddingCustomerAddress = false;

    /// <summary>Ο ταμίας γέμισε τη ξεχωριστή φόρμα «νέα διεύθυνση» και πατά «Αποθήκευση» — προστίθεται
    /// κατευθείαν στον πελάτη, χωρίς να χρειάζεται να ολοκληρωθεί πρώτα παραγγελία με αυτή τη διεύθυνση
    /// (βλ. CustomerStore.AddOtherAddress). Επιλέγεται ΚΑΙ αυτόματα για την τρέχουσα παραγγελία — ο
    /// ταμίας τη γράφει εδώ επειδή θέλει να παραδοθεί ΕΚΕΙ αυτή η παραγγελία, όχι απλώς να αποθηκευτεί
    /// για το μέλλον· χωρίς αυτό η παραγγελία έφευγε με ό,τι διεύθυνση ήταν ήδη επιλεγμένη (συνήθως η
    /// κύρια), εκτός αν ο ταμίας πατούσε ΚΑΙ το καινούριο κουμπί που εμφανίζεται μετά την αποθήκευση.</summary>
    [RelayCommand]
    private void ConfirmAddCustomerAddress()
    {
        if (_addressOptionsCustomer is not { } customer || NewAddressStreet.Trim().Length == 0)
            return;
        var street = NewAddressStreet.Trim();
        var number = NewAddressNumber.Trim();
        var area = NewAddressArea.Trim();
        var postalCode = NewAddressPostalCode.Trim();
        var floor = NewAddressFloor.Trim();

        CustomerStore.Instance.AddOtherAddress(customer, street, number, area, postalCode, floor);
        LoadCustomerAddressOptions(customer);
        IsAddingCustomerAddress = false;

        _suppressAddressAutocomplete = true;
        CustomerAddress = street;
        _suppressAddressAutocomplete = false;
        CustomerStreetNumber = number;
        CustomerArea = area;
        CustomerPostalCode = postalCode;
        CustomerFloor = floor;
    }

    // ---- προτάσεις διεύθυνσης για τη φόρμα «νέα διεύθυνση πελάτη» — ίδια λογική με το OnCustomerAddressChanged
    // παρακάτω, αλλά ξεχωριστή λίστα/dropdown (κάτω από το δικό της πεδίο Οδού), ώστε οι δύο φόρμες να μην
    // μοιράζονται προτάσεις όταν είναι και οι δύο ορατές μαζί. ----

    private CancellationTokenSource? _newAddressSuggestCts;
    private bool _suppressNewAddressAutocomplete;
    private string _lastNewAddressSuggestQuery = "";

    public ObservableCollection<AddressSuggestionViewModel> NewAddressSuggestions { get; } = [];
    public bool HasNewAddressSuggestions => NewAddressSuggestions.Count > 0;

    partial void OnNewAddressStreetChanged(string value)
    {
        _newAddressSuggestCts?.Cancel();
        if (_suppressNewAddressAutocomplete || value.Trim().Length < 3)
        {
            NewAddressSuggestions.Clear();
            _lastNewAddressSuggestQuery = "";
            OnPropertyChanged(nameof(HasNewAddressSuggestions));
            return;
        }

        var cts = new CancellationTokenSource();
        _newAddressSuggestCts = cts;
        _ = DebouncedSuggestNewAddressAsync(value.Trim(), cts.Token);
    }

    private async Task DebouncedSuggestNewAddressAsync(string query, CancellationToken token)
    {
        try
        {
            await Task.Delay(110, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        if (token.IsCancellationRequested)
            return;

        var results = await DeliveryRouteService.SuggestAddressesAsync(query);
        if (token.IsCancellationRequested)
            return;

        // Ίδιο fix με το OnCustomerAddressChanged/DebouncedSuggestAddressAsync — κρατάμε τη λίστα ορατή
        // και όταν ο ταμίας διαγράφει χαρακτήρες (backspace) πάνω στην ίδια διεύθυνση, όχι μόνο όταν
        // γράφει προς τα εμπρός· αλλιώς κάθε backspace σε μισοτελειωμένη λέξη άδειαζε τη λίστα οριστικά.
        if (results.Count == 0)
        {
            var sameAddress = _lastNewAddressSuggestQuery.Length > 0
                && (query.StartsWith(_lastNewAddressSuggestQuery, StringComparison.OrdinalIgnoreCase)
                    || _lastNewAddressSuggestQuery.StartsWith(query, StringComparison.OrdinalIgnoreCase));
            if (!sameAddress)
            {
                NewAddressSuggestions.Clear();
                OnPropertyChanged(nameof(HasNewAddressSuggestions));
            }
            return;
        }

        _lastNewAddressSuggestQuery = query;
        NewAddressSuggestions.Clear();
        foreach (var r in results)
            NewAddressSuggestions.Add(new AddressSuggestionViewModel { Suggestion = r });
        OnPropertyChanged(nameof(HasNewAddressSuggestions));
    }

    [RelayCommand]
    private async Task SelectNewAddressSuggestion(AddressSuggestionViewModel item)
    {
        _newAddressSuggestCts?.Cancel();
        NewAddressSuggestions.Clear();
        _lastNewAddressSuggestQuery = "";
        OnPropertyChanged(nameof(HasNewAddressSuggestions));

        var suggestion = item.Suggestion;
        if (suggestion.PlaceId is not null)
        {
            var resolved = await DeliveryRouteService.ResolveGooglePlaceAsync(suggestion.PlaceId);
            if (resolved is not null)
                suggestion = resolved;
        }

        var typedNumber = suggestion.HouseNumber.Length == 0
            ? Regex.Match(NewAddressStreet, @"\d+\s*[Α-Ωα-ωA-Za-z]?\s*$").Value.Trim()
            : "";

        _suppressNewAddressAutocomplete = true;
        NewAddressStreet = suggestion.Street;
        _suppressNewAddressAutocomplete = false;
        if (suggestion.HouseNumber.Length > 0)
            NewAddressNumber = suggestion.HouseNumber;
        else if (typedNumber.Length > 0 && NewAddressNumber.Trim().Length == 0)
            NewAddressNumber = typedNumber;
        if (suggestion.Area.Length > 0)
            NewAddressArea = suggestion.Area;
        if (suggestion.PostalCode.Length > 0)
            NewAddressPostalCode = suggestion.PostalCode;
    }

    /// <summary>Ο ταμίας διάλεξε άλλη αποθηκευμένη διεύθυνση από το picker (Βήμα 2).</summary>
    [RelayCommand]
    private void SelectCustomerAddressOption(CustomerAddressOptionViewModel option)
    {
        _suppressAddressAutocomplete = true;
        CustomerAddress = option.Address;
        _suppressAddressAutocomplete = false;
        CustomerStreetNumber = option.StreetNumber;
        CustomerArea = option.Area;
        CustomerPostalCode = option.PostalCode;
        CustomerFloor = option.Floor;
    }

    [RelayCommand]
    private void SelectCustomer(CustomerMatchViewModel match)
    {
        CustomerName = match.Customer.Name;
        CustomerPhone = match.Customer.Phone;
        // Συμπλήρωση από αποθηκευμένα στοιχεία πελάτη, όχι πληκτρολόγηση — δεν πρέπει να ανοίξει τις
        // προτάσεις διεύθυνσης (ίδιο πρόβλημα με το Τηλέφωνο/Διεύθυνση, βλ. CustomerMatches παραπάνω).
        _suppressAddressAutocomplete = true;
        CustomerAddress = match.Customer.Address;
        _suppressAddressAutocomplete = false;
        CustomerStreetNumber = match.Customer.StreetNumber;
        CustomerArea = match.Customer.Area;
        CustomerPostalCode = match.Customer.PostalCode;
        CustomerFloor = match.Customer.Floor;
        CustomerNotes = match.Customer.Notes;
        CustomerMemo = match.Customer.Memo;
        CustomerSearch = "";
        LoadCustomerAddressOptions(match.Customer);
    }

    // ---- προτάσεις διεύθυνσης από τον χάρτη, ενόσω πληκτρολογεί (βλ. DeliveryRouteService) ----

    private CancellationTokenSource? _addressSuggestCts;
    private bool _suppressAddressAutocomplete;
    /// <summary>Το τελευταίο κείμενο που έδωσε ΜΗ κενά αποτελέσματα — ώστε μια κενή απάντηση (βλ.
    /// DebouncedSuggestAddressAsync) να ξέρουμε αν πρέπει να κρατήσουμε την τρέχουσα λίστα ορατή (ο
    /// ταμίας απλά συνεχίζει να γράφει την ΙΔΙΑ διεύθυνση) ή να την αδειάσουμε (άλλαξε εντελώς κείμενο).</summary>
    private string _lastAddressSuggestQuery = "";

    public ObservableCollection<AddressSuggestionViewModel> AddressSuggestions { get; } = [];
    public bool HasAddressSuggestions => AddressSuggestions.Count > 0;

    /// <summary>Καλείται σε κάθε πληκτρολόγηση της διεύθυνσης — περιμένει λίγο πριν ρωτήσει τον
    /// χάρτη (το Nominatim θέλει &lt;= 1 αίτημα/δευτ., δεν αντέχει ερώτημα ανά χαρακτήρα).</summary>
    partial void OnCustomerAddressChanged(string value)
    {
        _addressSuggestCts?.Cancel();

        // Δεν καθαρίζουμε τις προτάσεις εδώ, σε κάθε πάτημα πλήκτρου — μόνο όταν φτάσουν οι καινούριες
        // (βλ. DebouncedSuggestAddressAsync) ή όταν η αναζήτηση ακυρώνεται εντελώς παρακάτω. Αλλιώς η
        // λίστα άδειαζε στιγμιαία σε κάθε χαρακτήρα πριν ξαναγεμίσει — φαινόταν σαν να «χάνεται»/τρεμοπαίζει
        // η αυτόματη συμπλήρωση ενώ ο ταμίας πληκτρολογεί, ακόμα και όταν τα γράμματα ταίριαζαν κανονικά.
        if (_suppressAddressAutocomplete || !ShowAddressField || value.Trim().Length < 3)
        {
            AddressSuggestions.Clear();
            _lastAddressSuggestQuery = "";
            OnPropertyChanged(nameof(HasAddressSuggestions));
            return;
        }

        var cts = new CancellationTokenSource();
        _addressSuggestCts = cts;
        _ = DebouncedSuggestAddressAsync(value.Trim(), cts.Token);
    }

    private async Task DebouncedSuggestAddressAsync(string query, CancellationToken token)
    {
        try
        {
            // 110ms — πιο άμεσο χωρίς να ρισκάρει το όριο του Nominatim (<=1 αίτημα/δευτ.): κάθε νέος
            // χαρακτήρας ακυρώνει το προηγούμενο αναμονή, οπότε φεύγει αίτημα μόνο όταν ο ταμίας
            // σταματήσει πραγματικά να πληκτρολογεί, όχι ανά χαρακτήρα — ο πραγματικός ρυθμός αιτημάτων
            // δεν εξαρτάται από αυτή την τιμή, μόνο πόσο γρήγορα αντιδρά μετά το τελευταίο πάτημα.
            await Task.Delay(110, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        if (token.IsCancellationRequested)
            return;

        var results = await DeliveryRouteService.SuggestAddressesAsync(query);
        if (token.IsCancellationRequested)
            return;

        // Το Nominatim δεν κάνει καλό prefix-ταίριασμα σε μισοτελειωμένη λέξη (δοκιμασμένο: «28ης» δίνει
        // αποτελέσματα, «28ης οκτ» δίνει μηδέν, μόνο η πλήρης «28ης Οκτωβρίου» ξαναδουλεύει) — μια κενή
        // απάντηση εδώ είναι πολύ πιθανό να είναι στιγμιαία, όχι πραγματική «δεν υπάρχει τίποτα». Κρατάμε
        // ορατή την τελευταία καλή λίστα όταν ο ταμίας συνεχίζει να επεξεργάζεται την ΙΔΙΑ διεύθυνση —
        // είτε γράφοντας προς τα εμπρός (το νέο κείμενο ξεκινά με αυτό που έδωσε τη λίστα) ΕΙΤΕ διαγράφοντας
        // μερικούς χαρακτήρες με backspace (το κείμενο που έδωσε τη λίστα ξεκινά με το νέο, μικρότερο
        // κείμενο) — πριν έλεγχε μόνο την πρώτη κατεύθυνση, οπότε ΚΑΘΕ backspace που έπεφτε σε μισοτελειωμένη
        // λέξη (πολύ συχνό, βλ. πάνω) άδειαζε αμέσως τη λίστα χωρίς να ξαναγεμίσει ποτέ, ακόμα κι αν ο
        // ταμίας ξαναέγραφε προς τα εμπρός την ίδια σωστή διεύθυνση. Αλλιώς (κάτι εντελώς άλλο) πρέπει να
        // αδειάσει, αλλιώς θα έδειχνε παλιές άσχετες προτάσεις.
        if (results.Count == 0)
        {
            var sameAddress = _lastAddressSuggestQuery.Length > 0
                && (query.StartsWith(_lastAddressSuggestQuery, StringComparison.OrdinalIgnoreCase)
                    || _lastAddressSuggestQuery.StartsWith(query, StringComparison.OrdinalIgnoreCase));
            if (!sameAddress)
            {
                AddressSuggestions.Clear();
                OnPropertyChanged(nameof(HasAddressSuggestions));
            }
            return;
        }

        _lastAddressSuggestQuery = query;
        AddressSuggestions.Clear();
        foreach (var r in results)
            AddressSuggestions.Add(new AddressSuggestionViewModel { Suggestion = r });
        OnPropertyChanged(nameof(HasAddressSuggestions));
    }

    /// <summary>Στο Google, η πρόταση φτάνει με μόνο Display+PlaceId (βλ. DeliveryRouteService) — τα
    /// πεδία της φόρμας λύνονται εδώ, μία φορά, μόνο για την επιλογή που πάτησε ο ταμίας.</summary>
    [RelayCommand]
    private async Task SelectAddressSuggestion(AddressSuggestionViewModel item)
    {
        _addressSuggestCts?.Cancel();
        AddressSuggestions.Clear();
        _lastAddressSuggestQuery = "";
        OnPropertyChanged(nameof(HasAddressSuggestions));

        var suggestion = item.Suggestion;
        if (suggestion.PlaceId is not null)
        {
            var resolved = await DeliveryRouteService.ResolveGooglePlaceAsync(suggestion.PlaceId);
            if (resolved is not null)
                suggestion = resolved;
        }

        // Αν ο ταμίας έγραψε οδό ΚΑΙ αριθμό μαζί στο ίδιο κουτί (π.χ. «Μαγνησίας 12») αλλά το Nominatim
        // δεν επέστρεψε house_number (δεν είναι πάντα καταχωρημένο ανά αριθμό στο OSM), ο αριθμός δεν
        // πρέπει να χαθεί όταν το κουτί διεύθυνσης ξαναγραφεί με μόνο το όνομα δρόμου — τον κρατάμε από
        // ό,τι είχε ήδη πληκτρολογηθεί και τον βάζουμε στο δικό του κουτί δίπλα.
        var typedNumber = suggestion.HouseNumber.Length == 0
            ? Regex.Match(CustomerAddress, @"\d+\s*[Α-Ωα-ωA-Za-z]?\s*$").Value.Trim()
            : "";

        _suppressAddressAutocomplete = true;
        CustomerAddress = suggestion.Street;
        _suppressAddressAutocomplete = false;
        if (suggestion.HouseNumber.Length > 0)
            CustomerStreetNumber = suggestion.HouseNumber;
        else if (typedNumber.Length > 0 && CustomerStreetNumber.Trim().Length == 0)
            CustomerStreetNumber = typedNumber;
        if (suggestion.Area.Length > 0)
            CustomerArea = suggestion.Area;
        if (suggestion.PostalCode.Length > 0)
            CustomerPostalCode = suggestion.PostalCode;
    }

    [RelayCommand] private void BackStep2() => ResetForm();

    [RelayCommand]
    private void ContinueStep2()
    {
        SaveCustomer();
        Products.SetRepeatableOrder(FindLastOrderLines(CustomerName));
        AdvanceTo(3);
    }

    /// <summary>Ψάχνει την πιο πρόσφατη ολοκληρωμένη παραγγελία αυτού του πελάτη (σημερινές + αρχείο
    /// ιστορικού) για το κουμπί «μία από τα ίδια» στα προϊόντα. Ταίριασμα με το όνομα, γιατί το
    /// αρχειοθετημένο ιστορικό δεν κρατάει τηλέφωνο ανά παραγγελία.</summary>
    private static IReadOnlyList<SoldLine>? FindLastOrderLines(string customerName)
    {
        var name = customerName.Trim();
        if (name.Length == 0)
            return null;

        return SalesStatsService.Instance.Orders
            .Concat(HistoryArchiveService.LoadOrders(DateTime.Now.AddDays(-180), DateTime.Now))
            .Where(o => string.Equals(o.Who.Trim(), name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.PlacedAt)
            .FirstOrDefault()?.Lines;
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

    /// <summary>Σηκώνεται όταν ολοκληρώνεται παραγγελία — τυπώνει αυτόματα την απόδειξη.</summary>
    public event Action? AutoPrintRequested;

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

        RecordStats();
        AutoPrintRequested?.Invoke();
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
        foreach (var o in OrderTypeOptions) o.IsSelected = false;
        foreach (var t in TableNumbers) t.IsSelected = false;
        foreach (var m in AppMethods) m.IsSelected = false;
        foreach (var t in PickupTimes) t.IsSelected = false;
        Products.Reset(NextDisplayNumber());
    }
}
