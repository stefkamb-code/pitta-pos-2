using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>
/// «Πόσα άτομα;» — ρωτιέται μόλις ανοίξει τραπέζι, ίδια ερώτηση με το κινητό του σερβιτόρου.
///
/// Το μαγαζί κόβει τις αποδείξεις σε ξεχωριστή ταμειακή μηχανή και ο κανόνας είναι <b>ένα άτομο = μία
/// απόδειξη</b>. Η στιγμή του ανοίγματος είναι η σωστή: εκεί ξέρει ο ταμίας/σερβιτόρος αν πληρώνουν
/// μαζί ή χωριστά. Στο τέλος θα έπρεπε να το ξαναθυμηθεί.
/// </summary>
public partial class PersonsDialog : Window
{
    /// <summary>Πόσα άτομα διάλεξε — 1 σημαίνει «μαζί», δηλαδή μία απόδειξη.</summary>
    public int Persons { get; private set; } = 1;

    private PersonsDialog(int table)
    {
        InitializeComponent();
        TitleText.Text = $"ΤΡΑΠΕΖΙ {table} — πόσα άτομα;";
    }

    /// <summary>
    /// Ρωτά και καταγράφει. Επιστρέφει το πλήθος· αν ο ταμίας πατήσει Escape/κλείσει, θεωρείται
    /// «μαζί» και <b>δεν μπλοκάρει</b> την παραγγελία — το χειρότερο που μπορεί να κάνει αυτή η
    /// ερώτηση είναι να σταματήσει τη δουλειά σε ώρα αιχμής.
    /// </summary>
    public static int Ask(Window owner, int table)
    {
        var dialog = new PersonsDialog(table) { Owner = owner };
        dialog.ShowDialog();
        TablePersonsService.Instance.SetCount(table, dialog.Persons);
        return dialog.Persons;
    }

    private void Pick(int persons)
    {
        Persons = Math.Clamp(persons, 1, TablePersonsService.MaxPersons);
        DialogResult = true;
    }

    private void Number_Click(object sender, RoutedEventArgs e) =>
        Pick(int.Parse((string)((Button)sender).Content));

    private void Together_Click(object sender, RoutedEventArgs e) => Pick(1);

    private void More_Click(object sender, RoutedEventArgs e)
    {
        Numbers.Visibility = Visibility.Collapsed;
        MorePanel.Visibility = Visibility.Visible;
        MoreBox.Focus();
    }

    private void MoreOk_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(MoreBox.Text, out var n) && n > 0)
            Pick(n);
    }

    /// <summary>Μόνο ψηφία — το πεδίο δέχεται πλήθος ατόμων, όχι κείμενο.</summary>
    private void MoreBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            return;
        }
        if (MorePanel.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Enter)
                MoreOk_Click(sender, e);
            return;
        }
        // Πληκτρολόγιο για ταχύτητα: 1-9 κατευθείαν, Enter = μαζί.
        if (e.Key is >= Key.D1 and <= Key.D9)
            Pick(e.Key - Key.D0);
        else if (e.Key is >= Key.NumPad1 and <= Key.NumPad9)
            Pick(e.Key - Key.NumPad0);
        else if (e.Key == Key.Enter)
            Pick(1);
    }
}
