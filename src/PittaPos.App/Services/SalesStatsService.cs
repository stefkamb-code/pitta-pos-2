using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>Πωληθέν είδος σε ολοκληρωμένη παραγγελία. Details = ψωμί/έξτρα/χωρίς/σημείωση.</summary>
/// <param name="ProductId">Ποιο προϊόν του καταλόγου είναι — κενό για παλιές παραγγελίες, πριν
/// προστεθεί το πεδίο. Χρειάζεται γιατί το <paramref name="Name"/> ΔΕΝ είναι το όνομα του προϊόντος:
/// είναι το σύνθετο όνομα της γραμμής, με το ψωμί μπροστά και τη λέξη «Πίττα» αφαιρεμένη (π.χ. το
/// «Πίττα κοτόπουλο» γίνεται «ΕΛ. κοτόπουλο»). Ψάχνοντας με το όνομα, το «ΜΙΑ ΑΠΟ ΤΑ ΙΔΙΑ» δεν
/// έβρισκε ΠΟΤΕ τις πίττες και τις παρέλειπε σιωπηλά — έμοιαζε σαν να «έβγαζε τα μισά προϊόντα».</param>
/// <param name="Customization">Οι ιδιαιτερότητες όπως τις διάλεξε ο ταμίας (ψωμί, χωρίς, έξτρα,
/// σημείωση, διπλή πίτα) — null για παλιές παραγγελίες ή για μη προσαρμόσιμα προϊόντα. Το
/// <paramref name="Details"/> είναι μόνο το ΚΕΙΜΕΝΟ τους για εμφάνιση/εκτύπωση· χωρίς τα δομημένα
/// δεδομένα εδώ, το «ΜΙΑ ΑΠΟ ΤΑ ΙΔΙΑ» δεν μπορούσε να τις επαναφέρει και ξανάβαζε το προϊόν σκέτο.</param>
public sealed record SoldLine(string Name, int Quantity, decimal Revenue, string Details = "",
    int DiscountPct = 0, string ProductId = "", LineCustomization? Customization = null,
    string PrintName = "")
{
    /// <summary>Το όνομα για το χαρτί: το ειδικό αν το προϊόν έχει ορίσει ένα, αλλιώς της οθόνης.
    /// Κενό και σε όλες τις παλιές παραγγελίες, που τυπώνονται όπως πάντα.</summary>
    public string NameForPrint => PrintName.Length > 0 ? PrintName : Name;

    public string QtyNameLabel => Quantity + " × " + Name;
    public string RevenueLabel => Order.FormatPrice(Revenue);
    public bool HasDetails => Details.Length > 0;
    public bool HasDiscount => DiscountPct > 0;
}

