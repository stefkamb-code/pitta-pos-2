using PittaPos.Core.Models;

namespace PittaPos.Core.Data;

/// <summary>
/// ΠΑΡΑΓΕΤΑΙ ΑΥΤΟΜΑΤΑ — ΜΗΝ ΤΟ ΓΡΑΦΕΙΣ ΜΕ ΤΟ ΧΕΡΙ.
///
/// <para>Πηγή: ο πραγματικός κατάλογος ενός μαγαζιού, 17 κατηγορίες / 142 προϊόντα / 22 έξτρα.
/// Παράχθηκε 11/08/2026.</para>
///
/// <para>Ξαναφτιάξ' το: <c>python scripts/menu-to-seed.py &lt;menu.json&gt;</c> — παίρνοντας το
/// menu.json από το <c>%AppData%\PittaPos2</c> του υπολογιστή που δουλεύει.</para>
/// </summary>
public static partial class MenuSeed
{
    /// <summary>Ο κοινός κατάλογος βασικών υλικών — η προεπιλογή για προϊόντα που δεν έχουν δηλώσει
    /// δικά τους (βλ. <c>MenuStore.IngredientsFor</c>).</summary>
    public static readonly IReadOnlyList<string> IncludedIngredients =
        ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Αλάτι", "Πιπέρι", "Κίτρινη σάλτσα", "Φέτα", "Γκούντα", "Πιπεριά"];

    /// <summary>Ο κοινός κατάλογος έξτρα με τις τιμές τους. Η σειρά είναι αυτή που βλέπει ο ταμίας.</summary>
    public static readonly IReadOnlyList<ExtraItem> Extras =
    [
        new() { Name = "Πατάτες", Price = 0.00m },
        new() { Name = "Γκούντα", Price = 0.80m },
        new() { Name = "Καυτερό", Price = 0.00m },
        new() { Name = "Μπέικον", Price = 1.00m },
        new() { Name = "Τζατζίκι", Price = 0.00m },
        new() { Name = "Αυγό", Price = 0.70m },
        new() { Name = "Γραβιέρα", Price = 0.80m },
        new() { Name = "Ζαμπόν", Price = 0.80m },
        new() { Name = "Φέτα", Price = 1.00m },
        new() { Name = "Μανιτάρια", Price = 1.20m },
        new() { Name = "Extra Χοιρινό 50gr", Price = 1.60m },
        new() { Name = "Extra Κοτόπουλο 50gr", Price = 1.60m },
        new() { Name = "Μαγιονέζα", Price = 0.00m },
        new() { Name = "Μαρούλι", Price = 0.00m },
        new() { Name = "Λουκάνικο", Price = 2.20m },
        new() { Name = "Αγρού", Price = 0.00m },
        new() { Name = "Πιπερία", Price = 0.00m },
        new() { Name = "Ρόζ", Price = 0.00m },
        new() { Name = "Κίτρινη", Price = 0.00m },
        new() { Name = "Λευκή", Price = 0.00m },
        new() { Name = "Κέτσαπ", Price = 0.00m },
        new() { Name = "Μουστάρδα", Price = 0.00m },
    ];

    /// <summary>Επιπλέον χρέωση «διπλή πίτα» ανά κατηγορία.</summary>
    public static readonly IReadOnlyDictionary<string, decimal> DoublePitaPrices =
        new Dictionary<string, decimal>
        {
            ["ΚΛΑΣΙΚΑ ΜΙΚΡΑ"] = 0.80m,
            ["ΠΙΤΤΕΣ"] = 1.10m,
            ["ΠΙΤΤΕΣ ΠΑΠΠΟΥ"] = 1.10m,
        };

    /// <summary>Τα έξτρα που επιτρέπονται στα περισσότερα προϊόντα, στη σειρά που τα θέλει το
    /// μαγαζί. Ιδιότητα, όχι πεδίο: κάθε προϊόν παίρνει ΔΙΚΗ ΤΟΥ λίστα, ώστε μια διαγραφή από τη
    /// Διαχείριση Καταλόγου να μην πειράζει τα υπόλοιπα.</summary>
    private static List<string> StandardExtras =>
        ["Μπέικον", "Γκούντα", "Τζατζίκι", "Αυγό", "Γραβιέρα", "Ζαμπόν", "Φέτα", "Μανιτάρια", "Πατάτες", "Μαγιονέζα", "Μαρούλι", "Λουκάνικο", "Αγρού", "Πιπερία", "Ρόζ", "Κίτρινη", "Λευκή", "Κέτσαπ", "Μουστάρδα", "Extra Χοιρινό 50gr", "Extra Κοτόπουλο 50gr"];

