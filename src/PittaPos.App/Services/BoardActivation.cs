using System.Windows;

namespace PittaPos.App.Services;

/// <summary>
/// Το σήμα «έλα μπροστά» για τις ξεχωριστές εφαρμογές (βλ. <see cref="AppMode"/>).
///
/// <para>Χρειάζεται γιατί ο πίνακας ανοίγει μία φορά: δεύτερο διπλό κλικ στη συντόμευση βρίσκει το
/// mutex πιασμένο και η νέα διεργασία κλείνει αμέσως. Χωρίς το σήμα, εκείνο το δεύτερο κλικ δεν θα
/// έκανε απολύτως τίποτα ορατό — ο ταμίας θα νόμιζε ότι «δεν ανοίγει».</para>
/// </summary>
public static class BoardActivation
{
    /// <summary>Τρέχει ήδη ο πίνακας; Τότε του στέλνεται το σήμα να έρθει μπροστά και επιστρέφει true.</summary>
    public static bool SignalExisting()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(AppMode.ActivateEvent, out var handle))
                return false;
            using (handle)
                return handle.Set();
        }
        catch (Exception)
        {
            // Δεν βρέθηκε/δεν ανοίγει το σήμα — θεωρείται «δεν τρέχει». Τη μοναδικότητα την κρατά
            // ούτως ή άλλως το mutex στο App.StartLiveBoard, όχι αυτό εδώ.
            return false;
        }
    }
}

/// <summary>
/// Η μεριά που ΑΚΟΥΕΙ το σήμα, μέσα στην ίδια την εφαρμογή πίνακα: όποτε ζητηθεί ξανά ο πίνακας από
/// αλλού, το παράθυρο έρχεται μπροστά αντί να μη γίνει τίποτα.
/// </summary>
public sealed class BoardActivationListener : IDisposable
{
    private readonly EventWaitHandle _signal;
    private readonly RegisteredWaitHandle _registration;

    public BoardActivationListener(Window window)
    {
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, AppMode.ActivateEvent);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _signal,
            (_, _) => window.Dispatcher.BeginInvoke(() => BringToFront(window)),
            null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
        // Τα Windows δεν αφήνουν μια διεργασία να «κλέψει» το προσκήνιο από άλλη — χωρίς αυτό το
        // παράθυρο απλώς αναβοσβήνει στη γραμμή εργασιών. Το Topmost για μια στιγμή το ανεβάζει και
        // το ξανακατεβάζει, ώστε να μη μείνει πάνω από όλα.
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _signal.Dispose();
    }
}