/// <summary>Ολοκληρωμένη παραγγελία όπως μετράει στα στατιστικά και στο ιστορικό.</summary>
public sealed class CompletedOrder
{
    public required int OrderNumber { get; init; }
    public required OrderType Type { get; init; }
    /// <summary>Πλατφόρμα για παραγγελίες ΕΦΑΡΜΟΓΩΝ (e-food/Wolt/BOX).</summary>
    public string? Channel { get; init; }
    /// <summary>Ο αριθμός παραγγελίας που δίνει η ίδια η πλατφόρμα (Wolt/e-food/BOX) — μόνο για ΕΦΑΡΜΟΓΕΣ,
    /// ίδιο πεδίο/λογική με BoardOrder.AppOrderRef/DisplayNumber. Στην απόδειξη (βλ. ReceiptWindow) γίνεται
    /// ο κύριος αριθμός #, πιο χρήσιμος για αντιστοίχιση με την πλατφόρμα παρά ο εσωτερικός μας μετρητής.</summary>
    public string? AppOrderRef { get; init; }
    /// <summary>Μετρητά ή κάρτα — μόνο για ΔΙΑΝΟΜΗ/BOX· null για τα υπόλοιπα. Διορθώσιμο εκ των υστέρων
    /// από το Ιστορικό (βλ. SalesStatsService.UpdatePaymentMethod) αν ο ταμίας πάτησε λάθος κουμπί.</summary>
    public PaymentMethod? PaymentMethod { get; init; }
    /// <summary>Όνομα πελάτη ή «Τραπέζι Ν» — κενό αν δεν δόθηκε.</summary>
    public string Who { get; init; } = "";
    /// <summary>Τηλέφωνο πελάτη — μόνο ΔΙΑΝΟΜΗ/BOX (εκεί συλλέγεται στο Βήμα 2), κενό για τα υπόλοιπα.</summary>
    public string Phone { get; init; } = "";
    /// <summary>Οδός/αριθμός/περιοχή/Τ.Κ. σε μία γραμμή — μόνο για ΔΙΑΝΟΜΗ/BOX, κενό για τα υπόλοιπα
    /// κανάλια. Ξεχωριστό πεδίο από όροφο/σχόλια (βλ. DeliveryFloor/DeliveryNotes παρακάτω) — στην
    /// απόδειξη (βλ. ReceiptWindow) τυπώνονται σε ξεχωριστές, ετικετοποιημένες γραμμές αντί για μία
    /// ενιαία πρόταση όλα μαζί, που ήταν δύσκολο να διαβαστεί γρήγορα από τον διανομέα.</summary>
    public string DeliveryAddress { get; init; } = "";
    /// <summary>Όροφος/κουδούνι, ελεύθερο κείμενο — μόνο ΔΙΑΝΟΜΗ/BOX.</summary>
    public string DeliveryFloor { get; init; } = "";
    /// <summary>Σχόλια παράδοσης (π.χ. «χωρίς κρεμμύδι», «χτύπα το κουδούνι δύο φορές») — μόνο ΔΙΑΝΟΜΗ/BOX.
    /// Διαφορετικό πεδίο από το γενικό Note παρακάτω (αυτό είναι π.χ. σημείωση σερβιτόρου σε τραπέζι).</summary>
    public string DeliveryNotes { get; init; } = "";
    public required decimal Total { get; init; }
    public required IReadOnlyList<SoldLine> Lines { get; init; }
    /// <summary>Γενική σημείωση παραγγελίας (όχι ανά προϊόν) — π.χ. από το κινητό του σερβιτόρου.</summary>
    public string Note { get; init; } = "";
    /// <summary>Έκπτωση σε όλη την παραγγελία (βήμα 5) — ξεχωριστή από τυχόν έκπτωση ανά προϊόν.</summary>
    public int OrderDiscountPct { get; init; }
    public DateTime PlacedAt { get; init; } = DateTime.Now;
    /// <summary>Ποια βάρδια ήταν ενεργή (χειροκίνητος διακόπτης) τη στιγμή της παραγγελίας — όχι με βάση την ώρα.</summary>
    public bool IsEveningShift { get; init; }

    public int ItemCount => Lines.Sum(l => l.Quantity);
    public string TimeLabel => PlacedAt.ToString("HH:mm");
    public string DateTimeLabel => PlacedAt.ToString("dd/MM/yyyy · HH:mm");
    public string TotalLabel => Order.FormatPrice(Total);
    public string WhoLabel => Who.Length > 0 ? Who : "—";
    public bool HasDeliveryInfo => DeliveryAddress.Length > 0 || DeliveryFloor.Length > 0 || DeliveryNotes.Length > 0;
    /// <summary>Αριθμός # στην απόδειξη — προτιμά τον αριθμό της πλατφόρμας (Wolt/e-food/BOX) όταν υπάρχει.</summary>
    public string DisplayNumber => Type == OrderType.Apps && !string.IsNullOrWhiteSpace(AppOrderRef)
        ? AppOrderRef!
        : OrderNumber.ToString();

    public string? PaymentIcon => PaymentMethod switch
    {
        Core.Models.PaymentMethod.Cash => "💶",
        Core.Models.PaymentMethod.Card => "💳",
        _ => null,
    };
    public bool HasPaymentMethod => PaymentMethod is not null;

    public string TypeLabel => Channel ?? Type switch
    {
        OrderType.Delivery => "ΔΙΑΝΟΜΗ",
        OrderType.Apps => "ΕΦΑΡΜΟΓΕΣ",
        OrderType.Pickup => "ΟΡΘΙΟΣ",
        OrderType.Table => "ΤΡΑΠΕΖΙ",
        _ => "",
    };

    public bool HasAnyDiscount => OrderDiscountPct > 0 || Lines.Any(l => l.HasDiscount);
}

