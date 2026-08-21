using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>Κανάλι αποστολής με το χρώμα του (όπως στο design). Διανομέας και BOX (τα δύο που παραδίδει
/// δικός μας διανομέας — βλ. OrderWizardViewModel.ShowCustomerForm) έχουν ο καθένας δικό του «Κάρτα»
/// παραλλαγή αντί για ένα γενικό κοινό κανάλι «Κάρτα» — έτσι το ΠΕΡΑΣΕ ΤΗΝ ΣΕ/στατιστικά ξέρουν ακριβώς
/// ποιος διανομέας πληρώθηκε με κάρτα. e-food/Wolt δεν έχουν παραλλαγή — τα πληρώνει ο πελάτης στην ίδια
/// την πλατφόρμα, δεν μας αφορά.</summary>
public sealed record ChannelInfo(string Name, Brush Brush)
{
    public static readonly IReadOnlyList<ChannelInfo> All =
    [
        new("Διανομέας", new SolidColorBrush(Color.FromRgb(0x20, 0x1e, 0x1d))),
        new("Κάρτα Διανομέα", new SolidColorBrush(Color.FromRgb(0x20, 0x1e, 0x1d))),
        new("e-food", new SolidColorBrush(Color.FromRgb(0xd3, 0x2f, 0x2f))),
        new("Wolt", new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xc0))),
        // Wolt Drive: ΔΙΚΗ ΜΑΣ παραγγελία (δικός μας πελάτης, δική μας διεύθυνση, δικά μας λεφτά) που
        // απλώς την παραδίδει κούριερ της Wolt αντί για τον διανομέα μας. Γι' αυτό δεν είναι πλατφόρμα
        // στο Βήμα 2 μαζί με e-food/Wolt — είναι κανάλι ΑΠΟΣΤΟΛΗΣ, δίπλα στον Διανομέα.
        new("Wolt Drive", new SolidColorBrush(Color.FromRgb(0x1e, 0x88, 0xe5))),
        new("BOX Μετρητά", new SolidColorBrush(Color.FromRgb(0xb8, 0x86, 0x0b))),
        new("BOX Κάρτα", new SolidColorBrush(Color.FromRgb(0xb8, 0x86, 0x0b))),
    ];
}

/// <summary>Παραγγελία στον πίνακα ζωντανών παραγγελιών.</summary>
public partial class BoardOrder : ObservableObject
{
    public required int OrderNumber { get; init; }
    /// <summary>Όνομα πελάτη ή «Τραπέζι Ν».</summary>
    public required string Name { get; init; }
    public string Address { get; init; } = "";
    /// <summary>Αλλάζει όταν διορθωθεί το κανάλι από το Ιστορικό (βλ. SalesStatsService.UpdateChannel):
    /// ο πίνακας πρέπει να δείχνει ό,τι δείχνει και το Ιστορικό, αλλιώς η ίδια παραγγελία είναι ΔΙΑΝΟΜΗ
    /// στη μία οθόνη και e-food στην άλλη.</summary>
    public required OrderType Type { get; set; }
    /// <summary>Πλατφόρμα για παραγγελίες εφαρμογών (e-food/Wolt/BOX).</summary>
    public string? Channel { get; set; }
    /// <summary>Ο αριθμός παραγγελίας που δίνει η ίδια η πλατφόρμα (Wolt/e-food/BOX) — μόνο για ΕΦΑΡΜΟΓΕΣ.
    /// Γίνεται ο κύριος αριθμός που φαίνεται στις Ζωντανές Παραγγελίες, βλ. DisplayNumber.</summary>
    public string? AppOrderRef { get; init; }
    /// <summary>Μετρητά ή κάρτα — μόνο για ΔΙΑΝΟΜΗ/BOX (τα παραδίδει δικός μας διανομέας, βλ.
    /// OrderWizardViewModel.ShowCustomerForm)· null για e-food/Wolt/ΠΑΡΑΛΑΒΗ/ΤΡΑΠΕΖΙ, δεν έχει νόημα εκεί.</summary>
    /// <summary>Αλλάζει όταν η παραγγελία περάσει (ή αλλάξει) σε κανάλι διανομέα: τα κανάλια
    /// «Διανομέας/BOX Μετρητά» και «Κάρτα Διανομέα/BOX Κάρτα» ΕΙΝΑΙ ο τρόπος πληρωμής, δεν είναι απλώς
    /// ετικέτες (βλ. OrderBoardService.PaymentOf).</summary>
    public PaymentMethod? PaymentMethod { get; set; }
    /// <summary>Αλλάζει όταν ακυρωθεί γραμμή από ήδη καταχωρημένη παραγγελία (βλ.
    /// SalesStatsService.RemoveLine) — γι' αυτό δεν είναι init: ο πίνακας δείχνει τι θα ΕΙΣΠΡΑΞΕΙ ο
    /// διανομέας, και ένα ποσό που έμεινε στο παλιό είναι λάθος λεφτά στο χέρι του.</summary>
    public required decimal Total { get; set; }
    public DateTime PlacedAt { get; init; } = DateTime.Now;
    /// <summary>Ποια βάρδια ήταν ενεργή (χειροκίνητος διακόπτης) τη στιγμή της παραγγελίας — όχι με βάση την ώρα.
    /// Ξαναγράφεται στο Dispatch/Reassign (βλ. OrderBoardService), ώστε μια παραγγελία που βρισκόταν σε
    /// αναμονή να μετρήσει στη βάρδια που πραγματικά την πέρασε ο ταμίας σε κανάλι, όχι σε όποια βάρδια
    /// έτυχε να είναι ενεργή όταν καταχωρήθηκε αρχικά — αλλιώς η μέτρηση καναλιού δείχνει πάντα 0 για
    /// παραγγελίες που έμειναν σε αναμονή από την προηγούμενη βάρδια.</summary>
    public bool IsEveningShift { get; set; }

