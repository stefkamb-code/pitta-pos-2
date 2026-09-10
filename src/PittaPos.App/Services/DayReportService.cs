using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>
/// Αναφορά κλεισίματος ημέρας: αναλυτικό κείμενο, τοπικό backup και αποστολή email.
/// </summary>
/// <summary>
/// Πού πήγαν τα λεφτά της ημέρας. Τα πέντε νούμερα αθροίζουν ΑΚΡΙΒΩΣ στον τζίρο — αν δεν βγαίνει,
/// φαίνεται αμέσως πόσο και πού λείπει (βλ. DayReportService.SplitMoney).
/// </summary>
/// <param name="Cash">Μετρητά: ΔΙΑΝΟΜΗ/BOX με τρόπο πληρωμής + οι εισπράξεις τραπεζιών σε μετρητά.</param>
/// <param name="Card">Κάρτα: το ίδιο, με κάρτα.</param>
/// <param name="Counter">ΟΡΘΙΟΣ: πληρώνεται μπροστά στο ταμείο και δεν καταγράφεται τρόπος.</param>
/// <param name="Platforms">e-food/Wolt: τα πληρώνει ο πελάτης μέσα στην εφαρμογή, δεν περνάει ευρώ από εδώ.</param>
/// <param name="Unsettled">Ό,τι απομένει — τραπέζια που έκλεισαν χωρίς να εξοφληθούν.</param>
public sealed record MoneySplit(decimal Cash, decimal Card, decimal Counter, decimal Platforms, decimal Unsettled);

/// <summary>
/// Ένα ανεξόφλητο κομμάτι της ημέρας: ποιο τραπέζι, τι ώρα χτυπήθηκε, πόσα έμειναν.
///
/// <para>Τρία χωριστά πεδία και όχι έτοιμη γραμμή, γιατί το ίδιο πράγμα εμφανίζεται σε δύο μορφές:
/// μία γραμμή κειμένου στο χαρτί/email (<see cref="Text"/>) και τρεις στήλες στο banner που σηκώνεται
/// μετά την αποστολή (βλ. SettingsWindow.SendReport_Click).</para>
/// </summary>
public sealed record UnsettledEntry(string Where, string When, decimal Amount)
{
    public string AmountLabel => Order.FormatPrice(Amount);
    public bool HasWhen => When.Length > 0;
    public string Text => HasWhen ? $"{Where} · {When} · {AmountLabel}" : $"{Where} · {AmountLabel}";
}

public static class DayReportService
{
    private static readonly CultureInfo Greek = CultureInfo.GetCultureInfo("el-GR");

    private static void AppendHeader(StringBuilder sb)
    {
        sb.AppendLine("ΠΙΤΤΑ ΤΟΥ ΠΑΠΠΟΥ — ΑΝΑΦΟΡΑ ΗΜΕΡΑΣ");
        sb.AppendLine(DateTime.Now.ToString("dddd d MMMM yyyy · HH:mm", Greek));
        sb.AppendLine(new string('=', 48));
        sb.AppendLine();
    }

    /// <summary>
    /// Ό,τι κάθεται ακόμα στην αναμονή του πίνακα ζωντανών χωρίς κανάλι. ΔΕΝ είναι μέσα στον τζίρο
    /// (βλ. SalesStatsService.CountedOrders) — τυπώνεται χωριστά ώστε τα λεφτά να μη λείπουν σιωπηλά
    /// από το χαρτί: ο ταμίας βλέπει αμέσως πόσα και ποιες παραγγελίες περιμένουν ακόμα διανομέα.
    /// Παραλείπεται εντελώς όταν δεν υπάρχει καμία, που είναι και το συνηθισμένο.
    /// </summary>
    private static void AppendAwaiting(StringBuilder sb, List<CompletedOrder> awaiting)
    {
        if (awaiting.Count == 0)
            return;
        AppendAwaitingTitle(sb);
        foreach (var o in awaiting.OrderBy(o => o.PlacedAt))
            sb.AppendLine($"  #{o.DisplayNumber} · {o.TimeLabel} · {o.TypeLabel}"
                + (o.Who.Length > 0 ? " · " + o.Who : "") + $"   {Order.FormatPrice(o.Total)}");
        sb.AppendLine($"  Σύνολο      : {Order.FormatPrice(awaiting.Sum(o => o.Total))}");
        sb.AppendLine();
    }

    /// <summary>Η ίδια πληροφορία για ΤΟ ΧΑΡΤΙ — μόνο πλήθος και σύνολο, χωρίς γραμμή ανά παραγγελία.
    /// Η γραμμή με όνομα πελάτη ξεπερνά εύκολα τους ~48 χαρακτήρες της αναφοράς, και το DayReportWindow
    /// μικραίνει τη γραμματοσειρά ΟΛΟΥ του χαρτιού για να χωρέσει την πιο μακριά γραμμή
    /// (βλ. DayReportWindow.FitTextToWidth) — δύο ξεχασμένες διανομές θα ζάρωναν ολόκληρη την αναφορά.</summary>
    private static void AppendAwaitingPrint(StringBuilder sb, List<CompletedOrder> awaiting)
    {
        if (awaiting.Count == 0)
            return;
        AppendAwaitingTitle(sb);
        sb.AppendLine($"  Παραγγελίες : {awaiting.Count}");
        sb.AppendLine($"  Σύνολο      : {Order.FormatPrice(awaiting.Sum(o => o.Total))}");
        sb.AppendLine();
    }

    private static void AppendAwaitingTitle(StringBuilder sb)
    {
        sb.AppendLine("ΣΕ ΑΝΑΜΟΝΗ — ΕΚΤΟΣ ΤΖΙΡΟΥ");
        sb.AppendLine("  (μπαίνουν μόλις περάσουν σε κανάλι)");
    }

