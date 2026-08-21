using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.App.Services;
using PittaPos.Core.Models;

namespace PittaPos.App.ViewModels;

/// <summary>Μία γραμμή υλικού μέσα σε προϊόν — «Κοτόπουλο 100».</summary>
public partial class MaterialLineViewModel : ObservableObject
{
    public required ConsumptionRowViewModel Owner { get; init; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _grams = "";

    [RelayCommand]
    private void Remove() => Owner.Materials.Remove(this);
}

/// <summary>Ένα προϊόν (ή ένα έξτρα) με τις καταναλώσεις του.</summary>
public partial class ConsumptionRowViewModel : ObservableObject
{
    public required string Title { get; init; }
    /// <summary>Το ένα από τα δύο είναι γεμάτο — ή προϊόν καταλόγου ή έξτρα.</summary>
    public Product? Product { get; init; }
    public ExtraItem? Extra { get; init; }

    public ObservableCollection<MaterialLineViewModel> Materials { get; } = [];

    [RelayCommand]
    private void Add() => Materials.Add(new MaterialLineViewModel { Owner = this });
}

/// <summary>Μια κατηγορία του καταλόγου, με τη «βάση» της σε γραμμάρια.</summary>
public partial class ConsumptionGroupViewModel : ObservableObject
{
    public required string Title { get; init; }
    public required IReadOnlyList<ConsumptionRowViewModel> Rows { get; init; }

    /// <summary>Πόσα γραμμάρια έχει ΤΥΠΙΚΑ ένα τεμάχιο αυτής της κατηγορίας — «κάθε μεγάλη πίττα έχει
    /// πάντα 100». Δεν αποθηκεύεται πουθενά: είναι μόνο το νούμερο που χρησιμοποιεί το «ΣΥΜΠΛΗΡΩΣΕ».</summary>
    [ObservableProperty] private string _baseGrams = "";

    /// <summary>
    /// Γεμίζει με μία κίνηση όσα προϊόντα της κατηγορίας είναι <b>ακόμα άδεια</b> — ποτέ δεν πατάει
    /// πάνω σε νούμερο που έχει γράψει ο χρήστης.
    ///
    /// <para>Το είδος του κρέατος βγαίνει από το όνομα του προϊόντος («Πίττα κοτόπουλο» → Κοτόπουλο),
    /// και δύο περιπτώσεις μετρώνται σε <b>τεμάχια</b> αντί για γραμμάρια, όπως ακριβώς τα είπε το
    /// μαγαζί: το <b>καλαμάκι</b> ζυγίζει 110 γρ. και μπαίνουν 1 στη μικρή, 2 στη μεγάλη, 3 στη μερίδα·
    /// το <b>μπιφτέκι</b> ζυγίζει 180 γρ. και μπαίνει μισό στη μικρή, ένα στη μεγάλη, δύο στη μερίδα.
    /// Ό,τι δεν αναγνωρίζεται (ομελέτα, τυριών, σαλάτες) μένει άδειο για το χέρι.</para>
    /// </summary>
    [RelayCommand]
    private void Fill()
    {
        if (!ConsumptionViewModel.TryGrams(BaseGrams, out var basis) || basis <= 0)
            return;

        foreach (var row in Rows)
        {
            if (row.Materials.Count > 0)
                continue;
            foreach (var (name, grams) in ConsumptionViewModel.Guess(row.Title, basis))
                row.Materials.Add(new MaterialLineViewModel { Owner = row, Name = name, Grams = ConsumptionViewModel.GramsText(grams) });
        }
    }
}

/// <summary>
/// Η οθόνη «ΚΑΤΑΝΑΛΩΣΕΙΣ»: πόσο και τι κρέας τρώει κάθε προϊόν. Από εδώ βγαίνει το σύνολο της ημέρας
/// («σήμερα έφυγαν 12,4 κιλά κοτόπουλο») — βλ. <see cref="ConsumptionService"/>.
/// </summary>
public partial class ConsumptionViewModel : ObservableObject
{
    private static readonly CultureInfo Greek = CultureInfo.GetCultureInfo("el-GR");

