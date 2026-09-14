using System.Windows;
using System.Windows.Input;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>Μία ερώτηση στη μέση της οθόνης με ΕΝΑ κουμπί ενέργειας — κλείσιμο ή Esc = άκυρο. Για ενέργειες που δεν
/// γυρίζουν πίσω με ένα πάτημα, όπως η αντικατάσταση του καταλόγου στο e-food.</summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string text, string action)
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        Title = title;
        TitleText.Text = title;
        BodyText.Text = text;
        ActionButton.Content = action;
    }

    /// <summary>true μόνο αν πατήθηκε το κουμπί της ενέργειας.</summary>
    public static bool Ask(Window owner, string title, string text, string action) =>
        new ConfirmDialog(title, text, action) { Owner = owner }.ShowDialog() == true;

    private void Action_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
