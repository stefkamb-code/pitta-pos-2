using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.App.Controls;
using PittaPos.App.Services;
using PittaPos.Core.Data;
using PittaPos.Core.Models;

namespace PittaPos.App.ViewModels;

/// <summary>Ένα κουτάκι επιλογής έξτρα στη φόρμα προϊόντος — αν επιτρέπεται σε αυτό το προϊόν.</summary>
public partial class ExtraToggleViewModel : ObservableObject
{
    public required string Name { get; init; }
    public required string PriceLabel { get; init; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>Μία γραμμή στη γενική διαχείριση του κοινού καταλόγου έξτρα (επεξεργάσιμο όνομα + τιμή + διαγραφή).</summary>
public partial class ExtraCatalogRowViewModel : ObservableObject
{
    /// <summary>Το ΑΠΟΘΗΚΕΥΜΕΝΟ όνομα — το κλειδί με το οποίο βρίσκεται το έξτρα. Δεν αλλάζει όσο ο
    /// ταμίας πληκτρολογεί· αλλάζει μόνο όταν πατηθεί ΑΠΟΘΗΚΕΥΣΗ (βλ. MenuStore.RenameExtra).</summary>
    public required string Name { get; init; }

    /// <summary>Το όνομα όπως γράφεται τώρα στο κουτί.</summary>
    [ObservableProperty]
    private string _nameText = "";

    [ObservableProperty]
    private string _priceText = "";
}

/// <summary>Μία επιλογή ΦΠΑ της κατηγορίας (ΦΑΓΗΤΟ / ΑΝΑΨΥΚΤΙΚΟ / ΠΟΤΟ).</summary>
public partial class VatOptionViewModel(VatKind kind, string label) : ObservableObject
{
    public VatKind Kind { get; } = kind;
    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Διαχείριση Καταλόγου — κατηγορίες, προϊόντα, τιμές.</summary>
public partial class MenuManagerViewModel : ObservableObject
{
    private static readonly CultureInfo Greek = CultureInfo.GetCultureInfo("el-GR");

    private readonly MenuStore _store = MenuStore.Instance;
    private readonly DispatcherTimer _statusTimer;

    public MenuManagerViewModel()
    {
        SelectedCategory = _store.Categories.FirstOrDefault();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusMessage = ""; };
        // Χωρίς αυτό, το ΕΞΤΡΑ (ManageExtrasWindow) θα έδειχνε άδεια λίστα αν ανοίξει πριν επιλεγεί
        // κάποιο προϊόν — το OnSelectedProductChanged δεν προλαβαίνει να τρέξει μόνο του τότε.
        RebuildExtraToggles();
        RebuildDoublePitaRows();
    }

    /// <summary>Στιγμιαία ένδειξη αποθήκευσης — καθαρίζει μόνη της μετά από λίγο.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusMessage = "";

    public bool HasStatus => StatusMessage.Length > 0;

    private void Flash(string message)
    {
        StatusMessage = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    // Νέες λίστες κάθε φορά ώστε το WPF να ξαναδιαβάζει τα ονόματα μετά από επεξεργασία
    public IReadOnlyList<MenuCategory> Categories => _store.Categories.ToList();
    public IReadOnlyList<Product> Products => SelectedCategory?.Products.ToList() ?? [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Products))]
    [NotifyPropertyChangedFor(nameof(HasCategory))]
    [NotifyPropertyChangedFor(nameof(ShowCategoryEditor))]
    private MenuCategory? _selectedCategory;

    /// <summary>Φόρμα νέας κατηγορίας — δεν δημιουργείται τίποτα μέχρι το πάτημα της ΔΗΜΙΟΥΡΓΙΑΣ.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCategoryEditor))]
    [NotifyPropertyChangedFor(nameof(CategoryFormTitle))]
    [NotifyPropertyChangedFor(nameof(CategorySaveLabel))]
    private bool _isNewCategory;

    public bool ShowCategoryEditor => HasCategory || IsNewCategory;

    /// <summary>Η μετονομασία κατηγορίας δείχνεται μόνο όταν ΔΕΝ δουλεύεις προϊόν — στη θέση της
    /// μπαίνει τότε η επιλογή κατηγορίας του προϊόντος. Τα υπόλοιπα της κατηγορίας (σειρά, διαγραφή,
    /// συμπεριφορά) μένουν πάντα ορατά.</summary>
    public bool ShowCategoryRename => !IsEditingProduct;
    public string CategoryFormTitle => IsNewCategory ? "ΝΕΑ ΚΑΤΗΓΟΡΙΑ" : "ΚΑΤΗΓΟΡΙΑ";
    public string CategorySaveLabel => IsNewCategory ? "ΔΗΜΙΟΥΡΓΙΑ ΚΑΤΗΓΟΡΙΑΣ" : "ΑΠΟΘΗΚΕΥΣΗ ΟΝΟΜΑΤΟΣ";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingProduct))]
    [NotifyPropertyChangedFor(nameof(ProductFormTitle))]
    // Χωρίς αυτό η οθόνη δεν ξαναρωτούσε αν πρέπει να δείχνει τη μετονομασία, οπότε έμεναν ορατά
    // ΚΑΙ τα δύο πεδία κατηγορίας μόλις άνοιγε κανείς προϊόν.
    [NotifyPropertyChangedFor(nameof(ShowCategoryRename))]
    private Product? _selectedProduct;

    public bool HasCategory => SelectedCategory is not null;
    public bool IsEditingProduct => SelectedProduct is not null;
    public string ProductFormTitle => SelectedProduct is null ? "ΝΕΟ ΠΡΟΪΟΝ" : "ΕΠΕΞΕΡΓΑΣΙΑ ΠΡΟΪΟΝΤΟΣ";

    [ObservableProperty] private string _categoryName = "";
    [ObservableProperty] private string _productName = "";
    [ObservableProperty] private string _productPrice = "";
    [ObservableProperty] private string _productDeliveryPrice = "";
    /// <summary>Κενό = τυπώνεται ό,τι λέει και το μενού (βλ. Product.PrintName).</summary>
    [ObservableProperty] private string _productPrintName = "";

    /// <summary>Σε ποια κατηγορία ανήκει το προϊόν. Αλλάζοντάς την και αποθηκεύοντας, το προϊόν
    /// μεταφέρεται — πριν έπρεπε να διαγραφεί και να ξαναγραφτεί από την αρχή στη σωστή.</summary>
    [ObservableProperty] private MenuCategory? _productCategory;
    [ObservableProperty] private bool _productCustomizable;

    /// <summary>Ένα κουτάκι ανά έξτρα του κοινού καταλόγου (_store.Extras) — ποια επιτρέπονται σε αυτό
    /// το προϊόν. Ξαναφτιάχνεται σε κάθε επιλογή προϊόντος και όποτε αλλάζει ο κοινός κατάλογος.</summary>
    public ObservableCollection<ExtraToggleViewModel> ExtraToggles { get; } = [];

    /// <summary>Ένα κουτάκι ανά υλικό του κοινού καταλόγου — ποια έχει ΜΕΣΑ αυτό το προϊόν, δηλαδή
    /// ποια θα μπορεί να «βγάλει» ο πελάτης. Ξεχωριστό από τα έξτρα: τα υλικά είναι ήδη μέσα και
    /// αφαιρούνται, τα έξτρα προστίθενται.</summary>
    public ObservableCollection<ExtraToggleViewModel> IngredientToggles { get; } = [];

    [ObservableProperty] private string _newIngredientName = "";

    /// <summary>Γενική λίστα διαχείρισης του κοινού καταλόγου έξτρα — όνομα/τιμή/διαγραφή, ανεξάρτητα
    /// από ποιο προϊόν είναι επιλεγμένο.</summary>
    public ObservableCollection<ExtraCatalogRowViewModel> ExtraCatalogRows { get; } = [];

    /// <summary>Χρέωση «διπλή πίτα» ανά κατηγορία (ΤΥΛΙΧΤΑ / ΚΛΑΣΙΚΑ ΜΙΝΙ, βλ. MenuSeed.SupportsDoublePita)
    /// — σταθερός κατάλογος δύο γραμμών, μόνο επεξεργάσιμη τιμή, χωρίς προσθήκη/διαγραφή.</summary>
    public ObservableCollection<ExtraCatalogRowViewModel> DoublePitaRows { get; } = [];

    [ObservableProperty] private string _newExtraName = "";
    [ObservableProperty] private string _newExtraPrice = "";

    // Ρυθμίσεις συμπεριφοράς της επιλεγμένης κατηγορίας. Αποθηκεύονται ΜΕΣΑ στην κατηγορία (menu.json),
    // όχι με βάση το όνομά της — γι' αυτό μια μετονομασία δεν τις χαλάει πια (βλ. MenuCategory).
    [ObservableProperty] private bool _categoryHasBread;
    [ObservableProperty] private bool _categoryFuseBread;
    [ObservableProperty] private bool _categoryDoublePita;

    /// <summary>Μεγάλη ή μικρή πίτα — καθορίζει ΠΟΙΑ από τις δύο χρεώσεις παίρνει η κατηγορία. Στα
    /// ΚΛΑΣΙΚΑ ΜΙΚΡΑ μπαίνει πάντα η μικρή, στις ΠΙΤΤΕΣ και ΠΙΤΤΕΣ ΠΑΠΠΟΥ πάντα η μεγάλη.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CategoryPitaSizeLabel))]
    private bool _categoryPitaLarge;

    public string CategoryPitaSizeLabel => CategoryPitaLarge ? "ΜΕΓΑΛΗ ΠΙΤΑ" : "ΜΙΚΡΗ ΠΙΤΑ";

    /// <summary>Ένα κουμπί που εναλλάσσει μικρή/μεγάλη — δύο τιμές, δεν χρειάζεται λίστα επιλογών.</summary>
    [RelayCommand]
    private void ToggleCategoryPitaSize() => CategoryPitaLarge = !CategoryPitaLarge;

    partial void OnCategoryPitaLargeChanged(bool value)
    {
        if (SaveCategoryFlag(c => c.DoublePitaLarge = value))
            RebuildDoublePitaRows();
    }

    /// <summary>Ο ΦΠΑ της κατηγορίας. Δεν βγαίνει από το όνομα — φαίνεται και διορθώνεται εδώ, ώστε
    /// μια μετονομασία («ΑΝΑΨΥΚΤΙΚΑ» → «ΔΡΟΣΙΣΤΙΚΑ») να μην αλλάξει σιωπηλά τον φόρο.</summary>
    [ObservableProperty] private VatKind _categoryVat;

    /// <summary>Οι επιλογές ΦΠΑ, στη σειρά που τις διαβάζει ο ταμίας. Το «αναψυκτικό» δεν είναι τρίτος
    /// συντελεστής — είναι 13% που γίνεται 24% στο τραπέζι, γι' αυτό γράφεται έτσι.</summary>
    public IReadOnlyList<VatOptionViewModel> VatOptions { get; } =
    [
        new(VatKind.Food, "13%"),
        new(VatKind.Alcohol, "24%"),
        // Τελευταία η ειδική περίπτωση: οι δύο σκέτοι συντελεστές είναι το 99% των κατηγοριών.
        new(VatKind.SoftDrink, "13% · 24% στο τραπέζι"),
    ];

    /// <summary>Κρυμμένες μέχρι να πατηθεί το κουμπί — μία γραμμή αντί για τρία κουμπιά μονίμως στη
    /// φόρμα. Ίδια λογική με τις κρυμμένες επιλογές του Ιστορικού.</summary>
    [ObservableProperty]
    private bool _showVatOptions;

    [RelayCommand]
    private void ToggleVatOptions() => ShowVatOptions = !ShowVatOptions;

    /// <summary>Το κουμπί γράφει πάντα τι ισχύει τώρα, ώστε να φαίνεται με μια ματιά χωρίς άνοιγμα.</summary>
    public string CategoryVatLabel =>
        "ΦΠΑ: " + (VatOptions.FirstOrDefault(o => o.Kind == CategoryVat)?.Label ?? "13%") + " ▾";

    [RelayCommand]
    private void SetCategoryVat(VatKind kind)
    {
        CategoryVat = kind;
        ShowVatOptions = false;
    }

    /// <summary>Μόνο το «ψωμί μέσα στο όνομα» εξαρτάται από το αν ρωτιέται καθόλου ψωμί — αλλιώς δεν
    /// υπάρχει ψωμί για να μπει πουθενά. Η «διπλή πίτα» είναι ΑΝΕΞΑΡΤΗΤΗ: τα ΚΛΑΣΙΚΑ ΜΙΚΡΑ π.χ. έχουν
    /// διπλή πίτα χωρίς καμία επιλογή ψωμιού, οπότε αν την κλείδωνε το ψωμί δεν θα μπορούσε καν να
    /// ξεπατηθεί από την οθόνη.</summary>
    public bool CategoryFuseBreadEnabled => HasCategory && CategoryHasBread;

    /// <summary>Ξαναχτίζει τη λίστα βασικών υλικών του ΕΠΙΛΕΓΜΕΝΟΥ προϊόντος. Τα βασικά υλικά είναι
    /// ξεχωριστά για κάθε προϊόν (η ομελέτα δεν έχει τα ίδια με τον γύρο), οπότε εδώ δείχνονται τα
    /// δικά του και όσα υπάρχουν στον κοινό κατάλογο ως έτοιμες επιλογές — τσεκαρισμένα μόνο όσα
    /// έχει πράγματι το προϊόν. Προϊόν που δεν ρυθμίστηκε ποτέ θεωρείται ότι έχει τα κοινά, ώστε να
    /// μη χρειαστεί να ξαναπεραστούν και τα 124 στο χέρι.</summary>
    private void RebuildIngredientToggles()
    {
        IngredientToggles.Clear();
        // ΜΟΝΟ τα υλικά αυτού του προϊόντος. Δεν ενώνεται με τον κοινό κατάλογο επίτηδες: αν
        // εμφανίζονταν και τα κοινά, ένα υλικό γραμμένο για μία πίτα θα ξεπρόβαλλε (έστω
        // ξετσέκαριστο) σε κάθε άλλο προϊόν — ακριβώς αυτό που δεν θέλουμε. Προϊόν που δεν
        // ρυθμίστηκε ποτέ ξεκινά από τα κοινά, ώστε να μη χρειαστεί να γραφτούν 124 φορές στο χέρι.
        foreach (var name in SelectedProduct?.Ingredients ?? _store.Ingredients)
            IngredientToggles.Add(new ExtraToggleViewModel { Name = name, PriceLabel = "", IsChecked = true });
    }

    /// <summary>Προσθήκη βασικού υλικού ΜΟΝΟ σε αυτό το προϊόν. Δεν αγγίζει τον κοινό κατάλογο και
    /// άρα δεν εμφανίζεται σε κανένα άλλο προϊόν — πριν έμπαινε στον κοινό κατάλογο και ξεπρόβαλλε
    /// αμέσως σε όλες τις κατηγορίες. Οριστικοποιείται με την ΑΠΟΘΗΚΕΥΣΗ, όπως το όνομα και η τιμή.</summary>
    [RelayCommand]
    private void AddIngredient()
    {
        var name = NewIngredientName.Trim();
        if (name.Length == 0)
            return;
        if (IngredientToggles.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            // Υπάρχει ήδη στη λίστα — απλώς σιγουρεύουμε ότι είναι τσεκαρισμένο, χωρίς διπλοεγγραφή.
            foreach (var dup in IngredientToggles.Where(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                dup.IsChecked = true;
            NewIngredientName = "";
            Flash("✓ Το υλικό υπήρχε ήδη — τσεκαρίστηκε");
            return;
        }

        IngredientToggles.Add(new ExtraToggleViewModel { Name = name, PriceLabel = "", IsChecked = true });
        NewIngredientName = "";
        RebuildSuggestions(); // ό,τι μπήκε στο προϊόν φεύγει από τις προτάσεις
        Flash("✓ Προστέθηκε — πάτα ΑΠΟΘΗΚΕΥΣΗ");
    }

    /// <summary>Διαγραφή βασικού υλικού ΜΟΝΟ από αυτό το προϊόν. Πριν έσβηνε το υλικό από τον κοινό
    /// κατάλογο και από κάθε άλλο προϊόν που το είχε.</summary>
    [RelayCommand]
    private void RemoveIngredient(ExtraToggleViewModel ingredient)
    {
        IngredientToggles.Remove(ingredient);
        RebuildSuggestions(); // ξαναγίνεται πρόταση, σε περίπτωση που το έβγαλες κατά λάθος
        Flash("✓ Αφαιρέθηκε — πάτα ΑΠΟΘΗΚΕΥΣΗ");
    }

    private bool _loadingCategoryFlags;

    partial void OnSelectedCategoryChanged(MenuCategory? value)
    {
        CategoryName = value?.Name ?? "";

        // Χωρίς αυτό, το γέμισμα των παρακάτω θα ενεργοποιούσε τους handlers και θα «αποθήκευε» τις
        // τιμές της προηγούμενης κατηγορίας πάνω στη νέα.
        _loadingCategoryFlags = true;
        CategoryHasBread = value?.HasBread ?? false;
        CategoryFuseBread = value?.FuseBreadIntoName ?? false;
        CategoryDoublePita = value?.SupportsDoublePita ?? false;
        CategoryPitaLarge = value?.DoublePitaLarge ?? (value is null || MenuSeed.GuessLargePita(value.Name));
        CategoryVat = value?.VatKind ?? (value is null ? VatKind.Food : MenuSeed.GuessVatKind(value.Name));
        ShowVatOptions = false; // κλειστές σε κάθε αλλαγή κατηγορίας
        _loadingCategoryFlags = false;
        OnPropertyChanged(nameof(CategoryFuseBreadEnabled));
        RefreshVatOptions();
    }

    partial void OnCategoryVatChanged(VatKind value)
    {
        SaveCategoryFlag(c => c.VatKind = value);
        RefreshVatOptions();
    }

    private void RefreshVatOptions()
    {
        foreach (var o in VatOptions)
            o.IsSelected = o.Kind == CategoryVat;
        OnPropertyChanged(nameof(CategoryVatLabel));
    }

    partial void OnCategoryHasBreadChanged(bool value)
    {
        if (SaveCategoryFlag(c => c.HasBread = value))
            OnPropertyChanged(nameof(CategoryFuseBreadEnabled));
    }

    partial void OnCategoryFuseBreadChanged(bool value) => SaveCategoryFlag(c => c.FuseBreadIntoName = value);

    partial void OnCategoryDoublePitaChanged(bool value)
    {
        if (SaveCategoryFlag(c => c.SupportsDoublePita = value))
            RebuildDoublePitaRows();
    }

    private bool SaveCategoryFlag(Action<MenuCategory> apply)
    {
        if (_loadingCategoryFlags || SelectedCategory is null)
            return false;
        apply(SelectedCategory);
        _store.Save();
        return true;
    }

    partial void OnSelectedProductChanged(Product? value)
    {
        ProductName = value?.Name ?? "";
        ProductPrice = value is null ? "" : value.Price.ToString("0.00", Greek);
        ProductDeliveryPrice = value?.DeliveryPrice?.ToString("0.00", Greek) ?? "";
        ProductPrintName = value?.PrintName ?? "";
        ProductCustomizable = value?.Customizable ?? false;
        // Νέο προϊόν: προεπιλογή η κατηγορία που κοιτάει ήδη ο χρήστης. Υπάρχον: αυτή που το έχει.
        ProductCategory = value is null
            ? SelectedCategory
            : Categories.FirstOrDefault(c => c.Products.Contains(value)) ?? SelectedCategory;
        RebuildExtraToggles();
    }

    /// <summary>null ExtraNames = όλα επιτρεπτά (προεπιλογή για νέο προϊόν ή προϊόν που δεν έχει
    /// ρυθμιστεί ακόμα) — καλείται ξανά και μετά από προσθήκη/διαγραφή/τιμή στον κοινό κατάλογο έξτρα.</summary>
    private void RebuildExtraToggles()
    {
        var value = SelectedProduct;
        ExtraToggles.Clear();
        ExtraCatalogRows.Clear();
        // Μαζί και τα βασικά υλικά: χωρίς αυτό χτίζονταν μόνο όταν πρόσθετε κανείς υλικό, οπότε
        // ανοίγοντας ένα προϊόν η λίστα φαινόταν άδεια σαν να μην είχε κανένα.
        RebuildIngredientToggles();

        // Η σειρά είναι ΤΟΥ ΠΡΟΪΟΝΤΟΣ: πρώτα τα δικά του έξτρα με τη σειρά που τα έχει σύρει ο ταμίας
        // (ExtraNames), μετά όσα του κοινού καταλόγου δεν έχει — ξετσεκάριστα, στη σειρά του καταλόγου.
        // Προϊόν χωρίς ρητή λίστα (null) παίρνει ολόκληρο τον κοινό κατάλογο με τη σειρά του, όπως πάντα.
        var ordered = value?.ExtraNames is { } names
            ? names.Where(n => _store.Extras.Any(e => e.Name == n))
                .Concat(_store.Extras.Select(e => e.Name).Where(n => !names.Contains(n)))
                .ToList()
            : _store.Extras.Select(e => e.Name).ToList();

        foreach (var name in ordered)
        {
            var extra = _store.Extras.First(e => e.Name == name);
            ExtraToggles.Add(new ExtraToggleViewModel
            {
                Name = extra.Name,
                PriceLabel = extra.Price > 0 ? Order.FormatPrice(extra.Price) : "δωρεάν",
                IsChecked = value?.ExtraNames is null || value.ExtraNames.Contains(extra.Name),
            });
        }

        // Ο κοινός κατάλογος μένει στη ΔΙΚΗ του σειρά — είναι άλλη λίστα, ανεξάρτητη από το προϊόν.
        foreach (var extra in _store.Extras)
        {
            ExtraCatalogRows.Add(new ExtraCatalogRowViewModel
            {
                Name = extra.Name,
                NameText = extra.Name,
                PriceText = extra.Price.ToString("0.00", Greek),
            });
        }

        RebuildSuggestions();
    }

    // ---- προτάσεις καθώς γράφεις (βλ. Controls/AutoComplete.cs) ----
    //
    // Τίποτα από αυτά δεν αποθηκεύεται χωριστά: οι προτάσεις βγαίνουν από τον ΙΔΙΟ τον κατάλογο, από
    // ό,τι έχει ήδη γραφτεί σε προϊόντα και κατηγορίες. Έτσι δεν υπάρχει δεύτερη λίστα να ξεσυγχρονιστεί,
    // και ένα υλικό που δεν το χρησιμοποιεί πια κανένα προϊόν παύει μόνο του να προτείνεται.

    /// <summary>Κάθε βασικό υλικό που υπάρχει σε οποιοδήποτε προϊόν — εκτός από όσα έχει ήδη αυτό εδώ.</summary>
    public IReadOnlyList<string> IngredientSuggestions { get; private set; } = [];

    /// <summary>Ονόματα γνωστά ως βασικά υλικά που ΔΕΝ υπάρχουν ακόμα στον κοινό κατάλογο έξτρα — δηλαδή
    /// ακριβώς όσα έχει νόημα να γίνουν έξτρα.</summary>
    public IReadOnlyList<string> ExtraSuggestions { get; private set; } = [];

    public IReadOnlyList<string> ProductNameSuggestions { get; private set; } = [];
    public IReadOnlyList<string> ProductPrintNameSuggestions { get; private set; } = [];
    public IReadOnlyList<string> CategoryNameSuggestions { get; private set; } = [];

    private void RebuildSuggestions()
    {
        var products = _store.Categories.SelectMany(c => c.Products).ToList();
        // Ο κοινός κατάλογος μπαίνει κι αυτός μέσα: τα προϊόντα που δεν ρυθμίστηκαν ποτέ έχουν
        // Ingredients = null και τα υλικά τους ζουν μόνο εκεί.
        var everyIngredient = products.Where(p => p.Ingredients is not null)
            .SelectMany(p => p.Ingredients!)
            .Concat(_store.Ingredients)
            .ToList();

        var onThisProduct = IngredientToggles.Select(t => AutoComplete.Normalize(t.Name)).ToHashSet();
        IngredientSuggestions = ByFrequency(everyIngredient)
            .Where(name => !onThisProduct.Contains(AutoComplete.Normalize(name)))
            .ToList();

        var alreadyExtra = _store.Extras.Select(e => AutoComplete.Normalize(e.Name)).ToHashSet();
        ExtraSuggestions = ByFrequency(everyIngredient)
            .Where(name => !alreadyExtra.Contains(AutoComplete.Normalize(name)))
            .ToList();

        ProductNameSuggestions = ByFrequency(products.Select(p => p.Name));
        // Και τα κανονικά ονόματα: το όνομα εκτύπωσης συνήθως ξεκινά σαν το κανονικό και μετά κόβεται.
        ProductPrintNameSuggestions = ByFrequency(products.Select(p => p.PrintName!)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Concat(products.Select(p => p.Name)));
        CategoryNameSuggestions = ByFrequency(_store.Categories.Select(c => c.Name));

        OnPropertyChanged(nameof(IngredientSuggestions));
        OnPropertyChanged(nameof(ExtraSuggestions));
        OnPropertyChanged(nameof(ProductNameSuggestions));
        OnPropertyChanged(nameof(ProductPrintNameSuggestions));
        OnPropertyChanged(nameof(CategoryNameSuggestions));
    }

    /// <summary>Τα πολυχρησιμοποιημένα πρώτα — σε 124 προϊόντα, το «κρεμμύδι» πρέπει να βγαίνει πριν από
    /// ένα υλικό που γράφτηκε μία φορά. Ονόματα που διαφέρουν μόνο σε τόνο/πεζά μετρούν ως ένα.</summary>
    private static List<string> ByFrequency(IEnumerable<string> names) =>
        names.Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(AutoComplete.Normalize)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCulture)
            .Select(group => group.First().Trim())
            .ToList();

    /// <summary>Βάζει το συρμένο υλικό στη θέση του υλικού-στόχου, μέσα στο ΕΠΙΛΕΓΜΕΝΟ προϊόν. Η σειρά
    /// που βλέπεις εδώ είναι αυτή που θα δει ο ταμίας και ο σερβιτόρος στον customizer.</summary>
    public void MoveIngredientTo(ExtraToggleViewModel dragged, ExtraToggleViewModel target) =>
        MoveWithin(IngredientToggles, dragged, target);

    /// <summary>Ίδιο για τα έξτρα του προϊόντος. Οριστικοποιείται με την ΑΠΟΘΗΚΕΥΣΗ, όπως όλα τα άλλα
    /// της φόρμας — μέχρι τότε αλλάζει μόνο ό,τι βλέπεις στην οθόνη.</summary>
    public void MoveExtraTo(ExtraToggleViewModel dragged, ExtraToggleViewModel target) =>
        MoveWithin(ExtraToggles, dragged, target);

    private static void MoveWithin(ObservableCollection<ExtraToggleViewModel> list,
        ExtraToggleViewModel dragged, ExtraToggleViewModel target)
    {
        if (ReferenceEquals(dragged, target))
            return;
        var from = list.IndexOf(dragged);
        var to = list.IndexOf(target);
        if (from < 0 || to < 0)
            return;
        list.Move(from, to);
    }

    /// <summary>ΔΥΟ γραμμές, μία ανά μέγεθος πίτας — όσες πίτες έχει και το μαγαζί. Πριν έβγαινε μία
    /// γραμμή ανά κατηγορία, οπότε οι ΠΙΤΤΕΣ και οι ΠΙΤΤΕΣ ΠΑΠΠΟΥ (ίδια ακριβώς μεγάλη πίτα) ζητούσαν
    /// δύο φορές την ίδια τιμή και μπορούσαν να ξεσυγχρονιστούν σιωπηλά.</summary>
    private void RebuildDoublePitaRows()
    {
        DoublePitaRows.Clear();
        foreach (var (key, label) in new[]
                 {
                     (MenuStore.SmallPitaKey, "ΜΙΚΡΗ ΠΙΤΑ"),
                     (MenuStore.LargePitaKey, "ΜΕΓΑΛΗ ΠΙΤΑ"),
                 })
        {
            DoublePitaRows.Add(new ExtraCatalogRowViewModel
            {
                Name = key,
                NameText = label,
                PriceText = _store.DoublePitaPrices.GetValueOrDefault(key).ToString("0.00", Greek),
            });
        }
    }

    /// <summary>Αλλαγή χρέωσης διπλής πίτας για μία από τις δύο κατηγορίες.</summary>
    [RelayCommand]
    private void UpdateDoublePitaPrice(ExtraCatalogRowViewModel row)
    {
        if (!TryParsePrice(row.PriceText, out var price))
        {
            MessageBox.Show("Η τιμή δεν είναι σωστή — γράψε π.χ. 1,00",
                "Χρέωση διπλής πίτας", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _store.UpdateDoublePitaPrice(row.Name, price);
        RebuildDoublePitaRows();
        Flash("✓ Ενημερώθηκε η χρέωση");
    }

    [RelayCommand]
    private void AddGlobalExtra()
    {
        var name = NewExtraName.Trim();
        if (name.Length == 0)
            return;
        if (_store.Extras.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("Υπάρχει ήδη έξτρα με αυτό το όνομα.", "Έξτρα",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        decimal price = 0;
        if (NewExtraPrice.Trim().Length > 0 && !TryParsePrice(NewExtraPrice, out price))
        {
            MessageBox.Show("Η τιμή δεν είναι σωστή — γράψε π.χ. 0,80 ή άφησέ την κενή για δωρεάν",
                "Τιμή έξτρα", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _store.AddExtra(name, price);
        NewExtraName = "";
        NewExtraPrice = "";
        RebuildExtraToggles();
        // Μπαίνει τσεκαρισμένο στο προϊόν που δουλεύεις — γι' αυτό το έγραψες. Παντού αλλού μένει
        // ξετσέκαριστο (βλ. MenuStore.AddExtra), όπως ακριβώς και στα βασικά υλικά.
        var addedExtra = ExtraToggles.FirstOrDefault(t => t.Name == name);
        if (addedExtra is not null)
            addedExtra.IsChecked = true;
        Flash("✓ Προστέθηκε το έξτρα");
    }

    /// <summary>Αποθηκεύει όνομα ΚΑΙ τιμή του έξτρα από τη γενική λίστα διαχείρισης — ένα κουμπί για τη
    /// γραμμή, ώστε μια διόρθωση ορθογραφίας να μη χρειάζεται διαγραφή και ξαναγράψιμο (που θα έσβηνε
    /// το έξτρα από όλα τα προϊόντα που το είχαν).</summary>
    [RelayCommand]
    private void UpdateExtraPrice(ExtraCatalogRowViewModel row)
    {
        if (!TryParsePrice(row.PriceText, out var price))
        {
            MessageBox.Show("Η τιμή δεν είναι σωστή — γράψε π.χ. 0,80",
                "Τιμή έξτρα", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newName = row.NameText.Trim();
        if (newName.Length == 0)
        {
            MessageBox.Show("Το έξτρα πρέπει να έχει όνομα.", "Όνομα έξτρα",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            row.NameText = row.Name;
            return;
        }
        if (newName != row.Name
            && _store.Extras.Any(e => e.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("Υπάρχει ήδη έξτρα με αυτό το όνομα.", "Όνομα έξτρα",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            row.NameText = row.Name;
            return;
        }

        // Πρώτα η τιμή (με το παλιό κλειδί), μετά η μετονομασία — αλλιώς η τιμή θα έψαχνε όνομα που
        // δεν υπάρχει πια και θα χανόταν σιωπηλά.
        _store.UpdateExtraPrice(row.Name, price);
        var renamed = newName != row.Name;
        if (renamed)
            _store.RenameExtra(row.Name, newName);
        RebuildExtraToggles();
        Flash(renamed ? "✓ Αποθηκεύτηκε" : "✓ Ενημερώθηκε η τιμή");
    }

    /// <summary>Μετακίνηση έξτρα με σύρσιμο μέσα στον κοινό κατάλογο — η σειρά εδώ είναι και η σειρά
    /// που τα βλέπει ο ταμίας στον customizer, οπότε τα πιο συχνά μπαίνουν πρώτα.</summary>
    public void MoveExtraTo(ExtraCatalogRowViewModel dragged, ExtraCatalogRowViewModel target)
    {
        _store.MoveExtraTo(dragged.Name, target.Name);
        RebuildExtraToggles();
    }

    [RelayCommand]
    private void DeleteGlobalExtra(ExtraCatalogRowViewModel extra)
    {
        var answer = MessageBox.Show(
            "Διαγραφή του έξτρα «" + extra.Name + "» από όλα τα προϊόντα;",
            "Διαγραφή έξτρα", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;
        _store.RemoveExtra(extra.Name);
        RebuildExtraToggles();
        Flash("✓ Διαγράφηκε");
    }

    [RelayCommand]
    private void SelectCategory(MenuCategory category)
    {
        IsNewCategory = false;
        SelectedCategory = category;
        SelectedProduct = null;
    }

    [RelayCommand]
    private void SelectProduct(Product product) => SelectedProduct = product;

    // ---- κατηγορίες ----

    [RelayCommand]
    private void AddCategory()
    {
        SelectedCategory = null;
        SelectedProduct = null;
        IsNewCategory = true;
        CategoryName = "";
    }

    [RelayCommand]
    private void SaveCategory()
    {
        var name = CategoryName.Trim();
        if (name.Length == 0)
            return;

        // Δύο κατηγορίες με το ΙΔΙΟ όνομα δεν επιτρέπονται — και δεν είναι θέμα τάξης: οι ιδιότητες
        // κατηγορίας (επιλογή ψωμιού, διπλή πίτα και η χρέωσή της) αναζητούνται με το ΟΝΟΜΑ, οπότε η
        // δεύτερη θα δούλευε σιωπηλά με τις ρυθμίσεις της πρώτης — και στο ταμείο και στο κινητό.
        // Το λάθος γίνεται εύκολα: το πεδίο προτείνει τα ονόματα που ήδη υπάρχουν.
        var clash = _store.Categories.FirstOrDefault(c =>
            c != SelectedCategory && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
        {
            MessageBox.Show("Υπάρχει ήδη κατηγορία «" + clash.Name + "».\n\nΔώσε διαφορετικό όνομα.",
                "Κατηγορία", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (IsNewCategory)
        {
            var upper = name.ToUpper(Greek);
            // Οι ιδιότητες ορίζονται ΡΗΤΑ από την αρχή (όχι null): αλλιώς η νέα κατηγορία θα δούλευε
            // με τους παλιούς κανόνες ονόματος μέχρι την επόμενη εκκίνηση, ενώ τα κουτάκια στην οθόνη
            // θα έδειχναν ήδη ξετσεκαρισμένα — δηλαδή οθόνη και συμπεριφορά θα διαφωνούσαν.
            var category = new MenuCategory
            {
                Id = MenuStore.NewId(),
                Name = upper,
                HasBread = MenuSeed.HasBreadChoice(upper),
                FuseBreadIntoName = MenuSeed.FuseBreadIntoName(upper),
                SupportsDoublePita = MenuSeed.SupportsDoublePita(upper),
                VatKind = MenuSeed.GuessVatKind(upper),
            };
            _store.Categories.Add(category);
            _store.Save();
            RefreshLists();
            IsNewCategory = false;
            SelectedCategory = category;
            Flash("✓ Η κατηγορία δημιουργήθηκε");
            return;
        }

        if (SelectedCategory is null)
            return;
        // Μέσω RenameCategory ώστε να μεταφερθεί και η χρέωση διπλής πίτας (κλειδί το όνομα).
        _store.RenameCategory(SelectedCategory, name.ToUpper(Greek));
        RefreshLists(); // ξαναχτίζει και τις γραμμές χρέωσης διπλής πίτας, με το νέο όνομα
        Flash("✓ Αποθηκεύτηκε");
    }

    [RelayCommand]
    private void DeleteCategory()
    {
        if (SelectedCategory is null)
            return;
        var answer = MessageBox.Show(
            "Διαγραφή της κατηγορίας «" + SelectedCategory.Name + "» και όλων των προϊόντων της;",
            "Διαγραφή κατηγορίας", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;
        _store.Categories.Remove(SelectedCategory);
        _store.Save();
        SelectedCategory = _store.Categories.FirstOrDefault();
        SelectedProduct = null;
        RefreshLists();
    }

    [RelayCommand]
    private void MoveCategoryUp() => MoveCategory(-1);

    [RelayCommand]
    private void MoveCategoryDown() => MoveCategory(1);

    private void MoveCategory(int delta)
    {
        if (SelectedCategory is null)
            return;
        var list = _store.Categories;
        var index = list.IndexOf(SelectedCategory);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count)
            return;
        (list[index], list[target]) = (list[target], list[index]);
        _store.Save();
        RefreshLists();
    }

    // ---- προϊόντα ----

    [RelayCommand]
    private void NewProduct() => SelectedProduct = null;

    [RelayCommand]
    private void SaveProduct()
    {
        if (SelectedCategory is null || ProductName.Trim().Length == 0)
            return;
        if (!TryParsePrice(ProductPrice, out var price))
        {
            MessageBox.Show("Η τιμή δεν είναι σωστή — γράψε π.χ. 7,20",
                "Τιμή", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        decimal? deliveryPrice = null;
        if (ProductDeliveryPrice.Trim().Length > 0)
        {
            if (!TryParsePrice(ProductDeliveryPrice, out var dp))
            {
                MessageBox.Show("Η τιμή εφαρμογών δεν είναι σωστή — γράψε π.χ. 7,90 ή άφησέ την κενή",
                    "Τιμή εφαρμογών", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            deliveryPrice = dp;
        }

        var printName = ProductPrintName.Trim();
        // Αν είναι όλα τσεκαρισμένα μένει null («όλα») αντί για ρητή λίστα — έτσι ένα μελλοντικό νέο
        // έξτρα στο MenuSeed.Extras εμφανίζεται αυτόματα σε προϊόντα που δεν έχουν περιοριστεί σκόπιμα.
        var checkedExtras = ExtraToggles.Where(t => t.IsChecked).Select(t => t.Name).ToList();
        // Μένει null ΜΟΝΟ αν είναι όλα τσεκαρισμένα ΚΑΙ με τη σειρά του κοινού καταλόγου. Πριν αρκούσε
        // το «όλα τσεκαρισμένα», οπότε μια αλλαγή σειράς σε προϊόν με όλα τα έξτρα δεν αποθηκευόταν
        // ποτέ — το προϊόν ξαναδιάβαζε την κοινή σειρά και το σύρσιμο έμοιαζε να μην πιάνει.
        var catalogOrder = _store.Extras.Select(e => e.Name).ToList();
        var extraNames = checkedExtras.Count == ExtraToggles.Count && checkedExtras.SequenceEqual(catalogOrder)
            ? null
            : checkedExtras;
        // ΑΝΤΙΘΕΤΑ με τα έξτρα: τα βασικά υλικά γράφονται ΠΑΝΤΑ ρητά, ποτέ null. Το null σημαίνει
        // «ό,τι λέει ο κοινός κατάλογος», οπότε ένα προϊόν που είχε τσεκαρισμένα όλα ξαναγύριζε σε
        // «όλα» και μάζευε μόνο του κάθε μελλοντικό υλικό. Τα υλικά είναι ανά προϊόν — το καθένα
        // κρατά ακριβώς τη λίστα που βλέπεις στη φόρμα, ούτε ένα παραπάνω.
        var ingredients = IngredientToggles.Select(t => t.Name).ToList();

        if (SelectedProduct is null)
        {
            var product = new Product
            {
                Id = MenuStore.NewId(),
                Name = ProductName.Trim(),
                Price = price,
                DeliveryPrice = deliveryPrice,
                PrintName = printName.Length > 0 ? printName : null,
                Customizable = ProductCustomizable,
                ExtraNames = extraNames,
                Ingredients = ingredients,
            };
            (ProductCategory ?? SelectedCategory).Products.Add(product);
            _store.Save();
            RefreshLists();
            SelectedProduct = product;
            Flash("✓ Το προϊόν προστέθηκε");
        }
        else
        {
            SelectedProduct.Name = ProductName.Trim();
            SelectedProduct.Price = price;
            SelectedProduct.DeliveryPrice = deliveryPrice;
            SelectedProduct.PrintName = printName.Length > 0 ? printName : null;
            SelectedProduct.Customizable = ProductCustomizable;
            SelectedProduct.ExtraNames = extraNames;
            SelectedProduct.Ingredients = ingredients;

            // Αλλαγή κατηγορίας: το προϊόν μεταφέρεται στο τέλος της νέας. Κρατιέται το ίδιο
            // αντικείμενο (ίδιος κωδικός), οπότε παλιές παραγγελίες στο ιστορικό εξακολουθούν να
            // δείχνουν σωστά σε αυτό — π.χ. το «ΜΙΑ ΑΠΟ ΤΑ ΙΔΙΑ» δεν χάνει το προϊόν.
            var moved = false;
            if (ProductCategory is { } target && !target.Products.Contains(SelectedProduct))
            {
                foreach (var c in Categories)
                    c.Products.Remove(SelectedProduct);
                target.Products.Add(SelectedProduct);
                SelectedCategory = target;
                moved = true;
            }

            _store.Save();
            RefreshLists();
            Flash(moved ? "✓ Μεταφέρθηκε στην «" + ProductCategory!.Name + "»" : "✓ Αποθηκεύτηκε");
        }
    }

    /// <summary>Μετακίνηση προϊόντος με σύρσιμο μέσα στην κατηγορία του (drag &amp; drop στη λίστα).</summary>
    public void MoveProductTo(Product dragged, Product target)
    {
        if (SelectedCategory is null || dragged == target)
            return;
        var list = SelectedCategory.Products;
        var fromIndex = list.IndexOf(dragged);
        var toIndex = list.IndexOf(target);
        if (fromIndex < 0 || toIndex < 0)
            return;
        list.RemoveAt(fromIndex);
        // Η αφαίρεση μετατόπισε τους δείκτες κατά ένα όταν σέρνουμε προς τα κάτω στη λίστα
        if (fromIndex < toIndex)
            toIndex--;
        list.Insert(toIndex, dragged);
        _store.Save();
        RefreshLists();
    }

    [RelayCommand]
    private void DeleteProduct()
    {
        if (SelectedCategory is null || SelectedProduct is null)
            return;
        var answer = MessageBox.Show(
            "Διαγραφή του προϊόντος «" + SelectedProduct.Name + "»;",
            "Διαγραφή προϊόντος", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;
        SelectedCategory.Products.Remove(SelectedProduct);
        _store.Save();
        SelectedProduct = null;
        RefreshLists();
    }

    private static bool TryParsePrice(string text, out decimal price)
    {
        // Δέξου και κόμμα και τελεία ως υποδιαστολή (π.χ. 7,20 ή 7.20) για τιμές < 1000
        text = text.Trim().Replace("€", "").Replace(" ", "").Replace(",", ".");
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out price) && price >= 0;
    }

    private void RefreshLists()
    {
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(Products));
        RebuildDoublePitaRows();
        RebuildSuggestions();
    }
}
