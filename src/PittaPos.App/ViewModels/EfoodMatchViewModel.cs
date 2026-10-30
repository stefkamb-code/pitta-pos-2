using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.App.Services;
using PittaPos.Core.Data;
using PittaPos.Core.Efood;
using PittaPos.Core.Models;

namespace PittaPos.App.ViewModels;

// Το ToString είναι αυτό που δείχνει το κλειστό PosComboBox (το template του παρουσιάζει σκέτο το SelectionBoxItem).

/// <summary>Κατηγορία του ταμείου στο πρώτο dropdown — <c>Category</c> null = «δεν υπάρχει στο ταμείο».</summary>
public sealed record EfoodCategoryChoice(MenuCategory? Category, string Label)
{
    public override string ToString() => Label;
}

public sealed record EfoodProductChoice(Product Product, MenuCategory Category, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Η οθόνη ΑΝΤΙΣΤΟΙΧΙΣΗΣ (Ρυθμίσεις → E-FOOD): κάθε προϊόν του καταλόγου του e-food με το προϊόν του ταμείου που
/// είναι. Πράσινο = έτοιμο (αυτόματα με ίδιο όνομα και τιμή, ή επιβεβαιωμένο)· κίτρινο = θέλει ματιά, και μέχρι τότε
/// τυπώνεται με το όνομα του e-food.
/// </summary>
public partial class EfoodMatchViewModel : ObservableObject
{
    public EfoodMatchViewModel(EfoodMatchStore store, IEfoodMenu menu)
    {
        Store = store;
        Menu = menu;
        Categories = [new EfoodCategoryChoice(null, "— δεν υπάρχει στο ταμείο —"), .. menu.Categories.Select(c => new EfoodCategoryChoice(c, c.Name))];
        Rebuild();
        UpdateStatus("");
    }

    public EfoodMatchStore Store { get; }
    public IEfoodMenu Menu { get; }
    private MatchPlatform Platform => Store.Platform;

    /// <summary>«ΑΝΤΙΣΤΟΙΧΙΣΗ E-FOOD» / «ΑΝΤΙΣΤΟΙΧΙΣΗ BOX» — ίδια οθόνη για τις δύο πλατφόρμες.</summary>
    public string Title => "ΑΝΤΙΣΤΟΙΧΙΣΗ " + Platform.SettingsName;
    public string RefreshLabel => "↻  ΚΑΤΑΛΟΓΟΣ " + Platform.SettingsName;
    public string Intro =>
        $"Ποιο προϊόν του ταμείου είναι κάθε προϊόν {Platform.Of}. Δεν αλλάζει ούτε ο κατάλογος {Platform.Of} ούτε το μενού ή το χαρτί του ταμείου — μόνο το πώς μπαίνουν οι παραγγελίες {Platform.Label}. Όσα είναι κίτρινα τυπώνονται με το όνομα {Platform.Of} μέχρι να πατηθεί «ΣΩΣΤΟ» ή να διαλεχτεί προϊόν."
        + (Platform == EfoodMatchStore.BoxPlatform ? " Η Box δεν δίνει τιμές στον κατάλογο, οπότε κάθε πρόταση θέλει ένα «ΣΩΣΤΟ»." : "");

    public IReadOnlyList<EfoodCategoryChoice> Categories { get; }
    public IReadOnlyList<string> BreadChoices { get; } = [EfoodMatchRowViewModel.AnyBread, .. MenuSeed.BreadOptions];
    internal IReadOnlyDictionary<string, EfoodSuggestion?> Suggestions { get; private set; } = new Dictionary<string, EfoodSuggestion?>();

    private List<EfoodMatchRowViewModel> _all = [];

    [ObservableProperty] private IReadOnlyList<EfoodMatchRowViewModel> _rows = [];
    [ObservableProperty] private bool _showPendingOnly;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _pendingLabel = "";
    [ObservableProperty] private string _allLabel = "";
    [ObservableProperty] private string _emptyText = "";

    /// <summary>Οι γραμμές ξαναφτιάχνονται μόνο όταν έρθει κατάλογος· το φίλτρο μόνο με τα κουμπιά — μια γραμμή που μόλις
    /// επιβεβαιώθηκε μένει στη θέση της για να φαίνεται τι τυπώνει.</summary>
    private void Rebuild()
    {
        var wasEmpty = _all.Count == 0;
        Suggestions = EfoodMatcher.SuggestAll(Store.Catalog, Menu);
        _all = Store.Catalog.Select(item => new EfoodMatchRowViewModel(this, item)).ToList();
        if (wasEmpty)
            ShowPendingOnly = _all.Any(r => r.NeedsConfirm);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Rows = ShowPendingOnly ? _all.Where(r => r.NeedsConfirm).ToList() : _all;
        EmptyText = Rows.Count > 0 ? ""
            : _all.Count == 0 ? $"Ο κατάλογος {Platform.Of} δεν έχει έρθει ακόμα — πάτα «{RefreshLabel.Replace("  ", " ")}»."
            : $"✓ Όλα τα προϊόντα {Platform.Of} έχουν αντιστοιχιστεί.";
        UpdateCounts();
    }

    internal void UpdateCounts()
    {
        PendingLabel = $"ΠΡΟΣ ΕΠΙΒΕΒΑΙΩΣΗ · {_all.Count(r => r.NeedsConfirm)}";
        AllLabel = $"ΟΛΑ · {_all.Count}";
    }

    private void UpdateStatus(string message) =>
        Status = message.Length > 0 ? message
            : Store.Catalog.Count == 0 ? ""
            : $"Κατάλογος {Platform.Label}: {Store.Catalog.Count} προϊόντα · ήρθε {Store.CatalogUpdated:d/M HH:mm}";

    [RelayCommand]
    private void ShowPending()
    {
        ShowPendingOnly = true;
        ApplyFilter();
    }

    [RelayCommand]
    private void ShowAll()
    {
        ShowPendingOnly = false;
        ApplyFilter();
    }

    [RelayCommand]
    private void Confirm(EfoodMatchRowViewModel? row) => row?.Confirm();

    [RelayCommand]
    private async Task RefreshCatalogAsync()
    {
        Status = $"Φέρνω τον κατάλογο {Platform.Of}… (έως 2 λεπτά)";
        var (ok, message) = await Store.RefreshAsync();
        if (ok)
            Rebuild();
        UpdateStatus(ok ? "" : "✕ " + message);
    }
}

/// <summary>Μία γραμμή: προϊόν του e-food → κατηγορία, προϊόν και ψωμί του ταμείου.</summary>
public partial class EfoodMatchRowViewModel : ObservableObject
{
    public const string AnyBread = "ό,τι διαλέξει ο πελάτης";

    private static readonly Brush PendingBrush = Frozen("#F0A500");
    private static readonly Brush MatchedBrush = Frozen("#2E9E5B");
    private static readonly Brush NoneBrush = Frozen("#B3B0B0");

    private readonly EfoodMatchViewModel _owner;
    private bool _loading;
    private EfoodCategoryChoice? _category;
    private EfoodProductChoice? _product;
    private string _bread = AnyBread;

    public EfoodMatchRowViewModel(EfoodMatchViewModel owner, EfoodCatalogItem item)
    {
        _owner = owner;
        Item = item;
        Load();
    }

    public EfoodCatalogItem Item { get; }
    public string EfoodName => Item.Name;
    // Ο κατάλογος της Box δεν έχει τιμές — δεν γράφεται «€0,00».
    public string EfoodInfo => Item.Category
        + (_owner.Store.Platform == EfoodMatchStore.BoxPlatform ? "" : " · " + Order.FormatPrice(Item.Price))
        + (Item.IsAvailable ? "" : " · μη διαθέσιμο");
    public IReadOnlyList<EfoodCategoryChoice> Categories => _owner.Categories;
    public IReadOnlyList<string> BreadChoices => _owner.BreadChoices;

    [ObservableProperty] private IReadOnlyList<EfoodProductChoice> _products = [];
    [ObservableProperty] private bool _hasCategory;
    [ObservableProperty] private bool _breadVisible;
    [ObservableProperty] private string _preview = "";
    [ObservableProperty] private string _priceNote = "";
    [ObservableProperty] private bool _needsConfirm;
    [ObservableProperty] private bool _canConfirm;
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private Brush _stripe = PendingBrush;

    // Οι τρεις επιλογές ΑΓΝΟΟΥΝ το null: το WPF σπρώχνει null σε ένα dropdown που ξεστήνεται (κύλιση, κλείσιμο, αλλαγή
    // λίστας) — αυτό δεν είναι απόφαση του χρήστη και δεν πρέπει να σβήσει ούτε το ψωμί μιας γραμμής.

    public EfoodCategoryChoice? SelectedCategory
    {
        get => _category;
        set
        {
            if (value is null || !SetProperty(ref _category, value))
                return;
            Products = value.Category is { } category
                ? category.Products.Select(p => new EfoodProductChoice(p, category, p.Name + "   " + Order.FormatPrice(p.DeliveryPrice ?? p.Price))).ToList()
                : [];
            HasCategory = value.Category is not null;
            if (_product is not null && !Products.Contains(_product))
            {
                _product = null;
                OnPropertyChanged(nameof(SelectedProduct));
            }
            if (!_loading)
                Refresh();
        }
    }

    public EfoodProductChoice? SelectedProduct
    {
        get => _product;
        set
        {
            if (value is null || !SetProperty(ref _product, value) || _loading)
                return;
            Commit();   // προϊόν διαλεγμένο με το χέρι = απόφαση
        }
    }

    public string? SelectedBread
    {
        get => _bread;
        set
        {
            if (value is null || !SetProperty(ref _bread, value) || _loading)
                return;
            if (_product is not null)
                Commit();
            else
                Refresh();
        }
    }

    /// <summary>«ΣΩΣΤΟ»: κρατάει την πρόταση όπως φαίνεται (ή το «δεν υπάρχει στο ταμείο»).</summary>
    public void Confirm()
    {
        if (Current() is not null)
            Commit();
    }

    private void Load()
    {
        _loading = true;
        var saved = _owner.Store.MatchFor(Item.Id);
        string productId = "", bread = "";
        if (saved is not null && _owner.Store.StateOf(Item) != EfoodMatchState.Pending)
            (productId, bread) = (saved.ProductId, saved.Bread);
        else if (_owner.Suggestions.GetValueOrDefault(EfoodMatcher.KeyOf(Item)) is { } suggestion)
            (productId, bread) = (suggestion.Product.Id, suggestion.Bread);

        SelectedCategory = (productId.Length == 0 ? null
            : Categories.FirstOrDefault(c => c.Category?.Products.Any(p => p.Id == productId) == true)) ?? Categories[0];
        SelectedProduct = Products.FirstOrDefault(p => p.Product.Id == productId);
        SelectedBread = bread.Length > 0 && BreadChoices.Contains(bread) ? bread : AnyBread;
        _loading = false;
        Refresh();
    }

    private void Commit()
    {
        var current = Current();
        if (current is null)
            return;
        _owner.Store.Set(Item, current.ProductId, current.Bread);
        Refresh();
        _owner.UpdateCounts();
    }

    /// <summary>Η γραμμή όπως φαίνεται τώρα στην οθόνη — null όσο λείπει το προϊόν μέσα σε κατηγορία.</summary>
    private EfoodMatch? Current() =>
        _category is null ? null
        : _category.Category is null ? new EfoodMatch { EfoodId = Item.Id }
        : _product is null ? null
        : new EfoodMatch { EfoodId = Item.Id, ProductId = _product.Product.Id, Bread = ShowsBread(_product) && _bread != AnyBread ? _bread : "" };

    private bool ShowsBread(EfoodProductChoice? choice) =>
        choice is not null && _owner.Menu.OpensIngredients(choice.Product) && _owner.Menu.HasBreadChoice(choice.Category.Name);

    private void Refresh()
    {
        BreadVisible = ShowsBread(_product);
        var current = Current();
        var saved = _owner.Store.MatchFor(Item.Id);
        var state = _owner.Store.StateOf(Item);
        var same = current is not null && saved is not null && state != EfoodMatchState.Pending
                   && saved.ProductId == current.ProductId && (!BreadVisible || saved.Bread == current.Bread);

        NeedsConfirm = !same;
        CanConfirm = current is not null;
        Preview = current is null ? "διάλεξε προϊόν" : "τυπώνεται: " + EfoodOrderReader.Preview(Item, current, _owner.Menu);
        var posPrice = _product is null || _category?.Category is null ? (decimal?)null : _product.Product.DeliveryPrice ?? _product.Product.Price;
        PriceNote = posPrice is { } price && price != Item.Price ? "τιμή ταμείου " + Order.FormatPrice(price) : "";
        StateText = !same ? ""
            : current!.ProductId.Length == 0 ? "✓ όχι στο ταμείο"
            : state == EfoodMatchState.Auto ? "✓ αυτόματα"
            : "✓";
        Stripe = !same ? PendingBrush : current!.ProductId.Length == 0 ? NoneBrush : MatchedBrush;
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
