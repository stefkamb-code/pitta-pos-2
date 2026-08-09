using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

/// <summary>Μία γραμμή στη γενική διαχείριση του κοινού καταλόγου έξτρα (όνομα + επεξεργάσιμη τιμή + διαγραφή).</summary>
public partial class ExtraCatalogRowViewModel : ObservableObject
{
    public required string Name { get; init; }

    [ObservableProperty]
    private string _priceText = "";
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
    public string CategoryFormTitle => IsNewCategory ? "ΝΕΑ ΚΑΤΗΓΟΡΙΑ" : "ΚΑΤΗΓΟΡΙΑ";
    public string CategorySaveLabel => IsNewCategory ? "ΔΗΜΙΟΥΡΓΙΑ ΚΑΤΗΓΟΡΙΑΣ" : "ΑΠΟΘΗΚΕΥΣΗ ΟΝΟΜΑΤΟΣ";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingProduct))]
    [NotifyPropertyChangedFor(nameof(ProductFormTitle))]
    private Product? _selectedProduct;

    public bool HasCategory => SelectedCategory is not null;
    public bool IsEditingProduct => SelectedProduct is not null;
    public string ProductFormTitle => SelectedProduct is null ? "ΝΕΟ ΠΡΟΪΟΝ" : "ΕΠΕΞΕΡΓΑΣΙΑ ΠΡΟΪΟΝΤΟΣ";

    [ObservableProperty] private string _categoryName = "";
    [ObservableProperty] private string _productName = "";
    [ObservableProperty] private string _productPrice = "";
    [ObservableProperty] private string _productDeliveryPrice = "";
    [ObservableProperty] private string _productDescription = "";
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

    /// <summary>Μόνο το «ψωμί μέσα στο όνομα» εξαρτάται από το αν ρωτιέται καθόλου ψωμί — αλλιώς δεν
    /// υπάρχει ψωμί για να μπει πουθενά. Η «διπλή πίτα» είναι ΑΝΕΞΑΡΤΗΤΗ: τα ΚΛΑΣΙΚΑ ΜΙΚΡΑ π.χ. έχουν
    /// διπλή πίτα χωρίς καμία επιλογή ψωμιού, οπότε αν την κλείδωνε το ψωμί δεν θα μπορούσε καν να
    /// ξεπατηθεί από την οθόνη.</summary>
    public bool CategoryFuseBreadEnabled => HasCategory && CategoryHasBread;

    /// <summary>Ξαναχτίζει τα κουτάκια υλικών: όλα όσα υπάρχουν στον κοινό κατάλογο, τσεκαρισμένα
    /// όσα έχει το προϊόν. Προϊόν χωρίς δική του λίστα θεωρείται ότι τα έχει όλα — έτσι δουλεύει
    /// ακριβώς όπως πριν, χωρίς να χρειαστεί να ξαναρυθμιστεί κανένα από τα 124.</summary>
    private void RebuildIngredientToggles()
    {
        IngredientToggles.Clear();
        var own = SelectedProduct?.Ingredients;
        foreach (var name in _store.Ingredients)
        {
            IngredientToggles.Add(new ExtraToggleViewModel
            {
                Name = name,
                PriceLabel = "",
                IsChecked = own is null || own.Contains(name),
            });
        }
    }

    /// <summary>Προσθήκη νέου υλικού στον κοινό κατάλογο — μπαίνει αμέσως τσεκαρισμένο στο προϊόν
    /// που δουλεύεις, γιατί γι' αυτό το έγραψες.</summary>
    [RelayCommand]
    private void AddIngredient()
    {
        var name = NewIngredientName.Trim();
        if (name.Length == 0 || _store.Ingredients.Contains(name))
            return;

        _store.AddIngredient(name);
        NewIngredientName = "";
        RebuildIngredientToggles();
        var added = IngredientToggles.FirstOrDefault(t => t.Name == name);
        if (added is not null)
            added.IsChecked = true;
        Flash("✓ Προστέθηκε το υλικό");
    }

    [RelayCommand]
    private void RemoveIngredient(ExtraToggleViewModel ingredient)
    {
        var answer = MessageBox.Show(
            "Διαγραφή του υλικού «" + ingredient.Name + "» από τον κατάλογο και από όλα τα προϊόντα;",
            "Διαγραφή υλικού", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;
        _store.RemoveIngredient(ingredient.Name);
        RebuildIngredientToggles();
        Flash("✓ Διαγράφηκε");
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
        _loadingCategoryFlags = false;
        OnPropertyChanged(nameof(CategoryFuseBreadEnabled));
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
        ProductDescription = value?.Description ?? "";
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
        foreach (var extra in _store.Extras)
        {
            ExtraToggles.Add(new ExtraToggleViewModel
            {
                Name = extra.Name,
                PriceLabel = extra.Price > 0 ? Order.FormatPrice(extra.Price) : "δωρεάν",
                IsChecked = value?.ExtraNames is null || value.ExtraNames.Contains(extra.Name),
            });
            ExtraCatalogRows.Add(new ExtraCatalogRowViewModel
            {
                Name = extra.Name,
                PriceText = extra.Price.ToString("0.00", Greek),
            });
        }
    }

    /// <summary>Μία γραμμή ανά κατηγορία που πραγματικά υποστηρίζει διπλή πίτα, με το τρέχον όνομά
    /// της (όχι σταθερή λίστα) — έτσι δουλεύει σωστά ακόμα κι αν η κατηγορία έχει μετονομαστεί.</summary>
    private void RebuildDoublePitaRows()
    {
        DoublePitaRows.Clear();
        foreach (var category in _store.Categories.Where(c => MenuStore.Instance.SupportsDoublePita(c.Name)))
        {
            DoublePitaRows.Add(new ExtraCatalogRowViewModel
            {
                Name = category.Name,
                PriceText = _store.DoublePitaPriceFor(category.Name).ToString("0.00", Greek),
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
        Flash("✓ Προστέθηκε το έξτρα");
    }

    /// <summary>Αλλαγή τιμής υπάρχοντος έξτρα από τη γενική λίστα διαχείρισης.</summary>
    [RelayCommand]
    private void UpdateExtraPrice(ExtraCatalogRowViewModel row)
    {
        if (!TryParsePrice(row.PriceText, out var price))
        {
            MessageBox.Show("Η τιμή δεν είναι σωστή — γράψε π.χ. 0,80",
                "Τιμή έξτρα", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _store.UpdateExtraPrice(row.Name, price);
        RebuildExtraToggles();
        Flash("✓ Ενημερώθηκε η τιμή");
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

        var description = ProductDescription.Trim();
        var printName = ProductPrintName.Trim();
        // Αν είναι όλα τσεκαρισμένα μένει null («όλα») αντί για ρητή λίστα — έτσι ένα μελλοντικό νέο
        // έξτρα στο MenuSeed.Extras εμφανίζεται αυτόματα σε προϊόντα που δεν έχουν περιοριστεί σκόπιμα.
        var checkedExtras = ExtraToggles.Where(t => t.IsChecked).Select(t => t.Name).ToList();
        var extraNames = checkedExtras.Count == ExtraToggles.Count ? null : checkedExtras;
        // Ίδια λογική με τα έξτρα: όλα τσεκαρισμένα -> null («όλα του καταλόγου»), ώστε ένα μελλοντικό
        // νέο υλικό να εμφανίζεται αυτόματα σε προϊόντα που δεν περιορίστηκαν σκόπιμα.
        var checkedIngredients = IngredientToggles.Where(t => t.IsChecked).Select(t => t.Name).ToList();
        var ingredients = checkedIngredients.Count == IngredientToggles.Count ? null : checkedIngredients;

        if (SelectedProduct is null)
        {
            var product = new Product
            {
                Id = MenuStore.NewId(),
                Name = ProductName.Trim(),
                Price = price,
                DeliveryPrice = deliveryPrice,
                Description = description.Length > 0 ? description : null,
                PrintName = printName.Length > 0 ? printName : null,
                Customizable = ProductCustomizable,
                ExtraNames = extraNames,
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
            SelectedProduct.Description = description.Length > 0 ? description : null;
            SelectedProduct.PrintName = printName.Length > 0 ? printName : null;
            SelectedProduct.Customizable = ProductCustomizable;
            SelectedProduct.ExtraNames = extraNames;

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
    }
}
