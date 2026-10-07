using System.IO;

namespace PittaPos.App.Services;

/// <summary>
/// Λειτουργία ΠΑΡΟΥΣΙΑΣΗΣ: το ίδιο ακριβώς πρόγραμμα, με δικά του δεδομένα δίπλα στο exe.
///
/// <para>Ανάβει ΜΟΝΟ όταν υπάρχει το αρχείο <see cref="MarkerFile"/> δίπλα στο exe — το setup του μαγαζιού δεν το έχει
/// ποτέ, οπότε ένα ταμείο μαγαζιού δεν μπορεί να βρεθεί κατά λάθος σε demo. Στο demo:</para>
/// <list type="bullet">
/// <item>Όλα τα δεδομένα (παραγγελίες, αρχείο, ρυθμίσεις, κατάλογος) ζουν στο <c>demo-data</c> δίπλα στο exe — ΤΙΠΟΤΑ
///   δεν διαβάζεται ή γράφεται στο %AppData%, άρα ούτε τα δεδομένα ενός πραγματικού ταμείου στον ίδιο υπολογιστή.</item>
/// <item>Άλλη θύρα (5291) και άλλο όνομα «μοναδικού παραθύρου»: ανοίγει και δίπλα σε πραγματικό ταμείο.</item>
/// <item>Ο server ακούει μόνο στον ίδιο τον υπολογιστή (127.0.0.1) — κανένα παράθυρο του τείχους προστασίας στη μέση
///   της παρουσίασης.</item>
/// <item>Το ιστορικό δεν ανεβαίνει στο site: το demo του site έχει ήδη τις ίδιες μέρες.</item>
/// </list>
/// </summary>
public static class DemoMode
{
    public const string MarkerFile = "pittapos-demo.txt";

    public static bool IsOn { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, MarkerFile));

    /// <summary>Ο φάκελος δεδομένων του demo (απόλυτη διαδρομή) — βλ. <see cref="AppIdentity.DataFolder"/>.</summary>
    public static string DataRoot => Path.Combine(AppContext.BaseDirectory, "demo-data");
}
