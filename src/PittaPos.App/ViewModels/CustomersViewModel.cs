using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PittaPos.App.Services;
using PittaPos.Core.Models;

namespace PittaPos.App.ViewModels;

/// <summary>Αγαπημένο προϊόν πελάτη στη λίστα λεπτομερειών.</summary>
public class FavoriteProductViewModel
{
    public required int Rank { get; init; }
    public required string Name { get; init; }
    public required int Count { get; init; }

    public string RankLabel => Rank + ".";
    public string CountLabel => "× " + Count;
}

/// <summary>Γραμμή πελάτη στη λίστα.</summary>
public class CustomerRowViewModel
{
    public required Customer Customer { get; init; }

    public string Name => Customer.Name.Length > 0 ? Customer.Name : "—";
    public string Phone => Customer.Phone;
    public string OrdersLabel => Customer.OrderCount + (Customer.OrderCount == 1 ? " παραγγελία" : " παραγγελίες");
    public string RevenueLabel => Order.FormatPrice(Customer.TotalRevenue);
}

/// <summary>Λίστα πελατών με μόνιμο ιστορικό ζωής.</summary>
public partial class CustomersViewModel : ObservableObject
{
    public CustomersViewModel()
    {
        Refresh();
        CustomerStore.Instance.Changed += Refresh;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Customers))]
    private string _search = "";

    /// <summary>"revenue" ή "frequency".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Customers))]
    [NotifyPropertyChangedFor(nameof(SortByRevenue))]
    [NotifyPropertyChangedFor(nameof(SortByFrequency))]
    private string _sortMode = "revenue";

    public bool SortByRevenue => SortMode == "revenue";
    public bool SortByFrequency => SortMode == "frequency";

    [RelayCommand]
    private void SetSort(string mode) => SortMode = mode;

    private List<Customer> _all = [];

    public IReadOnlyList<CustomerRowViewModel> Customers
    {
        get
        {
            var filtered = _all.Where(c => CustomerStore.Matches(c, Search));
            var sorted = SortMode == "frequency"
                ? filtered.OrderBy(AvgIntervalDays).ThenByDescending(c => c.OrderCount)
                : filtered.OrderByDescending(c => c.TotalRevenue).ThenByDescending(c => c.OrderCount);
            return sorted.Select(c => new CustomerRowViewModel { Customer = c }).ToList();
        }
    }

    /// <summary>Μέσος αριθμός ημερών ανάμεσα στις παραγγελίες· άπειρο αν λείπει ιστορικό (πάει τελευταίος).</summary>
    private static double AvgIntervalDays(Customer c) =>
        c.OrderCount >= 2 && c.FirstOrderAt is { } f && c.LastOrderAt is { } l
            ? (l - f).TotalDays / (c.OrderCount - 1)
            : double.MaxValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelected))]
    [NotifyPropertyChangedFor(nameof(NothingSelected))]
    [NotifyPropertyChangedFor(nameof(SelectedName))]
    [NotifyPropertyChangedFor(nameof(SelectedContact))]
    [NotifyPropertyChangedFor(nameof(OrderCountLabel))]
    [NotifyPropertyChangedFor(nameof(RevenueLabel))]
    [NotifyPropertyChangedFor(nameof(FrequencyLabel))]
    [NotifyPropertyChangedFor(nameof(FirstOrderLabel))]
    [NotifyPropertyChangedFor(nameof(LastOrderLabel))]
    [NotifyPropertyChangedFor(nameof(Favorites))]
    [NotifyPropertyChangedFor(nameof(NotesLabel))]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    [NotifyPropertyChangedFor(nameof(OtherAddresses))]
    [NotifyPropertyChangedFor(nameof(HasOtherAddresses))]
    private Customer? _selected;

    partial void OnSelectedChanged(Customer? value)
    {
        Memo = value?.Memo ?? "";
        MemoSaved = false;
    }

    /// <summary>Υπενθύμιση υπό επεξεργασία για τον επιλεγμένο πελάτη.</summary>
    [ObservableProperty]
    private string _memo = "";

    [ObservableProperty]
    private bool _memoSaved;

    [RelayCommand]
    private void SaveMemo()
    {
        if (Selected is null)
            return;
        CustomerStore.Instance.SetMemo(Selected, Memo);
        MemoSaved = true;
    }

    public bool HasSelected => Selected is not null;
    public bool NothingSelected => Selected is null && _all.Count > 0;
    public bool NoCustomers => _all.Count == 0;

    public string SelectedName => Selected?.Name ?? "";
    public string SelectedContact => Selected is null ? "" : string.Join(" · ",
        new[] { Selected.Phone, Selected.Address, Selected.Area, Selected.Floor }.Where(s => s.Length > 0));

    public string OrderCountLabel => Selected?.OrderCount.ToString() ?? "0";
    public string RevenueLabel => Order.FormatPrice(Selected?.TotalRevenue ?? 0);

    /// <summary>Μέσος όρος χρόνου ανάμεσα στις παραγγελίες του πελάτη.</summary>
    public string FrequencyLabel
    {
        get
        {
            if (Selected is null || Selected.OrderCount < 2
                || Selected.FirstOrderAt is null || Selected.LastOrderAt is null)
                return "—";
            var avg = (Selected.LastOrderAt.Value - Selected.FirstOrderAt.Value).TotalDays
                      / (Selected.OrderCount - 1);
            var days = Math.Max(1, (int)Math.Round(avg));
            return "κάθε " + days + (days == 1 ? " ημέρα" : " ημέρες");
        }
    }

    public string FirstOrderLabel => Selected?.FirstOrderAt?.ToString("dd/MM/yyyy") ?? "—";
    public string LastOrderLabel => Selected?.LastOrderAt?.ToString("dd/MM/yyyy") ?? "—";

    public string NotesLabel => Selected?.Notes ?? "";
    public bool HasNotes => (Selected?.Notes.Length ?? 0) > 0;

    public IReadOnlyList<FavoriteProductViewModel> Favorites =>
        Selected is null
            ? []
            : Selected.ProductCounts
                .OrderByDescending(p => p.Value)
                .Take(10)
                .Select((p, i) => new FavoriteProductViewModel { Rank = i + 1, Name = p.Key, Count = p.Value })
                .ToList();

    [RelayCommand]
    private void SelectCustomer(CustomerRowViewModel row) => Selected = row.Customer;

    /// <summary>Πρόσθετες διευθύνσεις του επιλεγμένου πελάτη (πέρα από την κύρια) — π.χ. δουλειά,
    /// μια περιστασιακή. Ο admin μπορεί να σβήσει όποια δεν ισχύει πια (μετακόμιση κ.λπ.).</summary>
    public IReadOnlyList<CustomerAddress> OtherAddresses => Selected?.OtherAddresses ?? [];
    public bool HasOtherAddresses => OtherAddresses.Count > 0;

    [RelayCommand]
    private void DeleteOtherAddress(CustomerAddress address)
    {
        if (Selected is null)
            return;
        CustomerStore.Instance.RemoveOtherAddress(Selected, address);
        OnPropertyChanged(nameof(OtherAddresses));
        OnPropertyChanged(nameof(HasOtherAddresses));
    }

    private void Refresh()
    {
        _all = CustomerStore.Instance.All.ToList();
        OnPropertyChanged(nameof(Customers));
        OnPropertyChanged(nameof(NoCustomers));
        OnPropertyChanged(nameof(NothingSelected));
    }
}
