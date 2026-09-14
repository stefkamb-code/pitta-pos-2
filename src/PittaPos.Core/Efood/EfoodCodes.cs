using System.Security.Cryptography;
using System.Text;

namespace PittaPos.Core.Efood;

/// <summary>
/// Οι κωδικοί (SKU) που στέλνει το ταμείο στο e-food μαζί με τον κατάλογο — και που γυρίζουν αυτούσιοι σε κάθε
/// παραγγελία ως <c>integrator_id</c>, ώστε να ξέρουμε ΑΚΡΙΒΩΣ ποιο προϊόν και ποια επιλογή διάλεξε ο πελάτης,
/// χωρίς καμία αντιστοίχιση με το χέρι.
///
/// <code>
/// κατηγορία  «synodeytika»              το Id της κατηγορίας, αυτούσιο
/// προϊόν     «syn1»                     το Id του προϊόντος, αυτούσιο
/// ομάδα      «pit3__bread»              bread (ψωμί) · ingr (χωρίς υλικό) · double (διπλή πίτα) · extra (έξτρα)
/// επιλογή    «pit3__extra_3f2a91c0»     + τα 8 πρώτα hex του SHA-256 του ονόματος
///            «pit3__double»             (μία μόνο επιλογή, χωρίς όνομα)
/// </code>
///
/// <para><b>Το όνομα μέσα στον κωδικό, ΟΧΙ η θέση.</b> Ο κατάλογος του e-food είναι φωτογραφία της στιγμής που
/// στάλθηκε, ενώ ο ταμίας αλλάζει τη σειρά των έξτρα με σύρσιμο: με αριθμό θέσης, ένα «+ Μπέικον» παραγγελμένο πάνω
/// στον παλιό κατάλογο θα διαβαζόταν ως κάποιο άλλο έξτρα. Με το όνομα η σειρά δεν παίζει ρόλο — και ένα
/// μετονομασμένο έξτρα απλώς δεν βρίσκεται, οπότε τυπώνεται με το όνομα που ήρθε.</para>
/// </summary>
public static class EfoodCodes
{
    public const string Bread = "bread";
    public const string Ingredient = "ingr";
    public const string DoublePita = "double";
    public const string Extra = "extra";

    private const string Separator = "__";

    public static string Tier(string productId, string kind) => productId + Separator + kind;

    public static string Option(string productId, string kind, string name) =>
        kind == DoublePita ? productId + Separator + DoublePita : productId + Separator + kind + "_" + Hash(name);

    /// <summary>Διαβάζει κωδικό επιλογής — null αν δεν είναι δικός μας (π.χ. επιλογή που πρόσθεσε το ίδιο το e-food).</summary>
    public static (string ProductId, string Kind, string Hash)? ParseOption(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        // Το ΤΕΛΕΥΤΑΙΟ «__»: ό,τι ακολουθεί (είδος + hex) δεν έχει ποτέ «__», ενώ ένα Id προϊόντος θα μπορούσε.
        var at = code.LastIndexOf(Separator, StringComparison.Ordinal);
        if (at <= 0)
            return null;
        var productId = code[..at];
        var rest = code[(at + Separator.Length)..];
        if (rest == DoublePita)
            return (productId, DoublePita, "");
        var underscore = rest.IndexOf('_');
        if (underscore <= 0)
            return null;
        var kind = rest[..underscore];
        var hash = rest[(underscore + 1)..];
        return kind is Bread or Ingredient or Extra && hash.Length == 8 ? (productId, kind, hash) : null;
    }

    public static string Hash(string name) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.Trim())))[..8].ToLowerInvariant();
}
