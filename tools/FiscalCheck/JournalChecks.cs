using PittaPos.Core.Fiscal;

/// <summary>
/// Ο «εγκέφαλος» (FiscalJournal) σε όλα τα σενάρια του μαγαζιού, με τον εικονικό πάροχο και με έναν «χαλασμένο» από πάνω
/// του: πεσμένο internet, χαμένη απάντηση, κάρτα που δεν περνά, ταμείο που κλείνει την ώρα που φεύγει παραστατικό. Ο
/// έλεγχος δεν αφορά κάποιον πάροχο — αφορά ό,τι κάνει το ταμείο, ίδιο για όλους.
/// </summary>
static class JournalChecks
{
    static int _failures;
    static DateTime _clock;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ✓ " : "  ✗ ") + what);
        if (!ok) _failures++;
    }

    static FiscalItem Food(int order, int line, string name, int qty, decimal revenue) => new(FiscalItem.KeyFor(order, line), name, qty, revenue, 13);
    static FiscalItem Drink(int order, int line, string name, int qty, decimal revenue) => new(FiscalItem.KeyFor(order, line), name, qty, revenue, 24);
    static FiscalItem Unit(int order, int line, int unit, string name, decimal revenue, int vat = 13) =>
        new(FiscalItem.KeyFor(order, line, unit), name, 1, revenue, vat);

    static readonly DateTime Day = new(2026, 10, 13);

    static (FiscalJournal Journal, SimulatedProvider Inner, ChaosProvider Chaos, MemoryFiscalJournalStore Store) New()
    {
        _clock = new DateTime(2026, 10, 13, 13, 0, 0);
        var store = new MemoryFiscalJournalStore();
        var inner = new SimulatedProvider(fast: true);
        return (new FiscalJournal(store, "t1", () => _clock) { TerminalId = "T88011" }, inner, new ChaosProvider(inner), store);
    }

    /// <summary>Περνάει ο χρόνος — για τις επαναλήψεις μετά από αποτυχία.</summary>
    static void Later(TimeSpan t) => _clock += t;

    static decimal Net(FiscalJournal j, int order) =>
        j.Entries.Where(e => e.State == FiscalEntryState.Issued && e.Kind == FiscalEntryKind.Receipt).SelectMany(e => e.Items).Where(i => i.Order == order).Sum(i => i.Revenue)
        - j.Entries.Where(e => e.State == FiscalEntryState.Issued && e.Kind == FiscalEntryKind.Credit).SelectMany(e => e.Items).Where(i => i.Order == order).Sum(i => i.Revenue);

    public static async Task<int> RunAsync()
    {
        _failures = 0;
        Console.WriteLine("\nΟ εγκέφαλος των παραστατικών (ίδιος για κάθε πάροχο)");

        Console.WriteLine("  — μόνο όταν το μαγαζί έχει πάροχο");
        Check(!FiscalJournal.IsActive(FiscalSettings.None), "Χωρίς πάροχο στις Ρυθμίσεις: ανενεργό — το ταμείο δουλεύει ακριβώς όπως σήμερα");
        Check(!FiscalJournal.IsActive(FiscalSettings.None with { Provider = "Epsilon Digital" }),
            "Πάροχος χωρίς οδηγό ακόμα: ανενεργό (όχι παραστατικά που κολλάνε)");
        Check(FiscalJournal.IsActive(FiscalSettings.None with { Provider = "Wrapp" })
              && FiscalJournal.IsActive(FiscalSettings.None with { Provider = FiscalProviders.Simulated }), "Wrapp / δοκιμή χωρίς πάροχο: ενεργό");

        Console.WriteLine("  — όρθιος, διανομή, εφαρμογές");
        {
            var (j, inner, _, _) = New();
            j.Sale(1, Day, [Food(1, 0, "ΠΙΤΤΑ ΧΟΙΡΙΝΟ", 2, 14.00m), Drink(1, 1, "Amstel", 1, 3.50m)], FiscalPay.Cash);
            j.Sale(2, Day, [Food(2, 0, "ΠΙΤΤΑ ΚΟΤΟΠΟΥΛΟ", 1, 7.20m)], FiscalPay.Card);
            j.Sale(3, Day, [Food(3, 0, "Πίττα Παππού", 1, 8.90m)], FiscalPay.Online);
            j.Sale(4, Day, [Food(4, 0, "Κέρασμα", 1, 0m)], FiscalPay.Cash);
            await j.ProcessAsync(inner);
            var docs = inner.Issued.ToList();
            Check(docs.Count == 3 && j.Entries.All(e => e.State == FiscalEntryState.Issued && e.Mark.Length > 0),
                "Τρεις παραγγελίες → τρεις αποδείξεις με ΜΑΡΚ· το κέρασμα (0 €) δεν κόβει τίποτα");
            Check(docs[0].Doc.Gross == 17.50m && docs[0].Doc.Type == FiscalDocType.RetailReceipt && docs[0].Doc.Pay == FiscalPay.Cash,
                "Μετρητά: 11.1 με το ίδιο σύνολο (17,50)");
            Check(docs[1].Doc.Pay == FiscalPay.Card && docs[1].Doc.TerminalId == "T88011", "Κάρτα: το ποσό πάει στο τερματικό του μαγαζιού");
            Check(docs[2].Doc.Pay == FiscalPay.Online && docs[2].Doc.TerminalId == "", "Πληρωμένη στην εφαρμογή: χωρίς τερματικό");
        }

        Console.WriteLine("  — τραπέζι: γύροι, άτομα, κλείσιμο");
        {
            var (j, inner, _, _) = New();
            j.TableRound(101, Day, "5@1300", "Τραπέζι 5", [Food(101, 0, "Χοιρινό μερίδα", 2, 21.00m), Drink(101, 1, "Coca-Cola", 2, 3.60m)]);
            j.TableRound(102, Day, "5@1300", "Τραπέζι 5", [Drink(102, 0, "Amstel", 2, 7.00m)]);
            await j.ProcessAsync(inner);
            var slips = inner.Issued.Where(d => d.Doc.Type == FiscalDocType.OrderSlip).ToList();
            Check(slips.Count == 2 && slips[0].Doc.TableName == "Τραπέζι 5", "Κάθε γύρος → δελτίο 8.6 τη στιγμή που γράφεται");
            Check(slips[0].Doc.ProviderTableRef == "" && slips[1].Doc.ProviderTableRef == slips[0].Result.ProviderTableRef && slips[1].Doc.ProviderTableRef.Length > 0,
                "Το πρώτο δελτίο ανοίγει το τραπέζι στον πάροχο, το δεύτερο πάει στο ίδιο");
            Check(j.OpenSlips.Count == 2, "Δύο ανοιχτά δελτία μέχρι να πληρωθούν");

            // Το Α πληρώνει με κάρτα τη μία μερίδα και μία κόκα κόλα.
            j.TablePayment(Day, "5@1300", "Τραπέζι 5", [Unit(101, 0, 0, "Χοιρινό μερίδα", 10.50m), Unit(101, 1, 0, "Coca-Cola", 1.80m, 24)], FiscalPay.Card);
            await j.ProcessAsync(inner);
            var a = inner.Issued.Last();
            Check(a.Doc.Type == FiscalDocType.RetailReceipt && a.Doc.Gross == 12.30m && a.Doc.CorrelatedMarks.SequenceEqual([slips[0].Result.Mark]),
                "Το Α πληρώνει τα δικά του → απόδειξη 12,30 που κλείνει ΜΟΝΟ το δελτίο του γύρου του");
            // Κλείνει όλο το τραπέζι με μετρητά.
            j.TablePayment(Day, "5@1300", "Τραπέζι 5",
                [Unit(101, 0, 1, "Χοιρινό μερίδα", 10.50m), Unit(101, 1, 1, "Coca-Cola", 1.80m, 24), Unit(102, 0, 0, "Amstel", 3.50m, 24), Unit(102, 0, 1, "Amstel", 3.50m, 24)],
                FiscalPay.Cash);
            await j.ProcessAsync(inner);
            var rest = inner.Issued.Last();
            Check(rest.Doc.Gross == 19.30m && rest.Doc.CorrelatedMarks.Count == 2, "Κλείσιμο τραπεζιού → απόδειξη 19,30 που κλείνει και τα δύο δελτία");
            Check(j.OpenSlips.Count == 0, "Κανένα ανοιχτό δελτίο μετά το κλείσιμο");
            Check(Net(j, 101) == 24.60m && Net(j, 102) == 7.00m, "Αποδείξεις = ό,τι γράφτηκε, ούτε λεπτό πάνω ή κάτω");
        }

        Console.WriteLine("  — πληρωμή που φτάνει τεμάχιο-τεμάχιο (δεύτερο ταμείο, κινητό)");
        {
            var (j, inner, _, store) = New();
            var gather = TimeSpan.FromSeconds(1);
            j.TableRound(111, Day, "8@1300", "Τραπέζι 8", [Food(111, 0, "Καλαμάκι", 3, 7.50m)]);
            await j.ProcessAsync(inner);
            j.TablePayment(Day, "8@1300", "Τραπέζι 8", [Unit(111, 0, 0, "Καλαμάκι", 2.50m)], FiscalPay.Cash, gather);
            j.TablePayment(Day, "8@1300", "Τραπέζι 8", [Unit(111, 0, 1, "Καλαμάκι", 2.50m)], FiscalPay.Cash, gather);
            await j.ProcessAsync(inner);
            Check(inner.Issued.Count == 1, "Όσο έρχονται τεμάχια του ίδιου πατήματος, η απόδειξη περιμένει");
            Check(new FiscalJournal(store, "t1", () => _clock).Entries.Any(e => e.Kind == FiscalEntryKind.Receipt && e.Items.Count == 2),
                "…αλλά είναι ήδη γραμμένη στο αρχείο (κλείσιμο του ταμείου τώρα δεν τη χάνει)");
            Later(TimeSpan.FromSeconds(2));
            await j.ProcessAsync(inner);
            Check(inner.Issued.Count == 2 && inner.Issued.Last().Doc.Gross == 5.00m, "Ένα πάτημα, δύο τεμάχια → ΜΙΑ απόδειξη 5,00");
            j.TablePayment(Day, "8@1300", "Τραπέζι 8", [Unit(111, 0, 2, "Καλαμάκι", 2.50m)], FiscalPay.Cash, gather);
            Later(TimeSpan.FromSeconds(2));
            await j.ProcessAsync(inner);
            Check(inner.Issued.Count == 3 && inner.Issued.Last().Doc.Gross == 2.50m, "Επόμενο πάτημα (αργότερα) → δική του απόδειξη");
        }

        Console.WriteLine("  — χωρίς internet");
        {
            var (j, inner, chaos, _) = New();
            chaos.Down = true;
            j.TableRound(201, Day, "7@1400", "Τραπέζι 7", [Food(201, 0, "Σαλάτα", 1, 7.50m)]);
            j.TablePayment(Day, "7@1400", "Τραπέζι 7", [Food(201, 0, "Σαλάτα", 1, 7.50m)], FiscalPay.Cash);
            j.Sale(202, Day, [Food(202, 0, "ΠΙΤΤΑ", 1, 7.00m)], FiscalPay.Cash);
            await j.ProcessAsync(chaos);
            Check(inner.Issued.Count == 0 && j.WaitingCount == 3, "Τίποτα δεν κόβεται, τίποτα δεν χάνεται: 3 περιμένουν");
            Check(j.Entries.All(e => e.LastError.Length > 0 || e.Kind == FiscalEntryKind.Receipt), "Το γιατί γράφεται (για την οθόνη/το site)");
            chaos.Down = false;
            await j.ProcessAsync(chaos);
            Check(inner.Issued.Count == 0, "Δεν ξαναχτυπάει αμέσως τον πάροχο — περιμένει λίγο (5'', 10'', … ως 5')");
            Later(TimeSpan.FromMinutes(6));
            await j.ProcessAsync(chaos);
            var issued = inner.Issued.ToList();
            Check(j.WaitingCount == 0 && issued.Count == 3, "Μόλις επανέλθει: κόβονται και τα τρία");
            var slipAt = issued.FindIndex(d => d.Doc.Type == FiscalDocType.OrderSlip);
            var tableReceiptAt = issued.FindIndex(d => d.Doc.CorrelatedMarks.Count > 0);
            Check(slipAt >= 0 && tableReceiptAt > slipAt && issued[tableReceiptAt].Doc.CorrelatedMarks[0] == issued[slipAt].Result.Mark,
                "Η απόδειξη του τραπεζιού περιμένει το ΜΑΡΚ του δελτίου της");
        }

        Console.WriteLine("  — χαμένη απάντηση / κλείσιμο ταμείου την ώρα που φεύγει");
        {
            var (j, inner, chaos, _) = New();
            chaos.LoseNext = 1;
            j.Sale(301, Day, [Food(301, 0, "ΠΙΤΤΑ", 1, 7.00m)], FiscalPay.Cash);
            await j.ProcessAsync(chaos);
            Check(inner.Issued.Count == 1 && j.WaitingCount == 1, "Κόπηκε στον πάροχο αλλά η απάντηση χάθηκε → το ταμείο ξαναδοκιμάζει");
            Later(TimeSpan.FromMinutes(1));
            await j.ProcessAsync(chaos);
            Check(inner.Issued.Count == 1 && j.Entries[0].Mark == inner.Issued.First().Result.Mark,
                "Η επανάληψη φέρνει ΤΟ ΙΔΙΟ παραστατικό — όχι δεύτερη απόδειξη");

            var (j2, inner2, chaos2, store2) = New();
            j2.Sale(302, Day, [Food(302, 0, "ΠΙΤΤΑ", 1, 7.00m)], FiscalPay.Cash);
            chaos2.Gate = new TaskCompletionSource();
            var stuck = j2.ProcessAsync(chaos2);                       // «φεύγει»…
            await Task.Delay(50);
            var restarted = new FiscalJournal(store2, "t1", () => _clock); // …και κόβεται το ρεύμα: ξανανοίγει από το αρχείο
            Check(restarted.WaitingCount == 1, "Μετά από κλείσιμο στη μέση: το παραστατικό είναι στο ημερολόγιο, ξαναφεύγει");
            await restarted.ProcessAsync(inner2);
            chaos2.Gate.SetResult();                                   // η παλιά απόπειρα φτάνει τελικά κι αυτή
            await stuck;
            Check(inner2.Issued.Count == 1, "Ακόμα και αν έφτασαν και οι δύο απόπειρες: μία απόδειξη");
        }

        Console.WriteLine("  — κάρτα που δεν περνά");
        {
            var (j, inner, chaos, _) = New();
            chaos.DeclineCards = true;
            var e = j.Sale(401, Day, [Food(401, 0, "ΠΙΤΤΑ", 2, 14.40m)], FiscalPay.Card)!;
            await j.ProcessAsync(chaos);
            Check(j.NeedsDecision.Count == 1 && inner.Issued.Count == 0, "Δεν πέρασε → περιμένει τον ταμία, δεν κόβεται τίποτα");
            Later(TimeSpan.FromMinutes(10));
            await j.ProcessAsync(chaos);
            Check(j.NeedsDecision.Count == 1, "Δεν ξαναστέλνεται μόνη της στο τερματικό (ο πελάτης μπορεί να έχει φύγει)");
            j.PayCardAsCash(e.Id);
            await j.ProcessAsync(chaos);
            Check(inner.Issued.Count == 1 && inner.Issued.First().Doc.Pay == FiscalPay.Cash && inner.Issued.First().Doc.TerminalId == "",
                "«Πλήρωσε μετρητά» → απόδειξη μετρητών");

            chaos.DeclineCards = true;
            var f = j.Sale(402, Day, [Food(402, 0, "ΠΙΤΤΑ", 1, 7.20m)], FiscalPay.Card)!;
            await j.ProcessAsync(chaos);
            chaos.DeclineCards = false;
            j.RetryCard(f.Id);
            await j.ProcessAsync(chaos);
            Check(inner.Issued.Last().Doc.Pay == FiscalPay.Card && j.NeedsDecision.Count == 0, "«Ξανά» → η κάρτα πέρασε τη δεύτερη φορά");
        }

        Console.WriteLine("  — ακυρώσεις");
        {
            var (j, inner, chaos, _) = New();
            chaos.Down = true;
            j.Sale(501, Day, [Food(501, 0, "ΠΙΤΤΑ", 1, 7.00m)], FiscalPay.Cash);
            j.Cancel(501);
            chaos.Down = false;
            Later(TimeSpan.FromMinutes(6));
            await j.ProcessAsync(chaos);
            Check(inner.Issued.Count == 0, "Ακύρωση πριν φύγει → δεν κόβεται τίποτα");

            j.Sale(502, Day, [Food(502, 0, "ΠΙΤΤΑ", 2, 14.00m), Drink(502, 1, "Amstel", 1, 3.50m)], FiscalPay.Cash);
            await j.ProcessAsync(inner);
            var receipt = inner.Issued.Last();
            j.Cancel(502, [FiscalItem.KeyFor(502, 1)]);
            await j.ProcessAsync(inner);
            var credit = inner.Issued.Last();
            Check(credit.Doc.Type == FiscalDocType.RetailCredit && credit.Doc.Gross == 3.50m && credit.Doc.CorrelatedMarks.SequenceEqual([receipt.Result.Mark]),
                "Ακύρωση γραμμής σε απόδειξη που κόπηκε → πιστωτικό 11.4 μόνο γι' αυτήν, πάνω στο ΜΑΡΚ της");
            Check(Net(j, 502) == 14.00m, "Απόδειξη − πιστωτικό = ό,τι έμεινε (14,00)");

            j.TableRound(503, Day, "9@1500", "Τραπέζι 9", [Food(503, 0, "Σουβλάκι", 3, 7.50m), Food(503, 1, "Πατάτες", 1, 3.50m)]);
            await j.ProcessAsync(inner);
            var slip = inner.Issued.Last();
            j.Cancel(503, [FiscalItem.KeyFor(503, 1)]);
            await j.ProcessAsync(inner);
            var newSlip = inner.Issued.Last();
            Check(inner.CancelledSlips.Contains(slip.Result.Mark), "Ακύρωση πιάτου σε δελτίο που δεν πληρώθηκε → ακύρωση του δελτίου στον πάροχο");
            Check(newSlip.Doc.Type == FiscalDocType.OrderSlip && newSlip.Doc.Gross == 7.50m, "…και νέο δελτίο με ό,τι έμεινε (7,50)");
            j.TablePayment(Day, "9@1500", "Τραπέζι 9", [Food(503, 0, "Σουβλάκι", 3, 7.50m)], FiscalPay.Cash);
            await j.ProcessAsync(inner);
            Check(inner.Issued.Last().Doc.CorrelatedMarks.SequenceEqual([newSlip.Result.Mark]) && j.OpenSlips.Count == 0,
                "Η πληρωμή κλείνει το νέο δελτίο, όχι το ακυρωμένο");

            j.TableRound(504, Day, "3@1600", "Τραπέζι 3", [Food(504, 0, "Μπιφτέκι", 1, 9.00m)]);
            await j.ProcessAsync(inner);
            j.TablePayment(Day, "3@1600", "Τραπέζι 3", [Food(504, 0, "Μπιφτέκι", 1, 9.00m)], FiscalPay.Cash);
            await j.ProcessAsync(inner);
            var paid = inner.Issued.Last();
            var cancelledBefore = inner.CancelledSlips.Count;
            j.Cancel(504);
            await j.ProcessAsync(inner);
            Check(inner.Issued.Last().Doc.Type == FiscalDocType.RetailCredit && inner.Issued.Last().Doc.CorrelatedMarks.SequenceEqual([paid.Result.Mark])
                  && inner.CancelledSlips.Count == cancelledBefore,
                "Ακύρωση γύρου που ΗΔΗ πληρώθηκε → πιστωτικό της απόδειξης (εκεί είναι τα χρήματα), όχι ακύρωση δελτίου");
            Check(Net(j, 504) == 0m, "Τζίρος της ακυρωμένης = 0");
            j.Cancel(504);
            await j.ProcessAsync(inner);
            Check(Net(j, 504) == 0m, "Δεύτερη ακύρωση της ίδιας → τίποτα (όχι δεύτερο πιστωτικό)");

            // Οι γραμμές «ανεβαίνουν» μετά από διαγραφή: η επόμενη ακύρωση πρέπει να πέσει στο σωστό προϊόν.
            j.Sale(505, Day, [Food(505, 0, "Γύρος", 1, 8.00m), Food(505, 1, "Πατάτες", 1, 3.50m), Drink(505, 2, "Amstel", 1, 3.50m)], FiscalPay.Cash);
            await j.ProcessAsync(inner);
            j.Cancel(505, [FiscalItem.KeyFor(505, 1)]);
            j.LineRemoved(505, 1);                     // στο ταμείο, η Amstel είναι τώρα η γραμμή 1
            j.Cancel(505, [FiscalItem.KeyFor(505, 1)]);
            j.LineRemoved(505, 1);
            await j.ProcessAsync(inner);
            var credits = inner.Issued.Where(d => d.Doc.Type == FiscalDocType.RetailCredit).TakeLast(2).Select(d => d.Doc.Lines[0].Name).ToList();
            Check(credits.SequenceEqual(["Πατάτες", "Amstel"]) && Net(j, 505) == 8.00m,
                "Ακύρωση γραμμής 1 δύο φορές στη σειρά (όπως στο ταμείο) → πιστώνονται οι πατάτες και ΜΕΤΑ η Amstel, μένει ο γύρος");

            j.TableRound(506, Day, "6@1800", "Τραπέζι 6", [Food(506, 0, "Σουβλάκι", 1, 2.50m), Food(506, 1, "Πίτα", 1, 0.50m), Drink(506, 2, "Νερό", 1, 0.50m)]);
            await j.ProcessAsync(inner);
            var cancelsBefore = inner.CancelledSlips.Count;
            j.Cancel(506, [FiscalItem.KeyFor(506, 0)]);
            j.LineRemoved(506, 0);
            await j.ProcessAsync(inner);
            j.Cancel(506, [FiscalItem.KeyFor(506, 0)]);  // η πίτα, που ανέβηκε στη θέση 0
            j.LineRemoved(506, 0);
            await j.ProcessAsync(inner);
            var lastSlip = inner.Issued.Last(d => d.Doc.Type == FiscalDocType.OrderSlip).Doc;
            Check(inner.CancelledSlips.Count == cancelsBefore + 2 && lastSlip.Lines.Count == 1 && lastSlip.Lines[0].Name == "Νερό",
                "Δύο ακυρώσεις στο ίδιο δελτίο → κάθε φορά ακύρωση του τρέχοντος δελτίου και νέο, στο τέλος μένει μόνο το νερό");
            Check(j.OpenSlips.Count == 1, "Ένα ανοιχτό δελτίο (το τελευταίο), όχι τα ακυρωμένα");
        }

        Console.WriteLine("  — διόρθωση μετρητά ↔ κάρτα εκ των υστέρων");
        {
            var (j, inner, _, _) = New();
            j.Sale(601, Day, [Food(601, 0, "ΠΙΤΤΑ", 1, 7.00m)], FiscalPay.Cash);
            await j.ProcessAsync(inner);
            var first = inner.Issued.Last();
            j.ChangePayment(601, FiscalPay.Card);
            await j.ProcessAsync(inner);
            var docs = inner.Issued.TakeLast(2).ToList();
            Check(docs[0].Doc.Type == FiscalDocType.RetailCredit && docs[0].Doc.Pay == FiscalPay.Cash && docs[0].Doc.CorrelatedMarks.SequenceEqual([first.Result.Mark]),
                "Πιστωτικό της απόδειξης μετρητών…");
            Check(docs[1].Doc.Type == FiscalDocType.RetailReceipt && docs[1].Doc.Pay == FiscalPay.Card && docs[1].Doc.TerminalId == "T88011",
                "…και νέα απόδειξη κάρτας μέσα από το τερματικό (κάρτα με πάροχο = πάντα συναλλαγή τερματικού)");
            Check(Net(j, 601) == 7.00m, "Ο τζίρος μένει 7,00");

            // Τραπέζι: μία απόδειξη για δύο άτομα, και αλλάζει τρόπο μόνο το ένα.
            j.TableRound(602, Day, "4@1700", "Τραπέζι 4", [Food(602, 0, "Γύρος", 1, 8.00m)]);
            j.TableRound(603, Day, "4@1700", "Τραπέζι 4", [Food(603, 0, "Μπιφτέκι", 1, 9.00m)]);
            j.TablePayment(Day, "4@1700", "Τραπέζι 4", [Food(602, 0, "Γύρος", 1, 8.00m), Food(603, 0, "Μπιφτέκι", 1, 9.00m)], FiscalPay.Cash);
            await j.ProcessAsync(inner);
            j.ChangePayment(603, FiscalPay.Card);
            await j.ProcessAsync(inner);
            var split = inner.Issued.TakeLast(2).ToList();
            Check(split[0].Doc.Type == FiscalDocType.RetailCredit && split[0].Doc.Gross == 9.00m && split[1].Doc.Gross == 9.00m && split[1].Doc.Pay == FiscalPay.Card,
                "Μία απόδειξη για δύο άτομα, αλλάζει μόνο το ένα → πιστωτικό και νέα απόδειξη ΜΟΝΟ για το δικό του (9,00)");
            Check(Net(j, 602) == 8.00m && Net(j, 603) == 9.00m, "Και τα δύο άτομα μετράνε σωστά");
        }

        Console.WriteLine("  — 24ωρο δελτίων, ημερολόγιο στο αρχείο");
        {
            var (j, inner, _, _) = New();
            j.TableRound(701, Day, "2@1300", "Τραπέζι 2", [Food(701, 0, "Γύρος", 1, 8.00m)]);
            await j.ProcessAsync(inner);
            Later(TimeSpan.FromHours(21));
            Check(j.OverdueSlips.Count == 1, "Δελτίο ανοιχτό πάνω από 20 ώρες → φαίνεται πριν περάσει το 24ωρο");
            j.TablePayment(Day, "2@1300", "Τραπέζι 2", [Food(701, 0, "Γύρος", 1, 8.00m)], FiscalPay.Cash);
            await j.ProcessAsync(inner);
            Check(j.OverdueSlips.Count == 0, "Μόλις πληρωθεί, φεύγει");

            var path = Path.Combine(Path.GetTempPath(), "fiscalcheck-journal-" + Guid.NewGuid().ToString("N")[..8] + ".json");
            try
            {
                var file = new FiscalJournal(new FileFiscalJournalStore(path), "t1", () => _clock);
                file.Sale(702, Day, [Food(702, 0, "ΠΙΤΤΑ", 1, 7.00m)], FiscalPay.Cash);
                await file.ProcessAsync(inner);
                var again = new FiscalJournal(new FileFiscalJournalStore(path), "t1", () => _clock);
                Check(again.Entries.Count == 1 && again.Entries[0].Mark == file.Entries[0].Mark && again.Entries[0].Items[0].Revenue == 7.00m,
                    "Το ημερολόγιο γράφεται στο αρχείο και ξαναδιαβάζεται ίδιο");
                Later(TimeSpan.FromDays(11));
                again.Sale(703, Day.AddDays(11), [Food(703, 0, "ΠΙΤΤΑ", 1, 7.00m)], FiscalPay.Cash);
                Check(again.Entries.Count == 1 && again.Entries[0].Items[0].Order == 703, "Όσα κόπηκαν πριν από 10 μέρες σβήνονται· όσα περιμένουν, ποτέ");
            }
            finally { File.Delete(path); }
        }

        return _failures;
    }

    /// <summary>
    /// Τα ίδια σενάρια (μέσα από τον εγκέφαλο) σε όποιον πάροχο δοθεί — ο εικονικός ή ένας αληθινός με δοκιμαστικό κλειδί.
    /// Αυτό είναι το «συμβόλαιο»: κάθε νέος οδηγός παρόχου πρέπει να το περνά πριν μπει σε μαγαζί.
    /// </summary>
    public static async Task<int> ContractAsync(IFiscalProvider provider, string terminal)
    {
        _failures = 0;
        Console.WriteLine($"\nΣυμβόλαιο παρόχου μέσα από τον εγκέφαλο: «{provider.Name}»");
        var stamp = DateTime.Now.ToString("HHmmss");
        var j = new FiscalJournal(new MemoryFiscalJournalStore(), "chk" + stamp) { TerminalId = terminal };
        var day = DateTime.Today;

        async Task<bool> Done()
        {
            for (var i = 0; i < 20 && j.WaitingCount > 0; i++)
            {
                await j.ProcessAsync(provider);
                if (j.WaitingCount > 0) await Task.Delay(1000);
            }
            var problems = j.Entries.Where(e => e.State is not (FiscalEntryState.Issued or FiscalEntryState.Dropped)).ToList();
            foreach (var p in problems) Console.WriteLine($"      {p.Kind} {p.Id}: {p.State} — {p.LastError}");
            return problems.Count == 0;
        }

        j.Sale(1, day, [Food(1, 0, "ΠΙΤΤΑ ΧΟΙΡΙΝΟ", 1, 7.00m), Drink(1, 1, "Amstel 500ml", 1, 3.50m)], FiscalPay.Cash);
        Check(await Done(), "Όρθιος μετρητά → απόδειξη");

        j.TableRound(2, day, "5@" + stamp, "Τραπέζι 5", [Food(2, 0, "Χοιρινό μερίδα", 2, 21.00m)]);
        j.TableRound(3, day, "5@" + stamp, "Τραπέζι 5", [Drink(3, 0, "Coca-Cola 330ml", 2, 3.60m)]);
        Check(await Done(), "Τραπέζι: δύο γύροι → δύο δελτία στο ίδιο τραπέζι");
        j.TablePayment(day, "5@" + stamp, "Τραπέζι 5", [Unit(2, 0, 0, "Χοιρινό μερίδα", 10.50m)], FiscalPay.Cash);
        j.TablePayment(day, "5@" + stamp, "Τραπέζι 5", [Unit(2, 0, 1, "Χοιρινό μερίδα", 10.50m), Drink(3, 0, "Coca-Cola 330ml", 2, 3.60m)], FiscalPay.Cash);
        Check(await Done() && j.OpenSlips.Count == 0, "Δύο αποδείξεις (μία ανά άτομο) κλείνουν τα δελτία");

        j.TableRound(4, day, "7@" + stamp, "Τραπέζι 7", [Food(4, 0, "Σαλάτα", 1, 7.50m), Food(4, 1, "Πατάτες", 1, 3.50m)]);
        await Done();
        j.Cancel(4, [FiscalItem.KeyFor(4, 1)]);
        j.TablePayment(day, "7@" + stamp, "Τραπέζι 7", [Food(4, 0, "Σαλάτα", 1, 7.50m)], FiscalPay.Cash);
        Check(await Done() && j.OpenSlips.Count == 0, "Ακύρωση πιάτου σε δελτίο → ακύρωση + νέο δελτίο → πληρωμή");

        j.Sale(5, day, [Food(5, 0, "ΠΙΤΤΑ", 1, 7.00m)], FiscalPay.Cash);
        await Done();
        j.Cancel(5);
        Check(await Done() && j.Entries.Any(e => e.Kind == FiscalEntryKind.Credit && e.State == FiscalEntryState.Issued), "Ακύρωση απόδειξης → πιστωτικό");

        return _failures;
    }
}

