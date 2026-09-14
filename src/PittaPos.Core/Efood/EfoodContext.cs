namespace PittaPos.Core.Efood;

/// <summary>
/// Ό,τι ξέρει το ταμείο για τον κατάλογο του e-food: τα προϊόντα και τις επιλογές του — για να καταλαβαίνει τι σημαίνει
/// κάθε material μιας παραγγελίας (ψωμί, «Μόνο με», υλικό που έμεινε, extra) — και την ΑΝΤΙΣΤΟΙΧΙΣΗ (προϊόν e-food →
/// προϊόν ταμείου + ψωμί). Χωρίς αυτά, η παραγγελία διαβάζεται με τα ονόματα και τυπώνεται όπως ήρθε.
/// </summary>
public sealed class EfoodContext
{
    public static EfoodContext Empty { get; } = new([], []);

    private readonly Dictionary<string, EfoodCatalogItem> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (EfoodCatalogTier Tier, EfoodCatalogOption Option)> _options = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EfoodMatch> _matches = new(StringComparer.Ordinal);

    public EfoodContext(IReadOnlyList<EfoodCatalogItem> catalog, IEnumerable<EfoodMatch> matches)
    {
        foreach (var item in catalog)
        {
            if (item.Id.Length > 0)
                _items.TryAdd(item.Id, item);
            foreach (var tier in item.Tiers)
                foreach (var option in tier.Options)
                    if (option.Id.Length > 0)
                        _options.TryAdd(option.Id, (tier, option));
        }
        foreach (var match in matches)
            if (match.EfoodId.Length > 0)
                _matches[match.EfoodId] = match;
    }

    /// <summary>Το προϊόν του καταλόγου του e-food με αυτόν τον αριθμό (το integrator_id του προϊόντος στην παραγγελία).</summary>
    public EfoodCatalogItem? Item(string efoodId) => _items.GetValueOrDefault(efoodId);

    /// <summary>Η επιλογή με αυτόν τον αριθμό (το integrator_id του material) και η ομάδα της.</summary>
    public (EfoodCatalogTier Tier, EfoodCatalogOption Option)? Option(string efoodId) =>
        _options.TryGetValue(efoodId, out var found) ? found : null;

    /// <summary>Η αντιστοίχιση του προϊόντος — αποθηκευμένη ή αυτόματη με ίδια τιμή· null αν δεν υπάρχει.</summary>
    public EfoodMatch? Match(string efoodId) => _matches.GetValueOrDefault(efoodId);

    /// <summary>
    /// Οι αποθηκευμένες γραμμές αντιστοίχισης, και για όσα προϊόντα δεν έχουν γραμμή οι αυτόματες προτάσεις με ΙΔΙΑ τιμή
    /// (βλ. <see cref="EfoodMatcher"/>). Όσα έχουν πρόταση με άλλη τιμή ή καμία πρόταση μένουν χωρίς αντιστοίχιση μέχρι
    /// να τα δει άνθρωπος — τυπώνονται με το όνομα του e-food.
    /// </summary>
    public static EfoodContext Build(IReadOnlyList<EfoodCatalogItem> catalog, IEnumerable<EfoodMatch> saved, IEfoodMenu menu)
    {
        var all = saved.Where(m => m.EfoodId.Length > 0).ToList();
        all.AddRange(AutoMatches(catalog, all, menu));
        return new EfoodContext(catalog, all);
    }

    /// <summary>
    /// Οι ΝΕΕΣ αυτόματες γραμμές: για όσα προϊόντα του καταλόγου δεν έχουν ήδη γραμμή και η πρόταση έχει ΙΔΙΑ τιμή. Το
    /// ταμείο τις κρατάει μόλις βγουν, ώστε μια μεταγενέστερη αλλαγή τιμής στο μενού του ταμείου να μην ξε-αντιστοιχίσει
    /// ό,τι ήδη δούλευε.
    /// </summary>
    public static IReadOnlyList<EfoodMatch> AutoMatches(IReadOnlyList<EfoodCatalogItem> catalog, IEnumerable<EfoodMatch> existing, IEfoodMenu menu)
    {
        var known = new HashSet<string>(existing.Select(m => m.EfoodId), StringComparer.Ordinal);
        var suggestions = EfoodMatcher.SuggestAll(catalog, menu);
        var added = new List<EfoodMatch>();
        foreach (var item in catalog)
        {
            if (item.Id.Length == 0 || !known.Add(item.Id)
                || suggestions.GetValueOrDefault(EfoodMatcher.KeyOf(item)) is not { SamePrice: true } suggestion)
                continue;
            added.Add(new EfoodMatch
            {
                EfoodId = item.Id,
                EfoodCode = item.Code,
                EfoodName = item.Name,
                EfoodCategory = item.Category,
                ProductId = suggestion.Product.Id,
                Bread = suggestion.Bread,
            });
        }
        return added;
    }
}