    private static void AppendSummary(StringBuilder sb, List<CompletedOrder> orders)
    {
        var revenue = orders.Sum(o => o.Total);
        var items = orders.Sum(o => o.ItemCount);
        sb.AppendLine("ΣΥΝΟΨΗ");
        sb.AppendLine($"  Παραγγελίες : {orders.Count}");
        sb.AppendLine($"  Τζίρος      : {Order.FormatPrice(revenue)}");
        AppendMoneySplit(sb, orders);
        sb.AppendLine($"  Τεμάχια     : {items}");
        sb.AppendLine($"  Μέση αξία   : {Order.FormatPrice(orders.Count == 0 ? 0 : revenue / orders.Count)}");
        sb.AppendLine();
    }

    /// <summary>Λιτή σύνοψη μόνο για την εκτύπωση στο χαρτί — τζίρος πρώτα, από κάτω μετρητά/κάρτα (μόνο
    /// ΔΙΑΝΟΜΗ/BOX έχουν καταγεγραμμένο τρόπο πληρωμής, βλ. CompletedOrder.PaymentMethod· παραλείπεται αν
    /// δεν υπάρχει καμία τέτοια παραγγελία), μετά σύνολο παραγγελιών· χωρίς τεμάχια/μέση αξία (βλ.
    /// BuildPrintSummary· η πλήρης σύνοψη μένει στο email/backup).</summary>
    private static void AppendPrintSummary(StringBuilder sb, List<CompletedOrder> orders)
    {
        var revenue = orders.Sum(o => o.Total);
        sb.AppendLine("ΣΥΝΟΨΗ");
        sb.AppendLine($"  Τζίρος      : {Order.FormatPrice(revenue)}");

        AppendMoneySplit(sb, orders);

        sb.AppendLine($"  Παραγγελίες : {orders.Count}");
        sb.AppendLine();
    }

    /// <summary>
    /// Πού πήγαν τα λεφτά του τζίρου — μετρητά, κάρτα, όρθιος, εφαρμογές, ανεξόφλητα. Μπαίνει και στο
    /// χαρτί και στο email: ο διαχωρισμός είναι το πρώτο πράγμα που κοιτάει κανείς και δεν έχει νόημα
    /// να τον βλέπει μόνο όποιος στέκεται μπροστά στον εκτυπωτή. Ζούσε μέσα στο AppendPrintSummary.
    /// </summary>
    private static void AppendMoneySplit(StringBuilder sb, List<CompletedOrder> orders)
    {
        var revenue = orders.Sum(o => o.Total);
        var split = SplitMoney(orders);
        var (cash, card, platforms, counter, unsettled) =
            (split.Cash, split.Card, split.Platforms, split.Counter, split.Unsettled);

        if (revenue > 0)
        {
            sb.AppendLine($"    Μετρητά   : {Order.FormatPrice(cash)}");
            sb.AppendLine($"    Κάρτα     : {Order.FormatPrice(card)}");
            if (counter != 0)
                sb.AppendLine($"    Όρθιος    : {Order.FormatPrice(counter)}");
            if (platforms != 0)
                sb.AppendLine($"    Εφαρμογές : {Order.FormatPrice(platforms)}");
            if (unsettled != 0)
            {
                sb.AppendLine($"    Ανεξόφλητα: {Order.FormatPrice(unsettled)}");
                foreach (var line in UnsettledBreakdown(orders, unsettled))
                    sb.AppendLine("      " + line.Text);
            }
        }
    }

    /// <summary>
    /// Ποιο τραπέζι έμεινε ανεξόφλητο και τι ώρα χτυπήθηκε — οι γραμμές που μπαίνουν από κάτω από τα
    /// «Ανεξόφλητα»: «ΤΡΑΠΕΖΙ 4 · 20:41 · €30,50».
    ///
    /// <para>Χωρίς αυτές, το νούμερο έλεγε μόνο ΟΤΙ λείπουν λεφτά και ο ταμίας έπρεπε να ψάξει μόνος του
    /// στο Ιστορικό ποιο τραπέζι δεν εξοφλήθηκε. Το ποσό είναι πάντα το <b>υπόλοιπο του τραπεζιού</b>
    /// (παραγγελίες μείον εισπράξεις), όχι το σύνολό του: σε τραπέζι όπου πλήρωσαν τρεις στους
    /// τέσσερις φαίνονται μόνο τα λεφτά του τέταρτου.</para>
    ///
    /// <para>Η ώρα είναι της παραγγελίας που έμεινε απλήρωτη — εύρος («20:41–21:24») όταν είναι
    /// περισσότερες από μία, γιατί σε ένα τραπέζι κάθε άτομο χτυπιέται χωριστά.</para>
    /// </summary>
    private static List<UnsettledEntry> UnsettledBreakdown(List<CompletedOrder> orders, decimal unsettled)
    {
        var lines = new List<UnsettledEntry>();
        var payments = TablePaymentsService.Instance;

        // ΤΡΑΠΕΖΙΑ — μία γραμμή ανά τραπέζι, όχι ανά άτομο: το τραπέζι είναι που «έμεινε», και τρεις
        // σχεδόν ταυτόχρονες γραμμές για την ίδια παρέα δεν λένε κάτι παραπάνω.
        foreach (var table in orders.Where(o => o.Type == OrderType.Table)
                     .GroupBy(o => o.TableNumberLabel)
                     .OrderBy(g => g.Min(o => o.PlacedAt)))
        {
            var number = int.TryParse(table.Key, out var t) ? t : 0;
            // Οι εισπράξεις μετριούνται ανά ΤΡΑΠΕΖΙ και όχι ανά παραγγελία, ώστε να πιάνονται και οι
            // παλιές εγγραφές που δεν κρατούσαν αριθμό παραγγελίας (βλ. TablePayment.OrderNumber).
            var owed = table.Sum(o => o.Total) - (number > 0 ? payments.TotalForTable(number) : 0m);
            if (owed < 0.005m)
                continue;

            // Ποιανού ατόμου η ώρα: εκείνων που δεν έχουν είσπραξη. Αν δεν ξεχωρίζει κανένα (παλιές
            // εγγραφές χωρίς αριθμό παραγγελίας), δείχνουμε το εύρος όλου του τραπεζιού.
            var unpaid = table.Where(o => payments.AmountFor(o.OrderNumber) < o.Total - 0.005m).ToList();
            if (unpaid.Count == 0)
                unpaid = table.ToList();

            var where = number > 0 ? $"ΤΡΑΠΕΖΙ {number}" : "ΤΡΑΠΕΖΙ";
            lines.Add(new UnsettledEntry(where, TimeRangeLabel(unpaid), owed));
        }

        // ΔΙΑΝΟΜΗ/BOX χωρίς τρόπο πληρωμής — δεν συμβαίνει με τη ροή του ταμείου (εκεί ζητιέται
        // υποχρεωτικά), αλλά υπάρχουν παλιές παραγγελίες από πριν μπει ο κανόνας. Χωρίς αυτές, η λίστα
        // θα έδειχνε λιγότερα από το νούμερο των «Ανεξόφλητων».
        foreach (var o in orders
                     .Where(o => o.PaymentMethod is null
                                 && (o.Type == OrderType.Delivery || (o.Type == OrderType.Apps && o.Channel == "BOX")))
                     .OrderBy(o => o.PlacedAt))
            lines.Add(new UnsettledEntry($"{o.TypeLabel} #{o.DisplayNumber}", o.TimeLabel, o.Total));

        // Ό,τι δεν καταφέραμε να χρεώσουμε σε συγκεκριμένο τραπέζι/παραγγελία. Δεν παραλείπεται: το
        // νόημα του «Ανεξόφλητα» είναι ότι τα νούμερα αθροίζουν ΑΚΡΙΒΩΣ, οπότε ούτε η λίστα από κάτω
        // επιτρέπεται να βγάζει λιγότερα από τη γραμμή που εξηγεί.
        var rest = unsettled - lines.Sum(l => l.Amount);
        if (Math.Abs(rest) >= 0.01m)
            lines.Add(new UnsettledEntry("ΥΠΟΛΟΙΠΟ", "", rest));

        return lines;
    }

