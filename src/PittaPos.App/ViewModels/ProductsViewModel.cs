using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.App.Services;
using PittaPos.Core.Data;
using PittaPos.Core.Models;

namespace PittaPos.App.ViewModels;

/// <summary>Βήμα 3 — Προϊόντα: rail κατηγοριών, tiles, customizer, ticket.</summary>
public partial class ProductsViewModel : ObservableObject
{
    private int _customLineSeq = 1;

    /// <summary>
    /// Το WPF χτυπάει το MouseDoubleClick ΠΡΙΝ από το Click του δεύτερου πατήματος. Χωρίς αυτή τη
    /// σημαία, το Click που ακολουθεί ξανάνοιγε αμέσως τα υλικά του προϊόντος που μόλις είχε μπει
    /// «κατευθείαν» στο δελτίο με διπλό κλικ.
    /// </summary>
    private bool _suppressNextTap;

    public ProductsViewModel()
    {
        Categories = [];
        ReloadCategories();
        // Ζωντανή ανανέωση όταν αλλάζει ο κατάλογος από τη Διαχείριση
        MenuStore.Instance.Changed += ReloadCategories;
    }

    /// <summary>Ξαναχτίζει τις κατηγορίες από το MenuStore κρατώντας την ενεργή και το καλάθι.</summary>
    private void ReloadCategories()
    {
        var activeId = ActiveCategory?.Category.Id;
        Categories.Clear();
        foreach (var c in MenuStore.Instance.Categories)
            Categories.Add(new CategoryItemViewModel(c));
        if (Categories.Count == 0)
        {
            ActiveCategory = null;
            Tiles.Clear();
            return;
        }
        SelectCategory(Categories.FirstOrDefault(c => c.Category.Id == activeId) ?? Categories[0]);
    }

    public ObservableCollection<CategoryItemViewModel> Categories { get; }
    public ObservableCollection<ProductTileViewModel> Tiles { get; } = [];
    public ObservableCollection<CartLineViewModel> Cart { get; } = [];

