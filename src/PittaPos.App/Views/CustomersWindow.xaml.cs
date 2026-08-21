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
        var vm = new CustomersViewModel();
        DataContext = vm;
        Closed += (_, _) => vm.Detach();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}
