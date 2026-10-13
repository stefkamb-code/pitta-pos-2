using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using PittaPos.Core.Fiscal;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>
/// Η πόρτα του ταμείου προς τα παραστατικά ΑΑΔΕ (βλ. <see cref="FiscalJournal"/>, ίδιο για κάθε πάροχο).
///
/// <para><b>Ανενεργό σε κάθε μαγαζί που δεν έχει διαλέξει πάροχο</b> στις Ρυθμίσεις του site
/// (<see cref="FiscalJournal.IsActive"/>): καμία κλήση από εδώ δεν κάνει τίποτα, δεν γράφεται αρχείο, δεν βγαίνει
/// χαρτί — το ταμείο δουλεύει ακριβώς όπως πριν. Ο πάροχος είναι έτοιμος· μπαίνει όταν το θελήσει το μαγαζί.</para>
///
/// <para>Ό,τι κι αν πάει στραβά εδώ μένει εδώ: γράφεται στο log και ξαναδοκιμάζεται — δεν φτάνει ποτέ στο «απρόσμενο
/// σφάλμα» του ταμία, και δεν σταματάει ποτέ μια παραγγελία ή μια πληρωμή.</para>
///
/// <para>ΜΟΝΟ στο κύριο ταμείο: εκεί καταλήγουν όλες οι παραγγελίες/πληρωμές (και του δεύτερου ταμείου και του κινητού).</para>
/// </summary>
public sealed class FiscalService
{
    public static FiscalService Instance { get; } = new();

    /// <summary>Πόσο μένει ανοιχτή μια πληρωμή τραπεζιού για τα υπόλοιπα τεμάχια του ίδιου πατήματος — το δεύτερο
    /// ταμείο και το κινητό τα στέλνουν ένα-ένα.</summary>
    private static readonly TimeSpan Gather = TimeSpan.FromSeconds(1.5);

    private FiscalJournal? _journal;
    private DispatcherTimer? _timer;
    private bool _busy;
    private readonly Dictionary<string, DateTime> _asked = [];
    private bool _hostActive;
    private FiscalPay? _payHint;

    public event Action? Changed;

    private FiscalService() { }

    /// <summary>Οι ρυθμίσεις ΑΑΔΕ του μαγαζιού (από το site) — ή της δοκιμής παρόχου (βλ. FiscalTestMode).</summary>
    private static FiscalSettings Fiscal => FiscalTestMode.IsOn ? FiscalTestMode.Settings : SiteLinkService.Instance.Fiscal;

    private static IFiscalProvider ProviderNow => FiscalTestMode.IsOn ? FiscalTestMode.Provider : SiteLinkService.Instance.Provider;

    /// <summary>Έχει το μαγαζί πάροχο (που το ταμείο ξέρει) και είμαστε στο κύριο ταμείο;</summary>
    public bool Active => !RemoteSync.IsClient && FiscalJournal.IsActive(Fiscal);

    /// <summary>
    /// Για την οθόνη, σε ΟΠΟΙΟ ταμείο κι αν είμαστε: έχει το μαγαζί πάροχο; Τότε ο ΟΡΘΙΟΣ ρωτάει ΜΕΤΡΗΤΑ/ΚΑΡΤΑ (η κάρτα
    /// πάει αμέσως στο τερματικό). Το δεύτερο ταμείο το μαθαίνει από το κύριο.
    /// </summary>
    public bool ProviderEnabled => RemoteSync.IsClient ? _hostActive : Active;

    public sealed record SyncDto(bool Active);

    /// <summary>Ο τρόπος πληρωμής που ξέρει ήδη μια εφαρμογή (π.χ. e-food: πληρωμένη online ή μετρητά στον διανομέα) για
    /// την παραγγελία που καταχωρείται ΑΜΕΣΩΣ μετά — χωρίς να γραφτεί στην ίδια την παραγγελία (εκεί θα μετρούσε δεύτερη
    /// φορά στα μετρητά/κάρτα της ημέρας).</summary>
    public IDisposable PayHint(FiscalPay pay)
    {
        _payHint = pay;
        return new Scope(() => _payHint = null);
    }

    private sealed class Scope(Action end) : IDisposable
    {
        public void Dispose() => end();
    }

    /// <summary>Τελείωσε ένα πάτημα πληρωμής στο κύριο ταμείο: ό,τι μάζευε φεύγει τώρα — η κάρτα αμέσως στο τερματικό.</summary>
    public void EndGather() => Safe("πληρωμή", j => j.EndGather());

