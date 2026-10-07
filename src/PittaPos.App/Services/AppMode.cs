using System.IO;
using System.Text.Json;

namespace PittaPos.App.Services;

/// <summary>Ποια από τις τρεις εφαρμογές είναι ΑΥΤΗ η διεργασία (ίδιο exe, άλλο όρισμα).</summary>
public enum AppRole
{
    /// <summary>Το ταμείο, όπως πάντα.</summary>
    Till,
    /// <summary>ΖΩΝΤΑΝΕΣ ΠΑΡΑΓΓΕΛΙΕΣ (--live).</summary>
    LiveOrders,
    /// <summary>ΣΤΑΤΙΣΤΙΚΑ, μαζί με το Διάγραμμα και το Ιστορικό (--stats).</summary>
    Stats,
}

/// <summary>
/// Με τι ταυτότητα τρέχει ΑΥΤΗ η διεργασία. Το ίδιο ακριβώς exe είναι και το ταμείο και οι δύο
/// ξεχωριστές εφαρμογές — <b>ΖΩΝΤΑΝΕΣ ΠΑΡΑΓΓΕΛΙΕΣ</b> (<c>--live</c>) και <b>ΣΤΑΤΙΣΤΙΚΑ</b>
/// (<c>--stats</c>) — που ανοίγουν από δικές τους συντομεύσεις στην Επιφάνεια.
///
/// <para><b>Γιατί ίδιο exe και όχι ξεχωριστά προγράμματα:</b> οι οθόνες υπάρχουν ήδη και πρέπει να
/// δείχνουν ΑΚΡΙΒΩΣ ό,τι και το ταμείο. Ξεχωριστά προγράμματα θα ήταν αντίγραφα που ξεμένουν πίσω σε
/// κάθε αλλαγή, με δικό τους setup και δική τους έκδοση να συντηρούνται. Έτσι η οθόνη μένει μία,
/// ενημερώνεται μαζί με το ταμείο, από την ίδια εγκατάσταση — αλλά ανοίγει και κλείνει μόνη της.</para>
///
/// <para><b>Πώς βλέπουν τα δεδομένα:</b> ΔΕΝ διαβάζουν τα αρχεία του ταμείου. Δύο διεργασίες πάνω στα
/// ίδια JSON γράφουν η μία πάνω στην άλλη — αργά ή γρήγορα χάνεται μια παραγγελία. Αντί γι' αυτό,
/// μπαίνουν στη θέση του «δεύτερου ταμείου» (βλ. <see cref="RemoteSync"/>, δουλεύει χρόνια) και
/// ρωτάνε το ταμείο ΑΥΤΟΥ του υπολογιστή μέσω του API του, στο 127.0.0.1.</para>
///
/// <para>Τα δικά τους αρχεία (ρυθμίσεις, καταγραφή σφαλμάτων, προφίλ χάρτη) ζουν σε δικό τους
/// υποφάκελο, <c>%AppData%\PittaPos2\&lt;φάκελος ρόλου&gt;</c> — βλ. <see cref="AppIdentity.DataFolder"/>.</para>
/// </summary>
public static class AppMode
{
    public const string BoardArgument = "--live";
    public const string StatsArgument = "--stats";

    /// <summary>Ο ρόλος αυτής της διεργασίας. Ορίζεται ΜΙΑ φορά, πριν ανοίξει οτιδήποτε άλλο.</summary>
    public static AppRole Role { get; private set; } = AppRole.Till;

    /// <summary>Τρέχουμε ως εφαρμογή πίνακα ζωντανών παραγγελιών;</summary>
    public static bool IsBoard => Role == AppRole.LiveOrders;

    /// <summary>Τρέχουμε ως εφαρμογή στατιστικών;</summary>
    public static bool IsStats => Role == AppRole.Stats;

    /// <summary>Οποιαδήποτε από τις δύο «θεατές»: δεν κρατούν δεδομένα, ρωτούν το ταμείο.</summary>
    public static bool IsViewer => Role != AppRole.Till;

