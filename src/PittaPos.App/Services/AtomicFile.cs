using System.IO;
using System.Text;

namespace PittaPos.App.Services;

/// <summary>
/// Γράφει αρχεία με ασφάλεια από απότομο κλείσιμο/διακοπή ρεύματος: γράφει πρώτα σε προσωρινό
/// αρχείο δίπλα στο τελικό και μετά το μετακινεί από πάνω του (atomic rename στον ίδιο δίσκο) —
/// έτσι ποτέ δεν μένει μισογραμμένο/χαλασμένο state αρχείο αν διακοπεί η εγγραφή στη μέση.
/// </summary>
public static class AtomicFile
{
    /// <summary>Ίδια κωδικοποίηση με το File.WriteAllText (UTF-8 χωρίς BOM) — τα αρχεία δεδομένων
    /// διαβάζονται από παντού αλλού με τα εργοστασιακά, δεν πρέπει να αλλάξει το πρώτο byte.</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string contents)
    {
        var tmp = path + ".tmp";

        // Το «γράψε δίπλα και μετονόμασε» από μόνο του ΔΕΝ αρκεί: το File.WriteAllText γυρίζει μόλις
        // τα δεδομένα μπουν στη μνήμη cache των Windows, ενώ η μετονομασία γράφεται στο ημερολόγιο του
        // NTFS. Σε διακοπή ρεύματος μπορεί να επιβιώσει η ΜΕΤΟΝΟΜΑΣΙΑ χωρίς τα ΔΕΔΟΜΕΝΑ — δηλαδή
        // ακριβώς το χαλασμένο (άδειο ή μισό) αρχείο που υποτίθεται ότι αποφεύγουμε, με τη διαφορά ότι
        // τώρα έχει σβήσει και το προηγούμενο καλό. Το Flush(true) κατεβάζει τα δεδομένα στον δίσκο
        // ΠΡΙΝ τη μετονομασία, οπότε η εγγύηση γίνεται αληθινή: ή το παλιό αρχείο ή το νέο, ποτέ μισό.
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using (var writer = new StreamWriter(stream, Utf8NoBom, leaveOpen: true))
                writer.Write(contents);
            stream.Flush(flushToDisk: true);
        }

        File.Move(tmp, path, overwrite: true);
    }
}
