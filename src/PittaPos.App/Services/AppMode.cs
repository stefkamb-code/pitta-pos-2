using System.IO;
using System.Text.Json;

namespace PittaPos.App.Services;

/// <summary>
/// Με τι ταυτότητα τρέχει ΑΥΤΗ η διεργασία: το ταμείο (όπως πάντα) ή η ξεχωριστή εφαρμογή
/// <b>ΖΩΝΤΑΝΕΣ ΠΑΡΑΓΓΕΛΙΕΣ</b> — ίδιο ακριβώς exe, δική της συντόμευση στην Επιφάνεια, με το όρισμα
/// <c>--live</c>.
///
/// <para><b>Γιατί ίδιο exe και όχι δεύτερο πρόγραμμα:</b> ο πίνακας υπάρχει ήδη
/// (LiveOrdersWindow/LiveOrdersViewModel) και πρέπει να δείχνει ΑΚΡΙΒΩΣ ό,τι και το ταμείο. Ένα
/// ξεχωριστό πρόγραμμα θα ήταν αντίγραφο που ξεμένει πίσω σε κάθε αλλαγή, με δεύτερο setup και δεύτερη
/// έκδοση να συντηρούνται. Έτσι η οθόνη μένει μία, ενημερώνεται μαζί με το ταμείο, από την ίδια
/// εγκατάσταση — αλλά ανοίγει και κλείνει μόνη της, σε δικό της παράθυρο και δική της διεργασία.</para>
///
/// <para><b>Πώς βλέπει τα δεδομένα:</b> ΔΕΝ διαβάζει τα αρχεία του ταμείου. Δύο διεργασίες πάνω στα
/// ίδια JSON γράφουν η μία πάνω στην άλλη — αργά ή γρήγορα χάνεται μια παραγγελία. Αντί γι' αυτό,
/// μπαίνει στη θέση του «δεύτερου ταμείου» (βλ. <see cref="RemoteSync"/>, δουλεύει χρόνια) και ρωτάει
/// το ταμείο ΑΥΤΟΥ του υπολογιστή μέσω του API του, στο 127.0.0.1. Άρα: ό,τι βλέπει το ταμείο, μέσα σε
/// δευτερόλεπτα, και κάθε «ΠΕΡΑΣΕ ΤΗΝ ΣΕ» γράφεται στο ταμείο και όχι σε δικό της αντίγραφο.</para>
///
/// <para>Τα δικά της αρχεία (ρυθμίσεις, καταγραφή σφαλμάτων, προφίλ χάρτη) ζουν σε δικό της
/// υποφάκελο, <c>%AppData%\PittaPos2\live-board</c> — βλ. <see cref="AppIdentity.DataFolder"/>.</para>
/// </summary>
public static class AppMode
{
    /// <summary>Το όρισμα της συντόμευσης «Ζωντανές Παραγγελίες».</summary>
    public const string BoardArgument = "--live";

    /// <summary>Δικό της mutex μοναδικού instance: τρέχει ΜΑΖΙ με το ταμείο (άλλο mutex), αλλά μία φορά.</summary>
    public const string BoardMutex = "PittaPos2.Board.SingleInstance";

    /// <summary>Το σήμα «έλα μπροστά», όταν ο πίνακας είναι ήδη ανοιχτός και τον ζητήσει ξανά είτε το
    /// κουμπί του ταμείου είτε δεύτερο διπλό κλικ στη συντόμευση — αλλιώς το πάτημα δεν θα έκανε τίποτα
    /// ορατό (βλ. LiveBoardLauncher).</summary>
    public const string BoardActivateEvent = "PittaPos2.Board.Activate";

    /// <summary>Υποφάκελος δεδομένων της εφαρμογής πίνακα, ΜΕΣΑ στον φάκελο του καταστήματος — ώστε ένα
    /// αντίγραφο ασφαλείας ή μια απεγκατάσταση να τον πιάνει μαζί με τα υπόλοιπα.</summary>
    public const string BoardFolderName = "live-board";

    /// <summary>Τρέχουμε ως εφαρμογή πίνακα; Ορίζεται ΜΙΑ φορά, πριν ανοίξει οτιδήποτε άλλο.</summary>
    public static bool IsBoard { get; private set; }

    /// <summary>Ζητήθηκε ο πίνακας από τη γραμμή εντολών;</summary>
    public static bool WantsBoard(string[] args) =>
        args.Any(a => string.Equals(a, BoardArgument, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Στήνει τη διεργασία ως εφαρμογή πίνακα. Καλείται ΠΡΩΤΗ, πριν αγγίξει κανείς ρυθμίσεις ή δεδομένα:
    /// από δω και πέρα κάθε store χτίζει τη διαδρομή του πάνω στον δικό μας υποφάκελο.
    /// </summary>
    public static void EnableBoard()
    {
        IsBoard = true;
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
        Directory.CreateDirectory(folder);

        // Χάρτης Διανομής (WebView2): δύο διεργασίες με το ίδιο προφίλ βγάζουν «Access Denied» στη
        // δεύτερη — το ίδιο σφάλμα που περιγράφεται στον έλεγχο μοναδικού instance (βλ. App.OnStartup).
        // Δικός της φάκελος, δικό της προφίλ, και ο χάρτης ανοίγει και από τις δύο.
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(folder, "webview2"));

        SeedSettings(folder);
    }

    /// <summary>
    /// Οι ρυθμίσεις της εφαρμογής πίνακα: αντίγραφο των ρυθμίσεων του ταμείου, με δύο αλλαγές —
    /// «δεύτερο ταμείο» προς το 127.0.0.1. Ξαναγράφεται σε ΚΑΘΕ άνοιγμα ώστε να ακολουθεί το ταμείο
    /// (π.χ. άλλαξε εκτυπωτής: η αναφορά διανομέα πρέπει να βγει από τον ίδιο).
    ///
    /// <para>Πάντα 127.0.0.1, ακόμα κι όταν το ταμείο αυτού του υπολογιστή είναι το ΔΕΥΤΕΡΟ ταμείο: το
    /// ερώτημα φεύγει τότε από εκείνο προς το κύριο, όπως φεύγει ούτως ή άλλως κάθε παραγγελία του. Έτσι
    /// ο πίνακας δεν εξαρτάται από IP που αλλάζουν στο δίκτυο του μαγαζιού — μιλάει πάντα στο ταμείο που
    /// κάθεται δίπλα του.</para>
    /// </summary>
    private static void SeedSettings(string boardFolder)
    {
        try
        {
            var tillPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppIdentity.StoreDataFolder, "settings.json");
            var settings = File.Exists(tillPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(tillPath)) ?? new AppSettings()
                : new AppSettings();
            settings.NetworkMode = "client";
            settings.HostAddress = "127.0.0.1";
            AtomicFile.WriteAllText(
                Path.Combine(boardFolder, "settings.json"),
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            // Δεν εμποδίζει το άνοιγμα: χωρίς αντίγραφο ο πίνακας ξεκινά με προεπιλογές και συνεχίζει
            // να ρωτάει κανονικά το ταμείο (τις κοινές ρυθμίσεις τις τραβάει ούτως ή άλλως από εκεί).
            AppLog.Write("live-board", "Δεν αντιγράφηκαν οι ρυθμίσεις του ταμείου: " + ex.Message);
        }
    }
}
