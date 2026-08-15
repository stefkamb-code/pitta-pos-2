using System.Windows;
using System.Windows.Input;
using PittaPos.App.ViewModels;

using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>Διαχείριση του κοινού καταλόγου έξτρα (προσθήκη/τιμή/διαγραφή) — μοιράζεται το ίδιο
/// MenuManagerViewModel με το γονικό παράθυρο, ώστε οι αλλαγές να φαίνονται αμέσως και στη φόρμα προϊόντος.</summary>
public partial class ManageExtrasWindow : Window
{
    public ManageExtrasWindow(MenuManagerViewModel viewModel)
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        DataContext = viewModel;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Σειρά έξτρα με σύρσιμο από τη λαβή «⠿» — ίδιος μηχανισμός με τη λίστα προϊόντων.</summary>
    private void ExtraDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ExtraCatalogRowViewModel dragged)
            return;
        e.Handled = true; // να μη φτάσει το πάτημα και στο πεδίο τιμής της γραμμής
        DragDrop.DoDragDrop((FrameworkElement)sender, dragged, DragDropEffects.Move);
    }

    private void ExtraRow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ExtraCatalogRowViewModel)) is not ExtraCatalogRowViewModel dragged)
            return;
        if (((FrameworkElement)sender).DataContext is not ExtraCatalogRowViewModel target)
            return;
        (DataContext as MenuManagerViewModel)?.MoveExtraTo(dragged, target);
    }
}
