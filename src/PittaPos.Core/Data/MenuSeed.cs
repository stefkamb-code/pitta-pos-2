using PittaPos.Core.Models;

namespace PittaPos.Core.Data;

/// <summary>
/// Ο ΒΑΣΙΚΟΣ ΚΑΤΑΛΟΓΟΣ — από αυτόν ξεκινά κάθε νέα εγκατάσταση, την πρώτη και μόνη φορά που δεν
/// υπάρχει ακόμα <c>menu.json</c> (βλ. <c>MenuStore.Load</c>). Σε υπάρχον κατάστημα δεν παίζει
/// κανένα ρόλο: ο κατάλογός του ζει στο <c>%AppData%</c> και δεν ξαναγράφεται ποτέ από εδώ.
///
/// <para>Το αρχείο αυτό έχει ΜΟΝΟ τη λογική (ψωμί, διπλή πίτα, σύνθεση ονομάτων). Τα ίδια τα
/// δεδομένα — κατηγορίες, προϊόντα, έξτρα, υλικά — είναι στο <c>MenuSeed.Catalogue.cs</c>, που
/// ΠΑΡΑΓΕΤΑΙ από τον πραγματικό κατάλογο ενός μαγαζιού με το <c>scripts/menu-to-seed.py</c>.
/// Μην τα γράφεις με το χέρι.</para>
/// </summary>
public static partial class MenuSeed
{
    public static readonly IReadOnlyList<string> BreadOptions = ["Ελληνική", "Αραβική", "Ψωμί"];

    // NOTE: Οι επιλογές υλικών/έξτρα του e-food είναι κλειδωμένες πίσω από διεύθυνση παράδοσης
    // και δεν ήταν δυνατό να αντληθούν. Αυτές παραμένουν όπως στο αρχικό design — αντικατάστησέ
    // τες με τις πραγματικές του καταστήματος όταν τις έχουμε.
    /// <summary>Ποιες customizable κατηγορίες έχουν καθόλου επιλογή ψωμιού (Ελληνική/Αραβική/Ψωμί) —
    /// άλλες (π.χ. ΠΙΤΤΑ CLUB) έχουν μόνο υλικά/έξτρα, χωρίς ψωμί.</summary>
    public static bool HasBreadChoice(string categoryLabel) =>
        categoryLabel is "ΤΥΛΙΧΤΑ" or "ΠΙΤΤΕΣ" or "ΠΙΤΤΕΣ ΠΑΠΠΟΥ" or "ΜΕΡΙΔΕΣ" or "ΜΕΡΙΔΕΣ ΠΑΠΠΟΥ";

    /// <summary>Μόνο τα ΤΥΛΙΧΤΑ/ΠΙΤΤΕΣ δείχνουν το ψωμί χωνεμένο στο ίδιο το όνομα (π.χ. «ΑΡ. Κοτόπουλο») — οι
    /// ΜΕΡΙΔΕΣ/ΜΕΡΙΔΕΣ ΠΑΠΠΟΥ το δείχνουν σε ξεχωριστή γραμμή από κάτω (π.χ. «Μερίδα κοτόπουλο» / «Αραβική»),
    /// όπως ήταν πριν φτιάξουμε τη σύντμηση στο όνομα — δεν βγάζει νόημα «ΑΡ. Μερίδα κοτόπουλο».</summary>
    public static bool FuseBreadIntoName(string categoryLabel) =>
        categoryLabel is "ΤΥΛΙΧΤΑ" or "ΠΙΤΤΕΣ" or "ΠΙΤΤΕΣ ΠΑΠΠΟΥ";

    /// <summary>Συντομογραφία ψωμιού για να φαίνεται άμεσα μέσα στο όνομα της γραμμής παραγγελίας.</summary>
    public static string BreadAbbreviation(string bread) => bread switch
    {
        "Ελληνική" => "ΕΛ.",
        "Αραβική" => "ΑΡ.",
        "Ψωμί" => "Ψ.",
        _ => bread.Length > 0 ? bread[..1].ToUpperInvariant() + "." : "",
    };

