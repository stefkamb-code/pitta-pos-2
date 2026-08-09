using System.IO;

namespace PittaPos.App.Services;

/// <summary>
/// Γράφει αρχεία με ασφάλεια από απότομο κλείσιμο/διακοπή ρεύματος: γράφει πρώτα σε προσωρινό
/// αρχείο δίπλα στο τελικό και μετά το μετακινεί από πάνω του (atomic rename στον ίδιο δίσκο) —
/// έτσι ποτέ δεν μένει μισογραμμένο/χαλασμένο state αρχείο αν διακοπεί η εγγραφή στη μέση.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }
}
