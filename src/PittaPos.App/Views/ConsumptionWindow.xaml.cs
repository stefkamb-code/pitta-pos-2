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
        var vm = new ConsumptionViewModel();
        DataContext = vm;
        // ΑΠΟΘΗΚΕΥΣΗ ΣΤΟ ΚΛΕΙΣΙΜΟ, χωρίς ερώτηση: εδώ μέσα συμπληρώνονται 145 γραμμές με το χέρι και
        // ένα κατά λάθος «←» τις πετούσε όλες σιωπηλά. Δεν μπαίνει παράθυρο «θες να αποθηκεύσεις;» —
        // ό,τι λύνεται μόνο του λύνεται σιωπηλά, και όλα τα νούμερα είναι ούτως ή άλλως ορατά και
        // διορθώσιμα την επόμενη φορά.
        Closed += (_, _) =>
        {
            vm.Save();
            vm.Detach();
        };
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}
