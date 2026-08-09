using PittaPos.Core.Models;

namespace PittaPos.Core.Data;

/// <summary>
/// Πλήρες μενού του καταστήματος όπως δημοσιεύεται στο e-food (shop 1062, Ιούλιος 2026):
/// όλες οι κατηγορίες, τα διαθέσιμα προϊόντα, οι περιγραφές, οι τιμές και το ποια
/// προϊόντα ανοίγουν customizer («με τα υλικά της επιλογής σας»).
/// Μέχρι να υπάρξει διαχείριση μενού μέσα από την εφαρμογή.
/// </summary>
public static class MenuSeed
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

    public static readonly IReadOnlyList<string> IncludedIngredients =
        ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Αλάτι", "Πιπέρι", "Κίτρινη σάλτσα"];

    /// <summary>Περιγραφή αφαιρεμένων υλικών για απόδειξη/ετικέτα παραγγελίας. Με λίγες αφαιρέσεις
    /// («χωρίς ντομάτα · χωρίς κρεμμύδι») διαβάζεται εύκολα, αλλά από 3 υλικά και πάνω η αράδα μεγαλώνει
    /// υπερβολικά — πιο σαφές να πει τι ΕΜΕΙΝΕ («μόνο με») παρά τι λείπει. Αν αφαιρέθηκαν όλα, είναι ΣΚΕΤΟ.</summary>
    public static IEnumerable<string> DescribeRemovedIngredients(IReadOnlyCollection<string> removed)
    {
        if (removed.Count == 0)
            return [];
        if (removed.Count >= IncludedIngredients.Count)
            return ["σκέτο"];
        if (removed.Count >= 3)
        {
            // "μόνο με" σε δική του γραμμή, μετά ένα υλικό ανά γραμμή — ίδια κάθετη λογική με το
            // χωρίς/έξτρα, ώστε ο υπάλληλος να τα διαβάζει το ένα κάτω απ' το άλλο, όχι σε μία αράδα.
            var remaining = IncludedIngredients.Where(i => !removed.Contains(i)).Select(Lowercase);
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

    public static readonly IReadOnlyList<ExtraItem> Extras =
    [
        new() { Name = "Γκούντα", Price = 0.80m },
        new() { Name = "Μπέικον", Price = 0.80m },
        new() { Name = "Τζατζίκι", Price = 0m },
        new() { Name = "Αυγό", Price = 0.70m },
        new() { Name = "Γραβιέρα", Price = 0.80m },
        new() { Name = "Ζαμπόν", Price = 0.80m },
        new() { Name = "Φέτα", Price = 1.00m },
        new() { Name = "Μανιτάρια", Price = 1.20m },
        new() { Name = "Πατάτες", Price = 0m },
        new() { Name = "Μαγιονέζα", Price = 0m },
        new() { Name = "Μαρούλι", Price = 0m },
        new() { Name = "Λουκάνικο", Price = 2.20m },
    ];

    public static readonly IReadOnlyList<string> PickupTimes = ["Άμεσα", "15'", "30'", "45'"];

    public const int TableCount = 12;

    private const bool C = true; // customizable — «με τα υλικά της επιλογής σας»

    public static readonly IReadOnlyList<MenuCategory> Categories =
    [
        new()
        {
            Id = "tylixta", Name = "ΤΥΛΙΧΤΑ",
            Products =
            [
                new() { Id = "tyl1", Name = "Πίττα κοτόπουλο", Customizable = C, Price = 7.20m, Description = "28cm πίττα, 100gr ψιλοκομμένο φιλέτο κοτόπουλο με τα υλικά της επιλογής σας" },
                new() { Id = "tyl2", Name = "Πίττα χοιρινό", Customizable = C, Price = 7.20m, Description = "28cm πίττα, 100gr ψιλοκομμένη χοιρινή μπριζόλα με τα υλικά της επιλογής σας" },
                new() { Id = "tyl3", Name = "Πίττα ανάμεικτο", Customizable = C, Price = 7.20m, Description = "28cm πίττα, 50gr κοτόπουλο & 50gr χοιρινή μπριζόλα με τα υλικά της επιλογής σας" },
                new() { Id = "tyl4", Name = "Πίττα μπιφτέκι", Customizable = C, Price = 7.20m, Description = "28cm πίττα, μπιφτέκι από 100% μοσχαρίσιο κιμά με τα υλικά της επιλογής σας" },
                new() { Id = "tyl5", Name = "Πίττα λουκάνικο", Customizable = C, Price = 6.00m, Description = "28cm πίττα, χωριάτικο λουκάνικο με τα υλικά της επιλογής σας" },
                new() { Id = "tyl6", Name = "Πίττα αλλαντικών", Customizable = C, Price = 7.20m, Description = "28cm πίττα, ζαμπόν & μπέικον με τα υλικά της επιλογής σας" },
                new() { Id = "tyl7", Name = "Πίττα τυριών", Customizable = C, Price = 6.30m, Description = "28cm πίττα, φέτα, gouda ή edam, regato & γραβιέρα με τα υλικά της επιλογής σας" },
                new() { Id = "tyl8", Name = "Πίττα ομελέτα", Customizable = C, Price = 6.10m, Description = "28cm πίττα, ομελέτα 2 αυγών με τα υλικά της επιλογής σας" },
                new() { Id = "tyl9", Name = "Πίττα λαχανικών", Customizable = C, Price = 4.90m, Description = "28cm πίττα λαχανικών με τα υλικά της επιλογής σας" },
                new() { Id = "tyl10", Name = "Πίττα σνίτσελ", Customizable = C, Price = 7.20m, Description = "28cm πίττα, σνίτσελ από χοιρινό κρέας με τα υλικά της επιλογής σας" },
                new() { Id = "tyl11", Name = "Πίττα κοστίνα", Customizable = C, Price = 7.20m, Description = "28cm πίττα, 100-120gr ψημένη χοιρινή πανσέτα με τα υλικά της επιλογής σας" },
                new() { Id = "tyl12", Name = "Πίττα μπιφτέκι λαχανικών", Customizable = C, Price = 7.00m, Description = "28cm πίττα, μπιφτέκι λαχανικών με τα υλικά της επιλογής σας" },
                new() { Id = "tyl13", Name = "Πίττα μπιφτέκι κοτόπουλο", Customizable = C, Price = 7.20m, Description = "28cm πίττα, μπιφτέκι κοτόπουλο με τα υλικά της επιλογής σας" },
                new() { Id = "tyl14", Name = "Πίττα ελληνικό", Customizable = C, Price = 6.30m, Description = "28cm πίττα, τρίμμα φέτας & ελιές ροδέλες με τα υλικά της επιλογής σας" },
                new() { Id = "tyl15", Name = "Πίττα κεμπάπ", Customizable = C, Price = 7.60m, Description = "28cm πίττα, 2 κεμπάπ με τα υλικά της επιλογής σας" },
                new() { Id = "tyl16", Name = "Πίττα χοιρινό καλαμάκι", Customizable = C, Price = 7.60m, Description = "28cm πίττα, χοιρινό καλαμάκι με τα υλικά της επιλογής σας" },
                new() { Id = "tyl17", Name = "Πίττα κοτόπουλο καλαμάκι", Customizable = C, Price = 7.60m, Description = "28cm πίττα, κοτόπουλο καλαμάκι με τα υλικά της επιλογής σας" },
                new() { Id = "tyl18", Name = "Πίττα κοτομπουκιές", Customizable = C, Price = 6.70m, Description = "28cm πίττα, κοτομπουκιές με τα υλικά της επιλογής σας" },
            ],
        },
        new()
        {
            Id = "klasika", Name = "ΚΛΑΣΙΚΑ ΜΙΝΙ",
            Products =
            [
                new() { Id = "kls1", Name = "Μίνι κοτόπουλο", Customizable = C, Price = 4.10m, Description = "18cm πίττα, φιλέτο κοτόπουλο 60gr με τα υλικά της επιλογής σας" },
                new() { Id = "kls2", Name = "Μίνι μπιφτέκι", Customizable = C, Price = 4.10m, Description = "18cm πίττα, μπιφτέκι με τα υλικά της επιλογής σας" },
                new() { Id = "kls3", Name = "Μίνι χοιρινό", Customizable = C, Price = 4.10m, Description = "18cm πίττα, χοιρινό 60gr με τα υλικά της επιλογής σας" },
                new() { Id = "kls4", Name = "Μίνι κεμπάπ", Customizable = C, Price = 4.10m, Description = "18cm πίττα, 1 κεμπάπ με τα υλικά της επιλογής σας" },
                new() { Id = "kls5", Name = "Μίνι χοιρινό καλαμάκι", Customizable = C, Price = 4.10m, Description = "18cm πίττα, καλαμάκι χοιρινό με τα υλικά της επιλογής σας" },
                new() { Id = "kls6", Name = "Μίνι κοτόπουλο καλαμάκι", Customizable = C, Price = 4.10m, Description = "18cm πίττα, καλαμάκι κοτόπουλο με τα υλικά της επιλογής σας" },
            ],
        },
        new()
        {
            Id = "clubs", Name = "ΠΙΤΤΑ CLUB",
            Products =
            [
                new() { Id = "clb1", Name = "Club κοτόπουλο", Customizable = C, Price = 9.90m, Description = "100gr φιλέτο κοτόπουλο, μαγιονέζα, ντομάτα, μαρούλι & μπέικον. Με πατάτες τηγανητές" },
                new() { Id = "clb2", Name = "Club χοιρινό", Customizable = C, Price = 9.90m, Description = "100gr μπριζόλα χοιρινή, μαγιονέζα, ντομάτα, μαρούλι & μπέικον. Με πατάτες τηγανητές" },
                new() { Id = "clb3", Name = "Club μπιφτέκι", Customizable = C, Price = 9.90m, Description = "Μπιφτέκι 100% μοσχαρίσιο, μαγιονέζα, ντομάτα, μαρούλι & μπέικον. Με πατάτες τηγανητές" },
                new() { Id = "clb4", Name = "Club αλλαντικών", Customizable = C, Price = 9.90m, Description = "Ζαμπόν, gouda ή edam, μαγιονέζα, ντομάτα, μαρούλι & μπέικον. Με πατάτες τηγανητές" },
                new() { Id = "clb5", Name = "Club μπιφτέκι λαχανικών", Customizable = C, Price = 9.90m, Description = "2 μπιφτέκια λαχανικών, μαγιονέζα ή BBQ, ντομάτα & μαρούλι. Με πατάτες τηγανητές" },
                new() { Id = "clb6", Name = "Club μπιφτέκι κοτόπουλο", Customizable = C, Price = 9.90m, Description = "1 μπιφτέκι κοτόπουλο, κίτρινη σάλτσα, μπέικον, ντομάτα & μαρούλι. Με πατάτες τηγανητές" },
                new() { Id = "clb7", Name = "Club ανάμεικτο", Customizable = C, Price = 9.90m, Description = "Φιλέτο κοτόπουλο & χοιρινή μπριζόλα, μαγιονέζα, ντομάτα, μαρούλι & μπέικον. Με πατάτες τηγανητές" },
            ],
        },
        new()
        {
            Id = "burgers", Name = "BURGERS",
            Products =
            [
                new() { Id = "brg1", Name = "Mini burger", Price = 4.10m, Description = "Ψωμάκι brioche με μπιφτέκι 60gr, ντομάτα & ροζ σάλτσα" },
                new() { Id = "brg2", Name = "Burger απλό", Customizable = C, Price = 6.40m, Description = "Μπιφτέκι 100% μοσχαρίσιο 180gr, μαρούλι, ντομάτα & ροζ σάλτσα παππού σε ψημένο ψωμάκι" },
                new() { Id = "brg3", Name = "Burger τυρί", Customizable = C, Price = 6.70m, Description = "Μπιφτέκι 180gr, μαρούλι, ντομάτα, ροζ σάλτσα, gouda ή edam σε ψημένο ψωμάκι" },
                new() { Id = "brg4", Name = "Burger special", Customizable = C, Price = 7.40m, Description = "Μπιφτέκι 180gr, μαρούλι, ντομάτα, ροζ σάλτσα, τυρί, αυγό & μπέικον σε ψημένο ψωμάκι" },
                new() { Id = "brg5", Name = "Burger μπιφτέκι λαχανικών", Customizable = C, Price = 6.20m, Description = "Μπιφτέκι λαχανικών 180gr, μαρούλι, ντομάτα & λευκή σάλτσα παππού σε ψημένο ψωμάκι" },
                new() { Id = "brg6", Name = "Burger μπιφτέκι κοτόπουλο", Customizable = C, Price = 6.20m, Description = "Μπιφτέκι κοτόπουλο 180gr, μαρούλι, ντομάτα & κίτρινη σάλτσα σε ψημένο ψωμάκι" },
                new() { Id = "brg7", Name = "Burger deluxe", Customizable = C, Price = 8.40m, Description = "Μπιφτέκι 180gr, τυρί, ροζ σάλτσα & πίκλες. Με πατάτες τηγανητές & σαλάτα" },
                new() { Id = "brg8", Name = "Burger κοτόπουλο deluxe", Customizable = C, Price = 8.40m, Description = "Μπιφτέκι κοτόπουλο 180gr, τυρί, ροζ σάλτσα & πίκλες. Με πατάτες τηγανητές & σαλάτα" },
                new() { Id = "brg9", Name = "Burger special deluxe", Customizable = C, Price = 9.60m, Description = "Μπιφτέκι 180gr, τυρί, ροζ σάλτσα, πίκλες, μπέικον & αυγό. Με πατάτες τηγανητές & σαλάτα" },
            ],
        },
        new()
        {
            Id = "kalamakia", Name = "ΚΑΛΑΜΑΚΙΑ",
            Products =
            [
                new() { Id = "klm1", Name = "Καλαμάκι χοιρινό", Price = 2.70m, Description = "100gr. Συνοδεύεται από πιττάκι" },
                new() { Id = "klm2", Name = "Καλαμάκι κοτόπουλο", Price = 2.70m, Description = "100gr. Συνοδεύεται από πιττάκι" },
                new() { Id = "klm3", Name = "Κεμπάπ", Price = 2.70m, Description = "100gr. Συνοδεύεται από πιττάκι" },
                new() { Id = "klm4", Name = "Λουκάνικο χωριάτικο", Price = 2.70m, Description = "Συνοδεύεται από πιττάκι" },
            ],
        },
        new()
        {
            Id = "merides", Name = "ΜΕΡΙΔΕΣ",
            Products =
            [
                new() { Id = "mrd1", Name = "Κοτόπουλο μερίδα", Customizable = C, Price = 10.80m, Description = "200gr φιλέτο κοτόπουλο, πατάτες, κίτρινη σάλτσα, μαρούλι, κρεμμύδι, ντομάτα & πίττα" },
                new() { Id = "mrd2", Name = "Χοιρινό μερίδα", Customizable = C, Price = 10.80m, Description = "200gr χοιρινό, πατάτες, λευκή σάλτσα, μαρούλι, κρεμμύδι, ντομάτα & πίττα" },
                new() { Id = "mrd3", Name = "Ανάμεικτο μερίδα", Customizable = C, Price = 10.80m, Description = "100gr κοτόπουλο & 100gr χοιρινό, πατάτες, σάλτσες, λαχανικά & πίττα" },
                new() { Id = "mrd4", Name = "Μπιφτέκι μερίδα", Customizable = C, Price = 10.80m, Description = "2 τεμ. 100% μοσχαρίσιος κιμάς, πατάτες, λευκή σάλτσα, λαχανικά & πίττα" },
                new() { Id = "mrd5", Name = "Σνίτσελ μερίδα", Customizable = C, Price = 10.80m, Description = "Χοιρινό σνίτσελ με κόκκινη σάλτσα & τυρί. Με πατάτες, ψωμί & πίττα" },
                new() { Id = "mrd6", Name = "Λουκάνικο μερίδα", Customizable = C, Price = 8.80m, Description = "2 τεμ. Με πατάτες, ketchup, μουστάρδα, λαχανικά & πίττα" },
                new() { Id = "mrd7", Name = "Ομελέτα μερίδα", Customizable = C, Price = 8.00m, Description = "3 αυγά, πιπεριά, ζαμπόν, μπέικον & τυρί. Με πατάτες, μαγιονέζα & πίττα" },
                new() { Id = "mrd8", Name = "Μπιφτέκι special μερίδα", Customizable = C, Price = 11.00m, Description = "2 τεμ. με τυρί, μπέικον & ροζ σάλτσα. Με πατάτες & πίττα" },
                new() { Id = "mrd9", Name = "Κοστίνα μερίδα", Customizable = C, Price = 10.50m, Description = "350gr ψημένη χοιρινή κοστίνα, ντομάτα, μαρούλι, σάλτσα λεμονιού ή BBQ, πατάτες & πίττα" },
                new() { Id = "mrd10", Name = "Μπιφτέκι λαχανικών μερίδα", Customizable = C, Price = 9.60m, Description = "2 τεμ. Με πατάτες, σάλτσα αγρού, λαχανικά & πίττα" },
                new() { Id = "mrd11", Name = "Μπιφτέκι κοτόπουλο μερίδα", Customizable = C, Price = 10.80m, Description = "2 τεμ. Με πατάτες, κίτρινη σάλτσα, λαχανικά & πίττα" },
                new() { Id = "mrd12", Name = "Λαχανικών μερίδα", Customizable = C, Price = 6.40m, Description = "Σοταρισμένα λαχανικά σβησμένα σε κρασί ή μπύρα. Με πατάτες & πίττα" },
                new() { Id = "mrd13", Name = "Κεμπάπ μερίδα", Customizable = C, Price = 11.00m, Description = "3 τεμ. Με πατάτες, σάλτσα αγρού ή τζατζίκι, κρεμμύδι, ντομάτα & πίττα" },
                new() { Id = "mrd14", Name = "Κοτομπουκιές πανέ μερίδα", Customizable = C, Price = 7.70m, Description = "8 τεμ. 100% φιλέτο στήθος. Με πατάτες, πιττάκι & κίτρινη σάλτσα" },
                new() { Id = "mrd15", Name = "Ποικιλία μικρή", Customizable = C, Price = 9.20m, Description = "1 λουκάνικο, κοτόπουλο & μπέικον. Με πιττάκι, πατάτες & σαλάτα" },
                new() { Id = "mrd16", Name = "Ποικιλία μεγάλη 3 ατόμων", Customizable = C, Price = 15.40m, Description = "1 μπιφτέκι, 1 λουκάνικο, 100gr κοστίνα & 70gr κοτόπουλο. Με πατάτες, 2 πιττάκια, μπέικον & σαλάτα" },
            ],
        },
        new()
        {
            Id = "merides-sketes", Name = "ΜΕΡΙΔΕΣ ΣΚΕΤΕΣ",
            Products =
            [
                new() { Id = "msk1", Name = "Φιλέτο κοτόπουλο σκέτο", Price = 7.10m, Description = "200gr ψιλοκομμένο φιλέτο κοτόπουλο (χωρίς συνοδευτικό)" },
                new() { Id = "msk2", Name = "Φιλέτο χοιρινό σκέτο", Price = 7.10m, Description = "200gr ψιλοκομμένη χοιρινή μπριζόλα (χωρίς συνοδευτικό)" },
                new() { Id = "msk3", Name = "Λουκάνικο σκέτο", Price = 6.80m, Description = "2 τεμάχια (χωρίς συνοδευτικό)" },
                new() { Id = "msk4", Name = "Μπιφτέκι μοσχαρίσιο σκέτο", Price = 6.90m, Description = "2 τεμ. από 100% μοσχαρίσιο κιμά (χωρίς συνοδευτικό)" },
                new() { Id = "msk5", Name = "Ανάμεικτη σκέτη", Price = 7.10m, Description = "100gr χοιρινό & 100gr κοτόπουλο (χωρίς συνοδευτικό)" },
            ],
        },
        new()
        {
            Id = "merides-pappou", Name = "ΜΕΡΙΔΕΣ ΠΑΠΠΟΥ",
            Products =
            [
                new() { Id = "mpp1", Name = "Κοτόπουλο Παππού", Customizable = C, Price = 11.00m, Description = "200gr κοτόπουλο με σοταρισμένα λαχανικά σβησμένα με μπύρα ή κρασί. Με πατάτες & πίττα" },
                new() { Id = "mpp2", Name = "Χοιρινό Παππού", Customizable = C, Price = 11.00m, Description = "200gr χοιρινή μπριζόλα με σοταρισμένα λαχανικά σβησμένα με μπύρα ή κρασί. Με πατάτες & πίττα" },
                new() { Id = "mpp3", Name = "Ανάμεικτο Παππού", Customizable = C, Price = 11.00m, Description = "100gr χοιρινό & 100gr κοτόπουλο με σοταρισμένα λαχανικά. Με πατάτες & πίττα" },
                new() { Id = "mpp4", Name = "Μπιφτέκι Παππού", Customizable = C, Price = 10.70m, Description = "2 μπιφτέκια με σοταρισμένα λαχανικά σβησμένα με μπύρα ή κρασί. Με πατάτες & πίττα" },
                new() { Id = "mpp5", Name = "Μπιφτέκι λαχανικών Παππού", Customizable = C, Price = 10.70m, Description = "2 μπιφτέκια λαχανικών με σοταρισμένα λαχανικά. Με πατάτες & πίττα" },
                new() { Id = "mpp6", Name = "Μπιφτέκι κοτόπουλο Παππού", Customizable = C, Price = 10.70m, Description = "2 μπιφτέκια κοτόπουλο με σοταρισμένα λαχανικά. Με πατάτες & πίττα" },
            ],
        },
        new()
        {
            Id = "parea", Name = "ΤΗΣ ΠΑΡΕΑΣ",
            Products =
            [
                new() { Id = "par1", Name = "Κοστίνα", Customizable = C, Price = 8.50m, Description = "Με σάλτσα BBQ ή λεμονιού. Η τελική τιμή διαμορφώνεται στο ζύγισμα" },
                new() { Id = "par2", Name = "Μπουκίτσες κοτόπουλο (20 τεμ.)", Price = 12.50m },
            ],
        },
        new()
        {
            Id = "salates", Name = "ΣΑΛΑΤΕΣ",
            Products =
            [
                new() { Id = "sal1", Name = "Παππού", Price = 9.10m, Description = "100gr κοτόπουλο, μαρούλι, αγγούρι, κρεμμύδι, πιπεριά, regato, γραβιέρα, ελαιόλαδο, ξύδι & κίτρινη sauce" },
                new() { Id = "sal2", Name = "Σεφ", Price = 8.00m, Description = "Μαρούλι, αγγούρι, ντομάτα, gouda ή edam, ζαμπόν, ροζ σάλτσα & αυγό βραστό" },
                new() { Id = "sal3", Name = "Χωριάτικη", Price = 7.50m, Description = "Ντομάτα, αγγούρι, κρεμμύδι, πιπεριά, φέτα, ελιές, ελαιόλαδο & ρίγανη" },
                new() { Id = "sal4", Name = "Λαχανικών", Price = 6.00m, Description = "Λάχανο, μαρούλι, καρότο, ελαιόλαδο, ξύδι ή λεμόνι" },
                new() { Id = "sal5", Name = "Μπάμπο", Price = 7.50m, Description = "Μαρούλι, αγγούρι, πιπεριά, λιαστή ντομάτα, γραβιέρα, ελαιόλαδο & βαλσάμικο" },
                new() { Id = "sal6", Name = "Ντάκος", Price = 7.30m, Description = "Παξιμάδι, ντομάτα, φέτα τρίμμα, ελιές, ελαιόλαδο & ρίγανη" },
                new() { Id = "sal7", Name = "Αγρού", Price = 7.00m, Description = "Λάχανο, καρότο, μαρούλι, αγγούρι, αγγούρι τουρσί, σάλτσα αγρού, ξύδι & ελαιόλαδο" },
                new() { Id = "sal8", Name = "Ορφανή", Price = 5.50m, Description = "Ντομάτα, αγγούρι, κρεμμύδι, πιπεριά, ελιές, ελαιόλαδο & ρίγανη" },
            ],
        },
        new()
        {
            Id = "synodeytika", Name = "ΣΥΝΟΔΕΥΤΙΚΑ",
            Products =
            [
                new() { Id = "syn1", Name = "Πατάτες τηγανητές", Price = 3.50m },
                new() { Id = "syn2", Name = "Πατάτες με τυρί & μπέικον", Price = 5.30m, Description = "Με μπέικον & gouda ή edam" },
                new() { Id = "syn3", Name = "Πατάτες με τυρί, μπέικον & μανιτάρια", Price = 6.40m, Description = "Με gouda ή edam, μπέικον & μανιτάρια" },
                new() { Id = "syn4", Name = "Σάλτσα Παππού", Price = 1.80m },
                new() { Id = "syn5", Name = "Σωσάκι μικρό", Price = 0.60m },
                new() { Id = "syn6", Name = "Φέτα", Price = 3.50m },
                new() { Id = "syn7", Name = "Τζατζίκι", Price = 3.00m, Description = "Μερίδα" },
                new() { Id = "syn8", Name = "Πίττα ελληνική", Price = 1.10m },
                new() { Id = "syn9", Name = "Πιττάκι μικρό", Price = 0.50m },
                new() { Id = "syn10", Name = "Πίττα μικρή", Price = 0.80m },
                new() { Id = "syn11", Name = "Ψωμί", Price = 1.10m, Description = "Μερίδα" },
            ],
        },
        new()
        {
            Id = "paidiko", Name = "ΠΑΙΔΙΚΟ MENU",
            Products =
            [
                new() { Id = "pdk1", Name = "Burgerάκι με μπιφτέκι", Price = 7.50m, Description = "Μπιφτέκι μικρό, ντομάτα & ketchup. Με πατατούλες, Amita Fun & δώρο έκπληξη!" },
                new() { Id = "pdk2", Name = "Παιδικό κοτόπουλο πιττάκι", Price = 7.50m, Description = "Πίττα μικρή με φιλέτο κοτόπουλο, ντομάτα & πατάτες. Με Amita Fun & δώρο έκπληξη!" },
                new() { Id = "pdk3", Name = "Παιδικό μπιφτέκι πιττάκι", Price = 7.50m, Description = "Πίττα μικρή με μπιφτέκι, ντομάτα & πατάτες. Με Amita Fun & δώρο έκπληξη!" },
                new() { Id = "pdk4", Name = "Παιδικό χοιρινό πιττάκι", Price = 7.50m, Description = "Πίττα μικρή με χοιρινή μπριζόλα, ντομάτα & πατάτες. Με Amita Fun & δώρο έκπληξη!" },
                new() { Id = "pdk5", Name = "Μπουκίτσες κοτόπουλο", Price = 7.50m, Description = "5 τεμ. πανέ. Με πατάτες, Amita Fun & δώρο έκπληξη!" },
            ],
        },
        new()
        {
            Id = "anapsyktika", Name = "ΑΝΑΨΥΚΤΙΚΑ",
            Products =
            [
                new() { Id = "dr1", Name = "Coca-Cola 330ml", Price = 1.90m },
                new() { Id = "dr2", Name = "Coca-Cola zero 330ml", Price = 1.90m },
                new() { Id = "dr3", Name = "Coca-Cola zero χωρίς καφεΐνη 330ml", Price = 1.90m },
                new() { Id = "dr4", Name = "Sprite 330ml", Price = 1.90m },
                new() { Id = "dr5", Name = "Fanta πορτοκαλάδα 330ml", Price = 1.90m },
                new() { Id = "dr6", Name = "Fanta πορτοκαλάδα μπλε 330ml", Price = 1.90m },
                new() { Id = "dr7", Name = "Soda Tuborg 330ml", Price = 1.50m },
                new() { Id = "dr8", Name = "Amita Fun 250ml", Price = 1.20m },
                new() { Id = "dr9", Name = "Coca-Cola 500ml", Price = 2.30m },
                new() { Id = "dr10", Name = "Coca-Cola zero 500ml", Price = 2.30m },
                new() { Id = "dr11", Name = "Coca-Cola 1.5lt", Price = 3.60m },
                new() { Id = "dr12", Name = "Νερό 500ml", Price = 0.50m },
            ],
        },
        new()
        {
            Id = "beers", Name = "ΜΠΥΡΕΣ · ΠΟΤΑ",
            Products =
            [
                new() { Id = "br1", Name = "Heineken 330ml", Price = 2.20m },
                new() { Id = "br2", Name = "Amstel 330ml", Price = 2.20m },
                new() { Id = "br3", Name = "Άλφα 330ml", Price = 2.20m },
                new() { Id = "br4", Name = "Heineken φιάλη 500ml", Price = 3.40m },
                new() { Id = "br5", Name = "Amstel φιάλη 500ml", Price = 3.40m },
                new() { Id = "br6", Name = "Kaiser φιάλη 500ml", Price = 3.40m },
                new() { Id = "br7", Name = "Άλφα φιάλη 500ml", Price = 3.40m },
            ],
        },
    ];
}
