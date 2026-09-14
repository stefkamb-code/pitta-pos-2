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
        var matches = new Dictionary<string, EfoodMatch>(StringComparer.Ordinal);
        foreach (var match in saved)
            if (match.EfoodId.Length > 0)
                matches[match.EfoodId] = match;
        var suggestions = EfoodMatcher.SuggestAll(catalog, menu);
        foreach (var item in catalog)
        {
            var key = EfoodMatcher.KeyOf(item);
            if (matches.ContainsKey(key) || suggestions.GetValueOrDefault(key) is not { SamePrice: true } suggestion)
                continue;
            matches[key] = new EfoodMatch
            {
                EfoodId = item.Id,
                EfoodCode = item.Code,
                EfoodName = item.Name,
                EfoodCategory = item.Category,
                ProductId = suggestion.Product.Id,
                Bread = suggestion.Bread,
            };
        }
        return new EfoodContext(catalog, matches.Values);
    }
}
