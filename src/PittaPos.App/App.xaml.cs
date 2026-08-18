using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PittaPos.App.Services;

namespace PittaPos.App;

public partial class App : Application
{
    // Κρατιέται ζωντανό όσο τρέχει η εφαρμογή — αν αφηνόταν να μαζευτεί από τον garbage collector θα
    // ελευθερωνόταν το mutex και θα «έσπαγε» τον έλεγχο μοναδικού instance παρακάτω.
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Δύο instances μαζί (π.χ. διπλό κλικ στη συντόμευση, ή ένα ξεχασμένο ανοιχτό από πριν) παλεύουν
        // για την ίδια θύρα του WaiterApiService ΚΑΙ για τον ίδιο φάκελο προφίλ WebView2 — το δεύτερο
        // βγάζει «Access Denied» μόλις ανοίξει ο Χάρτης Διανομής (δοκιμασμένο, βλ. crash-log). Καλύτερα να
        // ενημερωθεί ο ταμίας καθαρά εδώ, παρά να μπερδευτεί με σφάλματα σε τυχαία σημεία αργότερα.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, AppIdentity.SingleInstanceMutex, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                $"Η εφαρμογή Πίττα POS ({AppIdentity.StoreName}) τρέχει ήδη — δεν ανοίγει δεύτερο παράθυρο.",
                "Ήδη ανοιχτή", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Χωρίς αυτά, ένα οποιοδήποτε απρόσμενο σφάλμα (π.χ. null σε κάποιο ViewModel) έριχνε όλο
        // το ταμείο απότομα, στη μέση παραγγελίας — τώρα καταγράφεται και η εφαρμογή συνεχίζει.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Εφαρμογή του αποθηκευμένου θέματος πριν ανοίξει οποιοδήποτε παράθυρο. Το HookNewWindows
        // προηγείται ώστε να πιάσει και το πρώτο παράθυρο: κάθε παράθυρο που ανοίγει από δω και πέρα
        // παίρνει μόνο του τη σωστή μπάρα τίτλου (βλ. TitleBarTheme).
        TitleBarTheme.HookNewWindows();
        ThemeManager.Apply(SettingsStore.Instance.IsDark);

        // Αν ο κατάλογος του μαγαζιού δεν διαβάστηκε (π.χ. στιγμιαίο κλείδωμα του αρχείου), η εφαρμογή
        // δείχνει τα εργοστασιακά προϊόντα. Πρέπει να φανεί ΚΑΘΑΡΑ: αλλιώς ο ταμίας χτυπάει
        // παραγγελίες με λάθος ονόματα και τιμές νομίζοντας ότι είναι του καταστήματος.
        if (MenuStore.Instance.LoadFailed)
        {
            MessageBox.Show(
                "Ο κατάλογος του καταστήματος ΔΕΝ διαβάστηκε και εμφανίζονται προσωρινά τα εργοστασιακά " +
                "προϊόντα, με λάθος τιμές.\n\nΚλείσε και ξανάνοιξε την εφαρμογή.\n\nΜΗΝ αλλάξεις τίποτα " +
                "στη Διαχείριση Καταλόγου όσο βλέπεις αυτό το μήνυμα — οι αλλαγές δεν αποθηκεύονται, " +
                "ώστε να μη χαθεί ο πραγματικός κατάλογος.",
                "Προσοχή — προσωρινός κατάλογος", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ΠΡΟΘΕΡΜΑΝΣΗ ΠΕΛΑΤΟΛΟΓΙΟΥ. Ο πελατολόγιος φορτωνόταν την πρώτη φορά που τον ζητούσε κάποιος
        // — δηλαδή πάνω στο πρώτο πληκτρολόγημα σε παραγγελία διανομής, με σαράντα MB από τον δίσκο
        // εκείνη ακριβώς τη στιγμή. Τώρα φορτώνει εδώ, στο παρασκήνιο, όσο ο ταμίας βλέπει την αρχική:
        // μέχρι να φτάσει στον πρώτο πελάτη είναι έτοιμος. (Το static πεδίο Instance αρχικοποιείται
        // ούτως ή άλλως μία φορά και με ασφάλεια νημάτων — αν κάποιος προλάβει, απλώς περιμένει όσο
        // περίμενε και πριν.)
        _ = Task.Run(() => _ = CustomerStore.Instance);

        // Καθάρισμα «φαντασμάτων»: τραπέζια σημειωμένα ανοιχτά από παλιότερη μέρα που έκλεισε
        // πριν προστεθεί το TableStatusService.CloseAll() στο κλείσιμο μέρας.
        TableStatusService.Instance.CloseIfNoLiveOrders((table, since) =>
            SalesStatsService.Instance.Orders.Any(o =>
                o.Type == Core.Models.OrderType.Table && o.Who == "Τραπέζι " + table && o.PlacedAt >= since));

        // API για την εφαρμογή κινητού του σερβιτόρου (μόνο μέσα στο WiFi του μαγαζιού)
        WaiterApiService.Start();

        // Δεύτερο ταμείο: σιωπηλός σκοπός που κρατά ζωντανή τη σύνδεση με το κύριο — μόλις αλλάξει η IP
        // του, τη βρίσκει ξανά μόνος του μέσα σε δευτερόλεπτα (βλ. HostWatchdog).
        HostWatchdog.Start();

        // Δεύτερο ταμείο: αντίγραφο του αρχείου παλιότερων ημερών, ώστε Ιστορικό και Στατιστικά να
        // δείχνουν τα ίδια με το κύριο και πίσω στον χρόνο (βλ. HistoryArchiveService).
        HistoryArchiveService.StartClientMirror();

        // Αναγνώριση κλήσεων μέσω AMI του Grandstream UCM — ανενεργό αν δεν έχει ρυθμιστεί
        AmiClientService.Start();
    }

    /// <summary>Ό,τι αποθήκευση πελατών εκκρεμεί, γράφεται ΤΩΡΑ. Η αποθήκευση αναβάλλεται λίγα
    /// δευτερόλεπτα ώστε να μην παγώνει η οθόνη σε κάθε παραγγελία (βλ. CustomerStore.Save) — χωρίς
    /// αυτό, μια αλλαγή των τελευταίων δευτερολέπτων θα χανόταν στο κλείσιμο.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            CustomerStore.Instance.FlushPendingSave();
        }
        catch (Exception ex)
        {
            LogCrash("έξοδος", ex);
        }
        base.OnExit(e);
    }

    /// <summary>Σφάλμα πάνω στο UI thread — το πιο συχνό. Καταγραφή + συνέχεια αντί για crash.</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("UI thread", e.Exception);
        MessageBox.Show(
            "Παρουσιάστηκε ένα απρόσμενο σφάλμα, αλλά το ταμείο συνεχίζει να λειτουργεί.\n\n" + e.Exception.Message,
            "Σφάλμα", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true; // δεν αφήνει να ρίξει όλη την εφαρμογή
    }

    /// <summary>Σφάλμα σε background thread (π.χ. thread pool του Kestrel) — δεν μπορεί να «καταπιεί» αλλά τουλάχιστον καταγράφεται.</summary>
    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        LogCrash("background thread", e.ExceptionObject as Exception);

    /// <summary>Task που πέταξε σφάλμα χωρίς κανείς να το περιμένει (π.χ. fire-and-forget Task.Run) — απλή καταγραφή.</summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("unobserved task", e.Exception);
        e.SetObserved();
    }

    private static void LogCrash(string source, Exception? ex) => AppLog.Write(source, ex?.ToString() ?? "");
}