    public ConsumptionViewModel()
    {
        var groups = new List<ConsumptionGroupViewModel>();
        foreach (var category in MenuStore.Instance.Categories)
        {
            var rows = category.Products
                .Select(p => Build(new ConsumptionRowViewModel { Title = p.Name, Product = p }, p.Materials))
                .ToList();
            groups.Add(new ConsumptionGroupViewModel { Title = category.Name, Rows = rows });
        }

        // Τα έξτρα στο τέλος, σαν να ήταν κατηγορία: χωρίς αυτά, ένα «έξτρα κοτόπουλο» δεν μετριόταν
        // πουθενά και το σύνολο της ημέρας έβγαινε λιγότερο από την αλήθεια.
        var extraRows = MenuStore.Instance.Extras
            .Select(e => Build(new ConsumptionRowViewModel { Title = e.Name, Extra = e }, e.Materials))
            .ToList();
        if (extraRows.Count > 0)
            groups.Add(new ConsumptionGroupViewModel { Title = "ΕΞΤΡΑ", Rows = extraRows });

        Groups = groups;
        RefreshToday();
        // Το «ΕΦΥΓΑΝ ΣΗΜΕΡΑ» είναι ζωντανό νούμερο: η οθόνη ανοίγει από την κεφαλίδα της αρχικής και
        // μπορεί να μείνει ανοιχτή όλη τη βάρδια — χωρίς αυτό θα έδειχνε τα κιλά της στιγμής που άνοιξε.
        SalesStatsService.Instance.Changed += RefreshToday;
    }

    /// <summary>Ξεκολλάει από τα στατιστικά όταν κλείνει το παράθυρο — αλλιώς κάθε παραγγελία θα
    /// ξανακτύπαγε τον υπολογισμό για ένα παράθυρο που δεν υπάρχει πια, μία φορά για κάθε άνοιγμα.</summary>
    public void Detach() => SalesStatsService.Instance.Changed -= RefreshToday;

    private static ConsumptionRowViewModel Build(ConsumptionRowViewModel row, List<MaterialUse>? materials)
    {
        foreach (var use in materials ?? [])
            row.Materials.Add(new MaterialLineViewModel { Owner = row, Name = use.Name, Grams = GramsText(use.Grams) });
        return row;
    }

    public IReadOnlyList<ConsumptionGroupViewModel> Groups { get; }

    /// <summary>Τι έχει φύγει σήμερα — ο λόγος που υπάρχει όλη η οθόνη.</summary>
    [ObservableProperty] private IReadOnlyList<MaterialTotal> _today = [];

    [ObservableProperty] private string _status = "";

    public bool HasToday => Today.Count > 0;

    private void RefreshToday()
    {
        Today = ConsumptionService.Today();
        OnPropertyChanged(nameof(HasToday));
    }

    /// <summary>Γράφει τα πάντα πίσω στον κατάλογο. Άδειο όνομα ή μηδενικά γραμμάρια = η γραμμή δεν
    /// υπάρχει· προϊόν χωρίς γραμμές μένει με <c>null</c>, δηλαδή «δεν μετράει πουθενά».</summary>
    [RelayCommand]
    public void Save()
    {
        foreach (var group in Groups)
            foreach (var row in group.Rows)
            {
                var uses = row.Materials
                    .Where(m => m.Name.Trim().Length > 0 && TryGrams(m.Grams, out var g) && g > 0)
                    .Select(m => new MaterialUse { Name = m.Name.Trim(), Grams = TryGrams(m.Grams, out var g) ? g : 0 })
                    .ToList();
                var value = uses.Count > 0 ? uses : null;
                if (row.Product is { } product)
                    product.Materials = value;
                else if (row.Extra is { } extra)
                    extra.Materials = value;
            }

        MenuStore.Instance.Save();
        RefreshToday();
        Status = "✓ Αποθηκεύτηκε";
    }

    /// <summary>Δέχεται και «100» και «0,5» και «0.5» — ο ταμίας δεν πρέπει να σκέφτεται την υποδιαστολή.</summary>
    public static bool TryGrams(string text, out double grams) =>
        double.TryParse(text.Trim().Replace('.', ','), NumberStyles.Any, Greek, out grams);

    public static string GramsText(double grams) =>
        grams == Math.Floor(grams) ? ((long)grams).ToString(Greek) : grams.ToString("0.##", Greek);

