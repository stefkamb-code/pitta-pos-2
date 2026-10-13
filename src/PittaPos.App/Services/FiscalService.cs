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
    private bool _hostAutoReceipt = true;
    private FiscalPay? _payHint;

    /// <summary>Το ημερολόγιο ελέγχου (ένα αρχείο ανά μήνα, μόνο προσθήκες) — βλ. <see cref="FileFiscalAudit"/>.</summary>
    private static readonly FileFiscalAudit Audit = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder, "fiscal-audit"));

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

    /// <summary>«Αυτόματη έκδοση απόδειξης» (βλ. <see cref="ReceiptPolicy"/>). Ζει στο κύριο ταμείο, που κόβει τις αποδείξεις·
    /// το δεύτερο τη διαβάζει από εκεί.</summary>
    public bool AutoReceipt => RemoteSync.IsClient ? _hostAutoReceipt : SettingsStore.Instance.Settings.AutoReceipt;

    public sealed record SyncDto(bool Active, bool AutoReceipt = true);

    public sealed record AutoReceiptRequest(bool On, string ChangedBy);

    /// <summary>Ο διακόπτης «🧾 ΑΠΟΔΕΙΞΗ ΤΩΡΑ» του παραθύρου πληρωμής (βλ. OrderWizardViewModel.ReceiptNow): θυμάται τη θέση
    /// του και τη γράφει στο ημερολόγιο ελέγχου. Ό,τι περιμένει ήδη, συνεχίζει να περιμένει την αποστολή/πληρωμή του.</summary>
    public void SetAutoReceipt(bool on, string changedBy)
    {
        if (RemoteSync.IsClient)
        {
            _hostAutoReceipt = on;
            _ = RemoteSync.PostAsync("/api/sync/fiscal/auto", new AutoReceiptRequest(on, changedBy));
            return;
        }
        if (SettingsStore.Instance.Settings.AutoReceipt == on)
            return;
        SettingsStore.Instance.SetAutoReceipt(on);
        Audit.Write(new FiscalAuditRecord(DateTime.Now, "ρύθμιση", "", "", "", "", 0, "",
            $"Αυτόματη έκδοση απόδειξης: {(on ? "ΝΑΙ" : "ΟΧΙ")} — {changedBy}"));
        Changed?.Invoke();
    }

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
                _journal = new FiscalJournal(new FileFiscalJournalStore(Path.Combine(dir, "fiscal-journal.json")), StorePrefix())
                {
                    Audit = Audit,
                };
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
                {
                    _hostActive = dto.Active;
                    _hostAutoReceipt = dto.AutoReceipt;
                }
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
            // (βλ. PrintReceipts), το δελτίο τραπεζιού ποτέ. Εξαίρεση: ζητήθηκε ενώ περίμενε — τυπώνεται μόλις κοπεί.
            PrintRequested(journal);
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
        // ΠΟΤΕ φεύγει: το αποφασίζει ΜΟΝΟ το ReceiptPolicy (διακόπτης «Αυτόματη έκδοση» + υποχρεωτικά αυτόματα).
        var decision = ReceiptPolicy.Decide(order.Type, order.Channel, pay, AutoOf(order));
        j.Sale(order.OrderNumber, day, items, pay, PreloadFor(order, pay),
            decision.Deferred ? decision.MaxWait : null, decision.Reason, decision.WaitsFor);
    });

    /// <summary>Η διανομή πέρασε σε διανομέα (Ζωντανές, OrderBoardService.Dispatch/Reassign): η απόδειξη που περίμενε την
    /// αποστολή φεύγει τώρα — σε κάρτα προφορτώνεται στο POS του διανομέα πριν φτάσει στην πόρτα.</summary>
    public void OnDispatched(int orderNumber, string channel) =>
        Safe("αποστολή #" + orderNumber, j => j.Release([orderNumber], "πέρασε σε " + channel));

    /// <summary>Κλείσιμο ημέρας (DayReportService.CloseDay): ό,τι περιμένει ακόμα από τη μέρα που κλείνει, κόβεται τώρα.</summary>
    public void OnDayClosing(DateTime businessDay) =>
        Safe("κλείσιμο ημέρας", j => j.ReleaseBefore(businessDay.Date.AddHours(SalesStatsService.DayCutoffHour), "κλείσιμο ημέρας"));

    /// <summary>Ο διακόπτης «🧾 ΑΠΟΔΕΙΞΗ ΤΩΡΑ» όπως πατήθηκε στην πληρωμή αυτής της παραγγελίας· όπου δεν ρωτήθηκε, η
    /// τελευταία θέση του.</summary>
    private bool AutoOf(CompletedOrder order) => order.AutoReceipt ?? AutoReceipt;

    /// <summary>Το POS του διανομέα για προφόρτωση — μόνο σε κάρτα σε δική μας διανομή (ΔΙΑΝΟΜΗ, BOX).</summary>
    private static string PreloadFor(CompletedOrder order, FiscalPay pay) =>
        pay == FiscalPay.Card && (order.Type == OrderType.Delivery || (order.Type == OrderType.Apps && order.PaymentMethod is not null))
            ? DeliveryTerminal()
            : "";

    /// <summary>Η κατάσταση της απόδειξης μιας παραγγελίας, για το Ιστορικό. Held = περιμένει ακόμα (το κουμπί την κόβει).</summary>
    public sealed record ReceiptStatus(bool Held, string Text);

    /// <summary>Ίδιο με <see cref="StatusOf"/>, και από το δεύτερο ταμείο (ρωτά το κύριο, που έχει τις αποδείξεις).</summary>
    public async Task<ReceiptStatus> StatusOfAsync(int orderNumber) => RemoteSync.IsClient
        ? await RemoteSync.GetAsync<ReceiptStatus>($"/api/sync/fiscal/status/{orderNumber}") ?? new(false, "")
        : StatusOf(orderNumber);

    public ReceiptStatus StatusOf(int orderNumber)
    {
        if (Journal is not { } journal)
            return new(false, "");
        var lines = new List<string>();
        var held = false;
        foreach (var e in journal.EntriesFor(orderNumber).Where(e => e.IsLive))
        {
            var what = e.Kind switch
            {
                FiscalEntryKind.Slip => "Δελτίο παραγγελίας",
                FiscalEntryKind.Credit => "Πιστωτικό",
                FiscalEntryKind.SlipCancel => "Ακύρωση δελτίου",
                _ => "Απόδειξη",
            };
            lines.Add(e.State switch
            {
                FiscalEntryState.Held => $"⏳ {what} σε αναμονή — {e.HoldReason}, το αργότερο {e.HeldUntil:HH:mm}",
                FiscalEntryState.Issued => $"✓ {what} {e.Result?.Series} {e.Result?.Number} · ΜΑΡΚ {e.Mark}".Replace("  ", " "),
                FiscalEntryState.NeedsDecision => $"⚠ {what}: η κάρτα δεν πέρασε — περιμένει απόφαση",
                _ when e.LastError.Length > 0 => $"↻ {what}: ξαναδοκιμάζει ({e.LastError})",
                _ => $"→ {what}: στέλνεται στον πάροχο",
            });
            held |= e.State == FiscalEntryState.Held;
        }
        return new(held, string.Join("\n", lines));
    }

    /// <summary>
    /// Τυπώνει την απόδειξη (ΜΑΡΚ + QR) μιας παραγγελίας — μόνο όταν το ζητήσει το μαγαζί (Ιστορικό, ή κινητό μέσω
    /// /api/fiscal/print). Από το δεύτερο ταμείο πάει στο κύριο, που έχει τις αποδείξεις και τον εκτυπωτή. Επιστρέφει πόσες
    /// τυπώθηκαν ή θα τυπωθούν μόλις κοπούν (0 = δεν έχει απόδειξη, π.χ. τραπέζι που δεν πληρώθηκε).
    ///
    /// <para>Απόδειξη σε αναμονή (αυτόματη έκδοση κλειστή): το πάτημα ΕΙΝΑΙ η πληρωμή — κόβεται τώρα και τυπώνεται μόλις
    /// έρθει το ΜΑΡΚ.</para>
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
        var released = journal.Release(orders, "ζητήθηκε η απόδειξη (πληρωμή)", print: true);
        if (released > 0)
            Kick();
        var fiscal = Fiscal;
        var s = SettingsStore.Instance.Settings;
        var printed = released;
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

    /// <summary>Όσες ζητήθηκαν σε χαρτί πριν κοπούν: το χαρτί τους, μία φορά, μόλις κοπούν. Σημειώνεται ΠΡΙΝ το τύπωμα — αν
    /// σκάσει ο εκτυπωτής, δεν ξαναπροσπαθεί σε κάθε γύρο (τυπώνεται ξανά από το Ιστορικό).</summary>
    private void PrintRequested(FiscalJournal journal)
    {
        var fiscal = Fiscal;
        var s = SettingsStore.Instance.Settings;
        foreach (var e in journal.ToPrint)
        {
            journal.MarkPrinted(e.Id);
            try
            {
                var text = FiscalReceiptText.Build(e, s.ReceiptTitle, s.ShopAddress, fiscal.Afm, ProviderNow.Name);
                ReceiptPrinter.PrintFiscal(text, e.Result?.QrUrl ?? "", fiscal.Receipt == "other" ? fiscal.ReceiptPrinter : "",
                    preview: FiscalTestMode.IsOn);
            }
            catch (Exception ex)
            {
                AppLog.Write("fiscal", $"Η απόδειξη {e.Result?.Series} {e.Result?.Number} δεν τυπώθηκε: {ex.Message}");
            }
        }
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

    /// <summary>Διορθώθηκε ο τρόπος πληρωμής (Ιστορικό, Ζωντανές, ή μετρητά↔κάρτα ενός ατόμου τραπεζιού). Αν η απόδειξη
    /// περιμένει ακόμα, απλώς αλλάζει· και αν ο νέος τρόπος δεν επιτρέπει αναμονή (όρθιος → κάρτα), φεύγει τώρα.</summary>
    public void OnPaymentChanged(int orderNumber, PaymentMethod method) => Safe("αλλαγή πληρωμής #" + orderNumber, j =>
    {
        var pay = method == PaymentMethod.Card ? FiscalPay.Card : FiscalPay.Cash;
        if (Find(orderNumber) is { } order && order.Type != OrderType.Table)
            j.ChangePayment(orderNumber, pay, PreloadFor(order, pay),
                releaseHeld: !ReceiptPolicy.Decide(order.Type, order.Channel, pay, AutoOf(order)).Deferred);
        else
            j.ChangePayment(orderNumber, pay);
    });

    /// <summary>Διορθώθηκε το κανάλι (π.χ. e-food → ΔΙΑΝΟΜΗ): αλλάζει το «πληρωμένη στην πλατφόρμα».</summary>
    public void OnChannelChanged(CompletedOrder order) => Safe("αλλαγή καναλιού #" + order.OrderNumber, j =>
    {
        var pay = PayOf(order);
        var deferred = ReceiptPolicy.Decide(order.Type, order.Channel, pay, AutoOf(order)).Deferred;
        j.ChangePayment(order.OrderNumber, pay, PreloadFor(order, pay), releaseHeld: !deferred);
        // Έγινε εφαρμογή (πάντα αυτόματα) ενώ περίμενε με ίδιο τρόπο πληρωμής: φεύγει τώρα.
        if (!deferred)
            j.Release([order.OrderNumber], "άλλαξε το κανάλι σε " + order.TypeLabel);
    });

    // ================================================================ βοηθητικά

    private void Safe(string what, Action<FiscalJournal> action)
    {
        try
        {
            if (Journal is { } j)
            {
                action(j);
                Kick();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("fiscal", $"{what}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Μόλις γραφτεί κάτι, φεύγει — χωρίς να περιμένει τον επόμενο γύρο του χρονοδιακόπτη.</summary>
    private void Kick() => _ = Application.Current?.Dispatcher.BeginInvoke(async () =>
    {
        try { await TickAsync(); }
        catch (Exception ex) { AppLog.Write("fiscal", ex.GetType().Name + ": " + ex.Message); }
    }, DispatcherPriority.Background);

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
