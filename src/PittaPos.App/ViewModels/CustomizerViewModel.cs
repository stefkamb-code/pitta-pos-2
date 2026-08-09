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
        Ingredients = new ObservableCollection<IngredientViewModel>(
            MenuSeed.IncludedIngredients.Select(n => new IngredientViewModel(this, n)));
        // null ExtraNames = όλα επιτρεπτά (βλ. MenuManagerViewModel) — μόνο τα ρητά περιορισμένα προϊόντα
        // βλέπουν υποσύνολο του (επεξεργάσιμου, βλ. MenuStore.Extras) κοινού καταλόγου έξτρα.
        var catalogExtras = MenuStore.Instance.Extras;
        var allowedExtras = product.ExtraNames is null
            ? catalogExtras
            : catalogExtras.Where(e => product.ExtraNames.Contains(e.Name)).ToList();
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
            DiscountPct = editingLine.DiscountPct;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalLabel))]
    [NotifyPropertyChangedFor(nameof(DiscountLabel))]
    private int _discountPct;

    /// <summary>Όλα τα επιτρεπτά έξτρα του προϊόντος. Υπήρχε και πεδίο αναζήτησης από πάνω, αλλά σε
    /// ταμείο με αφή είναι πιο γρήγορο να τα βλέπεις όλα μαζί παρά να πληκτρολογείς.</summary>
    public IReadOnlyList<ExtraViewModel> VisibleExtras => Extras;

    /// <summary>ΣΚΕΤΟ — όλα τα υλικά αφαιρεμένα.</summary>
    public bool IsSketo => Ingredients.All(i => i.IsRemoved);

    public decimal UnitPriceWithExtras =>
        BasePrice + Extras.Sum(e => e.Quantity * e.Item.Price) + (IsDoublePita ? DoublePitaPrice : 0m);
    public decimal Total => NoCharge ? 0m : Quantity * UnitPriceWithExtras * (1 - DiscountPct / 100m);
    public string TotalLabel => Order.FormatPrice(Total);
    public string DiscountLabel => $"ΕΚΠΤ. {DiscountPct}%";

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
    [RelayCommand] private void IncDiscount() => DiscountPct = Math.Min(50, DiscountPct + 5);
    [RelayCommand] private void DecDiscount() => DiscountPct = Math.Max(0, DiscountPct - 5);

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
        mods.AddRange(MenuSeed.DescribeRemovedIngredients(customization.Removed));
        mods.AddRange(Extras.Where(e => e.Quantity > 0)
            .Select(e => "+ " + e.Name + (e.Quantity > 1 ? " ×" + e.Quantity : "")));
        if (NoCharge) mods.Add("ΔΩΡΕΑΝ");
        else if (DiscountPct > 0) mods.Add("−" + DiscountPct + "%");

        // ΤΥΛΙΧΤΑ: το ψωμί χωνεύεται στο ίδιο το όνομα («ΑΡ. Κοτόπουλο»). ΜΕΡΙΔΕΣ/ΜΕΡΙΔΕΣ ΠΑΠΠΟΥ: το
        // όνομα μένει ως έχει, το ψωμί γράφεται ολόγραφο σε ξεχωριστή γραμμή («Αραβική»), όπως πριν.
        // Άλλες customizable κατηγορίες: καμία αναφορά ψωμιού πουθενά.
        string name;
        string descLine1;
        if (IsDoublePita && MenuStore.Instance.SupportsDoublePita(CategoryLabel))
        {
            name = MenuSeed.ComposeDoublePitaName(Product.Name, CategoryLabel, customization.Bread);
            descLine1 = customization.Note;
        }
        else if (!HasBreadChoice)
        {
            name = Product.Name;
            descLine1 = customization.Note;
        }
        else if (MenuStore.Instance.FuseBreadIntoName(CategoryLabel))
        {
            name = MenuSeed.ComposeCustomizedName(Product.Name, customization.Bread);
            descLine1 = customization.Note;
        }
        else
        {
            name = Product.Name;
            descLine1 = customization.Note.Length > 0
                ? customization.Bread + "\n" + customization.Note
                : customization.Bread;
        }

        // Κάθε ιδιαιτερότητα (χωρίς/μόνο με/έξτρα/έκπτωση) σε δική της γραμμή αντί για μία αράδα με "·" —
        // πιο ευανάγνωστο στην απόδειξη όταν ένα προϊόν έχει πολλές τροποποιήσεις.
        _owner.CommitCustomizedLine(_editingLine, Product, customization, Quantity,
            UnitPriceWithExtras, DiscountPct, NoCharge, name, descLine1, string.Join("\n", mods));
    }
}