    /// <summary>Ποιος ρόλος ζητήθηκε από τη γραμμή εντολών (η συντόμευση).</summary>
    public static AppRole RoleFromArgs(string[] args)
    {
        if (Has(args, BoardArgument))
            return AppRole.LiveOrders;
        if (Has(args, StatsArgument))
            return AppRole.Stats;
        return AppRole.Till;

        static bool Has(string[] args, string flag) =>
            args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Υποφάκελος δεδομένων του ρόλου, ΜΕΣΑ στον φάκελο του καταστήματος — ώστε ένα αντίγραφο
    /// ασφαλείας ή μια απεγκατάσταση να τον πιάνει μαζί με τα υπόλοιπα.</summary>
    public static string FolderName => Role switch
    {
        AppRole.LiveOrders => "live-board",
        AppRole.Stats => "stats-app",
        _ => "",
    };

    /// <summary>Δικό του mutex μοναδικού instance: τρέχει ΜΑΖΙ με το ταμείο (άλλο όνομα), αλλά μία φορά.</summary>
    public static string Mutex => Role switch
    {
        AppRole.LiveOrders => "PittaPos2.Board.SingleInstance" + (DemoMode.IsOn ? ".Demo" : ""),
        AppRole.Stats => "PittaPos2.Stats.SingleInstance" + (DemoMode.IsOn ? ".Demo" : ""),
        _ => AppIdentity.SingleInstanceMutex,
    };

    /// <summary>Το σήμα «έλα μπροστά», όταν η εφαρμογή είναι ήδη ανοιχτή και ξαναπατηθεί η συντόμευση —
    /// αλλιώς το δεύτερο πάτημα δεν θα έκανε τίποτα ορατό (βλ. <see cref="BoardActivation"/>).</summary>
    public static string ActivateEvent => Role switch
    {
        AppRole.LiveOrders => "PittaPos2.Board.Activate",
        AppRole.Stats => "PittaPos2.Stats.Activate",
        _ => "PittaPos2.Till.Activate",
    } + (DemoMode.IsOn ? ".Demo" : "");

    /// <summary>Ποιο δικαίωμα ζητάει η εφαρμογή στο άνοιγμα (βλ. <see cref="StaffRight"/>).</summary>
    public static string RequiredRight => Role == AppRole.Stats ? StaffRight.Stats : StaffRight.LiveOrders;

    /// <summary>
    /// Στήνει τη διεργασία σε ρόλο «θεατή». Καλείται ΠΡΩΤΗ, πριν αγγίξει κανείς ρυθμίσεις ή δεδομένα:
    /// από δω και πέρα κάθε store χτίζει τη διαδρομή του πάνω στον δικό μας υποφάκελο.
    /// </summary>
    public static void EnableViewer(AppRole role)
    {
        Role = role;
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(folder);

        // Χάρτης Διανομής (WebView2): δύο διεργασίες με το ίδιο προφίλ βγάζουν «Access Denied» στη
        // δεύτερη — το ίδιο σφάλμα που περιγράφεται στον έλεγχο μοναδικού instance (βλ. App.OnStartup).
        // Δικός της φάκελος, δικό της προφίλ, και ο χάρτης ανοίγει από παντού.
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(folder, "webview2"));

        SeedSettings(folder);
    }

    /// <summary>
    /// Οι ρυθμίσεις της εφαρμογής: αντίγραφο των ρυθμίσεων του ταμείου, με δύο αλλαγές — «δεύτερο
    /// ταμείο» προς το 127.0.0.1. Ξαναγράφεται σε ΚΑΘΕ άνοιγμα ώστε να ακολουθεί το ταμείο (π.χ.
    /// άλλαξε εκτυπωτής: η αναφορά διανομέα πρέπει να βγει από τον ίδιο).
    ///
    /// <para>Πάντα 127.0.0.1, ακόμα κι όταν το ταμείο αυτού του υπολογιστή είναι το ΔΕΥΤΕΡΟ ταμείο: το
    /// ερώτημα φεύγει τότε από εκείνο προς το κύριο, όπως φεύγει ούτως ή άλλως κάθε παραγγελία του. Έτσι
    /// δεν εξαρτόμαστε από IP που αλλάζουν στο δίκτυο του μαγαζιού — μιλάμε πάντα στο ταμείο δίπλα μας.</para>
    /// </summary>
    private static void SeedSettings(string folder)
    {
        try
        {
            var tillPath = Path.Combine(AppIdentity.TillFolder, "settings.json");
            var settings = File.Exists(tillPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(tillPath)) ?? new AppSettings()
                : new AppSettings();
            settings.NetworkMode = "client";
            settings.HostAddress = "127.0.0.1";
            AtomicFile.WriteAllText(
                Path.Combine(folder, "settings.json"),
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            // Δεν εμποδίζει το άνοιγμα: χωρίς αντίγραφο ξεκινά με προεπιλογές και συνεχίζει να ρωτάει
            // κανονικά το ταμείο (τις κοινές ρυθμίσεις τις τραβάει ούτως ή άλλως από εκεί).
            AppLog.Write("viewer", "Δεν αντιγράφηκαν οι ρυθμίσεις του ταμείου: " + ex.Message);
        }
    }
}