    /// <summary>
    /// Τα ανεξόφλητα της μέρας όπως ακριβώς μπαίνουν στην αναφορά — για το banner που σηκώνεται μόλις
    /// σταλεί το email (βλ. SettingsWindow.SendReport_Click). Κενή λίστα όταν δεν υπάρχει τίποτα, που
    /// είναι και το συνηθισμένο: τότε δεν εμφανίζεται τίποτα απολύτως.
    /// </summary>
    public static (decimal Total, IReadOnlyList<UnsettledEntry> Entries) UnsettledToday()
    {
        // Ίδια πηγή με το Build/BuildPrintSummary — αλλιώς το banner θα μπορούσε να λέει άλλα από την
        // αναφορά που μόλις έφυγε.
        var orders = SalesStatsService.Instance.CountedOrders.OrderBy(o => o.PlacedAt).ToList();
        var unsettled = SplitMoney(orders).Unsettled;
        return unsettled == 0
            ? (0m, [])
            : (unsettled, UnsettledBreakdown(orders, unsettled));
    }

    /// <summary>«20:41» για μία παραγγελία, «20:41–21:24» για περισσότερες — η ώρα που χτυπήθηκε.</summary>
    private static string TimeRangeLabel(List<CompletedOrder> orders)
    {
        var first = orders.Min(o => o.PlacedAt).ToString("HH:mm");
        var last = orders.Max(o => o.PlacedAt).ToString("HH:mm");
        return first == last ? first : $"{first}–{last}";
    }

    /// <summary>Πού πήγαν τα λεφτά — τα ίδια ακριβώς νούμερα με το χαρτί, ώστε αναφορά και ΣΤΑΤΙΣΤΙΚΑ
    /// να μη λένε ΠΟΤΕ διαφορετικά πράγματα (η οθόνη τα δείχνει ζωντανά, βλ. StatsViewModel).</summary>
    public static MoneySplit SplitMoney(IReadOnlyList<CompletedOrder> orders)
    {
        var revenue = orders.Sum(o => o.Total);
        // ΔΙΑΝΟΜΗ/BOX κρατούν τον τρόπο πληρωμής πάνω στην παραγγελία· τα ΤΡΑΠΕΖΙΑ πληρώνονται τμηματικά
        // (ο καθένας τα δικά του, με διαφορετικό τρόπο ο καθένας), οπότε καταγράφονται ξεχωριστά ανά
        // είσπραξη — βλ. TablePaymentsService. Εδώ αθροίζονται και τα δύο στον ίδιο διαχωρισμό.
        // Τα ΤΡΑΠΕΖΙΑ εξαιρούνται ρητά από το πρώτο άθροισμα: μετρώνται ΜΟΝΟ από τις εισπράξεις τους,
        // αλλιώς μια παραγγελία τραπεζιού με τρόπο πληρωμής πάνω της θα μετρούσε δύο φορές.
        var tracked = orders.Where(o => o.PaymentMethod is not null && o.Type != OrderType.Table).ToList();
        var tables = TablePaymentsService.Instance;
        var cash = tracked.Where(o => o.PaymentMethod == Core.Models.PaymentMethod.Cash).Sum(o => o.Total)
            + tables.TotalFor(Core.Models.PaymentMethod.Cash);
        var card = tracked.Where(o => o.PaymentMethod == Core.Models.PaymentMethod.Card).Sum(o => o.Total)
            + tables.TotalFor(Core.Models.PaymentMethod.Card);
        // Οι πλατφόρμες πληρώνονται ΜΕΣΑ στην εφαρμογή (Wolt/e-food) — δεν περνάει ευρώ από το ταμείο,
        // οπότε δεν είναι ούτε μετρητά ούτε κάρτα. Το BOX εξαιρείται: το παραδίδει δικός μας διανομέας
        // και έχει κανονικό τρόπο πληρωμής, άρα μετρήθηκε ήδη παραπάνω.
        var platforms = orders
            .Where(o => o.Type == OrderType.Apps && o.Channel != "BOX")
            .Sum(o => o.Total);

        // Ό,τι απομένει: τζίρος που ΔΕΝ αντιστοιχεί σε καμία είσπραξη. Στην πράξη είναι τραπέζια που
        // έκλεισαν χωρίς να εξοφληθούν από την οθόνη τραπεζιού (το κλείσιμο ημέρας τα ελευθερώνει
        // χωρίς να τα χρεώσει σε τρόπο πληρωμής).
        //
        // ΓΙΑΤΙ ΤΥΠΩΝΕΤΑΙ: πριν, το χαρτί έδειχνε «Τζίρος 100» και από κάτω «Μετρητά 60 / Κάρτα 20»
        // χωρίς λέξη για τα υπόλοιπα 20 — ο ταμίας έβλεπε ότι δεν βγαίνει και δεν είχε πουθενά να
        // ψάξει. Τώρα τα τέσσερα νούμερα αθροίζουν ΑΚΡΙΒΩΣ στον τζίρο, οπότε ή βγαίνει με τη μία ή
        // φαίνεται αμέσως πόσο και πού λείπει.
        // ΟΡΘΙΟΣ: δεν καταγράφεται πια τρόπος πληρωμής (ζητήθηκε — ο πελάτης πληρώνει μπροστά στο
        // ταμείο εκείνη τη στιγμή). Τα λεφτά του δεν επιτρέπεται ούτε να μπουν αυθαίρετα στα μετρητά
        // ούτε να εμφανιστούν «ανεξόφλητα», που σημαίνει κάτι εντελώς άλλο: παίρνουν δική τους γραμμή
        // ώστε τα νούμερα να συνεχίζουν να αθροίζουν ΑΚΡΙΒΩΣ στον τζίρο. (Παλιές παραγγελίες ΟΡΘΙΟΥ με
        // καταγεγραμμένο τρόπο μετράνε κανονικά στα μετρητά/κάρτα και δεν ξαναμετριούνται εδώ.)
        var counter = orders
            .Where(o => o.Type == OrderType.Pickup && o.PaymentMethod is null)
            .Sum(o => o.Total);

        var unsettled = revenue - cash - card - platforms - counter;
        return new MoneySplit(cash, card, counter, platforms, unsettled);
    }

