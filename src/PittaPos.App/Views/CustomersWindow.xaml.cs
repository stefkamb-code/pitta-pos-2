using System.Windows;
using PittaPos.App.ViewModels;

namespace PittaPos.App.Views;

public partial class CustomersWindow : Window
{
    public CustomersWindow()
    {
        InitializeComponent();
        DataContext = new CustomersViewModel();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}