    /// <summary>Όνομα γραμμής παραγγελίας με το ψωμί μπροστά σε συντομογραφία, π.χ. «ΑΡ. Κοτόπουλο»
    /// αντί για «Πίττα κοτόπουλο (ΑΡ.)» — η λέξη «Πίττα» βγαίνει (είναι ήδη προφανές ότι είναι πίττα
    /// αφού έχει ψωμί), κρατώντας μόνο ό,τι διαφοροποιεί το προϊόν.</summary>
    public static string ComposeCustomizedName(string productName, string bread)
    {
        const string prefix = "Πίττα ";
        var rest = productName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? productName[prefix.Length..]
            : productName;
        return BreadAbbreviation(bread) + " " + rest;
    }

    /// <summary>Ποιες κατηγορίες προσφέρουν την επιλογή «διπλή πίτα» στον customizer — ΤΥΛΙΧΤΑ (νέο όνομα
    /// «ΠΙΤΤΕΣ») και ΚΛΑΣΙΚΑ ΜΙΝΙ, τα δύο πιτόψωμα του μαγαζιού. Δέχονται και τα παλιά ονόματα
    /// («ΚΛΑΣΙΚΑ ΜΙΚΡΑ», «ΤΥΛΙΧΤΑ») — το menu.json ήδη αποθηκευμένο στο μηχάνημα του μαγαζιού δεν
    /// ξαναγράφεται μόνο του όταν μετονομάζεται μια κατηγορία, οπότε ένα ήδη υπάρχον κατάστημα μπορεί να
    /// έχει είτε το παλιό είτε το νέο όνομα και πρέπει να δουλεύουν και τα δύο.</summary>
    public static bool SupportsDoublePita(string categoryLabel) =>
        categoryLabel is "ΤΥΛΙΧΤΑ" or "ΠΙΤΤΕΣ" or "ΚΛΑΣΙΚΑ ΜΙΝΙ" or "ΚΛΑΣΙΚΑ ΜΙΚΡΑ";

    private const string DoublePitaLabel = "ΔΙΠΛΗ ΠΙΤΑ";

    /// <summary>Όνομα γραμμής παραγγελίας όταν είναι επιλεγμένη διπλή πίτα, π.χ. «ΕΛ. ΔΙΠΛΗ ΠΙΤΑ Κοτόπουλο»
    /// στα ΤΥΛΙΧΤΑ (ψωμί + ΔΙΠΛΗ ΠΙΤΑ μπροστά, ίδια λογική με το <see cref="ComposeCustomizedName"/>) ή
    /// «ΜΙΝΙ ΔΙΠΛΗ ΠΙΤΑ κοτόπουλο» στα ΚΛΑΣΙΚΑ ΜΙΝΙ (αντικαθιστά το «Μίνι» του ονόματος ώστε να μη
    /// διπλασιάζεται η λέξη).</summary>
    public static string ComposeDoublePitaName(string productName, string categoryLabel, string bread)
    {
        if (categoryLabel is "ΤΥΛΙΧΤΑ" or "ΠΙΤΤΕΣ" or "ΠΙΤΤΕΣ ΠΑΠΠΟΥ")
        {
            const string prefix = "Πίττα ";
            var rest = productName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? productName[prefix.Length..]
                : productName;
            return BreadAbbreviation(bread) + " " + DoublePitaLabel + " " + rest;
        }

        // Δέχεται και το παλιό «Μικρό» πρόθεμα (πριν τη μετονομασία σε «Μίνι») για τον ίδιο λόγο.
        string[] miniPrefixes = ["Μίνι ", "Μικρό "];
        var miniRest = productName;
        foreach (var prefix in miniPrefixes)
        {
            if (productName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                miniRest = productName[prefix.Length..];
                break;
            }
        }
        return "ΜΙΝΙ " + DoublePitaLabel + " " + miniRest;
    }

