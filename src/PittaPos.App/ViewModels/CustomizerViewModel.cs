using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.App.Services;
using PittaPos.Core.Data;
using PittaPos.Core.Models;

namespace PittaPos.App.ViewModels;

/// <summary>Customizer πίττας — ψωμί, υλικά, έξτρα, σημείωση, χρέωση.</summary>
public partial class CustomizerViewModel : ObservableObject
{
    private readonly ProductsViewModel _owner;
    private readonly CartLineViewModel? _editingLine;

    public CustomizerViewModel(ProductsViewModel owner, Product product, string categoryLabel,
        CartLineViewModel? editingLine = null)
    {
        _owner = owner;
        Product = product;
        BasePrice = owner.PriceOf(product); // τιμή μαγαζιού ή διανομής ανάλογα με την παραγγελία
        CategoryLabel = categoryLabel;
        _editingLine = editingLine;

        // Μόνο τα ΤΥΛΙΧΤΑ έχουν επιλογή ψωμιού — άλλες customizable κατηγορίες (π.χ. ΠΙΤΤΑ CLUB) δεν
        // δείχνουν καθόλου ψωμί, ούτε βγαίνει συντομογραφία στο όνομα της γραμμής (βλ. Add() παρακάτω).
        HasBreadChoice = MenuStore.Instance.HasBreadChoice(categoryLabel);
        Breads = HasBreadChoice
            ? new ObservableCollection<BreadOptionViewModel>(
                MenuSeed.BreadOptions.Select(b => new BreadOptionViewModel(this, b)))
            : [];
        // Τα υλικά ΤΟΥ προϊόντος, αν έχει δηλώσει δικά του από τη Διαχείριση Καταλόγου — αλλιώς ο
        // κοινός κατάλογος, όπως δούλευε πάντα. Πριν ήταν μία σταθερή λίστα για όλα τα προϊόντα.
        Ingredients = new ObservableCollection<IngredientViewModel>(
            MenuStore.Instance.IngredientsFor(product).Select(n => new IngredientViewModel(this, n)));
        // null ExtraNames = όλα επιτρεπτά (βλ. MenuManagerViewModel) — μόνο τα ρητά περιορισμένα προϊόντα
        // βλέπουν υποσύνολο του (επεξεργάσιμου, βλ. MenuStore.Extras) κοινού καταλόγου έξτρα.
        // Η ΣΕΙΡΑ είναι του προϊόντος, όχι του κοινού καταλόγου: το ExtraNames είναι λίστα με σειρά, την
        // οποία ορίζει ο ταμίας σέρνοντας μέσα στο προϊόν (βλ. MenuManagerViewModel.MoveExtraTo). Πριν
        // φιλτραριζόταν ο κοινός κατάλογος, οπότε η σειρά έβγαινε ίδια παντού όσο κι αν την άλλαζες.
        var catalogExtras = MenuStore.Instance.Extras;
        var allowedExtras = product.ExtraNames is null
            ? catalogExtras
            : product.ExtraNames
                .Select(n => catalogExtras.FirstOrDefault(e => e.Name == n))
                .Where(e => e is not null)
                .Select(e => e!)
                .ToList();
        Extras = new ObservableCollection<ExtraViewModel>(
            allowedExtras.Select(e => new ExtraViewModel(this, e)));

        var c = editingLine?.Customization ?? new LineCustomization();
        SelectBread(c.Bread);
        foreach (var ing in Ingredients)
            ing.IsRemoved = c.Removed.Contains(ing.Name);
        foreach (var ex in Extras)
            ex.Quantity = c.Extras.GetValueOrDefault(ex.Name);
        Note = c.Note;
        IsDoublePita = c.DoublePita;
        if (editingLine is not null)
        {
            Quantity = editingLine.Quantity;
            NoCharge = editingLine.NoCharge;
        }
    }

    public Product Product { get; }
    public decimal BasePrice { get; }
    public string CategoryLabel { get; }
    public string ProductName => Product.Name;
    public string UnitPriceLabel => Order.FormatPrice(BasePrice);

