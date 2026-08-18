using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PittaPos.App.Controls;

/// <summary>
/// Μεγεθύνει ΟΛΟ το περιεχόμενο μαζί με το παράθυρο — γράμματα, κουμπιά, αποστάσεις στην ίδια
/// αναλογία — αλλά, σε αντίθεση με το Viewbox, <b>γεμίζει τον χώρο</b>.
///
/// <para>Το Viewbox ζωγραφίζει ένα χαρτί σταθερών διαστάσεων και το μεγαλώνει όσο χωράει: σε πλήρη
/// οθόνη έμεναν κενές λωρίδες δεξιά κι αριστερά, γιατί κρατούσε το αρχικό σχήμα του χαρτιού. Εδώ ο
/// συντελεστής μεγέθυνσης βγαίνει το ίδιο (η μικρότερη από τις δύο αναλογίες, ώστε τίποτα να μην
/// παραμορφώνεται), αλλά ο επιπλέον χώρος <b>δίνεται στο περιεχόμενο</b>: οι στήλες και οι λίστες
/// απλώνουν, όπως θα έκαναν σε ένα κανονικό παράθυρο.</para>
///
/// <para>Δηλαδή: μεγαλώνοντας το παράθυρο, τα γράμματα γίνονται μεγαλύτερα ΚΑΙ χωράνε περισσότερα —
/// αντί για ένα μικρό αντίγραφο στη μέση της οθόνης.</para>
/// </summary>
public class ScaleHost : Decorator
{
    /// <summary>Το μέγεθος για το οποίο είναι σχεδιασμένο το περιεχόμενο. Σε αυτό το μέγεθος ο
    /// συντελεστής είναι 1 και όλα φαίνονται όπως γράφτηκαν στο XAML.</summary>
    public double DesignWidth { get; set; } = 1264;

    public double DesignHeight { get; set; } = 761;

    private readonly ScaleTransform _scale = new(1, 1);

    protected override Size MeasureOverride(Size constraint)
    {
        // FrameworkElement και όχι σκέτο UIElement: το LayoutTransform ζει εκεί.
        if (Child is not FrameworkElement child)
            return default;

        // Άπειρη διάσταση (π.χ. μέσα σε ScrollViewer) δεν δίνει συντελεστή — τότε μένουμε στο 1:1.
        var width = double.IsInfinity(constraint.Width) ? DesignWidth : constraint.Width;
        var height = double.IsInfinity(constraint.Height) ? DesignHeight : constraint.Height;

        var scale = Math.Min(width / DesignWidth, height / DesignHeight);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            scale = 1;

        if (_scale.ScaleX != scale)
        {
            _scale.ScaleX = _scale.ScaleY = scale;
            // Ο μετασχηματισμός μπαίνει ΣΤΟ ΠΑΙΔΙ ως LayoutTransform: έτσι το WPF του δίνει αυτόματα
            // τον χώρο διαιρεμένο με τον συντελεστή, δηλαδή περισσότερες «μονάδες σχεδίασης» όσο
            // μεγαλώνει το παράθυρο. Ένα RenderTransform θα το ζωγράφιζε μεγάλο χωρίς να του δώσει
            // παραπάνω χώρο — ακριβώς η συμπεριφορά του Viewbox που θέλουμε να φύγει.
            child.LayoutTransform = _scale;
        }

        child.Measure(constraint);
        return new Size(
            double.IsInfinity(constraint.Width) ? child.DesiredSize.Width : constraint.Width,
            double.IsInfinity(constraint.Height) ? child.DesiredSize.Height : constraint.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
