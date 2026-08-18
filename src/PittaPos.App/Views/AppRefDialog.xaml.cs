using System.Windows;
using System.Windows.Input;
using PittaPos.App.Services;

namespace PittaPos.App.Views;

/// <summary>
/// «Ποιος είναι ο κωδικός της παραγγελίας;» — ρωτιέται όταν μια ήδη καταχωρημένη παραγγελία
/// διορθώνεται σε e-food / Wolt / BOX από το Ιστορικό.
///
/// <para>Χωρίς αυτόν, η διορθωμένη παραγγελία συνέχιζε να δείχνει τη σειρά της βάρδιας («05») σαν να
/// ήταν κωδικός πλατφόρμας — και δεν βρισκόταν ΠΟΤΕ ψάχνοντας με τον αριθμό που δίνει η e-food, που
/// είναι ο μόνος αριθμός που ξέρει η πλατφόρμα.</para>
/// </summary>
public partial class AppRefDialog : Window
{
    /// <summary>Ο κωδικός που πληκτρολογήθηκε — κενό αν ο ταμίας το προσπέρασε.</summary>
    public string Reference { get; private set; } = "";

    /// <summary>Το BOX δέχεται και γράμματα, οι υπόλοιπες πλατφόρμες μόνο ψηφία — ίδιος κανόνας με το
    /// πεδίο της παραγγελιοληψίας (βλ. OrderWizardViewModel.FilterOrderRef).</summary>
    private readonly bool _lettersAllowed;

    private AppRefDialog(string platform, string current)
    {
        InitializeComponent();
        // Μαύρη μπάρα τίτλου μαζί με το θέμα, ΠΡΙΝ φανεί το παράθυρο (βλ. TitleBarTheme).
        TitleBarTheme.Attach(this);
        _lettersAllowed = platform == "BOX";
        SubText.Text = $"Ο αριθμός που δίνει η {platform}";
        RefBox.Text = current;
        RefBox.SelectAll();
        Loaded += (_, _) => RefBox.Focus();
    }

    /// <summary>Ρωτά και επιστρέφει τον κωδικό — κενό σημαίνει «άφησέ τον όπως ήταν».</summary>
    public static string Ask(Window owner, string platform, string current)
    {
        var dialog = new AppRefDialog(platform, current) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Reference;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Reference = RefBox.Text.Trim();
        DialogResult = true;
    }

    private void RefBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !_lettersAllowed && !e.Text.All(char.IsDigit);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
        else if (e.Key == Key.Enter)
            Ok_Click(sender, e);
    }
}