    public bool HasBreadChoice { get; }
    public ObservableCollection<BreadOptionViewModel> Breads { get; }
    public ObservableCollection<IngredientViewModel> Ingredients { get; }
    public ObservableCollection<ExtraViewModel> Extras { get; }

    /// <summary>Μόνο ΤΥΛΙΧΤΑ και ΚΛΑΣΙΚΑ ΜΙΝΙ δείχνουν την επιλογή «διπλή πίτα».</summary>
    public bool ShowDoublePitaOption => MenuStore.Instance.SupportsDoublePita(CategoryLabel);

    /// <summary>Χρέωση διπλής πίτας αυτής της κατηγορίας — ρυθμίζεται από τη Διαχείριση Καταλόγου
    /// (MenuStore.DoublePitaPrices), διαφορετική για ΤΥΛΙΧΤΑ και ΚΛΑΣΙΚΑ ΜΙΝΙ.</summary>
    public decimal DoublePitaPrice => MenuStore.Instance.DoublePitaPriceFor(CategoryLabel);

    /// <summary>Η ετικέτα του κουμπιού στην οθόνη. Γράφεται πεζά-κεφαλαία («Διπλή Πίττα») επειδή είναι
    /// επιλογή που διαβάζει ο ταμίας, όχι τίτλος. Το όνομα που τυπώνεται στην απόδειξη μένει κεφαλαίο
    /// («ΔΙΠΛΗ ΠΙΤΑ», βλ. MenuSeed.DoublePitaLabel) — εκεί ξεχωρίζει μέσα στη γραμμή.</summary>
    /// Η τιμή γράφεται χωρίς παρενθέσεις: με αυτές η ετικέτα μάκραινε τόσο που το κουμπί δεν χωρούσε
    /// πια δίπλα στα ψωμιά και έπεφτε σε δεύτερη σειρά, ψηλώνοντας άσκοπα τον customizer.
    public string DoublePitaOptionLabel => DoublePitaPrice > 0
        ? "Διπλή Πίττα +" + Order.FormatPrice(DoublePitaPrice)
        : "Διπλή Πίττα";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalLabel))]
    private bool _isDoublePita;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalLabel))]
    private int _quantity = 1;

    [ObservableProperty]
    private string _note = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalLabel))]
    private bool _noCharge;

    /// <summary>Όλα τα επιτρεπτά έξτρα του προϊόντος. Υπήρχε και πεδίο αναζήτησης από πάνω, αλλά σε
    /// ταμείο με αφή είναι πιο γρήγορο να τα βλέπεις όλα μαζί παρά να πληκτρολογείς.</summary>
    public IReadOnlyList<ExtraViewModel> VisibleExtras => Extras;

    /// <summary>ΣΚΕΤΟ — όλα τα υλικά αφαιρεμένα.</summary>
    public bool IsSketo => Ingredients.All(i => i.IsRemoved);

    public decimal UnitPriceWithExtras =>
        BasePrice + Extras.Sum(e => e.Quantity * e.Item.Price) + (IsDoublePita ? DoublePitaPrice : 0m);
    public decimal Total => NoCharge ? 0m : Quantity * UnitPriceWithExtras;
    public string TotalLabel => Order.FormatPrice(Total);

    internal void OnCustomizationChanged()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(TotalLabel));
        OnPropertyChanged(nameof(IsSketo));
    }

    [RelayCommand] private void IncQty() => Quantity++;
    [RelayCommand] private void DecQty() => Quantity = Math.Max(1, Quantity - 1);

    public void SelectBread(string name)
    {
        foreach (var b in Breads)
            b.IsSelected = b.Name == name;
    }

    [RelayCommand]
    private void ToggleSketo()
    {
        var target = !IsSketo;
        foreach (var ing in Ingredients)
            ing.IsRemoved = target;
    }

    [RelayCommand] private void ToggleDoublePita() => IsDoublePita = !IsDoublePita;

    [RelayCommand] private void ToggleNoCharge() => NoCharge = !NoCharge;

    [RelayCommand] private void Close() => _owner.CloseCustomizer();

    /// <summary>Άνοιξε από το ✎ μιας γραμμής του δελτίου (διόρθωση), όχι για νέα προσθήκη.</summary>
    public bool IsEditingExistingLine => _editingLine is not null;

    /// <summary>Ό,τι ακριβώς κάνει το κουμπί ΠΡΟΣΘΗΚΗ — το καλεί το διπλό κλικ πάνω στη λίστα, ώστε
    /// το φαγητό να μπαίνει με ό,τι έχει ήδη διαλεγεί εδώ (ψωμί, υλικά, έξτρα, ποσότητα).</summary>
    public void Commit() => Add();

    [RelayCommand]
    private void Add()
    {
        var customization = new LineCustomization
        {
            Bread = Breads.FirstOrDefault(b => b.IsSelected)?.Name ?? MenuSeed.BreadOptions[0],
            Removed = Ingredients.Where(i => i.IsRemoved).Select(i => i.Name).ToList(),
            Extras = Extras.Where(e => e.Quantity > 0).ToDictionary(e => e.Name, e => e.Quantity),
            Note = Note.Trim(),
            DoublePita = IsDoublePita,
        };

        var mods = new List<string>();
        // Τα υλικά που δείχνει αυτή τη στιγμή ο customizer είναι ακριβώς τα βασικά του προϊόντος.
        mods.AddRange(MenuSeed.DescribeRemovedIngredients(
            customization.Removed, [.. Ingredients.Select(i => i.Name)]));
        mods.AddRange(Extras.Where(e => e.Quantity > 0)
            .Select(e => "+ " + e.Name + (e.Quantity > 1 ? " ×" + e.Quantity : "")));
        if (NoCharge) mods.Add("ΔΩΡΕΑΝ");

        // ΤΥΛΙΧΤΑ: το ψωμί χωνεύεται στο ίδιο το όνομα («ΑΡ. Κοτόπουλο»). ΜΕΡΙΔΕΣ/ΜΕΡΙΔΕΣ ΠΑΠΠΟΥ: το
        // όνομα μένει ως έχει, το ψωμί γράφεται ολόγραφο σε ξεχωριστή γραμμή («Αραβική»), όπως πριν.
        // Άλλες customizable κατηγορίες: καμία αναφορά ψωμιού πουθενά.
        // Η σύνθεση εξαρτάται μόνο από το όνομα-βάση, οπότε τρέχει δύο φορές: μία για την οθόνη και
        // μία για το χαρτί. Έτσι το «όνομα εκτύπωσης» παίρνει κανονικά το ψωμί μπροστά και τη «ΔΙΠΛΗ
        // ΠΙΤΑ», αντί να τυπώνεται σκέτο.
        string Compose(string baseName) =>
            IsDoublePita && MenuStore.Instance.SupportsDoublePita(CategoryLabel)
                ? MenuSeed.ComposeDoublePitaName(baseName, CategoryLabel, customization.Bread)
                : HasBreadChoice && MenuStore.Instance.FuseBreadIntoName(CategoryLabel)
                    ? MenuSeed.ComposeCustomizedName(baseName, customization.Bread)
                    : baseName;

        var name = Compose(Product.Name);
        var printName = Compose(Product.NameForPrint);

        // Το ψωμί ολόγραφο σε δική του γραμμή μόνο όταν ΔΕΝ έχει μπει μέσα στο όνομα.
        var breadOnOwnLine = HasBreadChoice && !MenuStore.Instance.FuseBreadIntoName(CategoryLabel)
            && !(IsDoublePita && MenuStore.Instance.SupportsDoublePita(CategoryLabel));
        var descLine1 = breadOnOwnLine
            ? customization.Note.Length > 0 ? customization.Bread + "\n" + customization.Note : customization.Bread
            : customization.Note;

        // Κάθε ιδιαιτερότητα (χωρίς/μόνο με/έξτρα) σε δική της γραμμή αντί για μία αράδα με "·" —
        // πιο ευανάγνωστο στην απόδειξη όταν ένα προϊόν έχει πολλές τροποποιήσεις.
        _owner.CommitCustomizedLine(_editingLine, Product, customization, Quantity,
            UnitPriceWithExtras, NoCharge, name, descLine1, string.Join("\n", mods), printName);
    }
}
