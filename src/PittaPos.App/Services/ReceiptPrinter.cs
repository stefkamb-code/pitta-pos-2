using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using PittaPos.App.Views;

namespace PittaPos.App.Services;

/// <summary>
/// Σιωπηλή εκτύπωση απόδειξης στον εκτυπωτή που έχει οριστεί στις Ρυθμίσεις, χωρίς dialog.
/// Χρησιμοποιείται τόσο από το ταμείο (αυτόματα με κάθε ολοκλήρωση παραγγελίας) όσο και από τις
/// παραγγελίες που στέλνει ο σερβιτόρος από το κινητό μέσω του <see cref="WaiterApiService"/>.
/// </summary>
public static class ReceiptPrinter
{
    public static void PrintOrder(CompletedOrder order)
    {
        // Δεύτερο ταμείο: ο εκτυπωτής είναι δεμένος στο κύριο, δεν υπάρχει δεύτερος. Η απόδειξη φεύγει
        // εκεί και τυπώνεται από εκείνο. Ο έλεγχος μπαίνει ΕΔΩ και όχι στα σημεία που καλούν, ώστε να
        // ισχύει παντού με μία κίνηση: αυτόματη εκτύπωση παραγγελίας, επανεκτύπωση από το Ιστορικό,
        // παραγγελία από το κινητό του σερβιτόρου.
        if (RemoteSync.IsClient)
        {
            _ = SendToHostAsync(order);
            return;
        }

        var printerName = SettingsStore.Instance.Settings.PrinterName;
        if (printerName.Length == 0)
        {
            Fail(order, "δεν έχει οριστεί εκτυπωτής στις Ρυθμίσεις");
            return;
        }

        ReceiptWindow? receiptSource = null;
        try
        {
            using var server = new LocalPrintServer();
            var queue = server.GetPrintQueues().FirstOrDefault(q => q.FullName == printerName);
            if (queue is null)
            {
                Fail(order, $"ο εκτυπωτής «{printerName}» δεν βρέθηκε στα Windows");
                return;
            }

            // Έλεγχος κατάστασης πριν σταλεί οτιδήποτε στον driver — αν ο εκτυπωτής είναι σβηστός/
            // αποσυνδεδεμένος, κάποιοι drivers κολλάνε για πολλή ώρα μέσα στο PrintVisual παρακάτω,
            // παγώνοντας όλο το ταμείο (μονό UI thread). Καλύτερα να χάσουμε αυτή την απόδειξη.
            queue.Refresh();
            if (queue.IsOffline || queue.IsInError || queue.IsNotAvailable)
            {
                Fail(order, $"ο εκτυπωτής «{printerName}» δεν είναι διαθέσιμος " +
                    $"(offline={queue.IsOffline}, σφάλμα={queue.IsInError}, μη διαθέσιμος={queue.IsNotAvailable})");
                return;
            }

            // Ανοίγει εκτός οθόνης μόνο για να «τυπωθεί» — ΔΕΝ είναι το Ιστορικό, δεν εκθέτει καμία
            // ενέργεια ασφαλείας ακόμα κι αν κατά λάθος γίνει ορατό (π.χ. Alt+Tab).
            receiptSource = new ReceiptWindow(order)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -5000,
                Top = -5000,
                ShowInTaskbar = false,
            };
            receiptSource.Show();
            receiptSource.UpdateLayout();

            var ticket = queue.DefaultPrintTicket;
            // Το ReceiptCard έχει σταθερό Width=420 στο XAML (βολικό για προεπισκόπηση στην οθόνη) — αλλά
            // αν το πραγματικό ρολό του εκτυπωτή είναι στενότερο (π.χ. 80mm/58mm), το driver έκοβε σιωπηλά
            // ό,τι ξεπερνούσε το πλάτος σελίδας αντί να τυλίξει (π.χ. «ΠΙΤΤΑ ΤΟΥ ΠΑΠΠΟΥ» έκοβε στο «ΠΑΠ»),
            // γιατί μόνο το ΜΕΤΑΔΕΔΟΜΕΝΟ πλάτος σελίδας (PageMediaSize) ενημερωνόταν παρακάτω, όχι το ίδιο
            // το visual. Ξαναρυθμίζουμε πρώτα το πλάτος του visual στο πραγματικό πλάτος του driver, ΠΡΙΝ
            // ξαναδιαβάσουμε ύψος/πλάτος για το PageMediaSize, ώστε το κείμενο να τυλίξει σωστά μέσα του.
            if (ticket.PageMediaSize?.Width is { } printerWidth && printerWidth > 0)
            {
                var safeWidth = SafeWidth(queue, ticket, printerWidth);
                receiptSource.ReceiptCard.Width = safeWidth;
                receiptSource.ReceiptCard.Padding = PaddingFor(safeWidth, printerWidth - safeWidth);
                receiptSource.UpdateLayout();
            }
            // Χωρίς αυτό, το PrintVisual χρησιμοποιεί το προεπιλεγμένο μήκος σελίδας του driver (συχνά
            // ολόκληρο μήκος σελίδας/ρολού) — ο εκτυπωτής τροφοδοτεί/κόβει πολύ περισσότερο χαρτί απ' όσο
            // πραγματικά τυπώθηκε. Κρατάμε το πλάτος του driver (ήδη σωστά ρυθμισμένο για το ρολό) αλλά
            // περιορίζουμε το ύψος στο πραγματικό ύψος περιεχομένου της απόδειξης.
            var width = ticket.PageMediaSize?.Width ?? receiptSource.ReceiptCard.ActualWidth;
            ticket.PageMediaSize = new PageMediaSize(width, receiptSource.ReceiptCard.ActualHeight);

            // Η στοίχιση της απόδειξης προκύπτει ΟΛΗ από αυτά τα νούμερα, που τα δίνει ο οδηγός του
            // εκτυπωτή του κάθε μηχανήματος. Ίδιος κώδικας σε δύο υπολογιστές έβγαλε τέλεια απόδειξη
            // στον έναν και στραβή στον άλλον — χωρίς να τα βλέπουμε, δεν υπάρχει τρόπος να ξέρουμε
            // γιατί. Καταγράφονται δίπλα στο exe, ώστε να διαβάζονται και σε ξένο μηχάνημα.
            LogPrintGeometry(printerName, queue, ticket, receiptSource.ReceiptCard);

            var dialog = new PrintDialog { PrintQueue = queue, PrintTicket = ticket };
            dialog.PrintVisual(receiptSource.ReceiptCard, "Απόδειξη #" + order.OrderNumber);
        }
        catch (Exception ex)
        {
            // Αποτυχία εκτύπωσης δεν πρέπει να μπλοκάρει το ταμείο
            Fail(order, ex.Message);
        }
        finally
        {
            receiptSource?.Close();
        }
    }

    /// <summary>Κάθε απόδειξη που ΔΕΝ τυπώθηκε αφήνει ίχνος. Πριν, όλες οι αποτυχίες ήταν εντελώς
    /// σιωπηλές: ο ταμίας νόμιζε ότι τυπώθηκε και δεν υπήρχε πουθενά τρόπος να διαπιστωθεί γιατί
    /// «δεν βγήκε χαρτί» — ούτε καν ότι έγινε καν προσπάθεια.</summary>
    /// <summary>
    /// Περιθώρια ανάλογα με το ΠΡΑΓΜΑΤΙΚΟ πλάτος του χαρτιού, ώστε να δουλεύει σε κάθε εκτυπωτή χωρίς
    /// χειροκίνητη ρύθμιση. Το σταθερό 24 του σχεδίου ήταν λογικό στις 420 μονάδες της οθόνης (~6%),
    /// αλλά σε ρολό 80mm (272 μονάδες εκτυπώσιμες) έτρωγε το 18% του χαρτιού δεξιά-αριστερά — σε ρολό
    /// 58mm ακόμα χειρότερα. Πάνω-κάτω μένει μικρότερο: εκεί είναι σκέτο χαμένο χαρτί σε κάθε απόδειξη.
    /// </summary>
    /// <param name="slack">Πόσο στενότερη είναι η απόδειξη από το εκτυπώσιμο πλάτος (το περιθώριο
    /// ασφαλείας, βλ. SafeWidth). Επειδή η εκτύπωση ξεκινά από την αριστερή άκρη, ΟΛΟ αυτό το κενό
    /// έπεφτε δεξιά και η απόδειξη φαινόταν μετατοπισμένη. Το μοιράζουμε στις δύο πλευρές δίνοντας
    /// τη μισή διαφορά επιπλέον αριστερά και αφαιρώντας τη δεξιά — έτσι το κείμενο κάθεται κεντραρισμένο
    /// στο χαρτί χωρίς να χρειάζεται μετατόπιση ολόκληρου του visual.</param>
    private static Thickness PaddingFor(double printerWidth, double slack = 0)
    {
        var side = Math.Clamp(printerWidth * 0.04, 6, 24);
        var shift = slack / 2;
        return new Thickness(side + shift, side * 0.6, Math.Max(0, side - shift), side * 0.6);
    }

    /// <summary>
    /// Το πλάτος που δίνουμε ΠΡΑΓΜΑΤΙΚΑ στην απόδειξη: το εκτυπώσιμο πλάτος του οδηγού, μείον ένα μικρό
    /// περιθώριο ασφαλείας.
    ///
    /// Οι θερμικοί έχουν συχνά λίγα χιλιοστά στη δεξιά άκρη που δεν τυπώνουν αλλά ΔΕΝ τα δηλώνουν
    /// (δοκιμασμένο: ο οδηγός έλεγε 272,1 σελίδα και 271,9 εκτυπώσιμα — πρακτικά τίποτα, κι όμως τα ποσά
    /// στη δεξιά άκρη έβγαιναν κομμένα). Επειδή τα ποσά είναι στοιχισμένα ακριβώς εκεί, χάνονταν ψηφία.
    /// Κόβοντας ~1,5mm εξασφαλίζεται ότι το τελευταίο ψηφίο τυπώνεται πάντα ολόκληρο.
    /// </summary>
    private static double SafeWidth(PrintQueue queue, PrintTicket ticket, double printerWidth)
    {
        var printable = printerWidth;
        try
        {
            if (queue.GetPrintCapabilities(ticket).PageImageableArea is { } area && area.ExtentWidth > 0)
                printable = Math.Min(printable, area.ExtentWidth);
        }
        catch (Exception)
        {
            // Οδηγός που δεν δίνει δυνατότητες — μένουμε στο πλάτος σελίδας
        }
        return Math.Max(printable * 0.5, printable - 6); // 6 μονάδες ≈ 1,6 mm
    }

    /// <summary>
    /// Γράφει δίπλα στο exe τα νούμερα που καθορίζουν τη στοίχιση της απόδειξης. Όχι στο %AppData%:
    /// σε ξένο μηχάνημα θέλουμε ένα αρχείο που βρίσκεται εύκολα, δίπλα στο πρόγραμμα.
    /// </summary>
    private static void LogPrintGeometry(string printerName, PrintQueue queue, PrintTicket ticket, FrameworkElement card)
    {
        try
        {
            double? imageable = null;
            try { imageable = queue.GetPrintCapabilities(ticket).PageImageableArea?.ExtentWidth; }
            catch (Exception) { /* οδηγός που δεν δίνει δυνατότητες */ }

            static string Mm(double? diu) => diu is null ? "—" : (diu.Value * 25.4 / 96.0).ToString("0.0") + "mm";

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  εκτυπωτής={printerName} | " +
                $"πλάτος σελίδας={Mm(ticket.PageMediaSize?.Width)} | " +
                $"εκτυπώσιμο={Mm(imageable)} | " +
                $"πλάτος απόδειξης={Mm(card.ActualWidth)} | ύψος={Mm(card.ActualHeight)}" +
                Environment.NewLine;
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "print-diagnostic.txt"), line);
        }
        catch (Exception)
        {
            // Η διάγνωση δεν πρέπει ποτέ να εμποδίσει μια εκτύπωση
        }
    }

    /// <summary>Στέλνει την απόδειξη στο κύριο ταμείο για εκτύπωση. Αν δεν φτάσει (κλειστό ταμείο,
    /// πεσμένο δίκτυο) μπαίνει στην ίδια ουρά με τις παραγγελίες και ξαναφεύγει μόλις επανέλθει —
    /// τυπώνεται τότε με καθυστέρηση, αλλά δεν χάνεται. Ο σερβιτόρος/ταμίας δεν περιμένει: η αποστολή
    /// γίνεται στο παρασκήνιο, όπως ακριβώς και η συγχρονισμένη καταχώρηση της παραγγελίας.</summary>
    private static async Task SendToHostAsync(CompletedOrder order)
    {
        if (await RemoteSync.PostAsync("/api/sync/print/order", order))
            return;
        PendingSyncService.Instance.Enqueue("/api/sync/print/order", order,
            $"Απόδειξη #{order.OrderNumber} προς εκτύπωση");
    }

    /// <summary>Στέλνει μια αναφορά στο κύριο ταμείο για εκτύπωση. Σε αντίθεση με την απόδειξη, ΔΕΝ
    /// μπαίνει στην ουρά αναμονής αν δεν φτάσει: μια αναφορά έχει νόημα ΤΩΡΑ — τυπωμένη μισή ώρα
    /// αργότερα δείχνει άλλα νούμερα και μπερδεύει (βλ. PendingSyncService: μόνο προσθήκες μπαίνουν
    /// εκεί, ποτέ ενέργειες της στιγμής).</summary>
    private static async Task SendTextToHostAsync(string text, string title)
    {
        if (!await RemoteSync.PostAsync("/api/sync/print/text", new { Text = text, Title = title }))
            AppLog.Write("printer", $"Δεν στάλθηκε στο κύριο ταμείο για εκτύπωση: {title}");
    }

    private static void Fail(CompletedOrder order, string reason) =>
        AppLog.Write("printer", $"Δεν τυπώθηκε η απόδειξη #{order.OrderNumber} " +
            $"({order.TypeLabel}, {order.TotalLabel}): {reason}");

    // ΑΦΑΙΡΕΘΗΚΕ το PrintLatestOrder(): τύπωνε «ό,τι είναι πιο πρόσφατο στη λίστα της ημέρας» αντί για
    // τη συγκεκριμένη παραγγελία που μόλις έγινε. Στο δεύτερο ταμείο η λίστα είναι αντίγραφο του κυρίου
    // με καθυστέρηση, οπότε τυπωνόταν Η ΠΡΟΗΓΟΥΜΕΝΗ παραγγελία — λάθος απόδειξη στον πελάτη. Όποιος
    // τυπώνει, δίνει πλέον ρητά ΤΟ CompletedOrder του (βλ. OrderWizardViewModel.AutoPrintRequested).

    /// <summary>Σιωπηλή εκτύπωση της πλήρους αναφοράς ημέρας (Ζ-report, βλ. DayReportService.Build) στον
    /// εκτυπωτή που έχει οριστεί στις Ρυθμίσεις. Καλείται από το DayReportService.CloseDay/PrintCurrentReport.</summary>
    public static void PrintDayReport(string report) => PrintPlainText(report, "Αναφορά Ημέρας");

    /// <summary>Σιωπηλή εκτύπωση της αναφοράς διανομέα (βλ. OrderBoardService.BuildDriverReport) στον
    /// εκτυπωτή που έχει οριστεί στις Ρυθμίσεις.</summary>
    public static void PrintDriverReport(string report) => PrintPlainText(report, DriverReportTitle);

    /// <summary>Ο τίτλος ταξιδεύει μαζί με το κείμενο ως το ταμείο που έχει τον εκτυπωτή (βλ.
    /// SendTextToHostAsync), οπότε πάνω του κρίνεται και το μέγεθος — αλλιώς η αναφορά που στέλνει το
    /// δεύτερο ταμείο θα τυπωνόταν στο μέγεθος της αναφοράς ημέρας.</summary>
    public const string DriverReportTitle = "Αναφορά Διανομέα";

    /// <summary>Η αναφορά διανομέα γράφεται στενή επίτηδες (34 χαρακτήρες, βλ.
    /// OrderBoardService.DriverReportWidth) ώστε να τυπωθεί με μεγαλύτερα γράμματα· η αναφορά ημέρας έχει
    /// στοιχισμένες στήλες ~48 χαρακτήρων και μένει στο μέγεθος σχεδίασης.
    ///
    /// <para>Το 22 είναι μόνο μέγεθος ΕΚΚΙΝΗΣΗΣ: το FitTextToWidth μόνο μικραίνει, οπότε στο ρολό το
    /// τελικό μέγεθος το ορίζει το πλάτος σε χαρακτήρες — εκεί ρύθμισε, όχι εδώ.</para></summary>
    private static double FontSizeFor(string title) => title == DriverReportTitle ? 22 : 14;

    /// <summary>Τελευταίο ορατό preview-παράθυρο (όταν δεν έχει οριστεί εκτυπωτής) — κλείνει το προηγούμενο
    /// πριν ανοίξει το επόμενο, αλλιώς επαναλαμβανόμενα κλικ σε «ΕΚΤΥΠΩΣΗ ΤΩΡΑ»/«ΕΚΤΥΠΩΣΕΙΣ ΔΙΑΝΟΜΕΑ»
    /// στοίβαζαν παράθυρα το ένα πάνω στο άλλο επ' άπειρον.</summary>
    private static Views.DayReportWindow? _previewWindow;

    /// <summary>Κοινός μηχανισμός εκτύπωσης μονόχωρου κειμένου (DayReportWindow) — ίδιος με το PrintOrder,
    /// με μονόχωρο κείμενο αντί για κάρτα απόδειξης. Αν δεν έχει οριστεί ακόμα εκτυπωτής, δείχνει το
    /// κείμενο σε ένα κανονικό (ορατό) παράθυρο αντί να μην κάνει τίποτα — έτσι δεν «χάνεται» χωρίς κανένα
    /// αποτέλεσμα πριν ρυθμιστεί ο εκτυπωτής.</summary>
    /// <summary>Εκτύπωση κειμένου που ήρθε από το δεύτερο ταμείο (βλ. /api/sync/print/text).</summary>
    public static void PrintText(string text, string title) => PrintPlainText(text, title);

    private static void PrintPlainText(string text, string title)
    {
        // Δεύτερο ταμείο: ίδιος λόγος με το PrintOrder — ο εκτυπωτής είναι δεμένος στο κύριο ταμείο.
        // Χωρίς αυτό, «🖨 ΕΚΤΥΠΩΣΕΙΣ ΔΙΑΝΟΜΕΑ» και «ΕΚΤΥΠΩΣΗ ΤΩΡΑ» άνοιγαν εδώ ένα παράθυρο
        // προεπισκόπησης («δεν έχει οριστεί εκτυπωτής») αντί να βγάλουν χαρτί — ο διανομέας έφευγε
        // χωρίς τη λίστα του και ο ταμίας νόμιζε ότι κάτι χάλασε.
        if (RemoteSync.IsClient)
        {
            _ = SendTextToHostAsync(text, title);
            return;
        }

        var printerName = SettingsStore.Instance.Settings.PrinterName;
        if (printerName.Length == 0)
        {
            _previewWindow?.Close();
            _previewWindow = new Views.DayReportWindow(text, FontSizeFor(title))
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = true,
                Title = title + " (προεπισκόπηση — δεν έχει οριστεί εκτυπωτής)",
            };
            _previewWindow.Show();
            return;
        }

        Views.DayReportWindow? textSource = null;
        try
        {
            using var server = new LocalPrintServer();
            var queue = server.GetPrintQueues().FirstOrDefault(q => q.FullName == printerName);
            if (queue is null)
            {
                AppLog.Write("printer", $"«{title}»: ο εκτυπωτής «{printerName}» δεν βρέθηκε στα Windows");
                return;
            }

            queue.Refresh();
            if (queue.IsOffline || queue.IsInError || queue.IsNotAvailable)
            {
                AppLog.Write("printer", $"«{title}»: ο εκτυπωτής «{printerName}» δεν είναι διαθέσιμος");
                return;
            }

            textSource = new Views.DayReportWindow(text, FontSizeFor(title))
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -5000,
                Top = -5000,
                ShowInTaskbar = false,
            };
            textSource.Show();
            textSource.UpdateLayout();

            var ticket = queue.DefaultPrintTicket;
            // Ίδιο fix με το PrintOrder — ξαναρυθμίζουμε το πλάτος του visual στο πραγματικό πλάτος του
            // driver πριν το τελικό layout, αλλιώς το κείμενο κόβεται στην άκρη αντί να τυλίξει.
            if (ticket.PageMediaSize?.Width is { } printerWidth && printerWidth > 0)
            {
                var safeWidth = SafeWidth(queue, ticket, printerWidth);
                var padding = PaddingFor(safeWidth, printerWidth - safeWidth);
                textSource.ReportCard.Width = safeWidth;
                textSource.ReportCard.Padding = padding;
                // Η αναφορά είναι μονόχωρη με στοιχισμένες στήλες — αν δεν χωράει η πιο μακριά γραμμή
                // στο χαρτί, αναδιπλώνεται και χαλάει όλη η στοίχιση. Προσαρμόζεται αυτόματα.
                textSource.FitTextToWidth(safeWidth - padding.Left - padding.Right);
                textSource.UpdateLayout();
            }
            var width = ticket.PageMediaSize?.Width ?? textSource.ReportCard.ActualWidth;
            ticket.PageMediaSize = new PageMediaSize(width, textSource.ReportCard.ActualHeight);

            var dialog = new PrintDialog { PrintQueue = queue, PrintTicket = ticket };
            dialog.PrintVisual(textSource.ReportCard, title);
        }
        catch (Exception ex)
        {
            // Αποτυχία εκτύπωσης δεν πρέπει να μπλοκάρει το ταμείο
            AppLog.Write("printer", $"Αποτυχία εκτύπωσης «{title}»: {ex.Message}");
        }
        finally
        {
            textSource?.Close();
        }
    }
}
