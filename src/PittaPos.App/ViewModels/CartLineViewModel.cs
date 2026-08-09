using CommunityToolkit.Mvvm.ComponentModel;
using PittaPos.Core.Models;

namespace PittaPos.App.ViewModels;

/// <summary>Γραμμή στο ticket της παραγγελίας.</summary>
public partial class CartLineViewModel : ObservableObject
{
    private readonly ProductsViewModel _owner;

    public CartLineViewModel(ProductsViewModel owner, string key, string productId, string name, decimal unitPrice)
    {
        _owner = owner;
        Key = key;
        ProductId = productId;
        Name = name;
        _unitPrice = unitPrice;
    }

    /// <summary>«p{productId}» για απλά tap, «s{n}» για γραμμές από customizer.</summary>
    public string Key { get; }
    public string ProductId { get; }

    /// <summary>Για customizable προϊόντα περιλαμβάνει τη συντομογραφία ψωμιού, π.χ. «ΠΙΤΤΑ ΚΟΤΟΠΟΥΛΟ
    /// (ΑΡ.)» — ξαναγράφεται σε κάθε ΠΡΟΣΘΗΚΗ/επεξεργασία (βλ. ProductsViewModel.CommitCustomizedLine),
    /// γι' αυτό είναι observable ώστε να ενημερώνεται η οθόνη όταν αλλάζει το ψωμί σε ήδη υπάρχουσα γραμμή.</summary>
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalLabel))]
    private int _quantity = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalLabel))]
    private decimal _unitPrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalLabel))]
    private int _discountPct;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalLabel))]
    private bool _noCharge;

    /// <summary>Ψωμί + σημείωση (πρώτη γκρίζα γραμμή).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDesc))]
    private string _descLine1 = "";

    /// <summary>Τροποποιήσεις — χωρίς/έξτρα/έκπτωση (δεύτερη κόκκινη γραμμή).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMods))]
    private string _descLine2 = "";

    /// <summary>Υπάρχει για customizable προϊόντα, ακόμη κι αν προστέθηκαν με απλό tap.</summary>
    public LineCustomization? Customization { get; set; }
    public bool CanEdit => Customization is not null;
    /// <summary>Βασική τιμή προϊόντος χωρίς έξτρα (για επανάνοιγμα customizer).</summary>
    public decimal BasePrice { get; set; }

    public bool HasDesc => DescLine1.Length > 0;
    public bool HasMods => DescLine2.Length > 0;

    public decimal Total => NoCharge ? 0m : Quantity * UnitPrice * (1 - DiscountPct / 100m);
    public string TotalLabel => Order.FormatPrice(Total);

    public decimal SubtotalContribution => Quantity * UnitPrice;

    partial void OnQuantityChanged(int value) => _owner.OnCartChanged();
    partial void OnUnitPriceChanged(decimal value) => _owner.OnCartChanged();
    partial void OnDiscountPctChanged(int value) => _owner.OnCartChanged();
    partial void OnNoChargeChanged(bool value) => _owner.OnCartChanged();

    public void Rename(string name)
    {
        Name = name;
        OnPropertyChanged(nameof(Name));
    }
}