/// <summary>Πάροχος που «χαλάει» κατά παραγγελία, πάνω από έναν αληθινό.</summary>
sealed class ChaosProvider(IFiscalProvider inner) : IFiscalProvider
{
    public bool Down { get; set; }
    public int LoseNext { get; set; }
    public bool DeclineCards { get; set; }
    public TaskCompletionSource? Gate { get; set; }

    public string Name => "χαλασμένος " + inner.Name;

    public async Task<FiscalResult> IssueAsync(FiscalDocument doc, CancellationToken ct = default)
    {
        if (Gate is { } gate) await gate.Task;
        if (Down) return FiscalResult.Fail("Δεν υπάρχει σύνδεση με τον πάροχο.");
        if (DeclineCards && doc.TerminalId.Length > 0) return FiscalResult.Fail("Η κάρτα απορρίφθηκε.");
        var result = await inner.IssueAsync(doc, ct);
        if (LoseNext > 0)
        {
            LoseNext--;
            return FiscalResult.Fail("Δεν ήρθε απάντηση.");
        }
        return result;
    }

    public Task<FiscalResult> CancelOrderSlipAsync(string mark, CancellationToken ct = default) =>
        Down ? Task.FromResult(FiscalResult.Fail("Δεν υπάρχει σύνδεση με τον πάροχο.")) : inner.CancelOrderSlipAsync(mark, ct);
}
