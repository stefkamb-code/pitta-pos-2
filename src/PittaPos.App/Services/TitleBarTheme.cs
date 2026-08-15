using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PittaPos.App.Services;

/// <summary>
/// Βάφει και τη ΓΡΑΜΜΗ ΤΙΤΛΟΥ των παραθύρων στο χρώμα του θέματος. Το WPF βάφει μόνο το εσωτερικό του
/// παραθύρου· το πλαίσιο γύρω (τίτλος, ελαχιστοποίηση/μεγιστοποίηση/κλείσιμο) το ζωγραφίζουν τα Windows
/// και μένει ΛΕΥΚΟ ακόμα κι όταν όλη η εφαρμογή έχει γυρίσει στο μαύρο θέμα — μια άσπρη λωρίδα πάνω από
/// μια μαύρη οθόνη.
///
/// Λύνεται με μία ρύθμιση του διαχειριστή παραθύρων των Windows (DWM). Όλα τα παρακάτω είναι τυλιγμένα
/// σε try/catch και σιωπηλά σε αποτυχία: σε παλιότερα Windows η ρύθμιση απλώς δεν υπάρχει, και μια
/// άσπρη μπάρα τίτλου δεν είναι λόγος να μη ανοίξει το ταμείο.
/// </summary>
public static class TitleBarTheme
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Ο κωδικός της ρύθμισης άλλαξε κάποια στιγμή στα Windows 10 — δοκιμάζονται και οι δύο,
    /// ο σωστός πιάνει και ο άλλος επιστρέφει σφάλμα που αγνοείται.</summary>
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeOld = 19;

    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

    /// <summary>Ήταν σκούρο το θέμα την τελευταία φορά — ώστε ένα παράθυρο που ανοίγει αργότερα
    /// (Ιστορικό, Ρυθμίσεις, Χάρτης) να γεννιέται ήδη με τη σωστή μπάρα.</summary>
    private static bool _dark;

    /// <summary>
    /// Βάφει τη μπάρα ΠΡΙΝ εμφανιστεί το παράθυρο. Καλείται στον constructor κάθε παραθύρου, αμέσως μετά
    /// το InitializeComponent.
    ///
    /// Η στιγμή έχει σημασία: το <c>SourceInitialized</c> είναι το πρώτο σημείο όπου υπάρχει παράθυρο των
    /// Windows να βαφτεί, και συμβαίνει ΠΡΙΝ αυτό γίνει ορατό. Με το <c>Loaded</c> (που δοκιμάστηκε πρώτο)
    /// το παράθυρο προλάβαινε να εμφανιστεί με άσπρη μπάρα και να μαυρίσει μπροστά στα μάτια σου — ένα
    /// άσχημο τρεμόπαιγμα σε κάθε άνοιγμα.
    /// </summary>
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => Apply(window, _dark);
    }

    /// <summary>Δίχτυ ασφαλείας: πιάνει και όποιο παράθυρο δεν κάλεσε το <see cref="Attach"/> (π.χ.
    /// κάποιο που θα προστεθεί αργότερα και ξεχαστεί). Εκεί το τρεμόπαιγμα παραμένει, αλλά τουλάχιστον
    /// δεν μένει άσπρη μπάρα σε μαύρο θέμα.</summary>
    public static void HookNewWindows()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window)
                    Apply(window, _dark);
            }));
    }

    /// <summary>Εφαρμόζει το θέμα σε όλα τα ήδη ανοιχτά παράθυρα — καλείται από το ThemeManager.</summary>
    public static void ApplyToAll(bool dark)
    {
        _dark = dark;
        try
        {
            foreach (Window window in Application.Current.Windows)
                Apply(window, dark);
        }
        catch (Exception)
        {
            // Η συλλογή παραθύρων μπορεί να αλλάζει τη στιγμή που την διατρέχουμε — δεν πειράζει,
            // το επόμενο άνοιγμα παραθύρου θα το διορθώσει.
        }
    }

    private static void Apply(Window window, bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            var value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, UseImmersiveDarkModeOld, ref value, sizeof(int));

            // Το «ξαναζωγράφισε το πλαίσιο» ΜΟΝΟ σε παράθυρο που είναι ήδη στην οθόνη — δηλαδή όταν
            // αλλάζει θέμα ενώ δουλεύει ο ταμίας. Σε παράθυρο που δεν έχει εμφανιστεί ακόμα δεν
            // χρειάζεται (θα γεννηθεί με το σωστό χρώμα) και είναι ΕΠΙΚΙΝΔΥΝΟ: αναγκάζει τα Windows να
            // ξαναϋπολογίσουν το μέγεθος πριν γίνει το πρώτο layout, και σε παράθυρο που μετράει το ύψος
            // του από το περιεχόμενό του (SizeToContent — έτσι είναι η απόδειξη) το χαλάει. Αποτέλεσμα:
            // η απόδειξη έφευγε στον εκτυπωτή με λάθος ύψος και δεν έβγαινε χαρτί.
            if (window.IsVisible)
                SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        }
        catch (Exception)
        {
            // Παλιότερα Windows / DWM απενεργοποιημένο — μένει η προεπιλεγμένη μπάρα τίτλου.
        }
    }
}
