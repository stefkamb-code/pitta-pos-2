using System.IO;

namespace PittaPos.App.Services;

/// <summary>
/// Ό,τι ξεχωρίζει ΑΥΤΟ το κατάστημα από το άλλο. Η εφαρμογή είναι η ίδια «Πίττα POS», αλλά δύο
/// καταστήματα δεν πρέπει ΠΟΤΕ να γράφουν στα ίδια αρχεία ούτε να πιάνουν την ίδια θύρα — αλλιώς
/// οι παραγγελίες, τα στατιστικά και οι πελάτες του ενός μαγαζιού μπερδεύονται με του άλλου.
///
/// Αν ποτέ στηθεί και τρίτο κατάστημα, αντιγράφεται ο φάκελος και αλλάζουν ΜΟΝΟ οι τιμές εδώ.
/// </summary>
public static class AppIdentity
{
    /// <summary>Φιλική ονομασία του καταστήματος — φαίνεται σε τίτλους παραθύρων και μηνύματα.</summary>
    public const string StoreName = "Κατάστημα 2";

    /// <summary>
    /// Υποφάκελος μέσα στο %AppData% όπου ζουν ΟΛΑ τα δεδομένα αυτού του καταστήματος
    /// (παραγγελίες, στατιστικά, πελάτες, ρυθμίσεις, μενού, αρχείο, αναφορές).
    /// Το πρώτο κατάστημα χρησιμοποιεί "PittaPos" — μην το ξαναβάλεις εδώ.
    /// </summary>
    public const string StoreDataFolder = "PittaPos2";

    /// <summary>
    /// Ο φάκελος δεδομένων ΑΥΤΗΣ της διεργασίας. Για το ταμείο είναι ο φάκελος του καταστήματος, όπως
    /// πάντα. Για τις ξεχωριστές εφαρμογές (Ζωντανές Παραγγελίες, Στατιστικά) είναι δικός τους υποφάκελος μέσα του
    /// (βλ. <see cref="AppMode"/>): εκείνη δεν κρατά δεδομένα — τα ρωτάει από το ταμείο — και δεν πρέπει
    /// να γράφει ΠΟΤΕ πάνω στα αρχεία του, γιατί δύο διεργασίες στο ίδιο JSON σβήνουν η μία την άλλη.
    /// </summary>
    /// <remarks>Στο demo (βλ. <see cref="DemoMode"/>) είναι ΑΠΟΛΥΤΗ διαδρομή δίπλα στο exe: το
    /// <c>Path.Combine(%AppData%, DataFolder)</c> κάθε store κρατά τότε μόνο το δεύτερο μέρος, οπότε κανένα αρχείο δεν
    /// πηγαίνει στο %AppData%.</remarks>
    public static string DataFolder => DemoMode.IsOn
        ? (AppMode.IsViewer ? Path.Combine(DemoMode.DataRoot, AppMode.FolderName) : DemoMode.DataRoot)
        : AppMode.IsViewer
            ? Path.Combine(StoreDataFolder, AppMode.FolderName)
            : StoreDataFolder;

    /// <summary>Ο φάκελος δεδομένων του ΤΑΜΕΙΟΥ (απόλυτη διαδρομή), όποιος κι αν είναι ο ρόλος αυτής της διεργασίας.</summary>
    public static string TillFolder => DemoMode.IsOn
        ? DemoMode.DataRoot
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StoreDataFolder);

    /// <summary>
    /// Όνομα του mutex μοναδικού instance. Πρέπει να διαφέρει από του πρώτου καταστήματος, αλλιώς
    /// η δεύτερη εφαρμογή νομίζει ότι «τρέχει ήδη» και δεν ανοίγει καθόλου.
    /// </summary>
    public static string SingleInstanceMutex => DemoMode.IsOn ? "PittaPos2.App.SingleInstance.Demo" : "PittaPos2.App.SingleInstance";

    /// <summary>
    /// Θύρα του ενσωματωμένου server (κινητό σερβιτόρου + συγχρονισμός δεύτερου ταμείου).
    /// Το πρώτο κατάστημα ακούει στο 5190 — δύο εφαρμογές στην ίδια θύρα δεν σηκώνονται μαζί.
    /// </summary>
    public static int ApiPort => DemoMode.IsOn ? 5291 : 5191;
}
