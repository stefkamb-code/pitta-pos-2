using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.Core.Models;

namespace PittaPos.App.ViewModels;

/// <summary>Κατηγορία στο αριστερό rail.</summary>
public partial class CategoryItemViewModel(MenuCategory category) : ObservableObject
{
    public MenuCategory Category { get; } = category;
    public string Name => Category.Name;

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>Tile προϊόντος στο κεντρικό πλέγμα. Η τιμή έρχεται ήδη λυμένη (μαγαζιού ή διανομής).</summary>
public partial class ProductTileViewModel(Product product, bool customizable, decimal price) : ObservableObject
{
    public Product Product { get; } = product;
    public bool Customizable { get; } = customizable;
    public decimal Price { get; } = price;

    public string Name => Product.Name;
    public string NameEn => Product.NameEn ?? "";
    public bool ShowEn => !string.IsNullOrEmpty(Product.NameEn);
    /// <summary>null όταν δεν υπάρχει περιγραφή, ώστε να μην εμφανίζεται κενό tooltip.</summary>
    public string? Description => string.IsNullOrEmpty(Product.Description) ? null : Product.Description;
    public string PriceLabel => Order.FormatPrice(Price);

    /// <summary>Ποσότητα της απλής (μη-customized) γραμμής στο καλάθι — badge στο tile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQty))]
    private int _quantity;

    public bool HasQty => Quantity > 0;
}

/// <summary>Επιλογή ψωμιού στο segmented control του customizer.</summary>
public partial class BreadOptionViewModel : ObservableObject
{
    private readonly CustomizerViewModel _owner;

    public BreadOptionViewModel(CustomizerViewModel owner, string name)
    {
        _owner = owner;
        Name = name;
    }

    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => _owner.SelectBread(Name);
}

/// <summary>Περιλαμβανόμενο υλικό — τάπα για αφαίρεση.</summary>
public partial class IngredientViewModel : ObservableObject
{
    private readonly CustomizerViewModel _owner;

    public IngredientViewModel(CustomizerViewModel owner, string name)
    {
        _owner = owner;
        Name = name;
    }

    public string Name { get; }

    [ObservableProperty]
    private bool _isRemoved;

    partial void OnIsRemovedChanged(bool value) => _owner.OnCustomizationChanged();

    [RelayCommand]
    private void Toggle() => IsRemoved = !IsRemoved;
}

/// <summary>Έξτρα υλικό με ποσότητα 0–2.</summary>
public partial class ExtraViewModel : ObservableObject
{
    private readonly CustomizerViewModel _owner;

    public ExtraViewModel(CustomizerViewModel owner, ExtraItem item)
    {
        _owner = owner;
        Item = item;
    }

    public ExtraItem Item { get; }
    public string Name => Item.Name;
    public string PriceLabel => Item.Price > 0 ? Order.FormatPrice(Item.Price) : "δωρεάν";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(QtyLabel))]
    private int _quantity;

    public bool IsActive => Quantity > 0;
    public string QtyLabel => Quantity + "×";

    partial void OnQuantityChanged(int value) => _owner.OnCustomizationChanged();

    [RelayCommand]
    private void Inc() => Quantity = Math.Min(10, Quantity + 1);

    [RelayCommand]
    private void Dec() => Quantity = Math.Max(0, Quantity - 1);

    /// <summary>Κλικ στη σειρά ανενεργού έξτρα το προσθέτει.</summary>
    [RelayCommand]
    private void Tap()
    {
        if (!IsActive)
            Quantity = 1;
    }
}
