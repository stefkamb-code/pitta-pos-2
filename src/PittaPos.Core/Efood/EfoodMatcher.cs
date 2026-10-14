using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PittaPos.Core.Data;
using PittaPos.Core.Models;

namespace PittaPos.Core.Efood;

/// <summary>
/// Μία γραμμή της ΑΝΤΙΣΤΟΙΧΙΣΗΣ: προϊόν του e-food → προϊόν του ταμείου, και με ποιο ψωμί. Αποθηκεύεται στο ταμείο·
/// ο κατάλογος του e-food δεν αλλάζει ποτέ.
/// </summary>
public sealed class EfoodMatch
{
    /// <summary>Ο κωδικός του προϊόντος στο e-food (π.χ. «IT_000000002449») — ο ίδιος και όταν το e-food δώσει
    /// άλλον αριθμό στο προϊόν· με αυτόν η γραμμή ξαναβρίσκει το προϊόν της (βλ. <see cref="EfoodMatchKeeper"/>).</summary>
    public string EfoodCode { get; set; } = "";

    /// <summary>Ο αριθμός του προϊόντος στο e-food — το κλειδί της γραμμής: αυτόν φέρνει κάθε παραγγελία ως integrator_id.</summary>
    public string EfoodId { get; set; } = "";

    public string EfoodName { get; set; } = "";
    public string EfoodCategory { get; set; } = "";

    /// <summary>Το προϊόν του ταμείου — κενό = «δεν υπάρχει στο ταμείο», οπότε τυπώνεται με το όνομα του e-food.</summary>
    public string ProductId { get; set; } = "";

    /// <summary>Το όνομα του προϊόντος του ταμείου τη στιγμή της απόφασης — για να ξαναβρεθεί αν το προϊόν φτιαχτεί
    /// ξανά με άλλο id (σβήσιμο και ξαναπέρασμα στη Διαχείριση Καταλόγου).</summary>
    public string ProductName { get; set; } = "";

    /// <summary>Το ψωμί που ΕΙΝΑΙ το προϊόν του e-food (π.χ. «Αραβική» για «Αραβική πίττα κοτόπουλο») — κενό = όποιο
    /// διαλέξει ο πελάτης, αλλιώς η προεπιλογή του ταμείου.</summary>
    public string Bread { get; set; } = "";

    /// <summary>Το είδε και το επιβεβαίωσε άνθρωπος («Σωστό»). Οι αυτόματες με ίδια τιμή δεν το χρειάζονται.</summary>
    public bool Confirmed { get; set; }
}

/// <summary>Πού βρίσκεται μια γραμμή της ΑΝΤΙΣΤΟΙΧΙΣΗΣ.</summary>
public enum EfoodMatchState
{
    /// <summary>Θέλει ματιά: καμία γραμμή, ή το προϊόν του ταμείου δεν υπάρχει πια — τυπώνεται με το όνομα του e-food.</summary>
    Pending,
    /// <summary>Αυτόματα, με τις ίδιες λέξεις και ίδια τιμή.</summary>
    Auto,
    /// <summary>Το επιβεβαίωσε άνθρωπος — και το «δεν υπάρχει στο ταμείο».</summary>
    Confirmed,
}

/// <summary>Αυτόματη πρόταση για ένα προϊόν του e-food.</summary>
/// <param name="SamePrice">Ίδια τιμή με την τιμή εφαρμογών του ταμείου — αλλιώς θέλει ματιά από άνθρωπο (π.χ. η
/// «Λαχανικών» του νηστίσιμου μενού δεν είναι η σαλάτα «Λαχανικών» του ταμείου).</param>
/// <param name="Sure">ΟΛΕΣ οι λέξεις ταιριάζουν και από τις δύο μεριές (με όποια σειρά, χωρίς τόνους/κεφαλαία/πτώσεις,
/// ελληνικά ή λατινικά). Μόνο τότε, και με ίδια τιμή, γράφεται μόνη της· αλλιώς είναι το πιο κοντινό προϊόν του
/// ταμείου, που φαίνεται έτοιμο στην οθόνη για να το επιβεβαιώσει ή να το αλλάξει άνθρωπος.</param>
public sealed record EfoodSuggestion(Product Product, MenuCategory Category, string Bread, bool SamePrice, bool Sure = true);