    [ObservableProperty]
    private CategoryItemViewModel? _activeCategory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTileGrid))]
    private CustomizerViewModel? _customizer;

    public bool IsTileGrid => Customizer is null;

    /// <summary>Κρατάει σημαδεμένη τη σειρά της λίστας που αντιστοιχεί στα υλικά που είναι ανοιχτά
    /// δίπλα. Η λίστα των προϊόντων μένει ορατή όσο δουλεύεις τον customizer, οπότε χωρίς αυτό δεν
    /// θα φαινόταν ποιο προϊόν πειράζεις.</summary>
    partial void OnCustomizerChanged(CustomizerViewModel? value)
    {
        var openId = value?.Product.Id;
        foreach (var tile in Tiles)
            tile.IsOpen = openId is not null && tile.Product.Id == openId;
    }

    [ObservableProperty]
    private int _orderNumber = 1044;

    /// <summary>Οι γραμμές της τελευταίας ολοκληρωμένης παραγγελίας αυτού του πελάτη, αν βρέθηκε
    /// (βλ. OrderWizardViewModel.ContinueStep2) — για το κουμπί «ΜΙΑ ΑΠΟ ΤΑ ΙΔΙΑ».</summary>
    private IReadOnlyList<SoldLine>? _repeatableOrder;

    [ObservableProperty]
    private bool _hasRepeatableOrder;

    public void SetRepeatableOrder(IReadOnlyList<SoldLine>? lines)
    {
        _repeatableOrder = lines is { Count: > 0 } ? lines : null;
        HasRepeatableOrder = _repeatableOrder is not null;
    }

    /// <summary>Ξαναβάζει στο καλάθι τα ίδια προϊόντα/ποσότητες της τελευταίας παραγγελίας — με
    /// προεπιλεγμένη προσαρμογή (ψωμί/έξτρα), όχι ακριβώς ίδια με τότε· ο ταμίας τα βλέπει/διορθώνει
    /// πριν προχωρήσει, ό,τι αλλάξει σήμερα. Προϊόντα που δεν υπάρχουν πια στον κατάλογο παραλείπονται.</summary>
    [RelayCommand]
    private void RepeatLastOrder()
    {
        if (_repeatableOrder is null)
            return;

        var catalog = MenuStore.Instance.Categories
            .SelectMany(c => c.Products.Select(p => (Category: c.Name, Product: p)))
            .ToList();
        var skipped = 0;
        foreach (var soldLine in _repeatableOrder)
        {
            var (category, product) = FindProductFor(soldLine, catalog);
            if (product is null)
            {
                skipped++;
                continue;
            }

            // Αντίγραφο, ΟΧΙ το ίδιο αντικείμενο: αλλιώς μια επεξεργασία της νέας γραμμής θα άλλαζε
            // αναδρομικά τις ιδιαιτερότητες της παλιάς, καταγεγραμμένης παραγγελίας στο ιστορικό.
            var customization = Clone(soldLine.Customization)
                ?? (product.Customizable ? new LineCustomization() : null);
            var (name, unitPrice, desc1, desc2) = Describe(product, category, customization);

            // Χωρίς ιδιαιτερότητες (π.χ. αναψυκτικά): ενώνονται σε μία γραμμή με άθροιση ποσότητας.
            if (customization is null)
            {
                var key = "p" + product.Id;
                var existing = Cart.FirstOrDefault(l => l.Key == key);
                if (existing is not null)
                {
                    existing.Quantity += soldLine.Quantity;
                    continue;
                }
                Cart.Add(new CartLineViewModel(this, key, product.Id, name, unitPrice)
                {
                    BasePrice = PriceOf(product),
                    Quantity = soldLine.Quantity,
                });
                continue;
            }

            // Με ιδιαιτερότητες: ΞΕΧΩΡΙΣΤΗ γραμμή η καθεμία — δύο ίδιες πίττες με διαφορετικά υλικά
            // δεν είναι το ίδιο προϊόν και δεν πρέπει να ενωθούν.
            Cart.Add(new CartLineViewModel(this, "s" + _customLineSeq++, product.Id, name, unitPrice)
            {
                BasePrice = PriceOf(product),
                Quantity = soldLine.Quantity,
                Customization = customization,
                DescLine1 = desc1,
                DescLine2 = desc2,
            });
        }
        if (skipped > 0)
            AppLog.Write("repeat-order",
                $"«Μια από τα ίδια»: {skipped} από {_repeatableOrder.Count} γραμμές δεν βρέθηκαν στον " +
                "τρέχοντα κατάλογο (διαγραμμένο ή μετονομασμένο προϊόν) και παραλείφθηκαν.");
        OnCartChanged();
    }

    private static LineCustomization? Clone(LineCustomization? c) => c is null ? null : new LineCustomization
    {
        Bread = c.Bread,
        Removed = [.. c.Removed],
        Extras = new Dictionary<string, int>(c.Extras),
        Note = c.Note,
        DoublePita = c.DoublePita,
    };

    /// <summary>
    /// Ξαναχτίζει όνομα γραμμής, τιμή μονάδας και γραμμές λεπτομερειών από τις ιδιαιτερότητες — ίδιοι
    /// κανόνες με τον customizer (βλ. CustomizerViewModel.Add), ώστε μια επαναλαμβανόμενη παραγγελία να
    /// βγαίνει ΑΚΡΙΒΩΣ όπως πατήθηκε την πρώτη φορά: «ΕΛ. χοιρινό» με τα «χωρίς» από κάτω, ποτέ σκέτο
    /// «Πίττα χοιρινό». Οι τιμές (προϊόντος, έξτρα, διπλής πίτας) διαβάζονται από τον ΣΗΜΕΡΙΝΟ κατάλογο,
    /// όχι από την παλιά παραγγελία — αν άλλαξε τιμοκατάλογος στο μεταξύ, ισχύει ο σημερινός.
    /// </summary>
    private (string Name, decimal UnitPrice, string DescLine1, string DescLine2) Describe(
        Product product, string category, LineCustomization? c)
    {
        var basePrice = PriceOf(product);
        if (c is null)
            return (product.Name, basePrice, "", "");

        var store = MenuStore.Instance;
        var hasBread = store.HasBreadChoice(category);
        var doublePita = c.DoublePita && store.SupportsDoublePita(category);
        var extrasTotal = c.Extras.Where(e => e.Value > 0)
            .Sum(e => e.Value * (store.Extras.FirstOrDefault(x => x.Name == e.Key)?.Price ?? 0m));
        var unitPrice = basePrice + extrasTotal + (doublePita ? store.DoublePitaPriceFor(category) : 0m);

        var name = doublePita
            ? MenuSeed.ComposeDoublePitaName(product.Name, category, c.Bread)
            : hasBread && store.FuseBreadIntoName(category)
                ? MenuSeed.ComposeCustomizedName(product.Name, c.Bread)
                : product.Name;

        // Το ψωμί ολόγραφο σε δική του γραμμή μόνο όταν ΔΕΝ έχει μπει μέσα στο όνομα.
        var breadOnOwnLine = hasBread && !store.FuseBreadIntoName(category) && !doublePita;
        var descLine1 = breadOnOwnLine
            ? c.Note.Length > 0 ? c.Bread + "\n" + c.Note : c.Bread
            : c.Note;

        var mods = new List<string>();
        mods.AddRange(MenuSeed.DescribeRemovedIngredients(c.Removed));
        mods.AddRange(c.Extras.Where(e => e.Value > 0)
            .Select(e => "+ " + e.Key + (e.Value > 1 ? " ×" + e.Value : "")));

        return (name, unitPrice, descLine1, string.Join("\n", mods));
    }

    /// <summary>
    /// Βρίσκει κατηγορία+προϊόν για μια πουλημένη γραμμή. Πρώτα με τον κωδικό (ακριβές, βλ.
    /// SoldLine.ProductId). Για παλιές παραγγελίες χωρίς κωδικό, δοκιμάζει το όνομα — ΚΑΙ σε όλες τις
    /// σύνθετες μορφές του: το αποθηκευμένο όνομα δεν είναι του προϊόντος αλλά της γραμμής, με το ψωμί
    /// μπροστά («ΕΛ. κοτόπουλο») ή και «ΔΙΠΛΗ ΠΙΤΑ». Τις παράγουμε με τις ΙΔΙΕΣ συναρτήσεις που τις
    /// έφτιαξαν, αντί να τις αποσυνθέσουμε — έτσι δεν ξεφεύγει καμία παραλλαγή.
    /// </summary>
    private static (string Category, Product? Product) FindProductFor(
        SoldLine line, List<(string Category, Product Product)> catalog)
    {
        if (line.ProductId.Length > 0)
        {
            var byId = catalog.FirstOrDefault(x => x.Product.Id == line.ProductId);
            if (byId.Product is not null)
                return (byId.Category, byId.Product);
        }

        foreach (var (category, product) in catalog)
        {
            if (string.Equals(product.Name, line.Name, StringComparison.OrdinalIgnoreCase))
                return (category, product);
            foreach (var bread in MenuSeed.BreadOptions)
            {
                if (string.Equals(MenuSeed.ComposeCustomizedName(product.Name, bread), line.Name,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(MenuSeed.ComposeDoublePitaName(product.Name, category, bread), line.Name,
                        StringComparison.OrdinalIgnoreCase))
                    return (category, product);
            }
        }
        return ("", null);
    }

    private bool _useDeliveryPrices;

    /// <summary>Τιμοκατάλογος διανομής/εφαρμογών — τον ορίζει το wizard με τον τύπο παραγγελίας.</summary>
    public bool UseDeliveryPrices
    {
        get => _useDeliveryPrices;
        set
        {
            if (_useDeliveryPrices == value)
                return;
            _useDeliveryPrices = value;
            if (ActiveCategory is not null)
                SelectCategory(ActiveCategory); // ξαναχτίζει τα tiles με τις σωστές τιμές
        }
    }

    /// <summary>Η τιμή του προϊόντος για τον τρέχοντα τιμοκατάλογο.</summary>
    public decimal PriceOf(Product p) =>
        UseDeliveryPrices && p.DeliveryPrice is { } d ? d : p.Price;

    /// <summary>Καλείται από το wizard όταν πατηθεί ΣΥΝΕΧΕΙΑ/Πίσω στο ticket.</summary>
    public Action? ContinueRequested { get; set; }
    public Action? BackRequested { get; set; }

    [ObservableProperty]
    private string _continueLabel = "ΣΥΝΕΧΕΙΑ";

    /// <summary>Ανοίγει/κλείνει το πεδίο σχολίων παραγγελίας (ΤΡΑΠΕΖΙ/ΠΑΡΑΛΑΒΗ, που δεν έχουν Βήμα 2).</summary>
    [ObservableProperty]
    private bool _showNoteField;

    [RelayCommand]
    private void ToggleNoteField() => ShowNoteField = !ShowNoteField;

    [RelayCommand]
    private void Continue() => ContinueRequested?.Invoke();

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();

    /// <summary>Καθαρίζει την οθόνη για νέα παραγγελία.</summary>
    public void Reset(int newOrderNumber)
    {
        Cart.Clear();
        Customizer = null;
        _useDeliveryPrices = false;
        OrderDiscountPct = 0;
        OrderNumber = newOrderNumber;
        ContinueLabel = "ΣΥΝΕΧΕΙΑ";
        ShowNoteField = false;
        // Αν ο κατάλογος έχει μείνει χωρίς καμία κατηγορία (π.χ. διαγράφηκαν όλες από τη Διαχείριση),
        // το Categories[0] έριχνε σφάλμα σε ΚΑΘΕ νέα παραγγελία — βλ. ίδιο έλεγχο στο ReloadCategories.
        if (Categories.Count > 0)
            SelectCategory(Categories[0]);
        else
        {
            ActiveCategory = null;
            Tiles.Clear();
        }
        OnCartChanged();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalLabel))]
    [NotifyPropertyChangedFor(nameof(OrderDiscountLabel))]
    [NotifyPropertyChangedFor(nameof(HasOrderDiscount))]
    private int _orderDiscountPct;

    partial void OnOrderDiscountPctChanged(int value)
    {
        if (value is < 0 or > 100)
            OrderDiscountPct = Math.Clamp(value, 0, 100);
    }

    public string ActiveCategoryName => ActiveCategory?.Category.Name ?? "";
    public string ProductCountLabel => Tiles.Count + " προϊόντα";
    public string CartCountLabel => Cart.Count + " είδη";
    public bool CartEmpty => Cart.Count == 0;
    public bool CartNotEmpty => Cart.Count > 0;

    public decimal Subtotal => Cart.Sum(l => l.SubtotalContribution);
    public decimal LineTotal => Cart.Sum(l => l.Total);
    public decimal ItemsDiscount => Subtotal - LineTotal;
    public decimal OrderDiscountAmount => LineTotal * (OrderDiscountPct / 100m);
    public decimal Total => LineTotal - OrderDiscountAmount;

    public string SubtotalLabel => Order.FormatPrice(Subtotal);
    public bool HasItemsDiscount => ItemsDiscount > 0.001m;
    public string ItemsDiscountLabel => "−" + Order.FormatPrice(ItemsDiscount);
    public bool HasOrderDiscount => OrderDiscountAmount > 0.001m;
    public string OrderDiscountLabel => "−" + Order.FormatPrice(OrderDiscountAmount);
    public string TotalLabel => Order.FormatPrice(Total);

    internal void OnCartChanged()
    {
        OnPropertyChanged(nameof(CartCountLabel));
        OnPropertyChanged(nameof(CartEmpty));
        OnPropertyChanged(nameof(CartNotEmpty));
        OnPropertyChanged(nameof(SubtotalLabel));
        OnPropertyChanged(nameof(HasItemsDiscount));
        OnPropertyChanged(nameof(ItemsDiscountLabel));
        OnPropertyChanged(nameof(HasOrderDiscount));
        OnPropertyChanged(nameof(OrderDiscountLabel));
        OnPropertyChanged(nameof(TotalLabel));
        RefreshTileQuantities();
    }

    /// <summary>
    /// Το πλήθος δίπλα στο προϊόν μετράει ΟΛΕΣ τις γραμμές του στο δελτίο, όχι μόνο την απλή. Τώρα που
    /// κάθε πίττα περνάει από τον customizer, δύο ίδιες πίττες με διαφορετικά υλικά πιάνουν ξεχωριστές
    /// γραμμές — ο ταμίας όμως θέλει να βλέπει «3» στο κοτόπουλο, όχι τίποτα.
    /// </summary>
    private void RefreshTileQuantities()
    {
        foreach (var tile in Tiles)
            tile.Quantity = Cart.Where(l => l.ProductId == tile.Product.Id).Sum(l => l.Quantity);
    }

    [RelayCommand]
    private void SelectCategory(CategoryItemViewModel item)
    {
        foreach (var c in Categories)
            c.IsActive = c == item;
        ActiveCategory = item;
        Customizer = null;
        _suppressNextTap = false; // ασφάλεια: να μη μείνει «κρεμασμένη» και φαγωθεί το πρώτο πάτημα

        Tiles.Clear();
        foreach (var p in item.Category.Products)
            Tiles.Add(new ProductTileViewModel(p, p.Customizable, PriceOf(p)));
        RefreshTileQuantities();
        OnPropertyChanged(nameof(ActiveCategoryName));
        OnPropertyChanged(nameof(ProductCountLabel));
    }

    /// <summary>
    /// Tap σε προϊόν της λίστας. Αν σηκώνει προσαρμογή (πίττες, τυλιχτά κ.λπ.) ανοίγουν τα υλικά του
    /// στη διπλανή στήλη και η λίστα μένει στη θέση της — ο ταμίας διαλέγει και πατάει ΠΡΟΣΘΗΚΗ.
    /// Τα υπόλοιπα (ποτά, γλυκά) πάνε κατευθείαν στο δελτίο: δεν έχουν τίποτα να διαλέξει κανείς,
    /// ένα ενδιάμεσο παράθυρο θα ήταν μόνο ένα παραπάνω πάτημα.
    /// </summary>
    [RelayCommand]
    private void TapProduct(ProductTileViewModel tile)
    {
        if (_suppressNextTap)
        {
            _suppressNextTap = false;
            return;
        }

        if (tile.Customizable)
        {
            OpenCustomizer(tile);
            return;
        }

        var key = "p" + tile.Product.Id;
        var existing = Cart.FirstOrDefault(l => l.Key == key);
        if (existing is not null)
        {
            existing.Quantity++;
            return;
        }

        Cart.Add(new CartLineViewModel(this, key, tile.Product.Id, tile.Product.Name, tile.Price)
        {
            BasePrice = tile.Price,
        });
        OnCartChanged();
    }

    /// <summary>
    /// Διπλό κλικ σε φαγητό: μπαίνει στο δελτίο ΑΜΕΣΩΣ, ακριβώς όπως θα έμπαινε με το κουμπί
    /// ΠΡΟΣΘΗΚΗ — δηλαδή με ό,τι έχει ήδη διαλεγεί δίπλα (ψωμί, υλικά, έξτρα, ποσότητα). Αν δεν έχει
    /// πειραχτεί τίποτα, μπαίνει με τα προεπιλεγμένα του: αυτό είναι και η συνηθισμένη περίπτωση,
    /// ο πελάτης που δεν ζητάει καμία αλλαγή.
    ///
    /// Τα υλικά ΔΕΝ κλείνουν, αλλά ξαναγυρίζουν στα προεπιλεγμένα: μόλις το φαγητό μπει στο δελτίο,
    /// τα έξτρα που διαλέχτηκαν γι' αυτό δεν ισχύουν πια. Αν έμεναν πατημένα, το επόμενο πάτημα θα
    /// έβαζε σιωπηλά άλλη μια πίττα με τα ίδια έξτρα — και θα χρεωνόταν κάτι που δεν ζητήθηκε.
    /// </summary>
    [RelayCommand]
    private void QuickAddProduct(ProductTileViewModel tile)
    {
        // Το δεύτερο από τα δύο κλικ δεν πρέπει να ξαναχτυπήσει το φαγητό.
        _suppressNextTap = true;

        // Απλό προϊόν (ποτό, γλυκό): το πρώτο κλικ το έβαλε ήδη στο δελτίο — διπλό κλικ πάνω του
        // σημαίνει ένα τεμάχιο, όχι δύο.
        if (!tile.Customizable)
            return;

        // Κανονικά το πρώτο από τα δύο κλικ έχει ήδη αφήσει ανοιχτά τα υλικά αυτού του φαγητού. Αν
        // όμως δίπλα υπήρχε διόρθωση γραμμής του δελτίου (από το ✎), ανοίγουμε καθαρά υλικά — αλλιώς
        // το διπλό κλικ θα άλλαζε την παλιά γραμμή αντί να προσθέσει καινούργια.
        if (Customizer is not { IsEditingExistingLine: false } open || open.Product.Id != tile.Product.Id)
        {
            open = new CustomizerViewModel(this, tile.Product, ActiveCategory?.Category.Name ?? "");
            Customizer = open;
        }

        open.Commit(); // μπαίνει στο δελτίο· το CommitCustomizedLine κλείνει τα υλικά

        // Ξανανοίγουν αμέσως, καθαρά, για το ίδιο φαγητό: η στήλη δεν αδειάζει μπροστά στον ταμία,
        // αλλά ούτε κουβαλάει τα έξτρα της προηγούμενης πίττας στην επόμενη.
        Customizer = new CustomizerViewModel(this, tile.Product, ActiveCategory?.Category.Name ?? "");
    }

    /// <summary>Κλικ στο badge ποσότητας του tile: αφαίρεση ενός.</summary>
    [RelayCommand]
    private void DecrementProduct(ProductTileViewModel tile) => DecrementLineByKey("p" + tile.Product.Id);

    [RelayCommand]
    private void OpenCustomizer(ProductTileViewModel tile)
    {
        // Αν είναι ήδη ανοιχτά τα υλικά ΑΥΤΟΥ του φαγητού, δεν ξαναχτίζονται από την αρχή: ένα δεύτερο
        // πάτημα στην ίδια σειρά θα έσβηνε ό,τι έχει ήδη διαλέξει ο ταμίας (π.χ. + γκούντα) — και το
        // πρώτο από τα δύο κλικ του διπλού κλικ είναι ακριβώς ένα τέτοιο πάτημα.
        if (Customizer is { IsEditingExistingLine: false } open && open.Product.Id == tile.Product.Id)
            return;

        Customizer = new CustomizerViewModel(this, tile.Product, ActiveCategory?.Category.Name ?? "");
    }

    [RelayCommand]
    private void EditLine(CartLineViewModel line)
    {
        if (line.Customization is null)
            return;
        // Από το ζωντανό μενού — το προϊόν μπορεί να έχει διαγραφεί από τη Διαχείριση
        var category = MenuStore.Instance.Categories.FirstOrDefault(c => c.Products.Any(p => p.Id == line.ProductId));
        var product = category?.Products.FirstOrDefault(p => p.Id == line.ProductId);
        if (category is null || product is null)
            return;
        Customizer = new CustomizerViewModel(this, product, category.Name, line);
    }

    public void CloseCustomizer() => Customizer = null;

    /// <summary>Καλείται από τον customizer στο ΠΡΟΣΘΗΚΗ — νέα γραμμή ή ενημέρωση υπάρχουσας. Το «name»
    /// έρχεται ήδη έτοιμο από τον CustomizerViewModel (αποφασίζει αν/πώς φαίνεται το ψωμί ανάλογα με
    /// την κατηγορία — βλ. MenuSeed.HasBreadChoice/FuseBreadIntoName).</summary>
    public void CommitCustomizedLine(CartLineViewModel? editingLine, Product product,
        LineCustomization customization, int quantity, decimal unitPrice, int discountPct,
        bool noCharge, string name, string descLine1, string descLine2)
    {
        var line = editingLine;
        if (line is null)
        {
            line = new CartLineViewModel(this, "s" + _customLineSeq++, product.Id, product.Name, unitPrice)
            {
                BasePrice = PriceOf(product),
            };
            Cart.Add(line);
        }

        line.Name = name;
        line.Customization = customization;
        line.UnitPrice = unitPrice;
        line.Quantity = quantity;
        line.DiscountPct = discountPct;
        line.NoCharge = noCharge;
        line.DescLine1 = descLine1;
        line.DescLine2 = descLine2;

        Customizer = null;
        OnCartChanged();
    }

    [RelayCommand]
    private void IncLine(CartLineViewModel line) => line.Quantity++;

    [RelayCommand]
    private void DecLine(CartLineViewModel line) => DecrementLineByKey(line.Key);

    private void DecrementLineByKey(string key)
    {
        var line = Cart.FirstOrDefault(l => l.Key == key);
        if (line is null)
            return;
        if (line.Quantity > 1)
            line.Quantity--;
        else
            RemoveLine(line);
    }

    [RelayCommand]
    private void RemoveLine(CartLineViewModel line)
    {
        Cart.Remove(line);
        OnCartChanged();
    }

    [RelayCommand] private void IncOrderDiscount() => OrderDiscountPct = Math.Min(100, OrderDiscountPct + 5);
    [RelayCommand] private void DecOrderDiscount() => OrderDiscountPct = Math.Max(0, OrderDiscountPct - 5);
}
