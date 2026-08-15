using System.Globalization;
using System.Text;

namespace PittaPos.App.Services;

/// <summary>
/// Κεφαλαία όπως γράφονται στα ελληνικά — δηλαδή ΧΩΡΙΣ τόνους. Το σκέτο <c>ToUpper</c> του .NET
/// κρατάει τον τόνο («Γιώργος» → «ΓΙΏΡΓΟΣ»), που δεν είναι σωστή γραφή και φαίνεται άσχημα και στην
/// απόδειξη. Τα διαλυτικά αντίθετα ΔΙΑΤΗΡΟΥΝΤΑΙ στα κεφαλαία («Μαυροΐδη» → «ΜΑΥΡΟΪΔΗ»), γιατί εκεί
/// αλλάζουν την προφορά.
/// </summary>
public static class GreekText
{
    public static string Upper(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var upper = text.ToUpperInvariant();
        var sb = new StringBuilder(upper.Length);
        foreach (var ch in upper)
        {
            // Ο τόνος μερικών γραμμάτων επιβιώνει του ToUpper σαν ξεχωριστός χαρακτήρας από πάνω
            // (π.χ. το «ΐ» γίνεται «Ϊ» + τόνος) — αυτοί πετιούνται εδώ, αλλιώς το κεφαλαίο βγαίνει
            // τονισμένο. Τα διαλυτικά ΔΕΝ είναι τέτοιος χαρακτήρας (είναι μέρος του «Ϊ»), δεν χάνονται.
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(ch switch
            {
                'Ά' => 'Α',
                'Έ' => 'Ε',
                'Ή' => 'Η',
                'Ί' => 'Ι',
                'Ό' => 'Ο',
                'Ύ' => 'Υ',
                'Ώ' => 'Ω',
                // Φωνήεν με τόνο ΚΑΙ διαλυτικά μαζί: μένουν μόνο τα διαλυτικά.
                'ΐ' => 'Ϊ',
                'ΰ' => 'Ϋ',
                _ => ch,
            });
        }
        return sb.ToString();
    }
}