    private static void AppendPerChannel(StringBuilder sb, List<CompletedOrder> orders)
    {
        sb.AppendLine("ΑΝΑ ΚΑΝΑΛΙ");
        foreach (var g in orders.GroupBy(o => o.TypeLabel).OrderByDescending(g => g.Sum(o => o.Total)))
            sb.AppendLine($"  {g.Key,-14} {g.Count(),3} παρ. · {g.Sum(o => o.ItemCount),4} τεμ. · {Order.FormatPrice(g.Sum(o => o.Total))}");
        sb.AppendLine();
    }

    /// <summary>Ανά κανάλι μόνο για την εκτύπωση — αριθμός παραγγελιών + τζίρος, χωρίς τεμάχια (η πλήρης
    /// εκδοχή με τεμάχια, AppendPerChannel, μένει στο email/backup). Χωρισμένο σε δύο ευδιάκριτα «κουτιά»:
    /// ΔΙΚΑ ΜΑΣ (Διανομέας/ΤΡΑΠΕΖΙ/ΠΑΡΑΛΑΒΗ) και ΕΦΑΡΜΟΓΕΣ (Wolt/e-food/BOX) — πιο εύκολο να διαβαστεί
    /// γρήγορα στο χαρτί παρά μία ενιαία λίστα με όλα μαζί.</summary>
    private static void AppendPrintPerChannel(StringBuilder sb, List<CompletedOrder> orders)
    {
        sb.AppendLine("ΑΝΑ ΚΑΝΑΛΙ");
        sb.AppendLine();

        void AppendBox(string title, IEnumerable<CompletedOrder> boxOrders)
        {
            var list = boxOrders.ToList();
            if (list.Count == 0)
                return;
            sb.AppendLine(title);
            sb.AppendLine(new string('-', 40));
            foreach (var g in list.GroupBy(o => o.TypeLabel).OrderByDescending(g => g.Sum(o => o.Total)))
                sb.AppendLine($"  {g.Key,-14} {g.Count(),3} παρ. · {Order.FormatPrice(g.Sum(o => o.Total))}");
            sb.AppendLine();
        }

        AppendBox("ΔΙΚΑ ΜΑΣ", orders.Where(o => o.Type != Core.Models.OrderType.Apps));
        AppendBox("ΕΦΑΡΜΟΓΕΣ", orders.Where(o => o.Type == Core.Models.OrderType.Apps));
    }

    /// <summary>
    /// Πόση α' ύλη έφυγε — «σήμερα πουλήθηκαν 12,40 κιλά κοτόπουλο». Βγαίνει από τα γραμμάρια που έχει
    /// δηλωμένα κάθε προϊόν (βλ. <see cref="ConsumptionService"/>) και <b>λείπει εντελώς</b> όσο δεν έχει
    /// δηλωθεί τίποτα — καλύτερα να μη γράφεται καθόλου παρά να τυπώνεται ένα άδειο μπλοκ.
    /// </summary>
    private static void AppendConsumption(StringBuilder sb)
    {
        var totals = ConsumptionService.Today();
        if (totals.Count == 0)
            return;

        sb.AppendLine("ΚΑΤΑΝΑΛΩΣΗ");
        foreach (var t in totals)
            sb.AppendLine($"  {t.Name,-20} {t.AmountLabel,12}");
        sb.AppendLine();
    }