    /// <summary>Σε ποιο κανάλι «πέρασε»· null = σε αναμονή.</summary>
    [ObservableProperty]
    private string? _sentVia;

    [ObservableProperty]
    private DateTime? _sentAt;

    public bool IsPending => SentVia is null;

    public string TypeLabel => Channel ?? Type switch
    {
        OrderType.Delivery => "ΔΙΑΝΟΜΗ",
        OrderType.Apps => "ΕΦΑΡΜΟΓΕΣ",
        OrderType.Pickup => "ΟΡΘΙΟΣ",
        OrderType.Table => "ΤΡΑΠΕΖΙ",
        _ => "",
    };

    public string TotalLabel => Order.FormatPrice(Total);

    /// <summary>Ο αριθμός που φαίνεται δίπλα στο «#» στις Ζωντανές Παραγγελίες — για ΕΦΑΡΜΟΓΕΣ με
    /// δηλωμένο αριθμό πλατφόρμας, αυτός είναι ο κύριος (πιο χρήσιμος για αντιστοίχιση με Wolt/e-food/BOX
    /// παρά ο εσωτερικός μας μετρητής)· διαφορετικά ο εσωτερικός OrderNumber, όπως πάντα.</summary>
    /// Ίδιος κανόνας με το CompletedOrder.DisplayNumber — η σειρά της βάρδιας διψήφια («01»), για να
    /// μη διαφωνούν οθόνη και χαρτί.
    public string DisplayNumber => !string.IsNullOrWhiteSpace(AppOrderRef)
        ? AppOrderRef!
        : OrderNumber < SalesStatsService.ExternalBandStart
            ? OrderNumber.ToString("00")
            : OrderNumber.ToString();

    /// <summary>Εικονίδιο μετρητών/κάρτας για τις Ζωντανές Παραγγελίες — null όταν δεν έχει νόημα
    /// (PaymentMethod == null), οπότε δεν εμφανίζεται τίποτα.</summary>
    public string? PaymentIcon => PaymentMethod switch
    {
        Core.Models.PaymentMethod.Cash => "💶",
        Core.Models.PaymentMethod.Card => "💳",
        _ => null,
    };

    /// <summary>Πληρωμένη με κάρτα — ο διανομέας ΔΕΝ εισπράττει τίποτα. Δείχνεται με ✓ δίπλα στο ποσό
    /// στις Ζωντανές Παραγγελίες, ώστε να ξεχωρίζει με μια ματιά τι πρέπει να μαζέψει και τι όχι.</summary>
    public bool IsPaidByCard => PaymentMethod == Core.Models.PaymentMethod.Card;

    public string ElapsedLabel
    {
        get
        {
            var diff = DateTime.Now - PlacedAt;
            if (diff < TimeSpan.Zero) diff = TimeSpan.Zero;
            return (int)diff.TotalMinutes + ":" + diff.Seconds.ToString("00");
        }
    }

    /// <summary>Πράσινο < 10', κίτρινο 10–19', κόκκινο ≥ 20'. Δεν σερβίρεται σε JSON (server-to-server
    /// sync/αποθήκευση) — ένα WPF Brush σέρνει μαζί του αναφορές (π.χ. System.Type) που ο System.Text.Json
    /// δεν υποστηρίζει και έσκαγε σιωπηλά κάθε POST/GET στο /api/sync/board μόλις υπήρχε έστω 1 παραγγελία,
    /// εμποδίζοντας τον συγχρονισμό των ζωντανών παραγγελιών ανάμεσα στα δύο ταμεία.</summary>
    [JsonIgnore]
    public Brush ElapsedBrush
    {
        get
        {
            var mins = (DateTime.Now - PlacedAt).TotalMinutes;
            var color = mins >= 20 ? Color.FromRgb(0xc0, 0x39, 0x2b)
                : mins >= 10 ? Color.FromRgb(0xb8, 0x86, 0x0b)
                : Color.FromRgb(0x2e, 0x7d, 0x32);
            return new SolidColorBrush(color);
        }
    }

