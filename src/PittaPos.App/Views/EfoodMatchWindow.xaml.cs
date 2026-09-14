using System.Windows;
using PittaPos.App.Services;
using PittaPos.App.ViewModels;
using PittaPos.Core.Efood;

namespace PittaPos.App.Views;

/// <summary>ΑΝΤΙΣΤΟΙΧΙΣΗ των προϊόντων του e-food με τα προϊόντα του ταμείου (βλ. EfoodMatchViewModel). Ανοίγει από
/// τις Ρυθμίσεις → E-FOOD, που θέλουν ήδη τον κωδικό του ιδιοκτήτη.</summary>
public partial class EfoodMatchWindow : Window
{
    public EfoodMatchWindow() : this(EfoodMatchStore.Instance, EfoodMenu.Live)
    {
    }

    public EfoodMatchWindow(EfoodMatchStore store, IEfoodMenu menu)
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        var vm = new EfoodMatchViewModel(store, menu);
        DataContext = vm;
        // Ο κατάλογος του e-food αλλάζει όποτε το μαγαζί προσθέτει κάτι εκεί — φέρνεται φρέσκος αν είναι πάνω από μία ώρα.
        Loaded += (_, _) =>
        {
            if (store.Catalog.Count == 0 || store.CatalogUpdated < DateTime.Now.AddHours(-1))
                vm.RefreshCatalogCommand.Execute(null);
        };
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}
