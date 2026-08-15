using System.Windows.Threading;

namespace PittaPos.App.Services;

/// <summary>
/// Κρατάει ζωντανή τη σύνδεση του δεύτερου ταμείου με το κύριο, ΣΙΩΠΗΛΑ. Καμία μπάρα, κανένα μήνυμα:
/// αν το δίκτυο του μαγαζιού λειτουργεί, το κύριο ταμείο πρέπει απλώς να βρίσκεται.
///
/// Γιατί χρειάζεται: η IP του κύριου ταμείου δεν είναι σταθερή — το router τη μοιράζει με DHCP και μια
/// διακοπή ρεύματος ή μια επανεκκίνηση μπορεί να τη δώσει αλλού. Από εκείνη τη στιγμή η γραμμένη
/// διεύθυνση δείχνει στο πουθενά και το δεύτερο ταμείο δεν βλέπει ούτε τραπέζια ούτε εκτυπώνει, χωρίς να
/// έχει αλλάξει κανείς τίποτα. Εδώ ελέγχεται τακτικά αν απαντά, και μόλις πάψει να απαντά σαρώνεται το
/// τοπικό δίκτυο για να βρεθεί η νέα του διεύθυνση (βλ. RemoteSync.DiscoverHostAsync).
///
/// Η αλλαγή πιάνει ΑΜΕΣΩΣ, χωρίς επανεκκίνηση: κάθε αίτημα χτίζει το URL του από τη ρύθμιση τη στιγμή
/// που στέλνεται. Στο κύριο ταμείο ο σκοπός δεν τρέχει καθόλου.
/// </summary>
public static class HostWatchdog
{
    /// <summary>Πόσο συχνά ρωτάμε «ζεις;». Φθηνό (ένα GET σε τοπικό δίκτυο) και αρκετά πυκνό ώστε μια
    /// αλλαγή IP να διορθώνεται μέσα σε δευτερόλεπτα, πριν προλάβει να τη δει ο ταμίας.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);

    /// <summary>Αν η σάρωση δεν βρει τίποτα (π.χ. ο άλλος υπολογιστής είναι κλειστό βράδυ), δεν έχει
    /// νόημα να ξανασαρώνεται κάθε 10 δευτερόλεπτα — το δίκτυο θα φορτωνόταν χωρίς λόγο για ώρες.</summary>
    private static readonly TimeSpan RescanCooldown = TimeSpan.FromSeconds(30);

    private static DateTime _lastFailedScan = DateTime.MinValue;
    private static bool _busy;

    public static void Start()
    {
        if (!RemoteSync.IsClient)
            return;

        var timer = new DispatcherTimer { Interval = CheckInterval };
        timer.Tick += async (_, _) => await TickAsync();
        timer.Start();
        // Και μία φορά αμέσως: αν η IP άλλαξε ενώ το ταμείο ήταν κλειστό, να έχει ήδη διορθωθεί πριν
        // προλάβει ο ταμίας να περάσει την πρώτη παραγγελία.
        _ = TickAsync();
    }

    private static async Task TickAsync()
    {
        // Ο timer χτυπά ξανά ενώ μια σάρωση τρέχει ακόμα — χωρίς αυτό θα ξεκινούσαν πολλές παράλληλες.
        if (_busy)
            return;
        _busy = true;
        try
        {
            if (await RemoteSync.PingHostAsync())
                return;
            if (DateTime.Now - _lastFailedScan < RescanCooldown)
                return;

            var found = await RemoteSync.FindAndSaveHostAsync();
            if (found is null)
                _lastFailedScan = DateTime.Now;
        }
        catch (Exception ex)
        {
            // Ο σκοπός δεν πρέπει ΠΟΤΕ να ρίξει το ταμείο — τρέχει από DispatcherTimer, δηλαδή πάνω στο UI.
            AppLog.Write("host-watchdog", $"Σφάλμα στον έλεγχο σύνδεσης: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }
}
