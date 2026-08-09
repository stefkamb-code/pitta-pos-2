using System.Windows;
using PittaPos.App.ViewModels;

namespace PittaPos.App.Views;

/// <summary>Διαχείριση του κοινού καταλόγου έξτρα (προσθήκη/τιμή/διαγραφή) — μοιράζεται το ίδιο
/// MenuManagerViewModel με το γονικό παράθυρο, ώστε οι αλλαγές να φαίνονται αμέσως και στη φόρμα προϊόντος.</summary>
public partial class ManageExtrasWindow : Window
{
    public ManageExtrasWindow(MenuManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}