    /// <summary>Το ημερολόγιο — μόνο όταν είναι ενεργό. Φτιάχνεται την πρώτη φορά που χρειαστεί.</summary>
    private FiscalJournal? Journal
    {
        get
        {
            if (!Active) return null;
            if (_journal is null)
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
                _journal = new FiscalJournal(new FileFiscalJournalStore(Path.Combine(dir, "fiscal-journal.json")), StorePrefix());
                _journal.Changed += () => Changed?.Invoke();
            }
            _journal.TerminalId = CounterTerminal();
            return _journal;
        }
    }

    /// <summary>Για την οθόνη/το site: πόσα περιμένουν, πόσα δελτία κινδυνεύουν με το 24ωρο.</summary>
    public int WaitingCount => _journal is { } j && Active ? j.WaitingCount : 0;
    public int OverdueSlips => _journal is { } j && Active ? j.OverdueSlips.Count : 0;

    /// <summary>Καλείται μία φορά στο άνοιγμα του ταμείου (App.OnStartup). Ο χρονοδιακόπτης τρέχει πάντα, αλλά χωρίς πάροχο
    /// δεν κάνει τίποτα — ώστε ο πάροχος να «πιάσει» μόλις μπει στο site, χωρίς επανεκκίνηση.</summary>
    public void Start()
    {
        if (_timer is not null)
            return;
        if (RemoteSync.IsClient)
        {
            RemoteSync.StartPolling(TimeSpan.FromSeconds(30), async () =>
            {
                if (await RemoteSync.GetAsync<SyncDto>("/api/sync/fiscal") is { } dto)
                    _hostActive = dto.Active;
            });
            return;
        }
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) =>
        {
            try { await TickAsync(); }
            catch (Exception ex) { AppLog.Write("fiscal", ex.GetType().Name + ": " + ex.Message); }
        };
        _timer.Start();
    }

    private async Task TickAsync()
    {
        if (_busy || Journal is not { } journal)
            return;
        _busy = true;
        try
        {
            await journal.ProcessAsync(ProviderNow);
            // Χαρτί ΔΕΝ βγαίνει μόνο του (απόφαση χρήστη 13/10/2026): η απόδειξη τυπώνεται όποτε τη ζητήσει το μαγαζί
            // (βλ. PrintReceipts), το δελτίο τραπεζιού ποτέ.
            AskAboutCards(journal);
        }
        finally { _busy = false; }
    }

    // ================================================================ τι έγινε στο ταμείο

    /// <summary>Καταχωρήθηκε παραγγελία (SalesStatsService.Record, κύριο ταμείο): εκτός τραπεζιού → απόδειξη, γύρος
    /// τραπεζιού → δελτίο.</summary>
    public void OnRecorded(CompletedOrder order) => Safe("καταχώρηση #" + order.OrderNumber, j =>
    {
        var day = SalesStatsService.BusinessDay(order.PlacedAt);
        var items = Items(order);
        if (order.Type == OrderType.Table)
        {
            if (TableOf(order) is { } table)
                j.TableRound(order.OrderNumber, day, Session(table, order), "Τραπέζι " + table, items);
            return;
        }
        var pay = _payHint ?? PayOf(order);
        // Διανομή (και BOX, που το φέρνει δικός μας διανομέας) με κάρτα: το ποσό προφορτώνεται στο φορητό POS του διανομέα
        // και πληρώνεται στην πόρτα. Όρθιος: η κάρτα πάει ΑΜΕΣΩΣ στο τερματικό του μαγαζιού.
        var ownDelivery = order.Type == OrderType.Delivery || (order.Type == OrderType.Apps && order.PaymentMethod is not null);
        j.Sale(order.OrderNumber, day, items, pay, ownDelivery && pay == FiscalPay.Card ? DeliveryTerminal() : "");
    });

    /// <summary>
    /// Τυπώνει την απόδειξη (ΜΑΡΚ + QR) μιας παραγγελίας — μόνο όταν το ζητήσει το μαγαζί (Ιστορικό, ή κινητό μέσω
    /// /api/fiscal/print). Από το δεύτερο ταμείο πάει στο κύριο, που έχει τις αποδείξεις και τον εκτυπωτή. Επιστρέφει πόσες
    /// τυπώθηκαν (0 = δεν έχει κοπεί ακόμα απόδειξη, π.χ. δεν πληρώθηκε ή περιμένει internet).
    /// </summary>
    public int PrintReceipts(IEnumerable<int> orderNumbers)
    {
        var orders = orderNumbers.ToList();
        if (RemoteSync.IsClient)
        {
            _ = RemoteSync.PostAsync("/api/sync/fiscal/print", new PrintRequest(orders, null));
            return -1;
        }
        if (Journal is not { } journal)
            return 0;
        var fiscal = Fiscal;
        var s = SettingsStore.Instance.Settings;
        var printed = 0;
        foreach (var e in journal.ReceiptsFor(orders))
        {
            try
            {
                var text = FiscalReceiptText.Build(e, s.ReceiptTitle, s.ShopAddress, fiscal.Afm, ProviderNow.Name);
                ReceiptPrinter.PrintFiscal(text, e.Result?.QrUrl ?? "", fiscal.Receipt == "other" ? fiscal.ReceiptPrinter : "", preview: FiscalTestMode.IsOn);
                journal.MarkPrinted(e.Id);
                printed++;
            }
            catch (Exception ex)
            {
                AppLog.Write("fiscal", $"Η απόδειξη {e.Result?.Series} {e.Result?.Number} δεν τυπώθηκε: {ex.Message}");
            }
        }
        return printed;
    }

    /// <summary>Οι αποδείξεις του τραπεζιού όπως είναι ανοιχτό τώρα (όλα τα άτομα/γύροι) — για το κινητό.</summary>
    public int PrintTableReceipts(int table)
    {
        if (!TableStatusService.Instance.OpenSince.TryGetValue(table, out var since))
            return 0;
        var orders = SalesStatsService.Instance.Orders
            .Where(o => o.Type == OrderType.Table && TableOf(o) == table && (o.TableOpenedAt ?? o.PlacedAt) >= since)
            .Select(o => o.OrderNumber);
        return PrintReceipts(orders);
    }

    /// <summary>Αίτημα τυπώματος από το δεύτερο ταμείο / το κινητό: παραγγελίες, ή ολόκληρο τραπέζι.</summary>
    public sealed record PrintRequest(List<int>? OrderNumbers, int? Table);

    /// <summary>Πληρώθηκε ένα τεμάχιο (ή μια γραμμή) τραπεζιού — TableSettlementService, κύριο ταμείο. Τα τεμάχια του
    /// ίδιου πατήματος γίνονται μία απόδειξη.</summary>
    public void OnSettled(int table, int orderNumber, int lineIndex, int unit, PaymentMethod method, decimal amount) =>
        Safe($"πληρωμή τραπεζιού {table}", j =>
        {
            if (Find(orderNumber) is not { } order || lineIndex < 0 || lineIndex >= order.Lines.Count) return;
            var line = order.Lines[lineIndex];
            var vat = VatBreakdownService.RateFor(line, order.Type);
            var item = unit >= 0
                ? new FiscalItem(FiscalItem.KeyFor(orderNumber, lineIndex, unit), line.Name, 1,
                    amount != 0 ? amount : decimal.Round(line.Revenue / Math.Max(1, line.Quantity), 2), vat)
                : new FiscalItem(FiscalItem.KeyFor(orderNumber, lineIndex), line.Name, line.Quantity, amount != 0 ? amount : line.Revenue, vat);
            j.TablePayment(SalesStatsService.BusinessDay(order.PlacedAt), Session(table, order), "Τραπέζι " + table, [item],
                method == PaymentMethod.Card ? FiscalPay.Card : FiscalPay.Cash, Gather);
        });

    /// <summary>Ακυρώθηκε ολόκληρη παραγγελία (OrderCancellationService) — καλείται ΠΡΙΝ σβηστεί.</summary>
    public void OnOrderCancelled(int orderNumber) => Safe("ακύρωση #" + orderNumber, j => j.Cancel(orderNumber));

    /// <summary>Ακυρώθηκε μία γραμμή (OrderCancellationService) — οι επόμενες ανεβαίνουν μία θέση, όπως στο ταμείο.</summary>
    public void OnLineCancelled(int orderNumber, int lineIndex) => Safe($"ακύρωση γραμμής #{orderNumber}", j =>
    {
        j.Cancel(orderNumber, [FiscalItem.KeyFor(orderNumber, lineIndex)]);
        j.LineRemoved(orderNumber, lineIndex);
    });

    /// <summary>Διορθώθηκε ο τρόπος πληρωμής (Ιστορικό, ή μετρητά↔κάρτα ενός ατόμου τραπεζιού).</summary>
    public void OnPaymentChanged(int orderNumber, PaymentMethod method) =>
        Safe("αλλαγή πληρωμής #" + orderNumber, j => j.ChangePayment(orderNumber, method == PaymentMethod.Card ? FiscalPay.Card : FiscalPay.Cash));

    /// <summary>Διορθώθηκε το κανάλι (π.χ. e-food → ΔΙΑΝΟΜΗ): αλλάζει το «πληρωμένη στην πλατφόρμα».</summary>
    public void OnChannelChanged(CompletedOrder order) =>
        Safe("αλλαγή καναλιού #" + order.OrderNumber, j => j.ChangePayment(order.OrderNumber, PayOf(order)));

    // ================================================================ βοηθητικά

    private void Safe(string what, Action<FiscalJournal> action)
    {
        try
        {
            if (Journal is { } j)
            {
                action(j);
                // Μόλις γραφτεί, φεύγει — χωρίς να περιμένει τον επόμενο γύρο του χρονοδιακόπτη.
                _ = Application.Current?.Dispatcher.BeginInvoke(async () =>
                {
                    try { await TickAsync(); }
                    catch (Exception ex) { AppLog.Write("fiscal", ex.GetType().Name + ": " + ex.Message); }
                }, DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("fiscal", $"{what}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static List<FiscalItem> Items(CompletedOrder order) =>
        [.. order.Lines.Select((l, i) => new FiscalItem(FiscalItem.KeyFor(order.OrderNumber, i), l.Name, l.Quantity, l.Revenue,
            VatBreakdownService.RateFor(l, order.Type)))];

    /// <summary>Πώς πληρώθηκε μια παραγγελία εκτός τραπεζιού. Εφαρμογή χωρίς τρόπο πληρωμής = πληρωμένη μέσα στην
    /// πλατφόρμα (ο τρόπος για αυτές είναι ερώτηση στον λογιστή — βλ. WrappProvider).</summary>
    private static FiscalPay PayOf(CompletedOrder order) => order.PaymentMethod switch
    {
        PaymentMethod.Card => FiscalPay.Card,
        PaymentMethod.Cash => FiscalPay.Cash,
        _ => order.Type == OrderType.Apps ? FiscalPay.Online : FiscalPay.Cash,
    };

    private static CompletedOrder? Find(int orderNumber) =>
        SalesStatsService.Instance.Orders.FirstOrDefault(o => o.OrderNumber == orderNumber);

    private static int? TableOf(CompletedOrder order) => int.TryParse(order.TableNumberLabel, out var t) ? t : null;

    /// <summary>Το τραπέζι από τη στιγμή που άνοιξε σε αυτή την παρέα — όλα τα δελτία και οι αποδείξεις της.</summary>
    private static string Session(int table, CompletedOrder order) =>
        $"{table}@{(order.TableOpenedAt ?? SalesStatsService.BusinessDay(order.PlacedAt)):yyyyMMddHHmmss}";

    private static string StorePrefix()
    {
        var key = SettingsStore.Instance.Settings.SiteKey.Trim();
        return key.Length == 0 ? "pos" : "s" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..6].ToLowerInvariant();
    }

    /// <summary>Το τερματικό του ταμείου (όρθιος, τραπέζι): το πρώτο που ΔΕΝ είναι του διανομέα.</summary>
    private static string CounterTerminal()
    {
        var terminals = Fiscal.Terminals;
        return (terminals.FirstOrDefault(t => !IsDelivery(t)) ?? terminals.FirstOrDefault())?.TerminalId ?? "";
    }

    /// <summary>Το φορητό POS του διανομέα: όποιο λέγεται «ΔΙΑΝΟΜ…» στις Ρυθμίσεις του site, αλλιώς το δεύτερο, αλλιώς το
    /// ίδιο με του ταμείου.</summary>
    private static string DeliveryTerminal()
    {
        var terminals = Fiscal.Terminals;
        return (terminals.FirstOrDefault(IsDelivery) ?? terminals.Skip(1).FirstOrDefault() ?? terminals.FirstOrDefault())?.TerminalId ?? "";
    }

    private static bool IsDelivery(FiscalTerminal t) =>
        t.Name.ToUpperInvariant().Replace('Ά', 'Α').Replace('Έ', 'Ε').Contains("ΔΙΑΝΟΜ") || t.Name.Contains("delivery", StringComparison.OrdinalIgnoreCase);

    /// <summary>Κάρτα που δεν πέρασε: ΜΙΑ ερώτηση στη μέση — «ξανά στο τερματικό» ή «μετρητά». Αν κλείσει χωρίς
    /// απάντηση, ξαναρωτάει σε ένα λεπτό· η απόδειξη δεν χάνεται.</summary>
    private void AskAboutCards(FiscalJournal journal)
    {
        foreach (var e in journal.NeedsDecision)
        {
            if (_asked.TryGetValue(e.Id, out var at) && DateTime.Now - at < TimeSpan.FromMinutes(1))
                continue;
            _asked[e.Id] = DateTime.Now;
            switch (Views.CardDeclinedDialog.Ask(Application.Current?.MainWindow, e.Total, e.LastError))
            {
                case true: journal.RetryCard(e.Id); break;
                case false: journal.PayCardAsCash(e.Id); break;
            }
            return; // μία ερώτηση τη φορά
        }
    }
}
