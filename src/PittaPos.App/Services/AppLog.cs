using System.IO;

namespace PittaPos.App.Services;

/// <summary>Κοινόχρηστη καταγραφή σε %AppData%\PittaPos\crash-log.txt, ώστε προβλήματα που δεν
/// ρίχνουν την εφαρμογή (π.χ. ο server δεν άνοιξε, το δεύτερο ταμείο δεν φτάνει το κύριο) να μένουν
/// κάπου γραμμένα για να τα δει κανείς εκ των υστέρων στο μαγαζί, αντί να χάνονται σιωπηλά.</summary>
public static class AppLog
{
    /// <summary>Πάνω από αυτό το μέγεθος το αρχείο αρχειοθετείται σε .old και ξεκινά καινούριο —
    /// ένα ταμείο δουλεύει χρόνια χωρίς να το αγγίξει κανείς, χωρίς όριο το αρχείο θα μεγάλωνε
    /// ασταμάτητα. Κρατιέται ΕΝΑ προηγούμενο, ώστε να μη χάνεται το ιστορικό μόλις γεμίσει.</summary>
    private const long MaxBytes = 1024 * 1024;

    /// <summary>Γράφουν και το UI thread και threads του Kestrel/δικτύου. Χωρίς κλείδωμα, δύο
    /// ταυτόχρονες εγγραφές έριχναν IOException («το αρχείο χρησιμοποιείται») και η καταγραφή
    /// χανόταν σιωπηλά — ακριβώς τη στιγμή που έχει φόρτο, δηλαδή όταν τη χρειάζεσαι περισσότερο.</summary>
    private static readonly object Gate = new();

    public static void Write(string source, string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.DataFolder);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash-log.txt");

            lock (Gate)
            {
                RotateIfTooBig(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] {message}\n\n");
            }
        }
        catch (Exception)
        {
            // Αν δεν γίνεται ούτε η καταγραφή, ας μη ρίξει άλλη εξαίρεση ο ίδιος ο logger
        }
    }

    private static void RotateIfTooBig(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxBytes)
                return;
            File.Move(path, path + ".old", overwrite: true);
        }
        catch (Exception)
        {
            // Αποτυχία αρχειοθέτησης δεν πρέπει να εμποδίσει την ίδια την καταγραφή
        }
    }
}