/// <summary>
/// Προτείνει ποιο προϊόν του ΤΑΜΕΙΟΥ ΤΟΥ ΜΑΓΑΖΙΟΥ (ο ζωντανός κατάλογος, όχι κάποιο έτοιμο μενού) είναι ένα προϊόν
/// του e-food, συγκρίνοντας ΛΕΞΕΙΣ:
/// <list type="bullet">
/// <item>με όποια σειρά («Κοτόπουλο μερίδα» = «Μερίδα κοτόπουλο»), χωρίς τόνους, κεφαλαία, σημεία στίξης, διπλά
/// γράμματα («Πίττα» = «Πίτα») και καταλήξεις («Πατάτες» = «Πατάτα», «Μπιφτέκια» = «Μπιφτέκι»)·</item>
/// <item>ελληνικά με λατινικά και μικρά ορθογραφικά («Burger» = «Μπέργκερ», «Coca-Cola» = «Κόκα Κόλα»)·</item>
/// <item>ποσότητες ως αριθμοί («330ml» = «330 ml» = «0,33 lt»), και ποτέ δύο διαφορετικές («330ml» ≠ «500ml»)·</item>
/// <item>το ψωμί μέσα στο όνομα του e-food («Αραβική πίττα κοτόπουλο», «Σάντουιτς κοτόπουλο») γίνεται το ψωμί της
/// γραμμής, σε κατηγορίες του ταμείου με επιλογή ψωμιού·</item>
/// <item>οι σπάνιες λέξεις του μενού του μαγαζιού μετράνε περισσότερο από τις κοινές («κοτόπουλο» > «πίττα»).</item>
/// </list>
/// Όσα δεν βγαίνουν σίγουρα, τα αποφασίζει άνθρωπος στην οθόνη ΑΝΤΙΣΤΟΙΧΙΣΗΣ.
/// </summary>
public static class EfoodMatcher
{
    /// <summary>Κάτω από αυτό η «πρόταση» θα ήταν μαντεψιά — καλύτερα κενό, να το διαλέξει άνθρωπος.</summary>
    private const double MinScore = 0.5;

    public static EfoodSuggestion? Suggest(EfoodCatalogItem item, IEfoodMenu menu) => new MenuIndex(menu).Suggest(item);

    /// <summary>Για πολλά προϊόντα μαζί — το ευρετήριο του μενού φτιάχνεται μία φορά.</summary>
    public static IReadOnlyDictionary<string, EfoodSuggestion?> SuggestAll(IReadOnlyList<EfoodCatalogItem> items, IEfoodMenu menu)
    {
        var index = new MenuIndex(menu);
        var result = new Dictionary<string, EfoodSuggestion?>(StringComparer.Ordinal);
        foreach (var item in items)
            result[KeyOf(item)] = index.Suggest(item);
        return result;
    }

    /// <summary>Το κλειδί ενός προϊόντος του e-food: ο ΑΡΙΘΜΟΣ του — αυτόν φέρνει κάθε παραγγελία ως integrator_id
    /// (έτσι ήρθαν οι πρώτες πραγματικές, 14/9/2026· το product.id έρχεται «no-valid-code-found»).</summary>
    public static string KeyOf(EfoodCatalogItem item) => item.Id.Length > 0 ? item.Id : item.Code;

    public static EfoodMatchState StateOf(EfoodMatch? match, IEfoodMenu menu) =>
        match is null ? EfoodMatchState.Pending
        : match.ProductId.Length > 0 && !menu.Categories.Any(c => c.Products.Any(p => p.Id == match.ProductId)) ? EfoodMatchState.Pending
        : match.Confirmed ? EfoodMatchState.Confirmed
        : EfoodMatchState.Auto;

