using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PittaPos.App.ViewModels;

namespace PittaPos.App.Views;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Διπλό κλικ σε σειρά προϊόντος — μπαίνει κατευθείαν στο δελτίο με τα προεπιλεγμένα του.
    ///
    /// Γίνεται από code-behind και όχι με MouseBinding στο XAML επειδή το Button «τρώει» το πάτημα
    /// του ποντικιού για τον δικό του μηχανισμό Click, οπότε ένα InputBinding πάνω του δεν θα
    /// πυροδοτούσε αξιόπιστα. Το πρώτο από τα δύο κλικ έχει ήδη ανοίξει τα υλικά δίπλα· η εντολή
    /// τα κλείνει μόνη της.
    /// </summary>
    private void ProductRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProductTileViewModel tile }
            && DataContext is ProductsViewModel vm
            && vm.QuickAddProductCommand.CanExecute(tile))
        {
            vm.QuickAddProductCommand.Execute(tile);
            e.Handled = true;
        }
    }
}