    /// <summary>Τι κρέας «λέει» το όνομα ενός προϊόντος, και πόσο. Βλ. <see cref="ConsumptionGroupViewModel.Fill"/>.</summary>
    public static IEnumerable<(string Name, double Grams)> Guess(string productName, double basis)
    {
        var n = Fold(productName);

        // Καλαμάκι και μπιφτέκι μετρώνται σε ΤΕΜΑΧΙΑ, όχι σε γραμμάρια: το καλαμάκι ζυγίζει 110 και το
        // μπιφτέκι 180, και αυτό που αλλάζει είναι ΠΟΣΑ μπαίνουν.
        //
        // Πιάνει ΜΟΝΟ στις τρεις γνωστές βάσεις (60 μικρή, 100 μεγάλη, 200 μερίδα) — και όχι σε
        // «μικρότερο/μεγαλύτερο από». Με εύρη, η κατηγορία ΚΑΛΑΜΑΚΙΑ (όπου η φυσική βάση είναι το ίδιο
        // το 110) έπεφτε στο «μέχρι 200» και έβγαζε 330 γραμμάρια για ΕΝΑ καλαμάκι. Ό,τι άλλο νούμερο
        // γράψει ο χρήστης το εννοεί κυριολεκτικά και μπαίνει ως έχει.
        double? piece = null;
        if (n.Contains("καλαμακι") || n.Contains("σουβλακι"))
            piece = basis switch { 60 => 110, 100 => 220, 200 => 330, _ => null };
        else if (n.Contains("μπιφτεκι"))
            piece = basis switch { 60 => 90, 100 => 180, 200 => 360, _ => null };

        var grams = piece ?? basis;

        // Πρώτα οι πιο ειδικές ονομασίες: το «μπιφτέκι κοτόπουλο» ΔΕΝ είναι κοτόπουλο φιλέτο.
        if (n.Contains("μπιφτεκι") && n.Contains("λαχανικ")) { yield return ("Μπιφτέκι λαχανικών", grams); yield break; }
        if (n.Contains("μπιφτεκι") && n.Contains("κοτοπουλο")) { yield return ("Μπιφτέκι κοτόπουλο", grams); yield break; }
        if (n.Contains("μπιφτεκι")) { yield return ("Μπιφτέκι", grams); yield break; }
        if (n.Contains("κοτομπουκ") || n.Contains("μπουκιτσ")) { yield return ("Κοτομπουκιές", grams); yield break; }
        if (n.Contains("κεμπαπ")) { yield return ("Κεμπάπ", grams); yield break; }
        if (n.Contains("λουκανικ")) { yield return ("Λουκάνικο", grams); yield break; }
        if (n.Contains("σνιτσελ")) { yield return ("Σνίτσελ", grams); yield break; }
        if (n.Contains("κοστινα") || n.Contains("πανσετα")) { yield return ("Κοστίνα", grams); yield break; }
        if (n.Contains("γυρο")) { yield return ("Γύρος", grams); yield break; }
        // Ανάμεικτο = μισό-μισό, όπως βγαίνει και στο χέρι.
        if (n.Contains("αναμεικτ") || n.Contains("αναμικτ"))
        {
            yield return ("Κοτόπουλο", grams / 2);
            yield return ("Χοιρινό", grams / 2);
            yield break;
        }
        if (n.Contains("κοτοπουλο")) { yield return ("Κοτόπουλο", grams); yield break; }
        if (n.Contains("χοιρινο")) { yield return ("Χοιρινό", grams); yield break; }
    }

    /// <summary>Πεζά χωρίς τόνους — «ΜΙΝΙ ΚΟΤΟΠΟΥΛΟ», «Πίττα κοτόπουλο» και «κοτοπουλο» πρέπει να
    /// πιάνονται όλα το ίδιο.</summary>
    private static string Fold(string text)
    {
        var lower = text.ToLower(Greek);
        var sb = new System.Text.StringBuilder(lower.Length);
        foreach (var c in lower)
            sb.Append(c switch
            {
                'ά' => 'α', 'έ' => 'ε', 'ή' => 'η', 'ί' or 'ϊ' or 'ΐ' => 'ι',
                'ό' => 'ο', 'ύ' or 'ϋ' or 'ΰ' => 'υ', 'ώ' => 'ω', 'ς' => 'σ',
                _ => c,
            });
        return sb.ToString();
    }
}