    /// <summary>Για σύγκριση ονομάτων: πεζά, χωρίς τόνους, «ς»→«σ», «&amp;»→«και», ένα κενό.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch == 'ς' ? 'σ' : ch);
        var plain = builder.ToString().Normalize(NormalizationForm.FormC).Replace('`', '\'').Replace("&", " και ");
        return Regex.Replace(plain, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Οι λέξεις που μετράνε σε ένα όνομα, όπως τις συγκρίνει η αντιστοίχιση: κανονικοποιημένες (<see cref="Normalize"/>),
    /// χωρίς σημεία στίξης, διπλά γράμματα και μικρές λέξεις («με», «και», «του»…), με τις ποσότητες ενωμένες σε μία
    /// λέξη στη βασική μονάδα («0,5 lt» → «500ml», «100 γρ» → «100gr»).
    /// </summary>
    public static IReadOnlyList<string> Words(string text)
    {
        var plain = Normalize(text);
        plain = Regex.Replace(plain, @"(\d)[.,](\d)", "$1.$2");
        plain = Regex.Replace(plain, @"[^\p{L}\p{N}.]+|(?<!\d)\.|\.(?!\d)", " ");
        plain = Regex.Replace(plain, @"(\d)(\p{L})", "$1 $2");
        plain = Regex.Replace(plain, @"(\p{L})(\d)", "$1 $2");
        var raw = plain.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var words = new List<string>(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (decimal.TryParse(raw[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            {
                if (i + 1 < raw.Length && Units.TryGetValue(Collapse(raw[i + 1]), out var unit))
                {
                    words.Add(Number(number * unit.Factor) + unit.Name);
                    i++;
                }
                else
                {
                    words.Add(Number(number));
                }
                continue;
            }
            var word = Collapse(raw[i]);
            if (word.Length >= 2 && !StopWords.Contains(word))
                words.Add(word);
        }
        return words;
    }

    // ── λέξεις ────────────────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "με", "και", "του", "τησ", "των", "το", "τα", "τη", "την", "τον", "τισ", "τουσ", "οι", "σε", "στο", "στη", "στην",
        "στον", "στα", "στισ", "στουσ", "απο", "για", "with", "and", "the", "of",
    };

    /// <summary>Μονάδες ποσότητας → βασική μονάδα (τα κλειδιά χωρίς τόνους και διπλά γράμματα, όπως βγαίνουν από τις λέξεις).</summary>
    private static readonly Dictionary<string, (decimal Factor, string Name)> Units = new(StringComparer.Ordinal)
    {
        ["ml"] = (1m, "ml"), ["cl"] = (10m, "ml"),
        ["l"] = (1000m, "ml"), ["lt"] = (1000m, "ml"), ["ltr"] = (1000m, "ml"), ["λ"] = (1000m, "ml"), ["λτ"] = (1000m, "ml"),
        ["λιτ"] = (1000m, "ml"), ["λιτρο"] = (1000m, "ml"), ["λιτρα"] = (1000m, "ml"), ["λιτρου"] = (1000m, "ml"),
        ["g"] = (1m, "gr"), ["gr"] = (1m, "gr"), ["γρ"] = (1m, "gr"), ["γραμ"] = (1m, "gr"), ["γραμαρια"] = (1m, "gr"),
        ["kg"] = (1000m, "gr"), ["κιλο"] = (1000m, "gr"), ["κιλα"] = (1000m, "gr"), ["κγ"] = (1000m, "gr"),
        ["cm"] = (1m, "cm"), ["εκ"] = (1m, "cm"),
        ["τεμ"] = (1m, "τεμ"), ["τμχ"] = (1m, "τεμ"), ["τεμαχια"] = (1m, "τεμ"), ["pcs"] = (1m, "τεμ"), ["pc"] = (1m, "τεμ"),
    };

    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Ένα γράμμα αντί για δύο ίδια στη σειρά: «πιττα» → «πιτα», «μοτσαρελλα» → «μοτσαρελα».</summary>
    private static string Collapse(string word)
    {
        var builder = new StringBuilder(word.Length);
        foreach (var ch in word)
            if (builder.Length == 0 || builder[^1] != ch || !char.IsLetter(ch))
                builder.Append(ch);
        return builder.ToString();
    }

    /// <summary>Καταλήξεις πτώσεων/αριθμού — η πιο μακριά πρώτη. Όχι υποκοριστικά: «σουβλάκι» ≠ «σούβλα».</summary>
    private static readonly string[] Endings =
        ["ιων", "ιεσ", "ουσ", "εων", "ων", "ου", "οι", "οσ", "εσ", "ασ", "ησ", "ισ", "ια", "α", "ο", "η", "ι", "ε", "υ"];

    private static string StemOf(string word)
    {
        foreach (var ending in Endings)
            if (word.Length - ending.Length >= 3 && word.EndsWith(ending, StringComparison.Ordinal))
                return word[..^ending.Length];
        return word;
    }

    private static readonly (string From, string To)[] GreekPairs =
    [
        ("μπ", "b"), ("ντ", "d"), ("γκ", "g"), ("γγ", "g"), ("τσ", "ts"), ("τζ", "dz"), ("ου", "u"), ("αι", "e"),
        ("ει", "i"), ("οι", "i"), ("υι", "i"), ("αυ", "av"), ("ευ", "ev"), ("ξ", "ks"), ("ψ", "ps"), ("θ", "th"),
    ];

    private static readonly (string From, string To)[] LatinPairs =
    [
        ("zz", "ts"), ("ch", "ts"), ("sh", "s"), ("ph", "f"), ("ck", "k"), ("ou", "u"), ("oo", "u"), ("ee", "i"),
        ("qu", "kv"), ("x", "ks"), ("j", "dz"),
    ];

    private const string GreekLetters = "αβγδεζηικλμνοπρστυφχω";
    private const string GreekLatin = "avgdeziiklmnoprstifho";

    /// <summary>Πώς ακούγεται η λέξη, με λατινικά — για να ταιριάζουν «burger»/«μπεργκερ», «coca»/«κοκα».</summary>
    private static string LatinOf(string word)
    {
        var text = word;
        foreach (var (from, to) in GreekPairs)
            text = text.Replace(from, to, StringComparison.Ordinal);
        foreach (var (from, to) in LatinPairs)
            text = text.Replace(from, to, StringComparison.Ordinal);
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var greek = GreekLetters.IndexOf(ch);
            builder.Append(greek >= 0 ? GreekLatin[greek] : ch switch { 'c' => 'k', 'y' => 'i', 'w' => 'v', 'z' => 's', _ => ch });
        }
        return Collapse(builder.ToString());
    }

    /// <summary>Μία λέξη έτοιμη για σύγκριση: η ρίζα της, ο ήχος της με λατινικά, ή η ποσότητα αν είναι αριθμός.</summary>
    private sealed class Word
    {
        public Word(string text)
        {
            Text = text;
            var match = Regex.Match(text, @"^(\d+(?:\.\d+)?)(\D*)$");
            if (match.Success)
            {
                Amount = decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                Unit = match.Groups[2].Value;
                Stem = Latin = text;
            }
            else
            {
                Stem = StemOf(text);
                Latin = LatinOf(text);
                IsLatin = text.All(ch => ch < 128);
            }
        }

        public string Text { get; }
        public string Stem { get; }
        public string Latin { get; }
        public decimal? Amount { get; }
        public string Unit { get; } = "";
        public bool IsNumber => Amount is not null;
        public bool IsLatin { get; }
    }

    private static IReadOnlyList<Word> Prepare(string text) => Words(text).Select(w => new Word(w)).ToList();

    /// <summary>Από εδώ και πάνω δύο λέξεις είναι η ΙΔΙΑ λέξη· πιο κάτω απλώς μοιάζουν (μετράνε μόνο στην πρόταση).</summary>
    private const double SameWord = 0.85;

    /// <summary>Πόσο ίδιες είναι δύο λέξεις, 0 ως 1.</summary>
    private static double Similarity(Word a, Word b)
    {
        if (a.Text == b.Text)
            return 1;
        if (a.IsNumber || b.IsNumber)
            return a.Amount == b.Amount && (a.Unit == b.Unit || a.Unit.Length == 0 || b.Unit.Length == 0) ? 1 : 0;
        if (a.Stem == b.Stem && a.Stem.Length >= 3)
            return 0.95;
        if (a.Latin == b.Latin)
            return 0.9;
        // Ορθογραφικά μόνο όταν μπαίνουν λατινικά («burger» — «μπεργκερ»): ανάμεσα σε δύο ελληνικές, ένα γράμμα
        // είναι συχνά άλλο φαγητό («καλαμάκι» — «καλαμάρι»).
        if (!a.IsLatin && !b.IsLatin)
            return 0;
        var shorter = Math.Min(a.Latin.Length, b.Latin.Length);
        var allowed = shorter >= 8 ? 2 : shorter >= 5 ? 1 : 0;
        if (allowed > 0 && Distance(a.Latin, b.Latin, allowed) <= allowed)
            return SameWord;
        if (shorter >= 5 && (a.Latin.Contains(b.Latin, StringComparison.Ordinal) || b.Latin.Contains(a.Latin, StringComparison.Ordinal)))
            return 0.8;
        return 0;
    }

    /// <summary>Απόσταση Levenshtein — σταματάει μόλις ξεπεράσει το <paramref name="limit"/>.</summary>
    private static int Distance(string a, string b, int limit)
    {
        if (Math.Abs(a.Length - b.Length) > limit)
            return limit + 1;
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var best = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                best = Math.Min(best, current[j]);
            }
            if (best > limit)
                return limit + 1;
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    // ── ψωμί μέσα στο όνομα ───────────────────────────────────────────────────────────────────────

    /// <summary>Λέξεις που λένε ποιο ψωμί είναι («αραβική», «σάντουιτς» = Ψωμί) — από τις επιλογές ψωμιού του ταμείου.</summary>
    private static readonly (Word Word, string Bread)[] BreadNames =
        MenuSeed.BreadOptions.SelectMany(b => Words(b).Select(w => (new Word(w), b)))
            .Append((new Word("σαντουιτσ"), "Ψωμί"))
            .ToArray();

    /// <summary>Λέξεις που λένε μόνο ότι είναι πίττα, όχι ποια.</summary>
    private static readonly Word[] PitaWords = [new("πιτα"), new("πιτεσ"), new("pita")];

    private static string BreadOf(Word word) =>
        BreadNames.FirstOrDefault(b => Similarity(b.Word, word) >= 0.95).Bread ?? "";

    private static bool IsBreadWord(Word word) =>
        BreadOf(word).Length > 0 || PitaWords.Any(p => Similarity(p, word) >= 0.95);

    // ── σύγκριση ονομάτων ─────────────────────────────────────────────────────────────────────────

    /// <summary>Το μενού του ταμείου έτοιμο για σύγκριση, με το πόσο σπάνια είναι κάθε λέξη του.</summary>
    private sealed class MenuIndex
    {
        private readonly List<Entry> _entries = [];
        private readonly Dictionary<string, int> _frequency = new(StringComparer.Ordinal);
        private readonly Dictionary<(string, string), double> _similar = [];
        private readonly Dictionary<string, double> _weights = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Word> _vocabulary = new(StringComparer.Ordinal);

        /// <summary>Οι ίδιες λέξεις ξαναέρχονται σε κάθε προϊόν — η σύγκριση γίνεται μία φορά για κάθε ζευγάρι.</summary>
        private double Similar(Word a, Word b)
        {
            if (!_similar.TryGetValue((a.Text, b.Text), out var similarity))
                _similar[(a.Text, b.Text)] = similarity = Similarity(a, b);
            return similarity;
        }

        private sealed record Entry(Product Product, MenuCategory Category, string Name, IReadOnlyList<Word> Words,
            IReadOnlyList<Word> WordsWithoutBread, IReadOnlyList<Word> CategoryWords, bool HasBread, string Bread, decimal Price);

        public MenuIndex(IEfoodMenu menu)
        {
            foreach (var category in menu.Categories)
            {
                var categoryWords = Prepare(category.Name);
                var hasBread = menu.HasBreadChoice(category.Name);
                foreach (var product in category.Products)
                {
                    var words = Prepare(product.Name);
                    _entries.Add(new Entry(product, category, Normalize(product.Name), words,
                        hasBread ? words.Where(w => !IsBreadWord(w)).ToList() : words, categoryWords, hasBread,
                        hasBread ? words.Select(BreadOf).FirstOrDefault(b => b.Length > 0) ?? "" : "",
                        product.DeliveryPrice ?? product.Price));
                    foreach (var key in words.Select(w => w.Stem).Distinct())
                        _frequency[key] = _frequency.GetValueOrDefault(key) + 1;
                    foreach (var word in words)
                        _vocabulary.TryAdd(word.Text, word);
                }
            }
        }

        /// <summary>Σπάνια λέξη στο μενού του μαγαζιού = βαριά· λέξη που έχουν πολλά προϊόντα («πίττα») = ελαφριά.</summary>
        private double Weight(Word word)
        {
            if (!_weights.TryGetValue(word.Stem, out var weight))
                _weights[word.Stem] = weight = Math.Log(1.0 + (_entries.Count + 1.0) / (1.0 + _frequency.GetValueOrDefault(word.Stem)));
            return weight;
        }

        public EfoodSuggestion? Suggest(EfoodCatalogItem item)
        {
            var name = Normalize(item.Name);
            var words = Prepare(item.Name);
            var categoryWords = Prepare(item.Category);
            var itemBread = words.Select(BreadOf).FirstOrDefault(b => b.Length > 0) ?? "";
            var withoutBread = words.Where(w => !IsBreadWord(w)).ToList();
            // Οι λέξεις του μενού που μοιάζουν με κάποια του e-food — προϊόν χωρίς καμία από αυτές δεν συγκρίνεται καν.
            var related = new HashSet<string>(_vocabulary.Values.Where(v => words.Any(w => Similar(w, v) > 0)).Select(v => v.Text),
                StringComparer.Ordinal);

            Entry? best = null;
            Rank bestRank = default;
            var bestBread = "";
            var categories = new Dictionary<MenuCategory, double>(ReferenceEqualityComparer.Instance);
            foreach (var entry in _entries)
            {
                if (entry.Name != name && !entry.Words.Any(w => related.Contains(w.Text)))
                    continue;
                IReadOnlyList<Word> a = words, b = entry.Words;
                var bread = "";
                if (entry.Name == name)
                {
                    // ίδιο όνομα: όπως πάντα — το ψωμί το διαλέγει ο πελάτης
                }
                else if (entry.HasBread && withoutBread.Count > 0)
                {
                    // «Αραβική πίττα κοτόπουλο» στο e-food = «Πίττα κοτόπουλο» του ταμείου με Αραβική — όχι όμως με
                    // προϊόν του ταμείου που είναι ήδη ΑΛΛΟ ψωμί.
                    if (itemBread.Length > 0 && entry.Bread.Length > 0 && itemBread != entry.Bread)
                        continue;
                    if (entry.WordsWithoutBread.Count > 0)
                    {
                        (a, b) = (withoutBread, entry.WordsWithoutBread);
                        bread = entry.Bread.Length == 0 ? itemBread : "";
                    }
                }

                var (score, complete) = entry.Name == name ? (1.0, true) : Compare(a, b);
                if (score < MinScore)
                    continue;
                // Χωρίς να βγουν οι λέξεις του ψωμιού: «Αραβική πίττα κοτόπουλο» → «Αραβική κοτόπουλο» του ταμείου (αν
                // το έχει) πριν από «Πίττα κοτόπουλο» με Αραβική.
                var whole = ReferenceEquals(a, words) ? score : Compare(words, entry.Words).Score;
                if (!categories.TryGetValue(entry.Category, out var category))
                    categories[entry.Category] = category = Dice(categoryWords, entry.CategoryWords);
                var rank = new Rank(complete, Math.Round(score, 3), Math.Round(whole, 3),
                    Math.Abs(entry.Price - item.Price) < 0.005m, category);
                if (best is null || rank.IsBetterThan(bestRank))
                    (best, bestRank, bestBread) = (entry, rank, bread);
            }

            return best is null ? null
                : new EfoodSuggestion(best.Product, best.Category, bestBread, bestRank.SamePrice, bestRank.Complete);
        }

        /// <summary>Πρώτα όσα ταιριάζουν σε ΟΛΕΣ τις λέξεις, μετά το πόσο, μετά με τις λέξεις του ψωμιού, μετά η ίδια
        /// τιμή, μετά η κατηγορία· σε ισοπαλία μένει το πρώτο του μενού.</summary>
        private readonly record struct Rank(bool Complete, double Score, double Whole, bool SamePrice, double Category)
        {
            public bool IsBetterThan(Rank other) =>
                Complete != other.Complete ? Complete
                : Score != other.Score ? Score > other.Score
                : Whole != other.Whole ? Whole > other.Whole
                : SamePrice != other.SamePrice ? SamePrice
                : Category > other.Category;
        }

        /// <summary>Ζευγαρώνει τις λέξεις των δύο ονομάτων (η καθεμία μία φορά, οι πιο ίδιες πρώτα). Βαθμός 0–1 με
        /// βάρος τη σπανιότητα, και αν ΟΛΕΣ οι λέξεις και των δύο βρήκαν την ίδια λέξη απέναντι.</summary>
        private (double Score, bool Complete) Compare(IReadOnlyList<Word> a, IReadOnlyList<Word> b)
        {
            if (a.Count == 0 || b.Count == 0)
                return (0, false);
            var pairs = new List<(int A, int B, double Similarity)>();
            for (var i = 0; i < a.Count; i++)
                for (var j = 0; j < b.Count; j++)
                    if (Similar(a[i], b[j]) is var similarity and > 0)
                        pairs.Add((i, j, similarity));
            pairs.Sort((x, y) => y.Similarity.CompareTo(x.Similarity));

            var usedA = new bool[a.Count];
            var usedB = new bool[b.Count];
            var strong = 0;
            double matched = 0;
            foreach (var (i, j, similarity) in pairs)
            {
                if (usedA[i] || usedB[j])
                    continue;
                usedA[i] = usedB[j] = true;
                matched += similarity * (Weight(a[i]) + Weight(b[j]));
                if (similarity >= SameWord)
                    strong++;
            }
            var total = a.Sum(Weight) + b.Sum(Weight);
            return (total > 0 ? matched / total : 0, strong == a.Count && strong == b.Count);
        }

        /// <summary>Πόσο μοιάζουν δύο κατηγορίες («Αραβικές πίττες» — «ΠΙΤΤΕΣ») — μόνο για να κρίνει ισοπαλίες.</summary>
        private double Dice(IReadOnlyList<Word> a, IReadOnlyList<Word> b)
        {
            if (a.Count == 0 || b.Count == 0)
                return 0;
            var common = a.Count(x => b.Any(y => Similar(x, y) >= SameWord));
            return 2.0 * common / (a.Count + b.Count);
        }
    }
}