    /// <summary>Τι γράφει η λίστα ενός καναλιού διανομέα: η ΔΙΕΥΘΥΝΣΗ, όχι το ονοματεπώνυμο. Εκεί μέσα
    /// ο διανομέας κοιτάζει πού πάει — το όνομα δεν του λέει τίποτα για τη διαδρομή. Πέφτει πίσω στο
    /// όνομα όπου δεν υπάρχει δική μας διεύθυνση (e-food/Wolt, τραπέζια).</summary>
    public string AddressOrName => Address.Length > 0 ? Address : Name;

    /// <summary>Το όνομα ΚΑΤΩ από τη διεύθυνση — κενό όταν διεύθυνση δεν υπάρχει (e-food/Wolt), γιατί
    /// τότε τη θέση της την έχει πάρει ήδη το όνομα και θα γραφόταν δύο φορές.</summary>
    public string NameUnderAddress => Address.Length > 0 ? Name : "";

    /// <summary>Νέος τρόπος πληρωμής — με ειδοποίηση, ώστε να αλλάξει και το εικονίδιο 💶/💳.</summary>
    public void UpdatePaymentMethod(PaymentMethod? method)
    {
        if (PaymentMethod == method)
            return;
        PaymentMethod = method;
        OnPropertyChanged(nameof(PaymentMethod));
        OnPropertyChanged(nameof(PaymentIcon));
        OnPropertyChanged(nameof(IsPaidByCard));
    }

    /// <summary>Νέο ποσό μετά από ακύρωση γραμμής — με ειδοποίηση, ώστε να αλλάξει και η οθόνη.</summary>
    public void UpdateTotal(decimal total)
    {
        if (Total == total)
            return;
        Total = total;
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(TotalLabel));
    }

    /// <summary>Ανανεώνει το ρολόι καθυστέρησης (καλείται κάθε δευτερόλεπτο).</summary>
    public void Tick()
    {
        OnPropertyChanged(nameof(ElapsedLabel));
        OnPropertyChanged(nameof(ElapsedBrush));
    }
}

/// <summary>
/// Κοινή κατάσταση των ζωντανών παραγγελιών ανάμεσα στην οθόνη POS και στον πίνακα.
/// JSON στο %AppData%\PittaPos — ώστε ένα ξαφνικό κλείσιμο/restart του υπολογιστή να μη χάνει τη μέρα.
/// </summary>
public partial class OrderBoardService : ObservableObject
{
    public static OrderBoardService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;

    public ObservableCollection<BoardOrder> Orders { get; } = [];

    [ObservableProperty]
    private int _pendingCount;

    /// <summary>Σηκώνεται σε κάθε μεταβολή (προσθήκη/αποστολή/επαναφορά).</summary>
    public event Action? Changed;

