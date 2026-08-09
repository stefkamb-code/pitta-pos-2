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

    private static void AppendSummary(StringBuilder sb, List<CompletedOrder> orders)
    {
        var revenue = orders.Sum(o => o.Total);
        var items = orders.Sum(o => o.ItemCount);
        sb.AppendLine("ΣΥΝΟΨΗ");
        sb.AppendLine($"  Παραγγελίες : {orders.Count}");
        sb.AppendLine($"  Τζίρος      : {Order.FormatPrice(revenue)}");
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
        sb.AppendLine("ΣΥΝΟΨΗ");
        sb.AppendLine($"  Τζίρος      : {Order.FormatPrice(orders.Sum(o => o.Total))}");

        // ΔΙΑΝΟΜΗ/BOX κρατούν τον τρόπο πληρωμής πάνω στην παραγγελία· τα ΤΡΑΠΕΖΙΑ πληρώνονται τμηματικά
        // (ο καθένας τα δικά του, με διαφορετικό τρόπο ο καθένας), οπότε καταγράφονται ξεχωριστά ανά
        // είσπραξη — βλ. TablePaymentsService. Εδώ αθροίζονται και τα δύο στον ίδιο διαχωρισμό.
        var tracked = orders.Where(o => o.PaymentMethod is not null).ToList();
        var tables = TablePaymentsService.Instance;
        var cash = tracked.Where(o => o.PaymentMethod == Core.Models.PaymentMethod.Cash).Sum(o => o.Total)
            + tables.TotalFor(Core.Models.PaymentMethod.Cash);
        var card = tracked.Where(o => o.PaymentMethod == Core.Models.PaymentMethod.Card).Sum(o => o.Total)
            + tables.TotalFor(Core.Models.PaymentMethod.Card);
        if (cash > 0 || card > 0)
        {
            sb.AppendLine($"    Μετρητά   : {Order.FormatPrice(cash)}");
            sb.AppendLine($"    Κάρτα     : {Order.FormatPrice(card)}");
        }

        sb.AppendLine($"  Παραγγελίες : {orders.Count}");
        sb.AppendLine();
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

    /// <summary>Χτίζει αναλυτική αναφορά όλης της ημέρας από τα στατιστικά — για email και τοπικό backup.</summary>
    public static string Build()
    {
        var orders = SalesStatsService.Instance.Orders
            .OrderBy(o => o.PlacedAt)
            .ToList();

        var sb = new StringBuilder();
        AppendHeader(sb);
        AppendSummary(sb, orders);
        AppendPerChannel(sb, orders);

        sb.AppendLine("ΑΝΑ ΠΡΟΪΟΝ");
        var products = orders.SelectMany(o => o.Lines)
            .GroupBy(l => l.Name)
            .Select(g => (Name: g.Key, Qty: g.Sum(l => l.Quantity), Rev: g.Sum(l => l.Revenue)))
            .OrderByDescending(p => p.Qty);
        foreach (var p in products)
            sb.AppendLine($"  {p.Qty,4} × {p.Name,-34} {Order.FormatPrice(p.Rev)}");
        sb.AppendLine();

        sb.AppendLine("ΠΑΡΑΓΓΕΛΙΕΣ ΑΝΑΛΥΤΙΚΑ");
        sb.AppendLine(new string('-', 48));
        foreach (var o in orders)
        {
            sb.AppendLine($"#{o.OrderNumber} · {o.TimeLabel} · {o.TypeLabel}"
                + (o.Who.Length > 0 ? " · " + o.Who : "") + $"   {Order.FormatPrice(o.Total)}");
            foreach (var l in o.Lines)
            {
                sb.AppendLine($"    {l.Quantity} × {l.Name}   {Order.FormatPrice(l.Revenue)}");
                if (l.Details.Length > 0)
                    sb.AppendLine($"        {l.Details}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Σύντομη αναφορά ημέρας μόνο με τζίρους (σύνοψη + ανά κανάλι, χωρίς ανά προϊόν/ανά
    /// παραγγελία) — αυτή τυπώνεται στον θερμικό εκτυπωτή· η πλήρης αναλυτική (Build) πάει σε email/backup.</summary>
    public static string BuildPrintSummary()
    {
        var orders = SalesStatsService.Instance.Orders
            .OrderBy(o => o.PlacedAt)
            .ToList();

        var sb = new StringBuilder();
        AppendHeader(sb);
        AppendPrintSummary(sb, orders);
        AppendPrintPerChannel(sb, orders);
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

        // Σε background thread — το SmtpClient.Send είναι συγχρονισμένη κλήση δικτύου (έως ~100 δλ.
        // default timeout)· αν έτρεχε εδώ θα πάγωνε ολόκληρο το ταμείο (το CloseDay καλείται και από
        // το DispatcherTimer αυτόματου κλεισίματος, πάνω στο UI thread) όσο δεν απαντά ο SMTP server.
        _ = Task.Run(() => TrySendEmail(report));
        ReceiptPrinter.PrintDayReport(BuildPrintSummary());
        SalesStatsService.Instance.Clear();
        CancellationLogService.Instance.Clear();
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
            AppLog.Write("close-day",
                $"Καθυστερημένες παραγγελίες ημέρας {group.Key:yyyy-MM-dd} ({group.Count()}) αρχειοθετήθηκαν " +
                "χωρίς να κλείσει η τρέχουσα βάρδια.");
        }
    }

    /// <summary>Αποθηκεύει την αναφορά τοπικά (backup) και επιστρέφει τη διαδρομή.</summary>
    public static string SaveToDisk(string report)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppIdentity.DataFolder, "reports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "anafora-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".txt");
        File.WriteAllText(path, report, new UTF8Encoding(true));
        return path;
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
