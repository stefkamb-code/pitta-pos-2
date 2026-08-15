using System.Windows;
using PittaPos.App.ViewModels;

using PittaPos.App.Services;

namespace PittaPos.App.Views;

public partial class CustomersWindow : Window
{
    public CustomersWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        DataContext = new CustomersViewModel();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}
