using System.Windows;
using PittaPos.App.Services;
using PittaPos.App.ViewModels;

namespace PittaPos.App.Views;

/// <summary>Πόσο και τι κρέας τρώει κάθε προϊόν, και τι έχει φύγει σήμερα — βλ. ConsumptionService.</summary>
public partial class ConsumptionWindow : Window
{
    public ConsumptionWindow()
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        DataContext = new ConsumptionViewModel();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}