    private OrderBoardService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "board-orders.json");
        if (RemoteSync.IsClient)
        {
            RemoteSync.StartPolling(TimeSpan.FromSeconds(2), RefreshFromHostAsync);
            return;
        }
        Load();
        PendingCount = Orders.Count(o => o.IsPending);
    }

    /// <summary>
    /// Δεύτερο ταμείο (client) — συγχρονίζει με το host. Ενημερώνει τα υπάρχοντα αντικείμενα στη θέση
    /// τους (ίδιο instance) αντί να τα ξαναφτιάχνει όλα, ώστε ο πίνακας ζωντανών παραγγελιών να μη
    /// «τρεμοπαίζει» σε κάθε poll.
    /// </summary>
    private async Task RefreshFromHostAsync()
    {
        var data = await RemoteSync.GetAsync<List<BoardOrder>>("/api/sync/board");
        if (data is null)
            return;

        var incomingNumbers = data.Select(o => o.OrderNumber).ToHashSet();
        for (var i = Orders.Count - 1; i >= 0; i--)
            if (!incomingNumbers.Contains(Orders[i].OrderNumber))
                Orders.RemoveAt(i);

        foreach (var incoming in data)
        {
            var existing = Orders.FirstOrDefault(o => o.OrderNumber == incoming.OrderNumber);
            if (existing is null)
                Orders.Add(incoming);
            else
            {
                existing.SentVia = incoming.SentVia;
                existing.SentAt = incoming.SentAt;
                // Και τα δύο αλλάζουν στο κύριο ταμείο ΜΕΤΑ την καταχώρηση: το ποσό όταν ακυρωθεί
                // γραμμή, η βάρδια όταν περάσει η παραγγελία σε κανάλι (βλ. Dispatch/Reassign). Χωρίς
                // αυτά, το δεύτερο ταμείο έδειχνε το παλιό ποσό μέχρι να φύγει η παραγγελία.
                existing.UpdateTotal(incoming.Total);
                existing.UpdatePaymentMethod(incoming.PaymentMethod);
                existing.IsEveningShift = incoming.IsEveningShift;
                // Και ο τύπος/πλατφόρμα: αλλάζουν με τη διόρθωση καναλιού απο το Ιστορικο
                // (βλ. SyncChannelChange), αλλιως το δευτερο ταμειο εδειχνε ακομα ΔΙΑΝΟΜΗ.
                existing.Type = incoming.Type;
                existing.Channel = incoming.Channel;
            }
        }

        PendingCount = Orders.Count(o => o.IsPending);
        Changed?.Invoke();
    }

    /// <summary>Νέο ποσό σε παραγγελία που είναι ΑΚΟΜΑ στον πίνακα — καλείται όταν ακυρωθεί γραμμή της
    /// (βλ. SalesStatsService.RemoveLine). Στο δεύτερο ταμείο δεν χρειάζεται τίποτα: η ακύρωση περνά
    /// ούτως ή άλλως από το κύριο, και ο πίνακας κατεβαίνει από εκεί.</summary>
    public void SetTotal(int orderNumber, decimal total)
    {
        var order = Orders.FirstOrDefault(o => o.OrderNumber == orderNumber);
        if (order is null || order.Total == total)
            return;
        order.UpdateTotal(total);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Δεύτερο ταμείο (client) — στέλνει τη μεταβολή στο host, μετά ξαναδιαβάζει την αληθινή κατάσταση.</summary>
    private async Task SyncThenRefreshAsync(string path, object body)
    {
        await RemoteSync.PostAsync(path, body);
        await RefreshFromHostAsync();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var orders = JsonSerializer.Deserialize<List<BoardOrder>>(File.ReadAllText(_path)) ?? [];
            foreach (var o in orders)
                Orders.Add(o);
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
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(Orders.ToList(), JsonOpts));
        }
        catch (Exception)
        {
            // Αποτυχία εγγραφής δεν πρέπει να μπλοκάρει το ταμείο
        }
    }

    public void Add(BoardOrder order)
    {
        if (RemoteSync.IsClient)
        {
            _ = SendOrQueueAsync(order);
            return;
        }
        // Φύλακας διπλοεγγραφής: η ίδια παραγγελία μπορεί να ξαναφτάσει εδώ από την ουρά αναμονής του
        // δεύτερου ταμείου (βλ. PendingSyncService) αν η πρώτη αποστολή είχε φτάσει αλλά χάθηκε η
        // απάντηση — αλλιώς θα εμφανιζόταν δύο φορές στον πίνακα ζωντανών παραγγελιών.
        if (Orders.Any(o => o.OrderNumber == order.OrderNumber))
            return;
        AutoDispatchPlatform(order);
        Orders.Add(order);
        Notify();
    }

    /// <summary>
    /// Wolt και e-food περνάνε ΜΟΝΕΣ τους στο κανάλι τους, χωρίς να σταθούν στην αναμονή: τις παραδίδει
    /// η ίδια η πλατφόρμα, οπότε το «ΠΕΡΑΣΕ ΤΗΝ ΣΕ» έχει ούτως ή άλλως ένα μόνο πιθανό κουμπί (βλ.
    /// LiveOrdersViewModel.AllowedChannels) — ήταν ένα πάτημα που δεν αποφάσιζε τίποτα και μόνο γέμιζε
    /// την αναμονή. Το BOX ΔΕΝ μπαίνει εδώ: το παραδίδει δικός μας διανομέας και ο ταμίας διαλέγει
    /// ακόμα μετρητά/κάρτα.
    /// </summary>
    private static void AutoDispatchPlatform(BoardOrder order)
    {
        if (order.Type != OrderType.Apps || !order.IsPending)
            return;
        if (order.Channel is not ("Wolt" or "e-food"))
            return;
        order.SentVia = order.Channel;
        order.SentAt = DateTime.Now;
    }

    /// <summary>Δεύτερο ταμείο — προσθήκη στον πίνακα· αν δεν φτάσει στο κύριο ταμείο μπαίνει σε ουρά
    /// (βλ. PendingSyncService) αντί να χαθεί: αλλιώς μια διανομή έμενε χωρίς καμία εγγραφή πουθενά,
    /// ενώ ο ταμίας είχε ήδη πάρει το χαρτί στα χέρια του.</summary>
    private async Task SendOrQueueAsync(BoardOrder order)
    {
        if (await RemoteSync.PostAsync("/api/sync/board", order))
            await RefreshFromHostAsync();
        else
            PendingSyncService.Instance.Enqueue("/api/sync/board", order, $"Διανομή #{order.OrderNumber}");
    }

    public void Dispatch(BoardOrder order, string channel)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/board/dispatch", new { order.OrderNumber, Channel = channel });
            return;
        }
        order.IsEveningShift = SettingsStore.Instance.Settings.IsEveningShift;
        order.SentVia = channel;
        order.SentAt = DateTime.Now;
        ApplyChannelPayment(order, channel);
        // ΤΩΡΑ μπαίνει στον τζίρο, στη βάρδια αυτής της στιγμής — όσο ήταν στην αναμονή δεν μετρούσε
        // πουθενά (βλ. SalesStatsService.CountedOrders για το γιατί).
        SalesStatsService.Instance.SetShift(order.OrderNumber, order.IsEveningShift);
        Notify();
    }

    public void Reassign(BoardOrder order, string channel)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/board/reassign", new { order.OrderNumber, Channel = channel });
            return;
        }
        order.IsEveningShift = SettingsStore.Instance.Settings.IsEveningShift;
        order.SentVia = channel;
        ApplyChannelPayment(order, channel);
        // Ίδιος κανόνας με το Dispatch: η βάρδια του τζίρου ακολουθεί τη βάρδια του καναλιού, ώστε
        // στατιστικά και ταμπελάκια καναλιών να μη λένε ποτέ διαφορετικά πράγματα.
        SalesStatsService.Instance.SetShift(order.OrderNumber, order.IsEveningShift);
        Notify();
    }

    /// <summary>
    /// Ποιον τρόπο πληρωμής σημαίνει ένα κανάλι. Τα τέσσερα κανάλια του διανομέα ΕΙΝΑΙ ο τρόπος
    /// πληρωμής — «BOX Κάρτα» δεν είναι ετικέτα, είναι «πληρώθηκε με κάρτα». null για e-food/Wolt, που
    /// πληρώνονται μέσα στην πλατφόρμα και δεν περνάνε από το ταμείο.
    /// </summary>
    public static PaymentMethod? PaymentOf(string? channel) => channel switch
    {
        "Διανομέας" or "BOX Μετρητά" => Core.Models.PaymentMethod.Cash,
        "Κάρτα Διανομέα" or "BOX Κάρτα" => Core.Models.PaymentMethod.Card,
        // Το «Wolt Drive» λέει ΠΟΙΟΣ παραδίδει, όχι πώς πληρώθηκε: ο τρόπος πληρωμής μένει αυτός που
        // διάλεξε ο ταμίας στην παραγγελία (μετρητά ή κάρτα) και δεν τον πειράζει το καναλι.
        _ => null,
    };

    /// <summary>
    /// Το κανάλι άλλαξε — άλλαξε μαζί και ο τρόπος πληρωμής, ΚΑΙ ΣΤΗΝ ΙΔΙΑ ΤΗΝ ΠΑΡΑΓΓΕΛΙΑ.
    ///
    /// <para>Χωρίς αυτό, αλλάζοντας «BOX Κάρτα» σε «BOX Μετρητά» άλλαζε μόνο σε ποιο κουτί κάθεται η
    /// παραγγελία στις Ζωντανές: η αναφορά ημέρας και το Ιστορικό συνέχιζαν να τη μετράνε ΚΑΡΤΑ, γιατί
    /// διαβάζουν το CompletedOrder.PaymentMethod. Ο ταμίας έβλεπε το ένα και η αναφορά έλεγε το άλλο.</para>
    /// </summary>
    private static void ApplyChannelPayment(BoardOrder order, string channel)
    {
        var method = PaymentOf(channel);
        if (method is null || order.PaymentMethod == method)
            return;
        order.UpdatePaymentMethod(method);
        SalesStatsService.Instance.UpdatePaymentMethod(order.OrderNumber, method);
    }

    /// <summary>
    /// Η ανάποδη κατεύθυνση: διορθώθηκε ο τρόπος πληρωμής από το Ιστορικό, οπότε η παραγγελία πρέπει να
    /// μετακομίσει και στο σωστό κουτί των Ζωντανών (αν είναι ακόμα εκεί και σε κανάλι διανομέα).
    /// ΔΕΝ ξανακαλεί τα στατιστικά — εκείνα μόλις ενημερώθηκαν, θα ήταν κύκλος.
    /// </summary>
    public void SyncChannelToPayment(int orderNumber, PaymentMethod? method)
    {
        if (method is null)
            return;
        var order = Orders.FirstOrDefault(o => o.OrderNumber == orderNumber);
        if (order is null)
            return;

        // Το εικονιδιο 💶/💳 του πινακα αλλαζει σε ΚΑΘΕ περιπτωση — ακομα κι αν η παραγγελια ειναι
        // ακομα σε αναμονη ή σε καναλι που δεν οριζει τροπο πληρωμης (Wolt Drive, e-food, Wolt).
        order.UpdatePaymentMethod(method);

        // Μετακομιζει σε αλλο κουτι ΜΟΝΟ οταν το ιδιο το καναλι ειναι ο τροπος πληρωμης. Το Wolt Drive
        // λεει ποιος παραδιδει, οχι πως πληρωθηκε: μενει εκει που ειναι.
        if (order.SentVia is null || PaymentOf(order.SentVia) is null)
        {
            Notify();
            return;
        }

        var wanted = order.SentVia switch
        {
            "Διανομέας" or "Κάρτα Διανομέα" => method == Core.Models.PaymentMethod.Card ? "Κάρτα Διανομέα" : "Διανομέας",
            _ => method == Core.Models.PaymentMethod.Card ? "BOX Κάρτα" : "BOX Μετρητά",
        };
        if (order.SentVia != wanted)
            order.SentVia = wanted;
        Notify();
    }

    /// <summary>
    /// Διορθώθηκε το κανάλι από το Ιστορικό — ακολουθεί και ο πίνακας των Ζωντανών.
    ///
    /// <para>Χωρίς αυτό, μια ΔΙΑΝΟΜΗ που διορθωνόταν σε e-food έμενε στο κουτί «Διανομέας»: μετρούσε
    /// στα μετρητά που περιμένει ο διανομέας, ενώ την είχε ήδη πληρώσει ο πελάτης στην πλατφόρμα.</para>
    ///
    /// <para>ΟΡΘΙΟΣ και ΤΡΑΠΕΖΙ δεν έχουν θέση στον πίνακα διανομής — εκεί η παραγγελία απλώς φεύγει
    /// από αυτόν (ο τζίρος και το Ιστορικό δεν πειράζονται καθόλου).</para>
    /// </summary>
    public void SyncChannelChange(int orderNumber, OrderType type, string? channel, PaymentMethod? method)
    {
        var order = Orders.FirstOrDefault(o => o.OrderNumber == orderNumber);
        if (order is null)
            return;

        if (type is OrderType.Pickup or OrderType.Table)
        {
            Orders.Remove(order);
            Notify();
            return;
        }

        order.Type = type;
        order.Channel = type == OrderType.Apps ? channel : null;
        order.UpdatePaymentMethod(method);

        // Όσο ήταν σε αναμονή, μένει σε αναμονή: ο ταμίας θα το περάσει μόνος του στο σωστό κανάλι.
        // Και αν την ειχε ηδη αναλαβει κουριερ της Wolt, εκει μενει: η διορθωση αφορα το ΤΙ ειναι η
        // παραγγελια, οχι ποιος την κραταει στο χερι — εκτος αν πηγε σε πλατφορμα, που την παραδιδει
        // πλεον η ιδια.
        if (order.SentVia is not null)
        {
            var isCard = method == Core.Models.PaymentMethod.Card;
            // Το Wolt Drive δινεται οπου παραδιδουμε εμεις — ΔΙΑΝΟΜΗ και BOX (βλ.
            // LiveOrdersViewModel.WoltDrive) — αρα εκει εχει νοημα να κρατηθει ο κουριερ οταν
            // διορθωθει το καναλι. Σε e-food/Wolt οχι: τις παραδιδει η πλατφορμα.
            var keepsCourier = order.SentVia == "Wolt Drive"
                && (type == OrderType.Delivery || (type == OrderType.Apps && channel == "BOX"));
            order.SentVia = (type, channel) switch
            {
                (OrderType.Apps, "e-food") => "e-food",
                (OrderType.Apps, "Wolt") => "Wolt",
                _ when keepsCourier => "Wolt Drive",
                (OrderType.Apps, _) => isCard ? "BOX Κάρτα" : "BOX Μετρητά",
                _ => isCard ? "Κάρτα Διανομέα" : "Διανομέας",
            };
        }
        Notify();
    }

    /// <summary>Πραγματική ακύρωση (αφαίρεση από τον πίνακα) — π.χ. ο πελάτης ακύρωσε. Η αφαίρεση από τον
    /// τζίρο (SalesStatsService) γίνεται ξεχωριστά από τον καλούντα (βλ. LiveOrdersViewModel), ίδια λογική
    /// με το CancelRound του τραπεζιού (δύο ξεχωριστά services, δύο ξεχωριστές κλήσεις sync).</summary>
    public void Cancel(BoardOrder order)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/board/cancel", new { order.OrderNumber });
            return;
        }
        Orders.Remove(order);
        Notify();
    }

    public void RevertToPending(BoardOrder order)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/board/revert", new { order.OrderNumber });
            return;
        }
        order.SentVia = null;
        order.SentAt = null;
        Notify();
        // Ξαναβγαίνει από τον τζίρο (η βάρδια της μένει όπως ήταν — θα ξαναγραφτεί μόλις περάσει πάλι
        // σε κανάλι). Τα στατιστικά διαβάζουν τον πίνακα αλλά ξαναχτίζονται μόνο στο δικό τους Changed,
        // γι' αυτό χρειάζεται ρητό σήμα — ΜΕΤΑ το Notify, ώστε να έχει ήδη σωθεί η νέα κατάσταση.
        SalesStatsService.Instance.RaiseChanged();
    }

    /// <summary>Κλείσιμο ημέρας — αδειάζει τον πίνακα, εκκρεμείς και ήδη περασμένες σε κανάλι, ώστε το
    /// επόμενο άνοιγμα να μη δείχνει ζωντανές παραγγελίες της προηγούμενης μέρας (βλ. DayReportService.CloseDay).</summary>
    public void Clear()
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/board/clear", new { });
            return;
        }
        Orders.Clear();
        Notify();
    }

    /// <summary>Αφαιρεί μόνο τις εγγραφές παλιότερης ημέρας-επιχείρησης, χωρίς να αδειάσει τον πίνακα
    /// της τρέχουσας βάρδιας — βλ. SalesStatsService.RemoveOtherBusinessDays για το γιατί.</summary>
    public void RemoveOtherBusinessDays(DateTime keep)
    {
        var stale = Orders.Where(o => SalesStatsService.BusinessDay(o.PlacedAt) != keep).ToList();
        if (stale.Count == 0)
            return;
        foreach (var o in stale)
            Orders.Remove(o);
        Notify();
    }

    /// <summary>Οι αριθμοί των παραγγελιών που περιμένουν ακόμα κανάλι. Αυτές ΔΕΝ μετράνε στον τζίρο —
    /// βλ. SalesStatsService.CountedOrders για ολόκληρο τον λόγο.</summary>
    public HashSet<int> AwaitingChannelNumbers => Orders.Where(o => o.IsPending).Select(o => o.OrderNumber).ToHashSet();

    private void Notify()
    {
        PendingCount = Orders.Count(o => o.IsPending);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Τα κανάλια που παραδίδει ο δικός μας διανομέας — βλ. ChannelInfo.All· e-food/Wolt δεν
    /// μπαίνουν εδώ, τα παραδίδει η ίδια η πλατφόρμα.</summary>
    private static readonly string[] DriverChannels = ["Διανομέας", "Κάρτα Διανομέα", "BOX Μετρητά", "BOX Κάρτα"];

    /// <summary>Πόσοι χαρακτήρες πλάτος γράφεται η αναφορά διανομέα.
    ///
    /// <para>ΑΥΤΟΣ ο αριθμός καθορίζει πόσο ΜΕΓΑΛΑ βγαίνουν τα γράμματα στο χαρτί: το DayReportWindow
    /// μικραίνει τη γραμματοσειρά μέχρι να χωρέσει η πιο μακριά γραμμή στο ρολό (βλ.
    /// DayReportWindow.FitTextToWidth), οπότε λιγότεροι χαρακτήρες = μεγαλύτερα γράμματα. Με τις
    /// παλιές γραμμές των ~43 χαρακτήρων («#01 · Κάρτα Διανομέα · Κωνσταντίνος   12,40») η αναφορά
    /// έβγαινε στο μισό μέγεθος και δεν διαβαζόταν.</para>
    ///
    /// <para><b>Το 24 ήταν υπερβολή</b> — στο μαγαζί βγήκαν τεράστια γράμματα. Στους 34 χαρακτήρες
    /// μένουν αισθητά μεγαλύτερα από την αναφορά ημέρας (48 χαρακτήρες) χωρίς να καταπίνουν το ρολό,
    /// και οι διευθύνσεις χωράνε συνήθως σε μία γραμμή. Αν προσθέσεις στοιχείο, σπάσ' το σε δεύτερη
    /// γραμμή — μη φαρδύνεις άλλο τη γραμμή.</para></summary>
    private const int DriverReportWidth = 34;

    /// <summary>Αναλυτική αναφορά διανομέα — όλες οι παραγγελίες που πέρασαν σήμερα σε κανάλι διανομέα
    /// (μετρητά/κάρτα, ΔΙΑΝΟΜΗ ή BOX), μία-μία με διεύθυνση, και σύνολο τζίρου στην κορυφή. Για εκτύπωση
    /// πριν βγει ο διανομέας — βλ. ReceiptPrinter.PrintDriverReport.</summary>
    public string BuildDriverReport()
    {
        var greek = CultureInfo.GetCultureInfo("el-GR");
        // ΜΟΝΟ η τρέχουσα βάρδια. Με αυτό το χαρτί ξεκαθαρίζονται λεφτά με τον διανομέα: χωρίς το
        // φίλτρο, ο βραδινός έπαιρνε μέσα και τις πρωινές παραδόσεις και του ζητούνταν μετρητά που δεν
        // είχε εισπράξει ποτέ. Ίδιος κανόνας με τα ταμπελάκια των καναλιών (βλ. LiveOrdersViewModel).
        var evening = SettingsStore.Instance.Settings.IsEveningShift;
        var orders = Orders
            .Where(o => o.SentVia is not null && DriverChannels.Contains(o.SentVia) && o.IsEveningShift == evening)
            .OrderBy(o => o.SentAt ?? o.PlacedAt)
            .ToList();

        // Μετρητά/κάρτα βγαίνουν κατευθείαν από το SentVia (το ίδιο το κανάλι λέει ήδη ποιο είναι) —
        // δεν χρειάζεται να ξαναφιλτράρουμε με βάση PaymentMethod, DriverChannels ήδη τα καλύπτει όλα.
        var cash = orders.Where(o => IsCashChannel(o.SentVia)).ToList();
        var card = orders.Where(o => !IsCashChannel(o.SentVia)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("ΠΙΤΤΑ ΤΟΥ ΠΑΠΠΟΥ");
        sb.AppendLine("ΔΙΑΝΟΜΕΑΣ");
        // Η βάρδια γράφεται στο χαρτί: δύο αναφορές της ίδιας μέρας είναι αλλιώς αξεχώριστες.
        sb.AppendLine(evening ? "ΒΡΑΔΙΝΗ ΒΑΡΔΙΑ" : "ΠΡΩΙΝΗ ΒΑΡΔΙΑ");
        sb.AppendLine(DateTime.Now.ToString("dd/MM/yyyy · HH:mm", greek));
        sb.AppendLine(new string('=', DriverReportWidth));
        sb.AppendLine(DriverRow($"ΣΥΝΟΛΟ ({orders.Count})", Order.FormatPrice(orders.Sum(o => o.Total))));
        sb.AppendLine(DriverRow($"ΜΕΤΡΗΤΑ ({cash.Count})", Order.FormatPrice(cash.Sum(o => o.Total))));
        sb.AppendLine(DriverRow($"ΚΑΡΤΑ ({card.Count})", Order.FormatPrice(card.Sum(o => o.Total))));
        sb.AppendLine(new string('=', DriverReportWidth));

        foreach (var o in orders)
        {
            // ΜΙΑ κενή γραμμή ΠΡΙΝ από κάθε παραγγελία: αρκεί για να ξεχωρίζει πού τελειώνει η μία
            // διεύθυνση. Με δύο, το χαρτί έβγαινε μισό κενό και η λίστα δεν χωρούσε σε μια ματιά.
            sb.AppendLine();
            // ΜΕΤΡΗΤΑ ή ΚΑΡΤΑ με το όνομά του, όχι το κανάλι: το «Διανομέας» και το «Κάρτα Διανομέα»
            // είναι εσωτερικά ονόματα καναλιών — αυτό που θέλει να ξέρει είναι αν θα εισπράξει.
            sb.AppendLine(DriverRow($"#{o.DisplayNumber} {(IsCashChannel(o.SentVia) ? "ΜΕΤΡΗΤΑ" : "ΚΑΡΤΑ")}", o.TotalLabel));
            foreach (var line in WrapPlain(o.AddressOrName))
                sb.AppendLine(line);
            foreach (var line in WrapPlain(o.NameUnderAddress))
                sb.AppendLine(line);
        }
        return sb.ToString();
    }

    /// <summary>Μετρητά ή κάρτα, από το ίδιο το κανάλι — τα δύο κανάλια «Διανομέας»/«BOX Μετρητά» ΕΙΝΑΙ
    /// τα μετρητά (βλ. ChannelInfo.All).</summary>
    private static bool IsCashChannel(string? sentVia) => sentVia is "Διανομέας" or "BOX Μετρητά";

    /// <summary>Ετικέτα αριστερά, ποσό δεξιά στην άκρη του χαρτιού. Αν δεν χωρέσουν μαζί, μένει ένα
    /// κενό ανάμεσά τους — καλύτερα στριμωγμένο παρά να αναδιπλωθεί και να μικρύνει όλη η αναφορά.</summary>
    private static string DriverRow(string left, string right)
    {
        var gap = DriverReportWidth - left.Length - right.Length;
        return left + new string(' ', Math.Max(1, gap)) + right;
    }

    /// <summary>Σπάει ελεύθερο κείμενο (διεύθυνση, όνομα) σε γραμμές του πλάτους της αναφοράς, στα κενά.
    /// Το σπάσιμο γίνεται ΕΔΩ και όχι από το WPF επίτηδες: μια γραμμή πιο φαρδιά από το χαρτί θα
    /// μίκραινε τη γραμματοσειρά ΟΛΗΣ της αναφοράς (βλ. DriverReportWidth).</summary>
    private static IEnumerable<string> WrapPlain(string text)
    {
        if (text.Length == 0)
            yield break;

        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = word;
            // Λέξη μεγαλύτερη από το χαρτί (σπάνιο: κολλημένη διεύθυνση) — κόβεται στα ίσια.
            while (w.Length > DriverReportWidth)
            {
                if (line.Length > 0) { yield return line.ToString(); line.Clear(); }
                yield return w[..DriverReportWidth];
                w = w[DriverReportWidth..];
            }
            if (line.Length > 0 && line.Length + 1 + w.Length > DriverReportWidth)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0)
                line.Append(' ');
            line.Append(w);
        }
        if (line.Length > 0)
            yield return line.ToString();
    }
}
