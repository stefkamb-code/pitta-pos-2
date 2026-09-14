using PittaPos.Core.Models;

namespace PittaPos.Core.Efood;

/// <summary>
/// Ό,τι χρειάζεται από τον κατάλογο του ταμείου για να σταλεί στο e-food και για να διαβαστούν πίσω οι παραγγελίες
/// του — με ΤΟΥΣ ΙΔΙΟΥΣ κανόνες που ακολουθεί ο customizer του ταμείου (ψωμί, βασικά υλικά, έξτρα, διπλή πίτα).
/// Στο ταμείο το υλοποιεί ο ζωντανός κατάλογος (MenuStore)· στις δοκιμές, ο MenuSeed.
/// </summary>
public interface IEfoodMenu
{
    IReadOnlyList<MenuCategory> Categories { get; }

    /// <summary>Ο κοινός κατάλογος έξτρα, με τις τιμές τους.</summary>
    IReadOnlyList<ExtraItem> Extras { get; }

    /// <summary>Αν το προϊόν ανοίγει ψωμί/υλικά/έξτρα όταν το πατάς (MenuStore.OpensIngredients).</summary>
    bool OpensIngredients(Product product);

    /// <summary>Τα βασικά υλικά του προϊόντος — αυτά που μπορούν να «βγουν» (MenuStore.IngredientsFor).</summary>
    IReadOnlyList<string> IngredientsFor(Product product);

    bool HasBreadChoice(string categoryName);
    bool FuseBreadIntoName(string categoryName);
    bool SupportsDoublePita(string categoryName);
    decimal DoublePitaPriceFor(string categoryName);
}

public static class EfoodMenuRules
{
    /// <summary>
    /// Τα έξτρα που προσφέρει ένα προϊόν, με τη σειρά του προϊόντος — ακριβώς όπως τα δείχνει ο customizer του
    /// ταμείου (CustomizerViewModel): μόνο στα «τροποποιήσιμα»· όλα όταν το προϊόν δεν έχει δική του λίστα, αλλιώς
    /// όσα ονόματα της λίστας του υπάρχουν στον κοινό κατάλογο. Διπλό όνομα μετράει μία φορά.
    /// </summary>
    public static IReadOnlyList<ExtraItem> ExtrasFor(Product product, IReadOnlyList<ExtraItem> catalogue)
    {
        if (!product.Customizable)
            return [];
        var chosen = product.ExtraNames is null
            ? catalogue
            : product.ExtraNames.Select(n => catalogue.FirstOrDefault(e => e.Name == n)).OfType<ExtraItem>();
        return chosen.DistinctBy(e => e.Name).ToList();
    }
}