    /// <summary>Περιγραφή αφαιρεμένων υλικών για απόδειξη/ετικέτα παραγγελίας. Με λίγες αφαιρέσεις
    /// («χωρίς ντομάτα · χωρίς κρεμμύδι») διαβάζεται εύκολα, αλλά από 3 υλικά και πάνω η αράδα μεγαλώνει
    /// υπερβολικά — πιο σαφές να πει τι ΕΜΕΙΝΕ («μόνο με») παρά τι λείπει. Αν αφαιρέθηκαν όλα, είναι ΣΚΕΤΟ.
    /// <para><paramref name="included"/> = τα βασικά υλικά ΤΟΥ ΣΥΓΚΕΚΡΙΜΕΝΟΥ προϊόντος
    /// (MenuStore.IngredientsFor). Πριν διάβαζε πάντα τη σταθερή <see cref="IncludedIngredients"/>,
    /// οπότε ό,τι υλικό πρόσθετε ο χρήστης από τη Διαχείριση δεν έβγαινε ποτέ στο «μόνο με:» της
    /// απόδειξης και το «σκέτο» μετριόταν με λάθος σύνολο.</para></summary>
    public static IEnumerable<string> DescribeRemovedIngredients(
        IReadOnlyCollection<string> removed, IReadOnlyCollection<string> included)
    {
        if (removed.Count == 0)
            return [];
        if (included.Count > 0 && removed.Count >= included.Count)
            return ["σκέτο"];
        if (removed.Count >= 3)
        {
            // "μόνο με" σε δική του γραμμή, μετά ένα υλικό ανά γραμμή — ίδια κάθετη λογική με το
            // χωρίς/έξτρα, ώστε ο υπάλληλος να τα διαβάζει το ένα κάτω απ' το άλλο, όχι σε μία αράδα.
            var remaining = included.Where(i => !removed.Contains(i)).Select(Lowercase);
            return new[] { "μόνο με:" }.Concat(remaining);
        }
        // Ίδια μορφή με το «μόνο με»: η λέξη-κλειδί σε δική της γραμμή με άνω κάτω τελεία και από κάτω
        // ένα υλικό ανά γραμμή. Πριν επαναλαμβανόταν το «χωρίς» σε κάθε γραμμή («χωρίς κρεμμύδι»,
        // «χωρίς ντομάτα»), που έπιανε τον ίδιο χώρο λέγοντας δύο φορές το ίδιο.
        return new[] { "χωρίς:" }.Concat(removed.Select(Lowercase));
    }

    private static string Lowercase(string s) => s.Length > 0 ? char.ToLower(s[0]) + s[1..] : s;

    /// <summary>Κεφαλαία για ευανάγνωστο ταμείο/απόδειξη — και αφαιρεί τον τόνο από τα φωνήεντα
    /// (ΆΈΉΊΌΎΏ→ΑΕΗΙΟΥΩ), όπως γράφονται παραδοσιακά τα κεφαλαία στα ελληνικά, όχι όπως τα βγάζει ωμά
    /// το ToUpper. Χρησιμοποιείται τόσο στο καλάθι επί οθόνης (ProductsView) όσο και στην απόδειξη
    /// (ReceiptWindow) — μόνο για εμφάνιση, ποτέ δεν αποθηκεύεται έτσι (Ιστορικό/Στατιστικά μένουν κανονικά).</summary>
    public static string ToUpperGreek(string text)
    {
        if (text.Length == 0)
            return text;
        var upper = text.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("el-GR"));
        return upper
            .Replace('Ά', 'Α').Replace('Έ', 'Ε').Replace('Ή', 'Η').Replace('Ί', 'Ι')
            .Replace('Ό', 'Ο').Replace('Ύ', 'Υ').Replace('Ώ', 'Ω');
    }

    public static readonly IReadOnlyList<string> PickupTimes = ["Άμεσα", "15'", "30'", "45'"];

    public const int TableCount = 12;
}