    /// <summary>Τι πουλήθηκε, <b>μόνο ανά κατηγορία</b> καταλόγου: «ΠΙΤΤΕΣ  148 τεμ.». Ζητήθηκε ρητά
    /// στη θέση της παλιάς λίστας ανά προϊόν — 124 γραμμές που κανείς δεν διαβάζει σε ένα email, ενώ οι
    /// δεκαπέντε γραμμές των κατηγοριών λένε αμέσως πού πήγε η μέρα. Τα νούμερα βγαίνουν από το
    /// <see cref="SalesBreakdown"/>, την ίδια πηγή με την οθόνη Στατιστικών, ώστε να μη λέει άλλα το
    /// email και άλλα η οθόνη.</summary>
    private static void AppendPerCategory(StringBuilder sb, List<CompletedOrder> orders)
    {
        sb.AppendLine("ΑΝΑ ΚΑΤΗΓΟΡΙΑ");
        foreach (var c in SalesBreakdown.ByCategory(orders))
            sb.AppendLine($"  {c.Name,-22} {c.Quantity,4} τεμ. · {Order.FormatPrice(c.Revenue)}");
        sb.AppendLine();
    }

    /// <summary>
    /// Ποιος ακύρωσε και τι — μία γραμμή ανά υπάλληλο, και από κάτω σπασμένο ανά κανάλι
    /// («Κώστας: 3 × e-food · 2 × ΟΡΘΙΟΣ»). Το όνομα το δίνει ο ονομαστικός κωδικός που ζητιέται σε κάθε
    /// ακύρωση (βλ. StaffPinDialog.RequireName), το κανάλι γράφεται τη στιγμή της ακύρωσης
    /// (<see cref="CancelledLine.Channel"/>) γιατί μετά η παραγγελία δεν υπάρχει πουθενά να ρωτηθεί.
    ///
    /// <para>Λείπει ολόκληρο το μπλοκ όταν δεν ακυρώθηκε τίποτα — όπως και η ΚΑΤΑΝΑΛΩΣΗ, καλύτερα καμία
    /// γραμμή παρά ένας άδειος τίτλος.</para>
    /// </summary>
    private static void AppendCancellations(StringBuilder sb, HashSet<DateTime> days)
    {
        // Φίλτρο ημέρας: το log καθαρίζει στο κλείσιμο (CloseDay), αλλά μια παλιά μέρα που
        // αρχειοθετήθηκε μέσα στη βάρδια αφήνει για λίγο και τις δικές της εγγραφές — η αναφορά
        // μιλάει ΜΟΝΟ για τη μέρα των παραγγελιών της (βλ. ReportDays).
        var cancelled = CancellationLogService.Instance.Entries
            .Where(c => days.Contains(SalesStatsService.BusinessDay(c.CancelledAt)))
            .ToList();
        if (cancelled.Count == 0)
            return;

        sb.AppendLine("ΑΚΥΡΩΣΕΙΣ ΑΝΑ ΥΠΑΛΛΗΛΟ");
        foreach (var staff in cancelled.GroupBy(c => c.CancelledByLabel).OrderByDescending(g => g.Sum(c => c.Revenue)))
        {
            sb.AppendLine($"  {staff.Key,-22} {staff.Count(),3} ακυρ. · {staff.Sum(c => c.Quantity),4} τεμ. · {Order.FormatPrice(staff.Sum(c => c.Revenue))}");
            var channels = staff.GroupBy(c => c.ChannelLabel)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} × {g.Key}");
            sb.AppendLine("      " + string.Join(" · ", channels));
        }
        // Σύνολο μόνο όταν ακύρωσαν πολλοί: με έναν υπάλληλο η γραμμή θα έλεγε δεύτερη φορά τα ίδια νούμερα.
        if (cancelled.Select(c => c.CancelledByLabel).Distinct().Count() > 1)
            sb.AppendLine($"  ΣΥΝΟΛΟ                 {cancelled.Count,3} ακυρ. · {cancelled.Sum(c => c.Quantity),4} τεμ. · {Order.FormatPrice(cancelled.Sum(c => c.Revenue))}");
        sb.AppendLine();
    }

    /// <summary>
    /// Ποιος άλλαξε τρόπο πληρωμής ή κανάλι από το Ιστορικό — ίδια μορφή με τις ακυρώσεις: μία γραμμή
    /// ανά υπάλληλο, και από κάτω οι αλλαγές του («21:05 · #12 ΔΙΑΝΟΜΗ · Μετρητά → Κάρτα · €15,50»).
    ///
    /// <para>Εδώ γράφεται η κάθε αλλαγή και όχι μόνο πλήθος, όπως στις ακυρώσεις: είναι λίγες, και η
    /// καθεμιά μετακινεί λεφτά (από το συρτάρι στην κάρτα, ή έξω από τα μετρητά/κάρτα) — όποιος διαβάζει
    /// την αναφορά θέλει να ξέρει ΠΟΙΑ παραγγελία να κοιτάξει. Το όνομα το δίνει ο προσωπικός κωδικός
    /// που ζητιέται σε κάθε αλλαγή (βλ. HistoryViewModel.AskStaffName).</para>
    ///
    /// <para>Λείπει ολόκληρο όταν δεν άλλαξε τίποτα.</para>
    /// </summary>
    private static void AppendChanges(StringBuilder sb, HashSet<DateTime> days)
    {
        var changes = OrderChangeLogService.Instance.Entries
            .Where(c => days.Contains(SalesStatsService.BusinessDay(c.ChangedAt)))
            .ToList();
        if (changes.Count == 0)
            return;

        static string Word(int n) => n == 1 ? "αλλαγή" : "αλλαγές";

        sb.AppendLine("ΑΛΛΑΓΕΣ ΑΝΑ ΥΠΑΛΛΗΛΟ");
        sb.AppendLine("  (τρόπος πληρωμής ή κανάλι, από το Ιστορικό)");
        foreach (var staff in changes.GroupBy(c => c.ChangedByLabel).OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"  {staff.Key,-22} {staff.Count(),3} {Word(staff.Count())} · {Order.FormatPrice(staff.Sum(c => c.Amount))}");
            foreach (var change in staff.OrderBy(c => c.ChangedAt))
                sb.AppendLine("      " + change.Text);
        }
        if (changes.Select(c => c.ChangedByLabel).Distinct().Count() > 1)
            sb.AppendLine($"  {"ΣΥΝΟΛΟ",-22} {changes.Count,3} {Word(changes.Count)} · {Order.FormatPrice(changes.Sum(c => c.Amount))}");
        sb.AppendLine();
    }

    /// <summary>
    /// Ποιες μέρες καλύπτει η αναφορά: οι μέρες των παραγγελιών της — μέσα στη βάρδια, το σήμερα.
    /// <para>Όχι σκέτο «σήμερα»: στο αυτόματο κλείσιμο των 5 το πρωί (βλ. CloseDay) οι παραγγελίες είναι
    /// της ΧΘΕΣΙΝΗΣ, και με φίλτρο «σήμερα» η αναφορά που σώζεται τότε έβγαινε χωρίς ακυρώσεις.</para>
    /// </summary>
    private static HashSet<DateTime> ReportDays(List<CompletedOrder> orders)
    {
        var days = orders.Select(o => SalesStatsService.BusinessDay(o.PlacedAt)).ToHashSet();
        if (days.Count == 0)
            days.Add(SalesStatsService.BusinessDay(DateTime.Now));
        return days;
    }

    /// <summary>Η αναφορά της ημέρας για email και τοπικό backup: ΟΛΑ τα στατιστικά — σύνοψη με τον
    /// διαχωρισμό των χρημάτων, ανά κανάλι, ανά κατηγορία, κατανάλωση α' ύλης, ακυρώσεις και αλλαγές
    /// (πληρωμής/καναλιού από το Ιστορικό) ανά υπάλληλο. Καμία λίστα ανά προϊόν και καμία ανά παραγγελία: ζητήθηκε ρητά να μη γεμίζει το email
    /// με εκατοντάδες γραμμές που δεν διαβάζει κανείς.</summary>
    public static string Build()
    {
        // Ο τζίρος και οι αναλύσεις του μετράνε ΜΟΝΟ ό,τι έχει περάσει σε κανάλι — όσες περιμένουν
        // ακόμα στην αναμονή βγαίνουν παρακάτω σε δικό τους μπλοκ (βλ. AppendAwaiting), ώστε όλα τα
        // νούμερα της αναφοράς να αθροίζουν μεταξύ τους και να μη χάνεται τίποτα.
        var orders = SalesStatsService.Instance.CountedOrders
            .OrderBy(o => o.PlacedAt)
            .ToList();
        var awaiting = SalesStatsService.Instance.AwaitingChannelOrders;

        var sb = new StringBuilder();
        AppendHeader(sb);
        AppendSummary(sb, orders);
        AppendAwaiting(sb, awaiting);
        AppendPerChannel(sb, orders);
        AppendPerCategory(sb, orders);
        AppendConsumption(sb);
        var days = ReportDays(orders);
        AppendCancellations(sb, days);
        AppendChanges(sb, days);

        // ΔΕΝ ακολουθεί λίστα με τις παραγγελίες μία-μία. Η αναφορά είναι ΤΑ ΣΤΑΤΙΣΤΙΚΑ της μέρας:
        // 137 παραγγελίες × τις γραμμές τους είναι χίλιες σειρές που δεν διαβάζει κανείς σε email, και
        // ό,τι χρειαστεί ποτέ ψάξιμο ανά παραγγελία υπάρχει ούτως ή άλλως στο Ιστορικό του ταμείου.
        return sb.ToString();
    }

    /// <summary>Σύντομη αναφορά ημέρας μόνο με τζίρους (σύνοψη + ανά κανάλι, χωρίς ανά προϊόν/ανά
    /// παραγγελία) — αυτή τυπώνεται στον θερμικό εκτυπωτή· η πλήρης αναλυτική (Build) πάει σε email/backup.</summary>
    public static string BuildPrintSummary()
    {
        // Ίδιος κανόνας με το Build: τζίρος = μόνο όσες πέρασαν σε κανάλι.
        var orders = SalesStatsService.Instance.CountedOrders
            .OrderBy(o => o.PlacedAt)
            .ToList();

        var sb = new StringBuilder();
        AppendHeader(sb);
        AppendPrintSummary(sb, orders);
        AppendAwaitingPrint(sb, SalesStatsService.Instance.AwaitingChannelOrders);
        AppendPrintPerChannel(sb, orders);
        AppendConsumption(sb);
        return sb.ToString();
    }

    /// <summary>Τυπώνει την αναφορά μέχρι τώρα, χωρίς καμία άλλη ενέργεια — καμία αποθήκευση/email/
    /// αρχειοθέτηση, ΔΕΝ μηδενίζει τίποτα. Για να βλέπει ο ταμίας πού βρίσκεται μέσα στη μέρα, όσες φορές
    /// θέλει· το πραγματικό κλείσιμο/μηδενισμός γίνεται μόνο αυτόματα στις 5πμ, βλ. CloseDay παρακάτω.</summary>
    public static void PrintCurrentReport() => ReceiptPrinter.PrintDayReport(BuildPrintSummary());

    /// <summary>Αναφορά + τοπικό backup + email + μηδενισμός ταμείου — καλείται ΜΟΝΟ αυτόματα όταν
    /// περάσει η ώρα-όριο ημέρας (5πμ, βλ. SalesStatsService.CheckAutoClose). Ο ταμίας δεν την καλεί πια
    /// χειροκίνητα — το κουμπί στις Ρυθμίσεις τυπώνει μόνο (PrintCurrentReport), δεν μηδενίζει.</summary>
    public static void CloseDay()
    {
        if (SalesStatsService.Instance.Orders.Count == 0 && OrderBoardService.Instance.Orders.Count == 0)
            return;

        // Αν υπάρχουν ΚΑΙ σημερινές παραγγελίες, τότε δεν είναι κανονικό κλείσιμο 5πμ: κάποια παλιά
        // παραγγελία εμφανίστηκε καθυστερημένα μέσα στη βάρδια (π.χ. έφτασε από την ουρά αναμονής του
        // δεύτερου ταμείου αφού επανήλθε το δίκτυο). Ένα πλήρες κλείσιμο εδώ θα μηδένιζε τη ΣΗΜΕΡΙΝΗ
        // μέρα στη μέση της βάρδιας — θα έσβηνε τον πίνακα, θα έκλεινε τραπέζια και θα έστελνε λάθος
        // αναφορά. Αρχειοθετούμε και αφαιρούμε μόνο τις παλιές, και συνεχίζει κανονικά η μέρα.
        var today = SalesStatsService.BusinessDay(DateTime.Now);
        var hasToday = SalesStatsService.Instance.Orders.Any(o => SalesStatsService.BusinessDay(o.PlacedAt) == today)
            || OrderBoardService.Instance.Orders.Any(o => SalesStatsService.BusinessDay(o.PlacedAt) == today);
        if (hasToday)
        {
            ArchiveStaleDaysOnly(today);
            return;
        }

        var report = Build();
        try
        {
            SaveToDisk(report);
        }
        catch (Exception)
        {
            return; // αν δεν σώθηκε το backup, μην καθαρίσεις
        }

        // Αρχειοθέτηση ανά ημέρα-επιχείρησης (συνήθως μία, εκτός αν το αυτόματο κλείσιμο βρήκε μπερδεμένες μέρες)
        // ΠΡΙΝ καθαρίσουν τα τρέχοντα στατιστικά, ώστε το Ιστορικό να μπορεί αργότερα να δείξει παλιότερες μέρες.
        foreach (var group in SalesStatsService.Instance.Orders.GroupBy(o => SalesStatsService.BusinessDay(o.PlacedAt)))
        {
            var cancellations = CancellationLogService.Instance.Entries
                .Where(c => SalesStatsService.BusinessDay(c.CancelledAt) == group.Key)
                .ToList();
            HistoryArchiveService.ArchiveDay(group.Key, group.ToList(), cancellations);
        }

        // ΚΑΝΕΝΑ EMAIL ΕΔΩ — ζητήθηκε ρητά. Η αναφορά φεύγει ΜΟΝΟ με το κουμπί «ΑΠΟΣΤΟΛΗ ΑΝΑΦΟΡΑΣ
        // ΣΤΟ EMAIL» (Ρυθμίσεις → ΤΑΜΕΙΟ), όταν το πατήσει ο χρήστης και βλέπει το αποτέλεσμα. Το
        // αυτόματο κλείσιμο συμβαίνει στις 5 το πρωί ή στο επόμενο άνοιγμα, ώρες που δεν κοιτάει
        // κανείς — ένα email που φεύγει μόνο του τότε δεν το περιμένει κανείς. Το χαρτί και το τοπικό
        // αντίγραφο (SaveToDisk παραπάνω) βγαίνουν κανονικά, οπότε τίποτα δεν χάνεται.
        ReceiptPrinter.PrintDayReport(BuildPrintSummary());
        SalesStatsService.Instance.Clear();
        CancellationLogService.Instance.Clear();
        OrderChangeLogService.Instance.Clear();
        TablePaymentsService.Instance.Clear();
        TableStatusService.Instance.CloseAll();
        OrderBoardService.Instance.Clear();
    }

    /// <summary>
    /// Αρχειοθετεί και αφαιρεί ΜΟΝΟ τις παραγγελίες παλιότερης ημέρας, αφήνοντας άθικτη τη μέρα που
    /// είναι σε εξέλιξη. Χωρίς email/εκτύπωση/μηδενισμό: εκείνη η μέρα έχει ήδη κλείσει κανονικά κάποια
    /// στιγμή στο παρελθόν — εδώ απλώς προσαρτάται μια καθυστερημένη παραγγελία στο αρχείο της
    /// (το ArchiveDay συγχωνεύει, δεν αντικαθιστά). Καταγράφεται, γιατί σημαίνει ότι κάτι είχε μείνει
    /// στην ουρά για ώρες.
    /// </summary>
    private static void ArchiveStaleDaysOnly(DateTime today)
    {
        var stale = SalesStatsService.Instance.RemoveOtherBusinessDays(today);
        OrderBoardService.Instance.RemoveOtherBusinessDays(today);
        if (stale.Count == 0)
            return;

        foreach (var group in stale.GroupBy(o => SalesStatsService.BusinessDay(o.PlacedAt)))
        {
            var cancellations = CancellationLogService.Instance.Entries
                .Where(c => SalesStatsService.BusinessDay(c.CancelledAt) == group.Key)
                .ToList();
            HistoryArchiveService.ArchiveDay(group.Key, group.ToList(), cancellations);
            // Οι παραγγελίες της παλιάς μέρας μόλις αφαιρέθηκαν από τα ζωντανά (RemoveOtherBusinessDays)·
            // το ίδιο πρέπει να γίνει και με τις ακυρώσεις της, αλλιώς μένουν και στις δύο πηγές που
            // ενώνει το Ιστορικό και εμφανίζονται διπλές.
            CancellationLogService.Instance.RemoveForDay(group.Key);
            OrderChangeLogService.Instance.RemoveForDay(group.Key);
            AppLog.Write("close-day",
                $"Καθυστερημένες παραγγελίες ημέρας {group.Key:yyyy-MM-dd} ({group.Count()}) αρχειοθετήθηκαν " +
                "χωρίς να κλείσει η τρέχουσα βάρδια.");
        }
    }

    /// <summary>Ο φάκελος με τις αποθηκευμένες αναφορές.</summary>
    private static string ReportsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder, "reports");

    /// <summary>Αποθηκεύει την αναφορά τοπικά (backup) και επιστρέφει τη διαδρομή.</summary>
    public static string SaveToDisk(string report)
    {
        Directory.CreateDirectory(ReportsDir);
        var path = Path.Combine(ReportsDir, "anafora-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".txt");
        File.WriteAllText(path, report, new UTF8Encoding(true));
        return path;
    }

    /// <summary>
    /// Στέλνει την αναφορά ΤΩΡΑ και, αν <b>δεν φύγει</b>, την κρατάει για να ξαναδοκιμάσει μόνη της.
    /// Καλείται μόνο από το κουμπί «ΑΠΟΣΤΟΛΗ ΑΝΑΦΟΡΑΣ ΣΤΟ EMAIL» — το κλείσιμο ημέρας δεν στέλνει
    /// τίποτα (βλ. CloseDay).
    ///
    /// <para>Η αναμονή υπάρχει για το πεσμένο internet της στιγμής: ο χρήστης βλέπει αμέσως «δεν
    /// στάλθηκε», αλλά δεν χρειάζεται να θυμηθεί να ξαναπατήσει — φεύγει μόνη της μόλις επανέλθει η
    /// γραμμή. <b>Κρατιέται ΜΙΑ μόνο αναφορά σε αναμονή</b>: αν πατηθεί το κουμπί τρεις φορές χωρίς
    /// δίκτυο, δεν θα έρθουν αργότερα τρία ίδια email.</para>
    /// </summary>
    public static (bool ok, string error) SendReportNow(string report)
    {
        var settings = SettingsStore.Instance.Settings;
        if (settings.SmtpUser.Length == 0 || settings.SmtpPassword.Length == 0)
            return (false, "Δεν έχει ρυθμιστεί το email αποστολής (Ρυθμίσεις → EMAIL ΑΝΑΦΟΡΑΣ).");

        var (ok, error) = TrySendEmail(report);
        if (ok)
        {
            ClearPending();
            return (true, "");
        }

        try
        {
            Directory.CreateDirectory(ReportsDir);
            ClearPending();
            File.WriteAllText(Path.Combine(ReportsDir, "pending-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".txt"),
                report, new UTF8Encoding(true));
            AppLog.Write("email", $"Η αναφορά ΔΕΝ στάλθηκε ({error}) — μπήκε σε αναμονή και θα ξαναδοκιμάσει μόνη της.");
        }
        catch (Exception ex)
        {
            AppLog.Write("email", $"Η αναφορά ΔΕΝ στάλθηκε ({error}) και δεν μπόρεσε ούτε να μπει σε αναμονή: {ex.Message}");
        }
        return (false, error);
    }

    /// <summary>Σβήνει ό,τι περιμένει — μία αναφορά σε αναμονή, ποτέ σωρός από ίδια email.</summary>
    private static void ClearPending()
    {
        try
        {
            if (!Directory.Exists(ReportsDir))
                return;
            foreach (var file in Directory.GetFiles(ReportsDir, "pending-*.txt"))
                File.Delete(file);
        }
        catch (Exception)
        {
            // Αν δεν σβήνει, το χειρότερο είναι μια διπλή αποστολή — δεν αξίζει να σκάσει τίποτα γι' αυτό.
        }
    }

    /// <summary>Ξαναστέλνει ό,τι έμεινε σε αναμονή. Σιωπηλό: αν πάλι δεν φύγει, μένει εκεί για την
    /// επόμενη φορά — ο ταμίας δεν έχει τίποτα να κάνει με αυτή την πληροφορία μέσα στη βάρδια.</summary>
    public static void RetryPendingEmail()
    {
        List<string> pending;
        try
        {
            if (!Directory.Exists(ReportsDir))
                return;
            pending = [.. Directory.GetFiles(ReportsDir, "pending-*.txt").OrderBy(f => f)];
        }
        catch (Exception)
        {
            return;
        }

        var s = SettingsStore.Instance.Settings;
        if (pending.Count == 0 || s.SmtpUser.Length == 0 || s.SmtpPassword.Length == 0)
            return;

        foreach (var file in pending)
        {
            try
            {
                var (ok, _) = TrySendEmail(File.ReadAllText(file));
                if (!ok)
                    return; // δεν παίζει το δίκτυο τώρα — άσε και τα υπόλοιπα για την επόμενη φορά
                File.Delete(file);
                AppLog.Write("email", $"Η αναφορά «{Path.GetFileName(file)}» που είχε μείνει σε αναμονή στάλθηκε.");
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    /// <summary>Ξεκινά την αυτόματη επανάληψη: μία φορά τώρα (άνοιγμα ταμείου) και μετά κάθε δέκα
    /// λεπτά. Καλείται από το <c>App.OnStartup</c>. Πάντα σε background thread — το SmtpClient.Send
    /// είναι συγχρονισμένη κλήση δικτύου και θα πάγωνε το ταμείο.</summary>
    public static void StartEmailRetry()
    {
        _ = Task.Run(RetryPendingEmail);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        timer.Tick += (_, _) => Task.Run(RetryPendingEmail);
        timer.Start();
    }

    /// <summary>Στέλνει την αναφορά με email. Επιστρέφει (επιτυχία, μήνυμα λάθους).</summary>
    public static (bool ok, string error) TrySendEmail(string report)
    {
        var s = SettingsStore.Instance.Settings;
        if (s.SmtpUser.Length == 0 || s.SmtpPassword.Length == 0)
            return (false, "Δεν έχει ρυθμιστεί το email αποστολής (Ρυθμίσεις → Email αναφοράς).");

        var to = s.ReportEmail.Length > 0 ? s.ReportEmail : s.SmtpUser;
        try
        {
            using var message = new MailMessage(s.SmtpUser, to)
            {
                Subject = "Πίττα του παππού — Αναφορά ημέρας " + DateTime.Now.ToString("dd/MM/yyyy HH:mm", Greek),
                Body = report,
            };
            using var client = new SmtpClient(s.SmtpHost, s.SmtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(s.SmtpUser, s.SmtpPassword),
                // Το προεπιλεγμένο είναι ΕΚΑΤΟ δευτερόλεπτα: με πεσμένη γραμμή, το κουμπί έμενε στο
                // «Στέλνω…» για πάνω από ενάμισι λεπτό πριν πει τι έγινε. Είκοσι φτάνουν και με σύνδεση
                // της πλάκας — αν δεν φύγει, μπαίνει σε αναμονή και ξαναδοκιμάζει μόνη της.
                Timeout = 20_000,
            };
            client.Send(message);
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
