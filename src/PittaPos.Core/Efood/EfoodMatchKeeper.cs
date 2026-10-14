namespace PittaPos.Core.Efood;

/// <summary>
/// Να μη χαθεί ποτέ απόφαση της ΑΝΤΙΣΤΟΙΧΙΣΗΣ. Κάθε γραμμή κρατιέται με τον αριθμό του προϊόντος του e-food (αυτόν
/// φέρνουν οι παραγγελίες)· όταν ο κατάλογος ξανάρθει με ΑΛΛΟ αριθμό για το ίδιο προϊόν, η γραμμή μετακομίζει στον νέο,
/// με την ίδια απόφαση. Το ίδιο κι από τη μεριά του ταμείου: αν το προϊόν του ταμείου σβήστηκε και ξαναπεράστηκε με
/// άλλο id, η γραμμή το ξαναβρίσκει με το όνομα.
/// </summary>
public static class EfoodMatchKeeper
{
    /// <summary>
    /// Διορθώνει τις γραμμές επί τόπου (κλειδί = <see cref="EfoodMatch.EfoodId"/>) και λέει αν άλλαξε κάτι για να
    /// αποθηκευτεί. Τρέχει ΠΡΙΝ από τις αυτόματες (<see cref="EfoodContext.AutoMatches"/>), ώστε μια απόφαση ανθρώπου να
    /// μη χάνει τη θέση της από μια αυτόματη πρόταση.
    /// </summary>
    public static bool Follow(IReadOnlyList<EfoodCatalogItem> catalog, IDictionary<string, EfoodMatch> matches, IEfoodMenu menu)
    {
        var changed = catalog.Count > 0 && FollowCatalog(catalog, matches);
        if (menu.Categories.Count > 0)
            changed |= FollowMenu(matches.Values, menu);
        return changed;
    }

    /// <summary>
    /// Γραμμές που δεν δείχνουν πια σε προϊόν του καταλόγου → στο προϊόν του καταλόγου που δεν έχει γραμμή και είναι το
    /// ίδιο: πρώτα με τον κωδικό του e-food (IT_…), μετά με ίδιο όνομα και κατηγορία, μετά με ίδιο όνομα μόνο αν είναι
    /// μοναδικό και στις δύο μεριές. Ό,τι δεν βρίσκει ταίρι μένει όπως είναι — ίσως το προϊόν απλώς λείπει προσωρινά.
    /// </summary>
    private static bool FollowCatalog(IReadOnlyList<EfoodCatalogItem> catalog, IDictionary<string, EfoodMatch> matches)
    {
        var live = new HashSet<string>(catalog.Select(i => i.Id).Where(id => id.Length > 0), StringComparer.Ordinal);
        var orphans = matches.Values.Where(m => !live.Contains(m.EfoodId)).ToList();
        var homeless = catalog.Where(i => i.Id.Length > 0 && !matches.ContainsKey(i.Id)).ToList();
        if (orphans.Count == 0 || homeless.Count == 0)
            return false;

        var changed = false;
        void Move(EfoodMatch match, EfoodCatalogItem item)
        {
            matches.Remove(match.EfoodId);
            match.EfoodId = item.Id;
            match.EfoodCode = item.Code;
            match.EfoodName = item.Name;
            match.EfoodCategory = item.Category;
            matches[item.Id] = match;
            orphans.Remove(match);
            homeless.Remove(item);
            changed = true;
        }

        foreach (var item in homeless.ToList())
        {
            if (item.Code.Length == 0)
                continue;
            var sameCode = orphans.Where(m => string.Equals(m.EfoodCode, item.Code, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sameCode.Count == 1)
                Move(sameCode[0], item);
        }

        static string Name(string text) => EfoodMatcher.Normalize(text);
        foreach (var item in homeless.ToList())
        {
            var same = orphans.Where(m => Name(m.EfoodName) == Name(item.Name) && Name(m.EfoodCategory) == Name(item.Category)).ToList();
            var twins = homeless.Count(i => Name(i.Name) == Name(item.Name) && Name(i.Category) == Name(item.Category));
            if (same.Count == 1 && twins == 1)
                Move(same[0], item);
        }

        foreach (var item in homeless.ToList())
        {
            var same = orphans.Where(m => Name(m.EfoodName) == Name(item.Name)).ToList();
            var twins = homeless.Count(i => Name(i.Name) == Name(item.Name));
            if (same.Count == 1 && twins == 1 && same[0].EfoodName.Length > 0)
                Move(same[0], item);
        }
        return changed;
    }

    /// <summary>Προϊόν του ταμείου που δεν υπάρχει πια με αυτό το id → το μοναδικό με το ίδιο όνομα. Όσα υπάρχουν
    /// κρατάνε το τωρινό τους όνομα (και οι παλιές γραμμές, πριν κρατιέται το όνομα), για την επόμενη φορά.</summary>
    private static bool FollowMenu(IEnumerable<EfoodMatch> matches, IEfoodMenu menu)
    {
        var products = menu.Categories.SelectMany(c => c.Products).ToList();
        var byId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var product in products)
            byId.TryAdd(product.Id, product.Name);

        var changed = false;
        foreach (var match in matches)
        {
            if (match.ProductId.Length == 0)
                continue;
            if (byId.TryGetValue(match.ProductId, out var name))
            {
                if (match.ProductName != name)
                {
                    match.ProductName = name;
                    changed = true;
                }
                continue;
            }
            if (match.ProductName.Length == 0)
                continue;
            var wanted = EfoodMatcher.Normalize(match.ProductName);
            var same = products.Where(p => EfoodMatcher.Normalize(p.Name) == wanted).DistinctBy(p => p.Id).ToList();
            if (same.Count == 1)
            {
                match.ProductId = same[0].Id;
                match.ProductName = same[0].Name;
                changed = true;
            }
        }
        return changed;
    }
}