/// <summary>
/// Στατιστικά πωλήσεων ημέρας. Η καταχώρηση γίνεται με κλειδί τον αριθμό παραγγελίας,
/// ώστε πίσω στα προϊόντα και ξανά ΣΥΝΕΧΕΙΑ να ενημερώνει την ίδια παραγγελία αντί να διπλομετρά.
/// JSON στο %AppData%\PittaPos — ώστε ένα ξαφνικό κλείσιμο/restart του υπολογιστή να μη χάνει τη μέρα.
/// </summary>
public class SalesStatsService
{
    public static SalesStatsService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Dictionary<int, CompletedOrder> _orders = [];

    /// <summary>Σηκώνεται σε κάθε καταχώρηση/ενημέρωση παραγγελίας.</summary>
    public event Action? Changed;

    public IReadOnlyCollection<CompletedOrder> Orders => _orders.Values;

    /// <summary>Ώρα-όριο ημέρας: πριν από αυτήν η νύχτα μετράει ακόμα στην προηγούμενη μέρα.</summary>
    private const int DayCutoffHour = 5;

    /// <summary>Ίδιος κανόνας ημέρας-επιχείρησης, διαθέσιμος και σε άλλα services (π.χ. αρχειοθέτηση ιστορικού).</summary>
    public static DateTime BusinessDay(DateTime t) => t.Hour < DayCutoffHour ? t.Date.AddDays(-1) : t.Date;

    /// <summary>Επόμενος αριθμός παραγγελίας — μοναδική πηγή αλήθειας, καλείται ΣΥΓΧΡΟΝΑ ακριβώς πριν
    /// χρησιμοποιηθεί (όχι λεπτά νωρίτερα σαν προεπισκόπηση) από OrderWizardViewModel.ContinueStep3 ΚΑΙ
    /// WaiterApiService.SubmitOrder. Πριν, ο ταμίας κρατούσε δεσμευμένο τον αριθμό από τη στιγμή που
    /// άνοιγε η φόρμα (λεπτά ολόκληρα όσο περιηγούνταν τα προϊόντα) ενώ το κινητό του σερβιτόρου υπολόγιζε
    /// δικό του «επόμενο» ανεξάρτητα τη στιγμή της υποβολής — αν έπεφταν στον ίδιο αριθμό, η παραγγελία
    /// που καταχωρούνταν ΔΕΥΤΕΡΗ αντικαθιστούσε σιωπηλά την πρώτη στο Dictionary (ίδιο κλειδί), η πρώτη
    /// «χανόταν» εντελώς — ούτε στο Ιστορικό. Καλώντας αυτό ΤΗΝ ΤΕΛΕΥΤΑΙΑ ΣΤΙΓΜΗ (όχι νωρίτερα), χωρίς
    /// await ανάμεσα σε υπολογισμό και καταχώρηση, το παράθυρο σύγκρουσης κλείνει.</summary>
    public int NextOrderNumber() => 1 + Math.Max(
        1043,
        Math.Max(
            Orders.Select(o => o.OrderNumber).DefaultIfEmpty(0).Max(),
            OrderBoardService.Instance.Orders.Select(o => o.OrderNumber).DefaultIfEmpty(0).Max()));

