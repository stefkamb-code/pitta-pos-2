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

/// <summary>Προϊόν στην κάθετη λίστα της κατηγορίας. Η τιμή έρχεται ήδη λυμένη (μαγαζιού ή διανομής).</summary>
public partial class ProductTileViewModel(Product product, bool customizable, decimal price) : ObservableObject
{
    public Product Product { get; } = product;
    public bool Customizable { get; } = customizable;
    public decimal Price { get; } = price;

    public string Name => Product.Name;

    /// <summary>
    /// Το όνομα όπως μπαίνει στη λίστα. Όταν ανοίγουν τα υλικά δίπλα, η λίστα στενεύει και τα μακριά
    /// ονόματα έσπαγαν σε δεύτερη σειρά — οι σειρές έβγαιναν άνισες και χανόταν ο ρυθμός του ματιού.
    /// Γι' αυτό κονταίνουν οι ενδιάμεσες λέξεις: «Πίττα μπιφτέκι λαχανικών» → «Πίττα μπιφτ. λαχανικών».
    /// </summary>
    public string ShortName => Shorten(Product.Name);

    /// <summary>Ολόκληρο το όνομα στο tooltip — τίποτα δεν χάνεται επειδή κόπηκε στη λίστα.
    /// Η περιγραφή δεν δείχνεται πια πουθενά: το μαγαζί δεν τη χρειάζεται και γέμιζε την οθόνη.</summary>
    public string ListToolTip => Name;

    /// <summary>
    /// Κονταίνει λέξεις μέχρι να χωρέσει το όνομα σε μία σειρά. Η πρώτη λέξη μένει ακέραιη (είναι
    /// σχεδόν πάντα το είδος — «Πίττα», «Μερίδα») και η τελευταία επίσης (είναι αυτή που ξεχωρίζει
    /// το προϊόν από τα διπλανά του), οπότε κόβονται μόνο οι ενδιάμεσες.
    /// </summary>
    private static string Shorten(string name, int maxChars = 22)
    {
        if (name.Length <= maxChars)
            return name;

        var words = name.Split(' ');
        for (var i = 1; i < words.Length - 1 && string.Join(" ", words).Length > maxChars; i++)
        {
            if (words[i].Length > 6)
                words[i] = words[i][..5] + ".";
        }

        var shortened = string.Join(" ", words);
        if (shortened.Length <= maxChars)
            return shortened;

        // Κόβεται η ΜΕΣΗ, όχι το τέλος. Τα ονόματα εδώ ξεχωρίζουν από την ουρά τους: «Πατάτες με τυρί
        // & μπέικον» και «Πατάτες με τυρ, μπέικ. & μανιτάρια» έχουν ίδια αρχή, οπότε κόβοντας από πίσω
        // έβγαιναν και τα δύο «Πατάτες με τυρ…» — ο ταμίας δεν μπορούσε να τα ξεχωρίσει καθόλου.
        const int tail = 12;
        var head = maxChars - tail - 1;
        return shortened[..head] + "…" + shortened[^tail..];
    }

    public string NameEn => Product.NameEn ?? "";
    public bool ShowEn => !string.IsNullOrEmpty(Product.NameEn);
    /// <summary>null όταν δεν υπάρχει περιγραφή, ώστε να μην εμφανίζεται κενό tooltip.</summary>
    public string? Description => string.IsNullOrEmpty(Product.Description) ? null : Product.Description;
    public string PriceLabel => Order.FormatPrice(Price);

    /// <summary>Πόσα τεμάχια αυτού του προϊόντος έχουν μπει στο δελτίο — badge στη σειρά.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQty))]
    private int _quantity;

    public bool HasQty => Quantity > 0;

    /// <summary>Το προϊόν του οποίου τα υλικά είναι ανοιχτά αυτή τη στιγμή στη διπλανή στήλη. Η λίστα
    /// δεν φεύγει πια από την οθόνη όταν ανοίγει ο customizer, οπότε χρειάζεται σημάδι για να φαίνεται
    /// με ποια σειρά αντιστοιχεί ό,τι βλέπεις δίπλα.</summary>
    [ObservableProperty]
    private bool _isOpen;
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