    public static readonly IReadOnlyList<MenuCategory> Categories =
    [
        new()
        {
            Id = "synodeytika", Name = "ΣΥΝΟΔΕΥΤΙΚΑ", HasBread = false, FuseBreadIntoName = false, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "syn1", Name = "Πατάτες τηγανητές", Price = 3.50m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "syn2", Name = "Πατάτες με τυρί & μπέικον", Price = 5.30m, ExtraNames = StandardExtras, Ingredients = [], Description = "Με μπέικον & gouda ή edam" },
                new() { Id = "syn3", Name = "Πατάτες τυρί, μπέικον, μανιτ", Price = 6.40m, ExtraNames = StandardExtras, Ingredients = [], Description = "Με gouda ή edam, μπέικον & μανιτάρια" },
                new() { Id = "syn4", Name = "Σάλτσα Παππού", Price = 1.80m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "syn5", Name = "Σωσάκι μικρό", Price = 0.60m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "syn6", Name = "Φέτα", Price = 3.50m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "syn7", Name = "Τζατζίκι", Price = 3.00m, ExtraNames = StandardExtras, Ingredients = [], Description = "Μερίδα" },
                new() { Id = "syn8", Name = "Πίττα ελληνική", Price = 1.10m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "syn9", Name = "Πιττάκι μικρό", Price = 0.60m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "syn10", Name = "Πίττα μικρή", Price = 0.80m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "syn11", Name = "Ψωμί", Price = 1.10m, ExtraNames = StandardExtras, Ingredients = [], Description = "Μερίδα" },
            ],
        },
        new()
        {
            Id = "salates", Name = "ΣΑΛΑΤΕΣ", HasBread = false, FuseBreadIntoName = false, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "sal1", Name = "Παππού", Price = 9.10m, ExtraNames = StandardExtras, Ingredients = ["Αγγούρι", "Μαρούλι", "Πιπεριά", "Κρεμμύδι", "Γραβιέρα", "Ρεγκάντο", "Λάδι", "Ξύδι"], Description = "100gr κοτόπουλο, μαρούλι, αγγούρι, κρεμμύδι, πιπεριά, regato, γραβιέρα, ελαιόλαδο, ξύδι & κίτρινη sauce" },
                new() { Id = "sal2", Name = "Σεφ", Price = 8.00m, ExtraNames = StandardExtras, Ingredients = ["Αυγό", "Γκούντα", "Ζαμπόν", "Ντομάτα", "Αγγούρι", "Μαρούλι", "Ρόζ σάλτσα"], Description = "Μαρούλι, αγγούρι, ντομάτα, gouda ή edam, ζαμπόν, ροζ σάλτσα & αυγό βραστό" },
                new() { Id = "sal3", Name = "Χωριάτικη", Price = 7.50m, ExtraNames = StandardExtras, Ingredients = ["Φέτα", "Ντομάτα", "Αγγούρι", "Πιπεριά", "Κρεμμύδι", "Λάδι", "Ελιές"], Description = "Ντομάτα, αγγούρι, κρεμμύδι, πιπεριά, φέτα, ελιές, ελαιόλαδο & ρίγανη" },
                new() { Id = "sal4", Name = "Λαχανικών", Price = 6.00m, ExtraNames = StandardExtras, Ingredients = ["Μαρούλι", "Λάχανο", "Καρότο", "Λάδι", "Λεμόνι"], Description = "Λάχανο, μαρούλι, καρότο, ελαιόλαδο, ξύδι ή λεμόνι" },
                new() { Id = "sal5", Name = "Μπάμπο", Price = 7.50m, ExtraNames = StandardExtras, Ingredients = ["Αγγούρι", "Μαρούλι", "Πιπεριά", "Λιαστή ντομάτα", "Λάδι", "Βαλσάμικο", "Γραβιέρα"], Description = "Μαρούλι, αγγούρι, πιπεριά, λιαστή ντομάτα, γραβιέρα, ελαιόλαδο & βαλσάμικο" },
                new() { Id = "sal6", Name = "Ντάκος", Price = 7.30m, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Φέτα", "Ελιές", "Λάδι", "Ρίγανη"], Description = "Παξιμάδι, ντομάτα, φέτα τρίμμα, ελιές, ελαιόλαδο & ρίγανη" },
                new() { Id = "sal7", Name = "Αγρού", Price = 7.00m, ExtraNames = StandardExtras, Ingredients = ["Λάχανο", "Καρότο", "Αγγούρι", "Μαρούλι", "Λάδι", "Ξύδι", "Αγρού σάλτσα", "Τουρσί"], Description = "Λάχανο, καρότο, μαρούλι, αγγούρι, αγγούρι τουρσί, σάλτσα αγρού, ξύδι & ελαιόλαδο" },
                new() { Id = "sal8", Name = "Ορφανή", Price = 5.50m, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Αγγούρι", "Κρεμμύδι", "Πιπεριά", "Λάδι"], Description = "Ντομάτα, αγγούρι, κρεμμύδι, πιπεριά, ελιές, ελαιόλαδο & ρίγανη" },
            ],
        },
        new()
        {
            Id = "tylixta", Name = "ΠΙΤΤΕΣ", HasBread = true, FuseBreadIntoName = true, SupportsDoublePita = true,
            Products =
            [
                new() { Id = "tyl1", Name = "ΠΙΤΤΑ ΚΟΤΟΠΟΥΛΟ", Price = 7.00m, DeliveryPrice = 7.20m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Αλάτι", "Πιπέρι", "Κίτρινη σάλτσα"], Description = "28cm πίττα, 100gr ψιλοκομμένο φιλέτο κοτόπουλο με τα υλικά της επιλογής σας" },
                new() { Id = "tyl2", Name = "ΠΙΤΤΑ ΧΟΙΡΙΝΟ", Price = 7.00m, DeliveryPrice = 7.20m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Λευκή σάλτσα", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, 100gr ψιλοκομμένη χοιρινή μπριζόλα με τα υλικά της επιλογής σας" },
                new() { Id = "tyl3", Name = "Πίττα ανάμεικτο", Price = 7.00m, DeliveryPrice = 7.20m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Λευκή σαλτσα", "Κίτρινη σάλτσα", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, 50gr κοτόπουλο & 50gr χοιρινή μπριζόλα με τα υλικά της επιλογής σας" },
                new() { Id = "tyl4", Name = "Πίττα μπιφτέκι", Price = 7.00m, DeliveryPrice = 7.20m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Λευκή σάλτσα", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, μπιφτέκι από 100% μοσχαρίσιο κιμά με τα υλικά της επιλογής σας" },
                new() { Id = "tyl5", Name = "Πίττα λουκάνικο", Price = 5.70m, DeliveryPrice = 6.00m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Κέτσαπ", "Μουστάρδα", "Μαγιονέζα", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, χωριάτικο λουκάνικο με τα υλικά της επιλογής σας" },
                new() { Id = "tyl6", Name = "Πίττα αλλαντικών", Price = 7.00m, DeliveryPrice = 7.20m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ζαμπόν", "Μπεικον", "Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Γκούντα", "Μαγιονέζα", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, ζαμπόν & μπέικον με τα υλικά της επιλογής σας" },
                new() { Id = "tyl7", Name = "Πίττα τυριών", Price = 6.10m, DeliveryPrice = 6.30m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Φέτα", "Γκούντα", "Γραβιέρα", "Ρεγκάντο", "Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Μαγιονέζα", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, φέτα, gouda ή edam, regato & γραβιέρα με τα υλικά της επιλογής σας" },
                new() { Id = "tyl8", Name = "Πίττα ομελέτα", Price = 5.60m, DeliveryPrice = 6.10m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Μαγιονέζα", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, ομελέτα 2 αυγών με τα υλικά της επιλογής σας" },
                new() { Id = "tyl9", Name = "Πίττα λαχανικών", Price = 4.70m, DeliveryPrice = 4.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Λάχανο", "Καρότο", "Πιπεριά", "Πατάτες", "Μαγιονέζα", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα λαχανικών με τα υλικά της επιλογής σας" },
                new() { Id = "tyl10", Name = "Πίττα σνίτσελ", Price = 7.00m, DeliveryPrice = 7.20m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Λευκή σάλτσα", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, σνίτσελ από χοιρινό κρέας με τα υλικά της επιλογής σας" },
                new() { Id = "tyl11", Name = "Πίττα κοστίνα", Price = 7.00m, DeliveryPrice = 7.20m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μπάρμπεκιου σάλτσα", "Κρεμμύδι", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, 100-120gr ψημένη χοιρινή πανσέτα με τα υλικά της επιλογής σας" },
                new() { Id = "tyl12", Name = "Πίττα μπιφτέκι λαχανικών", Price = 6.80m, DeliveryPrice = 7.00m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Αγρού σάλτσα", "Κρεμμύδι", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, μπιφτέκι λαχανικών με τα υλικά της επιλογής σας" },
                new() { Id = "tyl13", Name = "Πίττα μπιφτέκι κοτόπουλο", Price = 7.00m, DeliveryPrice = 7.20m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κίτρινη σάλτσα", "Κρεμμύδι", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, μπιφτέκι κοτόπουλο με τα υλικά της επιλογής σας" },
                new() { Id = "tyl14", Name = "Πίττα ελληνικό", Price = 6.10m, DeliveryPrice = 6.30m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Φέτα", "Ντομάτα", "Μαγιονέζα", "Ελιές", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, τρίμμα φέτας & ελιές ροδέλες με τα υλικά της επιλογής σας" },
                new() { Id = "tyl15", Name = "Πίττα κεμπάπ", Price = 7.40m, DeliveryPrice = 7.60m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Αγρού σάλτσα", "Κρεμμύδι", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, 2 κεμπάπ με τα υλικά της επιλογής σας" },
                new() { Id = "tyl16", Name = "Πίττα χοιρινό καλαμάκι", Price = 7.40m, DeliveryPrice = 7.60m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Κρεμμύδι", "Αγρού σάλτσα", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, χοιρινό καλαμάκι με τα υλικά της επιλογής σας" },
                new() { Id = "tyl17", Name = "Πίττα κοτόπουλο καλαμάκι", Price = 7.40m, DeliveryPrice = 7.60m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Κρεμμύδι", "Αγρού σάλτσα", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, κοτόπουλο καλαμάκι με τα υλικά της επιλογής σας" },
                new() { Id = "tyl18", Name = "Πίττα κοτομπουκιές", Price = 6.50m, DeliveryPrice = 6.70m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Κίτρινη σάλτσα", "Αλάτι", "Πιπέρι"], Description = "28cm πίττα, κοτομπουκιές με τα υλικά της επιλογής σας" },
            ],
        },
        new()
        {
            Id = "u8def4005e6a198d", Name = "ΠΙΤΤΕΣ ΠΑΠΠΟΥ", HasBread = true, FuseBreadIntoName = true, SupportsDoublePita = true,
            Products =
            [
                new() { Id = "u8def63bdfd730ee", Name = "Πίττα Κοτόπουλο Παππού", Price = 8.70m, DeliveryPrice = 8.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Αλάτι", "Πιπέρι", "Κίτρινη σάλτσα", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, 100gr ψιλοκομμένο φιλέτο κοτόπουλο με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "u8def644e83ae046", Name = "Πίττα Χοιρινό Παππού", Price = 8.70m, DeliveryPrice = 8.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Λευκή σάλτσα", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, 100gr ψιλοκομμένη χοιρινή μπριζόλα με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap3", Name = "Πίττα ανάμεικτο Παππού", Price = 8.70m, DeliveryPrice = 8.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Λευκή σαλτσα", "Κίτρινη σάλτσα", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, 50gr κοτόπουλο & 50gr χοιρινή μπριζόλα με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap4", Name = "Πίττα μπιφτέκι Παππού", Price = 8.70m, DeliveryPrice = 8.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Λευκή σάλτσα", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, μπιφτέκι από 100% μοσχαρίσιο κιμά με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap5", Name = "Πίττα λουκάνικο Παππού", Price = 7.40m, DeliveryPrice = 7.70m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Κέτσαπ", "Μουστάρδα", "Μαγιονέζα", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, χωριάτικο λουκάνικο με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap6", Name = "Πίττα αλλαντικών Παππού", Price = 8.70m, DeliveryPrice = 8.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ζαμπόν", "Μπεικον", "Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Γκούντα", "Μαγιονέζα", "Αλάτι", "Πιπέρι", "Φέτα", "Πιπεριά"], Description = "28cm πίττα, ζαμπόν & μπέικον με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap7", Name = "Πίττα τυριών Παππού", Price = 7.80m, DeliveryPrice = 8.00m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Φέτα", "Γκούντα", "Γραβιέρα", "Ρεγκάντο", "Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Μαγιονέζα", "Αλάτι", "Πιπέρι", "Πιπεριά"], Description = "28cm πίττα, φέτα, gouda ή edam, regato & γραβιέρα με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap8", Name = "Πίττα ομελέτα Παππού", Price = 7.30m, DeliveryPrice = 7.80m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Μαγιονέζα", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, ομελέτα 2 αυγών με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap9", Name = "Πίττα λαχανικών Παππού", Price = 6.40m, DeliveryPrice = 6.60m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Λάχανο", "Καρότο", "Πιπεριά", "Πατάτες", "Μαγιονέζα", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα"], Description = "28cm πίττα λαχανικών με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap10", Name = "Πίττα σνίτσελ Παππού", Price = 8.70m, DeliveryPrice = 8.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Λευκή σάλτσα", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, σνίτσελ από χοιρινό κρέας με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap11", Name = "Πίττα κοστίνα Παππού", Price = 8.70m, DeliveryPrice = 8.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μπάρμπεκιου σάλτσα", "Κρεμμύδι", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, 100-120gr ψημένη χοιρινή πανσέτα με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap12", Name = "Πίττα μπιφτέκι λαχανικών Παππού", Price = 8.50m, DeliveryPrice = 8.70m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Αγρού σάλτσα", "Κρεμμύδι", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, μπιφτέκι λαχανικών με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap13", Name = "Πίττα μπιφτέκι κοτόπουλο Παππού", Price = 8.70m, DeliveryPrice = 8.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κίτρινη σάλτσα", "Κρεμμύδι", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, μπιφτέκι κοτόπουλο με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap14", Name = "Πίττα ελληνικό Παππού", Price = 7.80m, DeliveryPrice = 8.00m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Φέτα", "Ντομάτα", "Μαγιονέζα", "Ελιές", "Αλάτι", "Πιπέρι", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, τρίμμα φέτας & ελιές ροδέλες με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap15", Name = "Πίττα κεμπάπ Παππού", Price = 9.10m, DeliveryPrice = 9.30m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Αγρού σάλτσα", "Κρεμμύδι", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, 2 κεμπάπ με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap16", Name = "Πίττα χοιρινό καλαμάκι Παππού", Price = 9.10m, DeliveryPrice = 9.30m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Κρεμμύδι", "Αγρού σάλτσα", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, χοιρινό καλαμάκι με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap17", Name = "Πίττα κοτόπουλο καλαμάκι Παππού", Price = 9.10m, DeliveryPrice = 9.30m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Κρεμμύδι", "Αγρού σάλτσα", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, κοτόπουλο καλαμάκι με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
                new() { Id = "pap18", Name = "Πίττα κοτομπουκιές Παππού", Price = 8.20m, DeliveryPrice = 8.40m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Κίτρινη σάλτσα", "Αλάτι", "Πιπέρι", "Φέτα", "Γκούντα", "Πιπεριά"], Description = "28cm πίττα, κοτομπουκιές με φέτα, gouda, πιπεριά & τα υλικά της επιλογής σας" },
            ],
        },
        new()
        {
            Id = "klasika", Name = "ΚΛΑΣΙΚΑ ΜΙΚΡΑ", HasBread = false, FuseBreadIntoName = false, SupportsDoublePita = true,
            Products =
            [
                new() { Id = "kls1", Name = "ΜΙΝΙ ΚΟΤΟΠΟΥΛΟ", Price = 4.00m, DeliveryPrice = 4.10m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Κρεμμύδι", "Πατάτες", "Αγρού σάλτσα", "Αλάτι", "Πιπέρι"], Description = "18cm πίττα, φιλέτο κοτόπουλο 60gr με τα υλικά της επιλογής σας" },
                new() { Id = "kls2", Name = "ΜΙΝΙ ΧΟΙΡΙΝΟ", Price = 4.00m, DeliveryPrice = 4.10m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Κρεμμύδι", "Πατάτες", "Αγρού σάλτσα", "Αλάτι", "Πιπέρι"], Description = "18cm πίττα, μπιφτέκι με τα υλικά της επιλογής σας" },
                new() { Id = "kls4", Name = "ΜΙΝΙ ΚΕΜΠΑΠ", Price = 4.00m, DeliveryPrice = 4.10m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Κρεμμύδι", "Πατάτες", "Αγρού σάλτσα", "Αλάτι", "Πιπέρι"], Description = "18cm πίττα, 1 κεμπάπ με τα υλικά της επιλογής σας" },
                new() { Id = "kls5", Name = "ΜΙΝΙ ΧΟΙΡΙΝΟ ΚΑΛΑΜΑΚΙ", Price = 4.00m, DeliveryPrice = 4.10m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Κρεμμύδι", "Πατάτες", "Αγρού σάλτσα", "Αλάτι", "Πιπέρι"], Description = "18cm πίττα, καλαμάκι χοιρινό με τα υλικά της επιλογής σας" },
                new() { Id = "kls6", Name = "ΜΙΝΙ ΚΟΤΟΠΟΥΛΟ ΚΑΛΑΜΑΚΙ", Price = 4.00m, DeliveryPrice = 4.10m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Κρεμμύδι", "Πατάτες", "Αγρού σάλτσα", "Αλάτι", "Πιπέρι"], Description = "18cm πίττα, καλαμάκι κοτόπουλο με τα υλικά της επιλογής σας" },
            ],
        },
        new()
        {
            Id = "kalamakia", Name = "ΚΑΛΑΜΑΚΙΑ", HasBread = false, FuseBreadIntoName = false, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "klm1", Name = "Καλαμάκι χοιρινό", Price = 2.50m, DeliveryPrice = 2.70m, ExtraNames = StandardExtras, Ingredients = ["Πιττάκι", "Σάλτσα λεμονιού", "Ρίγανη", "Αλάτι", "Πιπέρι"], Description = "100gr. Συνοδεύεται από πιττάκι" },
                new() { Id = "klm2", Name = "Καλαμάκι κοτόπουλο", Price = 2.50m, DeliveryPrice = 2.70m, ExtraNames = StandardExtras, Ingredients = ["Πιττάκι", "Σάλτσα λεμονιού", "Ρίγανη", "Αλάτι", "Πιπέρι"], Description = "100gr. Συνοδεύεται από πιττάκι" },
                new() { Id = "klm3", Name = "Κεμπάπ", Price = 2.50m, DeliveryPrice = 2.70m, ExtraNames = StandardExtras, Ingredients = ["Πιττάκι", "Σάλτσα λεμονιού", "Ρίγανη", "Αλάτι", "Πιπέρι"], Description = "100gr. Συνοδεύεται από πιττάκι" },
                new() { Id = "klm4", Name = "Λουκάνικο χωριάτικο", Price = 2.50m, DeliveryPrice = 2.70m, ExtraNames = StandardExtras, Ingredients = ["Πιττάκι", "Σάλτσα λεμονιού", "Ρίγανη", "Αλάτι", "Πιπέρι"], Description = "Συνοδεύεται από πιττάκι" },
            ],
        },
        new()
        {
            Id = "clubs", Name = "ΠΙΤΤΑ CLUB", HasBread = false, FuseBreadIntoName = false, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "clb1", Name = "Club κοτόπουλο", Price = 9.70m, DeliveryPrice = 9.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Μαγιονέζα", "Μπεικον", "Πατάτες"], Description = "100gr φιλέτο κοτόπουλο, μαγιονέζα, ντομάτα, μαρούλι & μπέικον. Με πατάτες τηγανητές" },
                new() { Id = "clb2", Name = "Club χοιρινό", Price = 9.70m, DeliveryPrice = 9.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Μαγιονέζα", "Μπεικον", "Πατάτες"], Description = "100gr μπριζόλα χοιρινή, μαγιονέζα, ντομάτα, μαρούλι & μπέικον. Με πατάτες τηγανητές" },
                new() { Id = "clb3", Name = "Club μπιφτέκι", Price = 9.70m, DeliveryPrice = 9.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Μαγιονέζα", "Μπεικον", "Πατάτες"], Description = "Μπιφτέκι 100% μοσχαρίσιο, μαγιονέζα, ντομάτα, μαρούλι & μπέικον. Με πατάτες τηγανητές" },
                new() { Id = "clb4", Name = "Club αλλαντικών", Price = 9.70m, DeliveryPrice = 9.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Μαγιονέζα", "Μπεικον", "Πατάτες"], Description = "Ζαμπόν, gouda ή edam, μαγιονέζα, ντομάτα, μαρούλι & μπέικον. Με πατάτες τηγανητές" },
                new() { Id = "clb5", Name = "Club μπιφτέκι λαχανικών", Price = 9.70m, DeliveryPrice = 9.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Μαγιονέζα", "Πατάτες"], Description = "2 μπιφτέκια λαχανικών, μαγιονέζα ή BBQ, ντομάτα & μαρούλι. Με πατάτες τηγανητές" },
                new() { Id = "clb6", Name = "Club μπιφτέκι κοτόπουλο", Price = 9.70m, DeliveryPrice = 9.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κίτρινη σάλτσα", "Μπεικον", "Πατάτες"], Description = "1 μπιφτέκι κοτόπουλο, κίτρινη σάλτσα, μπέικον, ντομάτα & μαρούλι. Με πατάτες τηγανητές" },
                new() { Id = "clb7", Name = "Club ανάμεικτο", Price = 9.70m, DeliveryPrice = 9.90m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Μαγιονέζα", "Μπεικον", "Πατάτες"], Description = "Φιλέτο κοτόπουλο & χοιρινή μπριζόλα, μαγιονέζα, ντομάτα, μαρούλι & μπέικον. Με πατάτες τηγανητές" },
            ],
        },
        new()
        {
            Id = "burgers", Name = "BURGERS", HasBread = false, FuseBreadIntoName = false, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "brg1", Name = "Mini burger", Price = 4.00m, DeliveryPrice = 4.10m, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Ρόζ σάλτσα"], Description = "Ψωμάκι brioche με μπιφτέκι 60gr, ντομάτα & ροζ σάλτσα" },
                new() { Id = "brg2", Name = "Burger απλό", Price = 6.20m, DeliveryPrice = 6.40m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Ρόζ σάλτσα"], Description = "Μπιφτέκι 100% μοσχαρίσιο 180gr, μαρούλι, ντομάτα & ροζ σάλτσα παππού σε ψημένο ψωμάκι" },
                new() { Id = "brg3", Name = "Burger τυρί", Price = 6.50m, DeliveryPrice = 6.70m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Γκούντα", "Ρόζ σάλτσα"], Description = "Μπιφτέκι 180gr, μαρούλι, ντομάτα, ροζ σάλτσα, gouda ή edam σε ψημένο ψωμάκι" },
                new() { Id = "brg4", Name = "Burger special", Price = 7.20m, DeliveryPrice = 7.40m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Γκούντα", "Μπεικον", "Αυγό", "Ρόζ σάλτσα"], Description = "Μπιφτέκι 180gr, μαρούλι, ντομάτα, ροζ σάλτσα, τυρί, αυγό & μπέικον σε ψημένο ψωμάκι" },
                new() { Id = "brg5", Name = "Burger μπιφτέκι λαχανικών", Price = 6.00m, DeliveryPrice = 6.20m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Λευκή σάλτσα"], Description = "Μπιφτέκι λαχανικών 180gr, μαρούλι, ντομάτα & λευκή σάλτσα παππού σε ψημένο ψωμάκι" },
                new() { Id = "brg6", Name = "Burger μπιφτέκι κοτόπουλο", Price = 6.00m, DeliveryPrice = 6.20m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κίτρινη σάλτσα"], Description = "Μπιφτέκι κοτόπουλο 180gr, μαρούλι, ντομάτα & κίτρινη σάλτσα σε ψημένο ψωμάκι" },
                new() { Id = "brg7", Name = "Burger deluxe", Price = 8.20m, DeliveryPrice = 8.40m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Γκούντα", "Ρόζ σάλτσα", "Πατάτες", "Πίκλες", "Σαλ.Μαρούλι", "Σαλ.Αγγούρι", "Σαλ.Ντομ", "Σαλ.Κρεμ"], Description = "Μπιφτέκι 180gr, τυρί, ροζ σάλτσα & πίκλες. Με πατάτες τηγανητές & σαλάτα" },
                new() { Id = "brg8", Name = "Burger κοτόπουλο deluxe", Price = 8.20m, DeliveryPrice = 8.40m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Γκούντα", "Κίτρινη σάλτσα", "Πατάτες", "Πίκλες", "Σαλ.Μαρούλι", "Σαλ.Αγγούρι", "Σαλ.Ντομ", "Σαλ.Κρεμ"], Description = "Μπιφτέκι κοτόπουλο 180gr, τυρί, ροζ σάλτσα & πίκλες. Με πατάτες τηγανητές & σαλάτα" },
                new() { Id = "brg9", Name = "Burger special deluxe", Price = 9.40m, DeliveryPrice = 9.60m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Γκούντα", "Μπεικον", "Αυγό", "Ρόζ σάλτσα", "Πατάτες", "Πίκλες", "Σαλ.Μαρούλι", "Σαλ.Αγγούρι", "Σαλ.Ντομ", "Σαλ.Κρεμ"], Description = "Μπιφτέκι 180gr, τυρί, ροζ σάλτσα, πίκλες, μπέικον & αυγό. Με πατάτες τηγανητές & σαλάτα" },
            ],
        },
        new()
        {
            Id = "merides", Name = "ΜΕΡΙΔΕΣ", HasBread = true, FuseBreadIntoName = false, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "mrd1", Name = "Κοτόπουλο μερίδα", Price = 10.50m, DeliveryPrice = 10.80m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Μαρούλι", "Κρεμμύδι", "Αλάτι", "Πιπέρι", "Κίτρινη σάλτσα", "Πίττα", "Πατάτες", "Ρίγανη"], Description = "200gr φιλέτο κοτόπουλο, πατάτες, κίτρινη σάλτσα, μαρούλι, κρεμμύδι, ντομάτα & πίττα" },
                new() { Id = "mrd2", Name = "Χοιρινό μερίδα", Price = 10.50m, DeliveryPrice = 10.80m, Customizable = true, ExtraNames = StandardExtras, Description = "200gr χοιρινό, πατάτες, λευκή σάλτσα, μαρούλι, κρεμμύδι, ντομάτα & πίττα" },
                new() { Id = "mrd3", Name = "Ανάμεικτο μερίδα", Price = 10.50m, DeliveryPrice = 10.80m, Customizable = true, ExtraNames = StandardExtras, Description = "100gr κοτόπουλο & 100gr χοιρινό, πατάτες, σάλτσες, λαχανικά & πίττα" },
                new() { Id = "mrd4", Name = "Μπιφτέκι μερίδα", Price = 10.50m, DeliveryPrice = 10.80m, Customizable = true, ExtraNames = StandardExtras, Description = "2 τεμ. 100% μοσχαρίσιος κιμάς, πατάτες, λευκή σάλτσα, λαχανικά & πίττα" },
                new() { Id = "mrd5", Name = "Σνίτσελ μερίδα", Price = 10.50m, DeliveryPrice = 10.80m, Customizable = true, ExtraNames = StandardExtras, Description = "Χοιρινό σνίτσελ με κόκκινη σάλτσα & τυρί. Με πατάτες, ψωμί & πίττα" },
                new() { Id = "mrd6", Name = "Λουκάνικο μερίδα", Price = 8.60m, DeliveryPrice = 8.80m, Customizable = true, ExtraNames = StandardExtras, Description = "2 τεμ. Με πατάτες, ketchup, μουστάρδα, λαχανικά & πίττα" },
                new() { Id = "mrd7", Name = "Ομελέτα μερίδα", Price = 7.80m, DeliveryPrice = 8.00m, Customizable = true, ExtraNames = StandardExtras, Description = "3 αυγά, πιπεριά, ζαμπόν, μπέικον & τυρί. Με πατάτες, μαγιονέζα & πίττα" },
                new() { Id = "mrd8", Name = "Μπιφτέκι special μερίδα", Price = 10.70m, DeliveryPrice = 11.00m, Customizable = true, ExtraNames = StandardExtras, Description = "2 τεμ. με τυρί, μπέικον & ροζ σάλτσα. Με πατάτες & πίττα" },
                new() { Id = "mrd9", Name = "Κοστίνα μερίδα", Price = 10.30m, DeliveryPrice = 10.50m, Customizable = true, ExtraNames = StandardExtras, Description = "350gr ψημένη χοιρινή κοστίνα, ντομάτα, μαρούλι, σάλτσα λεμονιού ή BBQ, πατάτες & πίττα" },
                new() { Id = "mrd10", Name = "Μπιφτέκι λαχανικών μερίδα", Price = 10.50m, DeliveryPrice = 10.70m, Customizable = true, ExtraNames = StandardExtras, Description = "2 τεμ. Με πατάτες, σάλτσα αγρού, λαχανικά & πίττα" },
                new() { Id = "mrd11", Name = "Μπιφτέκι κοτόπουλο μερίδα", Price = 10.50m, DeliveryPrice = 10.70m, Customizable = true, ExtraNames = StandardExtras, Description = "2 τεμ. Με πατάτες, κίτρινη σάλτσα, λαχανικά & πίττα" },
                new() { Id = "mrd12", Name = "Λαχανικών μερίδα", Price = 6.20m, DeliveryPrice = 6.40m, Customizable = true, ExtraNames = StandardExtras, Description = "Σοταρισμένα λαχανικά σβησμένα σε κρασί ή μπύρα. Με πατάτες & πίττα" },
                new() { Id = "mrd13", Name = "Κεμπάπ μερίδα", Price = 10.50m, DeliveryPrice = 11.00m, Customizable = true, ExtraNames = StandardExtras, Description = "3 τεμ. Με πατάτες, σάλτσα αγρού ή τζατζίκι, κρεμμύδι, ντομάτα & πίττα" },
                new() { Id = "mrd14", Name = "Κοτομπουκιές πανέ μερίδα", Price = 7.50m, DeliveryPrice = 7.70m, Customizable = true, ExtraNames = StandardExtras, Description = "8 τεμ. 100% φιλέτο στήθος. Με πατάτες, πιττάκι & κίτρινη σάλτσα" },
                new() { Id = "mrd15", Name = "Ποικιλία μικρή", Price = 9.00m, DeliveryPrice = 9.20m, Customizable = true, ExtraNames = StandardExtras, Description = "1 λουκάνικο, κοτόπουλο & μπέικον. Με πιττάκι, πατάτες & σαλάτα" },
                new() { Id = "mrd16", Name = "Ποικιλία μεγάλη 3 ατόμων", Price = 15.20m, DeliveryPrice = 15.40m, Customizable = true, ExtraNames = StandardExtras, Description = "1 μπιφτέκι, 1 λουκάνικο, 100gr κοστίνα & 70gr κοτόπουλο. Με πατάτες, 2 πιττάκια, μπέικον & σαλάτα" },
            ],
        },
        new()
        {
            Id = "merides-sketes", Name = "ΜΕΡΙΔΕΣ ΣΚΕΤΕΣ", HasBread = false, FuseBreadIntoName = false, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "msk1", Name = "Φιλέτο κοτόπουλο σκέτο", Price = 6.90m, DeliveryPrice = 7.10m, ExtraNames = StandardExtras, Ingredients = [], Description = "200gr ψιλοκομμένο φιλέτο κοτόπουλο (χωρίς συνοδευτικό)" },
                new() { Id = "msk2", Name = "Φιλέτο χοιρινό σκέτο", Price = 6.90m, DeliveryPrice = 7.10m, ExtraNames = StandardExtras, Ingredients = [], Description = "200gr ψιλοκομμένη χοιρινή μπριζόλα (χωρίς συνοδευτικό)" },
                new() { Id = "msk3", Name = "Λουκάνικο σκέτο", Price = 6.80m, ExtraNames = StandardExtras, Ingredients = [], Description = "2 τεμάχια (χωρίς συνοδευτικό)" },
                new() { Id = "msk4", Name = "Μπιφτέκι μοσχαρίσιο σκέτο", Price = 6.70m, DeliveryPrice = 6.90m, ExtraNames = StandardExtras, Ingredients = [], Description = "2 τεμ. από 100% μοσχαρίσιο κιμά (χωρίς συνοδευτικό)" },
                new() { Id = "msk5", Name = "Ανάμεικτη σκέτη", Price = 6.90m, DeliveryPrice = 7.10m, ExtraNames = StandardExtras, Ingredients = [], Description = "100gr χοιρινό & 100gr κοτόπουλο (χωρίς συνοδευτικό)" },
            ],
        },
        new()
        {
            Id = "merides-pappou", Name = "ΜΕΡΙΔΕΣ ΠΑΠΠΟΥ", HasBread = true, FuseBreadIntoName = true, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "mpp1", Name = "Κοτόπουλο Παππού", Price = 10.70m, DeliveryPrice = 11.00m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Μανιτάρια", "Ντομάτα", "Κρεμμύδι", "Πιπεριά", "Πατάτες", "Αλάτι", "Πιπέρι"], Description = "200gr κοτόπουλο με σοταρισμένα λαχανικά σβησμένα με μπύρα ή κρασί. Με πατάτες & πίττα" },
                new() { Id = "mpp2", Name = "Χοιρινό Παππού", Price = 10.70m, DeliveryPrice = 11.00m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Μανιτάρια", "Ντομάτα", "Κρεμμύδι", "Πιπεριά", "Πατάτες", "Αλάτι", "Πιπέρι"], Description = "200gr χοιρινή μπριζόλα με σοταρισμένα λαχανικά σβησμένα με μπύρα ή κρασί. Με πατάτες & πίττα" },
                new() { Id = "mpp3", Name = "Ανάμεικτο Παππού", Price = 10.70m, DeliveryPrice = 11.00m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Μανιτάρια", "Ντομάτα", "Κρεμμύδι", "Πιπεριά", "Πατάτες", "Αλάτι", "Πιπέρι"], Description = "100gr χοιρινό & 100gr κοτόπουλο με σοταρισμένα λαχανικά. Με πατάτες & πίττα" },
                new() { Id = "mpp4", Name = "Μπιφτέκι Παππού", Price = 10.50m, DeliveryPrice = 10.70m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Μανιτάρια", "Ντομάτα", "Κρεμμύδι", "Πιπεριά", "Πατάτες", "Αλάτι", "Πιπέρι"], Description = "2 μπιφτέκια με σοταρισμένα λαχανικά σβησμένα με μπύρα ή κρασί. Με πατάτες & πίττα" },
                new() { Id = "mpp5", Name = "Μπιφτέκι λαχανικών Παππού", Price = 10.50m, DeliveryPrice = 10.70m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Μανιτάρια", "Ντομάτα", "Κρεμμύδι", "Πιπεριά", "Πατάτες", "Αλάτι", "Πιπέρι"], Description = "2 μπιφτέκια λαχανικών με σοταρισμένα λαχανικά. Με πατάτες & πίττα" },
                new() { Id = "mpp6", Name = "Μπιφτέκι κοτόπουλο Παππού", Price = 10.50m, DeliveryPrice = 10.70m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Μανιτάρια", "Ντομάτα", "Κρεμμύδι", "Πιπεριά", "Πατάτες", "Αλάτι", "Πιπέρι"], Description = "2 μπιφτέκια κοτόπουλο με σοταρισμένα λαχανικά. Με πατάτες & πίττα" },
            ],
        },
        new()
        {
            Id = "parea", Name = "ΤΗΣ ΠΑΡΕΑΣ", HasBread = false, FuseBreadIntoName = false, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "par1", Name = "Κοστίνα 500 Gr.", Price = 8.50m, DeliveryPrice = 8.70m, Customizable = true, ExtraNames = StandardExtras, Ingredients = [], Description = "Με σάλτσα BBQ ή λεμονιού. Η τελική τιμή διαμορφώνεται στο ζύγισμα" },
                new() { Id = "u8def3fec7b2bd78", Name = "Κοστίνα Κιλό", Price = 17.00m, DeliveryPrice = 18.00m, Customizable = true, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "par2", Name = "Μπουκίτσες κοτόπουλο (20 τεμ.)", Price = 12.50m, Customizable = true, ExtraNames = StandardExtras, Ingredients = [] },
            ],
        },
        new()
        {
            Id = "paidiko", Name = "ΠΑΙΔΙΚΟ MENU", HasBread = false, FuseBreadIntoName = false, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "pdk1", Name = "Burgerάκι με μπιφτέκι", Price = 7.50m, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Κέτσαπ"], Description = "Μπιφτέκι μικρό, ντομάτα & ketchup. Με πατατούλες, Amita Fun & δώρο έκπληξη!" },
                new() { Id = "pdk2", Name = "Παιδικό κοτόπουλο πιττάκι", Price = 7.50m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Πατάτες"], Description = "Πίττα μικρή με φιλέτο κοτόπουλο, ντομάτα & πατάτες. Με Amita Fun & δώρο έκπληξη!" },
                new() { Id = "pdk3", Name = "Παιδικό μπιφτέκι πιττάκι", Price = 7.50m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Πατάτες"], Description = "Πίττα μικρή με μπιφτέκι, ντομάτα & πατάτες. Με Amita Fun & δώρο έκπληξη!" },
                new() { Id = "pdk4", Name = "Παιδικό χοιρινό πιττάκι", Price = 7.50m, Customizable = true, ExtraNames = StandardExtras, Ingredients = ["Ντομάτα", "Πατάτες"], Description = "Πίττα μικρή με χοιρινή μπριζόλα, ντομάτα & πατάτες. Με Amita Fun & δώρο έκπληξη!" },
                new() { Id = "pdk5", Name = "Μπουκίτσες κοτόπουλο", Price = 7.50m, Customizable = true, ExtraNames = StandardExtras, Ingredients = [], Description = "5 τεμ. πανέ. Με πατάτες, Amita Fun & δώρο έκπληξη!" },
            ],
        },
        new()
        {
            Id = "anapsyktika", Name = "ΑΝΑΨΥΚΤΙΚΑ", HasBread = false, FuseBreadIntoName = false, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "dr1", Name = "Coca-Cola 330ml", Price = 1.90m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "dr2", Name = "Coca-Cola zero 330ml", Price = 1.90m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "dr3", Name = "Coca-Cola zero χωρίς καφεΐνη 330ml", Price = 1.90m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "dr4", Name = "Sprite 330ml", Price = 1.90m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "dr5", Name = "Fanta πορτοκαλάδα 330ml", Price = 1.90m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "dr6", Name = "Fanta πορτοκαλάδα μπλε 330ml", Price = 1.90m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "dr7", Name = "Soda Tuborg 330ml", Price = 1.50m, ExtraNames = StandardExtras },
                new() { Id = "dr8", Name = "Amita Fun 250ml", Price = 1.50m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "dr9", Name = "Coca-Cola 500ml", Price = 2.30m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "dr10", Name = "Coca-Cola zero 500ml", Price = 2.30m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "dr11", Name = "Coca-Cola 1.5lt", Price = 3.60m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "dr12", Name = "Νερό 500ml", Price = 0.50m, ExtraNames = StandardExtras, Ingredients = [] },
            ],
        },
        new()
        {
            Id = "beers", Name = "ΜΠΥΡΕΣ · ΠΟΤΑ", HasBread = false, FuseBreadIntoName = false, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "br1", Name = "Heineken 330ml", Price = 2.20m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "br2", Name = "Amstel 330ml", Price = 2.20m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "br3", Name = "Άλφα 330ml", Price = 2.20m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "br4", Name = "Heineken φιάλη 500ml", Price = 3.40m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "br5", Name = "Amstel φιάλη 500ml", Price = 3.40m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "br6", Name = "Kaiser φιάλη 500ml", Price = 3.40m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "br7", Name = "Άλφα φιάλη 500ml", Price = 3.40m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "u8def3e60f887ae6", Name = "ΠΟΤΗΡΙ ΜΠΥΡΑ ΜΙΚΡΟ", Price = 2.50m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "u8def3e619e43e60", Name = "ΠΟΤΗΡΙ ΜΠΥΡΑ ΜΕΓΑΛΟ", Price = 3.50m, ExtraNames = StandardExtras, Ingredients = [] },
            ],
        },
        new()
        {
            Id = "u8def3dbd775bf31", Name = "ΚΡΑΣΙΑ", HasBread = false, FuseBreadIntoName = false, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "u8def3ff11c8cedd", Name = "Ποτήρι Κρασί", Price = 2.00m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "u8def3dbf4c80097", Name = "ΧΥΜΑ ΚΡΑΣΙ ΜΙΣΟ ΚΙΛΟ", Price = 3.50m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "u8def3ff1fc518bb", Name = "Χυμα Κρασί 1 Κιλό", Price = 5.20m, ExtraNames = StandardExtras },
            ],
        },
        new()
        {
            Id = "u8def3ff2f719a83", Name = "ΠΡΟΣΩΠΙΚΟ", HasBread = false, FuseBreadIntoName = false, SupportsDoublePita = false,
            Products =
            [
                new() { Id = "u8def3ff37389108", Name = "ΑΝΑΨΥΚΤΙΚΌ", Price = 1.00m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "u8def3ff3e3d7166", Name = "ΔΙΑΦΟΡΑ ΜΕΓΑΛΗΣ ΠΙΤΤΑΣ", Price = 3.00m, ExtraNames = StandardExtras, Ingredients = [] },
                new() { Id = "u8def3ff425d79ff", Name = "ΝΕΡΟ", Price = 0.30m, ExtraNames = StandardExtras, Ingredients = [] },
            ],
        },
    ];
}