    private SalesStatsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "day-orders.json");

        if (RemoteSync.IsClient)
        {
            // Το κύριο ταμείο (host) είναι υπεύθυνο για το αυτόματο κλείσιμο ώρας-ορίου — αν το έκανε
            // και το δεύτερο ταμείο θα μπορούσε να «τρέξουν» δύο κλεισίματα μαζί (διπλό email/αρχείο).
            RemoteSync.StartPolling(TimeSpan.FromSeconds(3), RefreshFromHostAsync);
            return;
        }

        Load();

        // Ελέγχει κάθε λεπτό αν πέρασε η ώρα-όριο (π.χ. ανοιχτό το βράδυ μέχρι τις 5) και κλείνει αυτόματα.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += (_, _) => CheckAutoClose();
        timer.Start();
        // Αναβάλλεται μετά την κατασκευή, ώστε το Instance να έχει ήδη οριστεί (αποφυγή reentrancy).
        Application.Current?.Dispatcher.BeginInvoke(CheckAutoClose);
    }

    /// <summary>Δεύτερο ταμείο (client) — αντικαθιστά την τοπική εικόνα με τις παραγγελίες του host.</summary>
    private async Task RefreshFromHostAsync()
    {
        var orders = await RemoteSync.GetAsync<List<CompletedOrder>>("/api/sync/orders");
        if (orders is null)
            return;
        _orders.Clear();
        foreach (var o in orders)
            _orders[o.OrderNumber] = o;
        Changed?.Invoke();
    }

    /// <summary>Δεύτερο ταμείο (client) — στέλνει τη μεταβολή στο host, μετά ξαναδιαβάζει την αληθινή κατάσταση.</summary>
    private async Task SyncThenRefreshAsync(string path, object body)
    {
        await RemoteSync.PostAsync(path, body);
        await RefreshFromHostAsync();
    }

    /// <summary>Αν υπάρχουν παραγγελίες (ολοκληρωμένες ή ζωντανές σε αναμονή/κανάλι) από προηγούμενη
    /// «επιχειρηματική μέρα», κλείσε αυτόματα (χωρίς ερώτηση) — ελέγχει και τον πίνακα ζωντανών παραγγελιών,
    /// όχι μόνο τα ολοκληρωμένα στατιστικά, ώστε παραγγελίες που έμειναν σε αναμονή χωρίς να ολοκληρωθούν
    /// ποτέ (π.χ. ξεχάστηκαν) να μη μείνουν κολλημένες για πάντα σαν «ζωντανές» της προηγούμενης μέρας.</summary>
    private void CheckAutoClose()
    {
        var today = BusinessDay(DateTime.Now);
        var stale = _orders.Values.Any(o => BusinessDay(o.PlacedAt) != today)
            || OrderBoardService.Instance.Orders.Any(o => BusinessDay(o.PlacedAt) != today);
        if (stale)
            DayReportService.CloseDay();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var orders = JsonSerializer.Deserialize<List<CompletedOrder>>(File.ReadAllText(_path)) ?? [];
            foreach (var o in orders)
                _orders[o.OrderNumber] = o;
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
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_orders.Values.ToList(), JsonOpts));
        }
        catch (Exception)
        {
            // Αποτυχία εγγραφής δεν πρέπει να μπλοκάρει το ταμείο
        }
    }

    /// <summary>Καταχωρεί μια ολοκληρωμένη παραγγελία και επιστρέφει τον αριθμό με τον οποίο αποθηκεύτηκε
    /// τελικά — συνήθως ίδιος με order.OrderNumber, ΕΚΤΟΣ αν φτάνει εδώ (μέσω δικτύου, βλ.
    /// SyncThenRefreshAsync/RecordAndConfirmNumberAsync) με αριθμό που κάποιος άλλος πρόλαβε στο
    /// ενδιάμεσο. Το δεύτερο ταμείο υπολογίζει τον αριθμό του από τοπικό (ελαφρώς καθυστερημένο)
    /// αντίγραφο πριν στείλει εδώ την καταχώρηση — η καθυστέρηση δικτύου ξανανοίγει το ίδιο παράθυρο
    /// σύγκρουσης που έκλεινε το NextOrderNumber μόνο για σύγχρονες, τοπικές καταχωρήσεις στο host.
    /// Αντί να αντικατασταθεί σιωπηλά η προηγούμενη παραγγελία στο Dictionary (ίδιο κλειδί, ό,τι
    /// συνέβαινε πριν — μια ολόκληρη παραγγελία «χανόταν» χωρίς ίχνος, ούτε στο Ιστορικό), εδώ
    /// ανιχνεύεται η σύγκρουση και ανατίθεται καινούριος αριθμός.</summary>
    public int Record(CompletedOrder order)
    {
        if (RemoteSync.IsClient)
        {
            _ = SendOrQueueAsync(order);
            return order.OrderNumber;
        }
        // Πάντα στο UI thread: οι παραγγελίες από το κινητό του σερβιτόρου φτάνουν σε thread του Kestrel,
        // του ταμείου στο UI thread — το Dictionary ΔΕΝ είναι thread-safe, οπότε ταυτόχρονη εγγραφή από
        // τα δύο μπορεί να «φάει» καταχώρηση (ή να χαλάσει εσωτερικά τη δομή) χωρίς κανένα σφάλμα. Εδώ
        // σειριοποιούνται όλες οι εγγραφές, ό,τι thread κι αν καλεί.
        return OnUi(() =>
        {
            var number = order.OrderNumber;
            if (_orders.ContainsKey(number))
            {
                number = NextOrderNumber();
                // Καταγράφεται ώστε να φαίνεται ότι ΣΥΝΕΒΗ σύγκρουση (και πόσο συχνά) — παλιότερα η
                // παραγγελία απλώς εξαφανιζόταν χωρίς κανένα ίχνος πουθενά.
                AppLog.Write("orders", $"Σύγκρουση αριθμού #{order.OrderNumber} — δόθηκε #{number} " +
                    $"({order.TypeLabel}, {order.WhoLabel}, {order.TotalLabel})");
            }
            _orders[number] = number == order.OrderNumber ? order : WithOrderNumber(order, number);
            Save();
            Changed?.Invoke();
            return number;
        });
    }

    /// <summary>Τρέχει τη δουλειά στο UI thread (ή αμέσως, αν είμαστε ήδη εκεί).</summary>
    private static T OnUi<T>(Func<T> func)
    {
        var dispatcher = Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess() ? func() : dispatcher.Invoke(func);
    }

    /// <summary>Ίδιο με Record, αλλά (μόνο στο δεύτερο ταμείο) ΠΕΡΙΜΕΝΕΙ την απάντηση του host με τον
    /// τελικό αριθμό αντί να είναι fire-and-forget — για όπου ο καλών πρέπει να τυπώσει/δείξει αμέσως
    /// τον σωστό αριθμό (π.χ. απόδειξη παραγγελίας από το κινητό του σερβιτόρου). Στο κύριο ταμείο
    /// (host) συμπεριφέρεται ακριβώς σαν το συγχρονισμένο Record.</summary>
    public async Task<int> RecordAndConfirmNumberAsync(CompletedOrder order)
    {
        if (!RemoteSync.IsClient)
            return Record(order);

        var (ok, confirmed) = await RemoteSync.PostForResultAsync<int?>("/api/sync/orders", order);
        if (!ok)
        {
            // Δεν έφτασε στο κύριο ταμείο — μπαίνει σε ουρά αντί να χαθεί. Ο αριθμός μένει ο τοπικός
            // (τυπώνεται έτσι στο χαρτί)· αν στο μεταξύ τον πιάσει άλλος, το κύριο ταμείο θα δώσει
            // καινούριο όταν φτάσει η καθυστερημένη αποστολή — δεν σβήνει ποτέ την άλλη παραγγελία.
            PendingSyncService.Instance.Enqueue("/api/sync/orders", order, $"Παραγγελία #{order.OrderNumber}");
            return order.OrderNumber;
        }
        await RefreshFromHostAsync();
        return confirmed ?? order.OrderNumber;
    }

    /// <summary>Δεύτερο ταμείο — στέλνει την παραγγελία· αν δεν φτάσει, μπαίνει σε ουρά για επανάληψη
    /// (βλ. PendingSyncService) αντί να χαθεί σιωπηλά όπως πριν.</summary>
    private async Task SendOrQueueAsync(CompletedOrder order)
    {
        if (await RemoteSync.PostAsync("/api/sync/orders", order))
            await RefreshFromHostAsync();
        else
            PendingSyncService.Instance.Enqueue("/api/sync/orders", order, $"Παραγγελία #{order.OrderNumber}");
    }

    /// <summary>Αντίγραφο μιας παραγγελίας με διαφορετικό μόνο τον αριθμό (βλ. Record) — ίδια λίστα
    /// πεδίων με τις άλλες ανακατασκευές παρακάτω (RemoveLine/UpdatePaymentMethod/UpdateChannel).</summary>
    public static CompletedOrder WithOrderNumber(CompletedOrder order, int number) => new()
    {
        OrderNumber = number,
        Type = order.Type,
        Channel = order.Channel,
        AppOrderRef = order.AppOrderRef,
        PaymentMethod = order.PaymentMethod,
        Who = order.Who,
        Phone = order.Phone,
        DeliveryAddress = order.DeliveryAddress,
        DeliveryFloor = order.DeliveryFloor,
        DeliveryNotes = order.DeliveryNotes,
        Total = order.Total,
        Lines = order.Lines,
        Note = order.Note,
        OrderDiscountPct = order.OrderDiscountPct,
        PlacedAt = order.PlacedAt,
        IsEveningShift = order.IsEveningShift,
    };

    /// <summary>
    /// Αφαιρεί μόνο τις παραγγελίες που ΔΕΝ ανήκουν στη δοσμένη ημέρα-επιχείρησης, και τις επιστρέφει.
    /// Χρησιμοποιείται όταν φτάσει καθυστερημένα παραγγελία παλιάς μέρας ενώ τρέχει κανονικά η σημερινή
    /// βάρδια (π.χ. από την ουρά αναμονής του δεύτερου ταμείου): τότε πρέπει να αρχειοθετηθεί και να
    /// φύγει ΜΟΝΟ εκείνη, χωρίς να μηδενιστεί η μέρα που είναι σε εξέλιξη (βλ. DayReportService.CloseDay).
    /// </summary>
    public List<CompletedOrder> RemoveOtherBusinessDays(DateTime keep)
    {
        var stale = _orders.Values.Where(o => BusinessDay(o.PlacedAt) != keep).ToList();
        if (stale.Count == 0)
            return stale;
        foreach (var o in stale)
            _orders.Remove(o.OrderNumber);
        Save();
        Changed?.Invoke();
        return stale;
    }

    /// <summary>Καθαρίζει όλα τα στοιχεία ημέρας (κλείσιμο μέρας).</summary>
    public void Clear()
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/orders/clear", new { });
            return;
        }
        _orders.Clear();
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Πραγματική διαγραφή ενός προϊόντος από ήδη καταχωρημένη παραγγελία — αφαιρείται και από τον τζίρο.
    /// Καταγράφεται στο CancellationLogService πριν φύγει, για το ιστορικό ακυρωμένων.
    /// </summary>
    public void RemoveLine(int orderNumber, int lineIndex, string cancelledBy = "", DateTime? cancelledAt = null)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/orders/remove-line", new { OrderNumber = orderNumber, LineIndex = lineIndex });
            return;
        }
        if (!_orders.TryGetValue(orderNumber, out var order))
            return;
        var lines = order.Lines.ToList();
        if (lineIndex < 0 || lineIndex >= lines.Count)
            return;

        var removed = lines[lineIndex];
        CancellationLogService.Instance.Log(new CancelledLine(
            order.OrderNumber, order.WhoLabel, removed.Name, removed.Quantity, removed.Revenue, cancelledAt ?? DateTime.Now, cancelledBy));

        lines.RemoveAt(lineIndex);
        if (lines.Count == 0)
        {
            _orders.Remove(orderNumber);
        }
        else
        {
            _orders[orderNumber] = new CompletedOrder
            {
                OrderNumber = order.OrderNumber,
                Type = order.Type,
                Channel = order.Channel,
                AppOrderRef = order.AppOrderRef,
                PaymentMethod = order.PaymentMethod,
                Who = order.Who,
                Phone = order.Phone,
                DeliveryAddress = order.DeliveryAddress,
                DeliveryFloor = order.DeliveryFloor,
                DeliveryNotes = order.DeliveryNotes,
                Total = lines.Sum(l => l.Revenue) * (1 - order.OrderDiscountPct / 100m),
                Lines = lines,
                Note = order.Note,
                OrderDiscountPct = order.OrderDiscountPct,
                PlacedAt = order.PlacedAt,
                IsEveningShift = order.IsEveningShift,
            };
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>Διορθώνει τον τρόπο πληρωμής μιας ήδη ολοκληρωμένης παραγγελίας — π.χ. ο ταμίας πάτησε
    /// κατά λάθος Μετρητά αντί για Κάρτα (βλ. Ιστορικό). Μόνο για σημερινές παραγγελίες (ακόμα στη μνήμη·
    /// ίδιος περιορισμός με RemoveLine/RemoveOrder — αρχειοθετημένες προηγούμενες μέρες δεν αγγίζονται).</summary>
    public void UpdatePaymentMethod(int orderNumber, PaymentMethod? method)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/orders/payment-method", new { OrderNumber = orderNumber, PaymentMethod = method });
            return;
        }
        if (!_orders.TryGetValue(orderNumber, out var order))
            return;

        _orders[orderNumber] = new CompletedOrder
        {
            OrderNumber = order.OrderNumber,
            Type = order.Type,
            Channel = order.Channel,
            AppOrderRef = order.AppOrderRef,
            PaymentMethod = method,
            Who = order.Who,
            Phone = order.Phone,
            DeliveryAddress = order.DeliveryAddress,
            DeliveryFloor = order.DeliveryFloor,
            DeliveryNotes = order.DeliveryNotes,
            Total = order.Total,
            Lines = order.Lines,
            Note = order.Note,
            OrderDiscountPct = order.OrderDiscountPct,
            PlacedAt = order.PlacedAt,
            IsEveningShift = order.IsEveningShift,
        };
        Save();
        Changed?.Invoke();
    }

    /// <summary>Διορθώνει το κανάλι/τύπο μιας ήδη ολοκληρωμένης παραγγελίας — π.χ. πέρασε κατά λάθος ως
    /// e-food ενώ ήταν κάτι άλλο (βλ. Ιστορικό). Μόνο για σημερινές παραγγελίες (ακόμα στη μνήμη), ίδιος
    /// περιορισμός με UpdatePaymentMethod/RemoveLine/RemoveOrder — αρχειοθετημένες μέρες δεν αγγίζονται.</summary>
    public void UpdateChannel(int orderNumber, OrderType type, string? channel)
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/orders/channel", new { OrderNumber = orderNumber, Type = type, Channel = channel });
            return;
        }
        if (!_orders.TryGetValue(orderNumber, out var order))
            return;

        // Τρόπος πληρωμής έχει νόημα μόνο για ΔΙΑΝΟΜΗ/BOX (βλ. OrderWizardViewModel.ShowCustomerForm) —
        // αν το διορθωμένο κανάλι δεν είναι ένα απ' τα δύο, καθαρίζεται· αλλιώς θα έμενε "κολλημένο" από
        // το προηγούμενο (λάθος) κανάλι, π.χ. Πληρωμή: Μετρητά σε μια e-food παραγγελία.
        var keepsPayment = type == OrderType.Delivery || (type == OrderType.Apps && channel == "BOX");

        _orders[orderNumber] = new CompletedOrder
        {
            OrderNumber = order.OrderNumber,
            Type = type,
            Channel = channel,
            AppOrderRef = order.AppOrderRef,
            PaymentMethod = keepsPayment ? order.PaymentMethod : null,
            Who = order.Who,
            Phone = order.Phone,
            DeliveryAddress = order.DeliveryAddress,
            DeliveryFloor = order.DeliveryFloor,
            DeliveryNotes = order.DeliveryNotes,
            Total = order.Total,
            Lines = order.Lines,
            Note = order.Note,
            OrderDiscountPct = order.OrderDiscountPct,
            PlacedAt = order.PlacedAt,
            IsEveningShift = order.IsEveningShift,
        };
        Save();
        Changed?.Invoke();
    }

    /// <summary>Πραγματική διαγραφή ολόκληρου γύρου (π.χ. λάθος παραγγελία) — αφαιρείται όλο από τον τζίρο.</summary>
    public void RemoveOrder(int orderNumber, string cancelledBy = "")
    {
        if (RemoteSync.IsClient)
        {
            _ = SyncThenRefreshAsync("/api/sync/orders/remove", new { OrderNumber = orderNumber, CancelledBy = cancelledBy });
            return;
        }
        if (!_orders.TryGetValue(orderNumber, out var order))
            return;

        // Ίδιο instant για όλες τις γραμμές — έτσι το Ιστορικό ξέρει ότι ακυρώθηκαν μαζί, σαν μία παραγγελία.
        var cancelledAt = DateTime.Now;
        foreach (var l in order.Lines)
            CancellationLogService.Instance.Log(new CancelledLine(
                order.OrderNumber, order.WhoLabel, l.Name, l.Quantity, l.Revenue, cancelledAt, cancelledBy));

        _orders.Remove(orderNumber);
        Save();
        Changed?.Invoke();
    }
}
