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
        return BreadAbbreviation(bread) + " " + WithoutPrefix(productName, "Πίττα ");
    }

    /// <summary>
    /// Κόβει το πρόθεμα αν υπάρχει, αγνοώντας πεζά/κεφαλαία <b>ΚΑΙ ΤΟΝΟΥΣ</b>.
    ///
    /// Το σκέτο <c>OrdinalIgnoreCase</c> δεν αρκεί στα ελληνικά: το «Μίνι» έχει τόνο, ενώ τα ελληνικά
    /// κεφαλαία γράφονται χωρίς («ΜΙΝΙ»). Οπότε ένα όνομα εκτύπωσης γραμμένο κεφαλαία δεν αναγνωριζόταν
    /// ως «Μίνι …» και η λέξη έμενε ΔΥΟ φορές πάνω στο χαρτί: «ΜΙΝΙ ΔΙΠΛΗ ΠΙΤΑ ΜΙΝΙ ΚΟΤΟΠΟΥΛΟ».
    /// Το ίδιο και με το «Πίττα».
    /// </summary>
    private static string WithoutPrefix(string text, string prefix)
    {
        if (text.Length < prefix.Length)
            return text;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (FoldGreek(text[i]) != FoldGreek(prefix[i]))
                return text;
        }
        return text[prefix.Length..];
    }

    /// <summary>Κεφαλαίο χωρίς τόνο — μόνο για σύγκριση, δεν αλλάζει τίποτα από όσα βλέπει ο πελάτης.</summary>
    private static char FoldGreek(char c) => char.ToUpperInvariant(c) switch
    {
        'Ά' => 'Α',
        'Έ' => 'Ε',
        'Ή' => 'Η',
        'Ί' or 'Ϊ' => 'Ι',
        'Ό' => 'Ο',
        'Ύ' or 'Ϋ' => 'Υ',
        'Ώ' => 'Ω',
        var upper => upper,
    };

    /// <summary>Ποιες κατηγορίες προσφέρουν την επιλογή «διπλή πίτα» στον customizer — ΤΥΛΙΧΤΑ (νέο όνομα
    /// «ΠΙΤΤΕΣ») και ΚΛΑΣΙΚΑ ΜΙΝΙ, τα δύο πιτόψωμα του μαγαζιού. Δέχονται και τα παλιά ονόματα
    /// («ΚΛΑΣΙΚΑ ΜΙΚΡΑ», «ΤΥΛΙΧΤΑ») — το menu.json ήδη αποθηκευμένο στο μηχάνημα του μαγαζιού δεν
    /// ξαναγράφεται μόνο του όταν μετονομάζεται μια κατηγορία, οπότε ένα ήδη υπάρχον κατάστημα μπορεί να
    /// έχει είτε το παλιό είτε το νέο όνομα και πρέπει να δουλεύουν και τα δύο.</summary>
    public static bool SupportsDoublePita(string categoryLabel) =>
        categoryLabel is "ΤΥΛΙΧΤΑ" or "ΠΙΤΤΕΣ" or "ΚΛΑΣΙΚΑ ΜΙΝΙ" or "ΚΛΑΣΙΚΑ ΜΙΚΡΑ";

    /// <summary>
    /// Μαντεύει την κατηγορία ΦΠΑ από το όνομα, ΜΟΝΟ ως αρχική τιμή για κατάλογο που δεν την έχει
    /// δηλωμένη (βλ. MenuStore.MigrateCategoryFlags). Από εκεί και πέρα γράφεται ρητά στο menu.json και
    /// φαίνεται/διορθώνεται στη Διαχείριση Καταλόγου — μια μετονομασία κατηγορίας δεν πρέπει να αλλάζει
    /// σιωπηλά τον φόρο.
    /// </summary>
    public static VatKind GuessVatKind(string categoryLabel)
    {
        var name = categoryLabel.ToUpperInvariant();
        if (name.Contains("ΑΝΑΨΥΚΤ"))
            return VatKind.SoftDrink;
        if (name.Contains("ΠΟΤΑ") || name.Contains("ΜΠΥΡ") || name.Contains("ΜΠΙΡ") || name.Contains("ΚΡΑΣ")
            || name.Contains("ΟΥΖ") || name.Contains("ΤΣΙΠΟΥΡ") || name.Contains("ΡΑΚ"))
            return VatKind.Alcohol;
        return VatKind.Food;
    }

    /// <summary>Μεγάλη ή μικρή πίτα, μαντεμένο από το όνομα — μόνο ως αρχική τιμή για κατηγορία που
    /// δεν το έχει δηλωμένο (βλ. MenuCategory.DoublePitaLarge). Μικρή είναι μόνο τα «μίνι/μικρά».</summary>
    public static bool GuessLargePita(string categoryLabel)
    {
        var name = categoryLabel.ToUpperInvariant();
        return !name.Contains("ΜΙΚΡ") && !name.Contains("ΜΙΝΙ");
    }

    private const string DoublePitaLabel = "ΔΙΠΛΗ ΠΙΤΑ";

    /// <summary>Όνομα γραμμής παραγγελίας όταν είναι επιλεγμένη διπλή πίτα, π.χ. «ΕΛ. ΔΙΠΛΗ ΠΙΤΑ Κοτόπουλο»
    /// στα ΤΥΛΙΧΤΑ (ψωμί + ΔΙΠΛΗ ΠΙΤΑ μπροστά, ίδια λογική με το <see cref="ComposeCustomizedName"/>) ή
    /// «ΜΙΝΙ ΔΙΠΛΗ ΠΙΤΑ κοτόπουλο» στα ΚΛΑΣΙΚΑ ΜΙΝΙ (αντικαθιστά το «Μίνι» του ονόματος ώστε να μη
    /// διπλασιάζεται η λέξη).</summary>
    public static string ComposeDoublePitaName(string productName, string categoryLabel, string bread)
    {
        if (categoryLabel is "ΤΥΛΙΧΤΑ" or "ΠΙΤΤΕΣ" or "ΠΙΤΤΕΣ ΠΑΠΠΟΥ")
            return BreadAbbreviation(bread) + " " + DoublePitaLabel + " " + WithoutPrefix(productName, "Πίττα ");

        // Δέχεται και το παλιό «Μικρό» πρόθεμα (πριν τη μετονομασία σε «Μίνι») για τον ίδιο λόγο.
        var miniRest = productName;
        foreach (var prefix in new[] { "Μίνι ", "Μικρό " })
        {
            var stripped = WithoutPrefix(productName, prefix);
            if (stripped.Length != productName.Length)
            {
                miniRest = stripped;
                break;
            }
        }
        return "ΜΙΝΙ " + DoublePitaLabel + " " + miniRest;
    }

    /// <summary>Περιγραφή αφαιρεμένων υλικών για απόδειξη/ετικέτα παραγγελίας. Ο κανόνας είναι
    /// <b>πόσα ΜΕΝΟΥΝ</b>: «χωρίς: ντομάτα, κρεμμύδι» σχεδόν πάντα, «μόνο με: λάδι, ελιές» μόνο όταν
    /// μένουν το πολύ ΔΥΟ υλικά, ΣΚΕΤΟ αν δεν μένει κανένα.
    ///
    /// <para>Το «χωρίς» είναι το κανονικό — γράφει αυτό ακριβώς που ξετσέκαρε ο ταμίας με το χέρι του,
    /// οπότε το δελτίο λέει ό,τι έχει μπροστά του στην οθόνη. Το «μόνο με» είναι η εξαίρεση για το
    /// ακραίο: όταν έχει μείνει ένα-δύο πράγματα, έξι αράδες «χωρίς» είναι χειρότερες από μία.</para>
    ///
    /// <para>Δύο φορές μπήκε λάθος κατώφλι: σταθερό «από 3 αφαιρέσεις και πάνω» (γύριζε τη Χωριάτικη
    /// ανάποδα στο τρίτο «χωρίς», που είναι καθημερινό αίτημα), και μετά αναλογία removed &gt; remaining
    /// (τότε 4 αφαιρέσεις στα 7 έλεγαν «μόνο με» ενώ έμεναν ακόμα ΤΡΙΑ υλικά — «μέχρι αν αφήσω 3 υλικά
    /// λέει μόνο με»). Μετριούνται ΤΑ ΥΠΟΛΟΙΠΑ, όχι οι αφαιρέσεις, και το όριο είναι το 2.</para>
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

        // Η λέξη-κλειδί σε δική της γραμμή με άνω κάτω τελεία και από κάτω ένα υλικό ανά γραμμή. Πριν
        // επαναλαμβανόταν το «χωρίς» σε κάθε γραμμή («χωρίς κρεμμύδι», «χωρίς ντομάτα»), που έπιανε τον
        // ίδιο χώρο λέγοντας δύο φορές το ίδιο.
        var remaining = included.Where(i => !removed.Contains(i)).ToList();
        if (included.Count > 0 && remaining.Count <= MonoMeMaxRemaining && removed.Count > remaining.Count)
            return new[] { "μόνο με:" }.Concat(remaining.Select(Lowercase));
        return new[] { "χωρίς:" }.Concat(removed.Select(Lowercase));
    }

    /// <summary>Πόσα υλικά το πολύ μένουν για να γυρίσει η γραμμή σε «μόνο με:». Με 3 υπόλοιπα γράφεται
    /// κανονικά «χωρίς:» — ρητή απαίτηση του ταμείου. Ίδιο νούμερο και στο κινητό (MenuScreen.kt).</summary>
    private const int MonoMeMaxRemaining = 2;

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
