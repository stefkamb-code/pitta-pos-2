namespace PittaPos.App.Services;

/// <summary>Ανάλυση χρόνου για το διάγραμμα πωλήσεων στα Στατιστικά.</summary>
public enum ChartGranularity { Hour, Day, Month, Year, Custom }

/// <summary>
/// Συγκεντρωτικός τζίρος ανά ώρα/ημέρα/μήνα για το διάγραμμα πωλήσεων. Συνδυάζει τις ζωντανές
/// (μη κλεισμένες ακόμα) παραγγελίες από το SalesStatsService με τις αρχειοθετημένες παλιότερες
/// μέρες από το HistoryArchiveService — ίδια λογική με το HistoryViewModel.
/// </summary>
public static class SalesChartService
{
    public static IReadOnlyList<(string Label, decimal Revenue)> Build(ChartGranularity granularity, DateTime anchor) =>
        granularity switch
        {
            ChartGranularity.Hour => BuildHours(anchor),
            ChartGranularity.Day => BuildDays(anchor),
            ChartGranularity.Month => BuildMonths(anchor),
            _ => BuildYears(anchor),
        };

    /// <summary>Πρώτο έτος της δεκαετίας που δείχνει η ανάλυση «ΧΡΟΝΟΣ» για δεδομένη μέρα-άγκυρα.</summary>
    public static int DecadeStart(DateTime anchor) => anchor.Year - (anchor.Year % 10);

    /// <summary>
    /// Ίδιο σχήμα με το Build(...) αλλά ένα χρόνο πριν — για τη σύγκριση «πέρσι» πάνω στο ίδιο διάγραμμα.
    /// Οι ετικέτες (ώρα/μέρα-μήνα/μήνας) δεν εξαρτώνται από το έτος, οπότε ταιριάζουν θέση-με-θέση.
    /// </summary>
    public static IReadOnlyList<decimal> BuildCompareValues(ChartGranularity granularity, DateTime anchor) =>
        Build(granularity, anchor.AddYears(-1)).Select(d => d.Revenue).ToList();

    /// <summary>Ίδιο για το «ΠΡΟΣΑΡΜΟΣΜΕΝΟ» εύρος — ακριβώς το ίδιο διάστημα, έναν χρόνο πριν.</summary>
    public static IReadOnlyList<decimal> BuildRangeCompareValues(DateTime from, DateTime to) =>
        BuildRange(from.AddYears(-1), to.AddYears(-1)).Select(d => d.Revenue).ToList();

    /// <summary>Μέγιστο πλήθος ημερών για το «ΠΡΟΣΑΡΜΟΣΜΕΝΟ» εύρος — προστασία από κατά λάθος τεράστιο διάστημα.</summary>
    private const int MaxCustomRangeDays = 366;

    /// <summary>Μία μπάρα τζίρου ανά ημέρα, για οποιοδήποτε εύρος επιλέγει ο χρήστης από ημερολόγιο (Από/Έως).</summary>
    public static IReadOnlyList<(string Label, decimal Revenue)> BuildRange(DateTime from, DateTime to)
    {
        if (from > to)
            (from, to) = (to, from);
        from = from.Date;
        to = to.Date;
        if ((to - from).Days >= MaxCustomRangeDays)
            to = from.AddDays(MaxCustomRangeDays - 1);

        var byDay = RevenueByDay(from, to);

        var days = (to - from).Days + 1;
        return Enumerable.Range(0, days)
            .Select(i => from.AddDays(i))
            .Select(d => (d.ToString("d/M", Greek), byDay.TryGetValue(d, out var r) ? r : 0m))
            .ToList();
    }

    private static IReadOnlyList<(string, decimal)> BuildHours(DateTime anchor)
    {
        var day = SalesStatsService.BusinessDay(anchor);
        var byHour = LoadOrders(day, day)
            .GroupBy(o => o.PlacedAt.Hour)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.Total));

        return Enumerable.Range(0, 24)
            .Select(h => (h.ToString("00"), byHour.TryGetValue(h, out var r) ? r : 0m))
            .ToList();
    }

    private static IReadOnlyList<(string, decimal)> BuildDays(DateTime anchor)
    {
        var first = new DateTime(anchor.Year, anchor.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var byDay = Group(first, last, d => d.Day);

        return Enumerable.Range(1, last.Day)
            .Select(d => (d.ToString(), byDay.TryGetValue(d, out var r) ? r : 0m))
            .ToList();
    }

    private static readonly System.Globalization.CultureInfo Greek =
        System.Globalization.CultureInfo.GetCultureInfo("el-GR");

    private static IReadOnlyList<(string, decimal)> BuildMonths(DateTime anchor)
    {
        var first = new DateTime(anchor.Year, 1, 1);
        var last = new DateTime(anchor.Year, 12, 31);
        var byMonth = Group(first, last, d => d.Month);

        return Enumerable.Range(1, 12)
            .Select(m => (Greek.DateTimeFormat.GetAbbreviatedMonthName(m).ToUpper(Greek), byMonth.TryGetValue(m, out var r) ? r : 0m))
            .ToList();
    }

    private static IReadOnlyList<(string, decimal)> BuildYears(DateTime anchor)
    {
        var decadeStart = DecadeStart(anchor);
        var first = new DateTime(decadeStart, 1, 1);
        var last = new DateTime(decadeStart + 9, 12, 31);
        var byYear = Group(first, last, d => d.Year);

        return Enumerable.Range(decadeStart, 10)
            .Select(y => (y.ToString(), byYear.TryGetValue(y, out var r) ? r : 0m))
            .ToList();
    }

    /// <summary>
    /// Τζίρος ανά ημέρα-επιχείρησης μέσα στο εύρος — <b>χωρίς να ανοιχτεί κανένα αρχείο ημέρας</b>.
    ///
    /// <para>Οι παλιές μέρες έρχονται από τη σύνοψη του αρχείου (βλ.
    /// HistoryArchiveService.DailySummaries: υπολογίζεται μία φορά στη ζωή κάθε μέρας), και μόνο η
    /// ΣΗΜΕΡΙΝΗ αθροίζεται ζωντανά — αυτή είναι ήδη στη μνήμη. Έτσι το διάγραμμα κοστίζει το ίδιο είτε
    /// το μαγαζί δουλεύει δύο εβδομάδες είτε δέκα χρόνια.</para>
    /// </summary>
    private static Dictionary<DateTime, decimal> RevenueByDay(DateTime from, DateTime to)
    {
        var today = SalesStatsService.BusinessDay(DateTime.Now);
        var result = new Dictionary<DateTime, decimal>();

        foreach (var (day, summary) in HistoryArchiveService.DailySummaries())
            if (day >= from.Date && day <= to.Date && day != today)
                result[day] = summary.Revenue;

        // Η σημερινή ζωντανά. CountedOrders, όχι Orders: το διάγραμμα δείχνει τζίρο, και όσες
        // περιμένουν ακόμα κανάλι δεν έχουν μπει σε αυτόν (βλ. SalesStatsService.CountedOrders).
        // Ίδιο φίλτρο ημέρας-επιχείρησης με το Ιστορικό: μια παραγγελία προηγούμενης μέρας που κάθεται
        // ακόμα εδώ ανήκει στη ΔΙΚΗ ΤΗΣ μέρα του διαγράμματος, όχι στη σημερινή.
        foreach (var o in SalesStatsService.Instance.CountedOrders)
        {
            var day = SalesStatsService.BusinessDay(o.PlacedAt).Date;
            if (day >= from.Date && day <= to.Date)
                result[day] = result.GetValueOrDefault(day) + o.Total;
        }
        return result;
    }

    /// <summary>Ο τζίρος του εύρους μαζεμένος σε ημέρα/μήνα/έτος, ανάλογα με το τι δείχνει το διάγραμμα.</summary>
    private static Dictionary<int, decimal> Group(DateTime from, DateTime to, Func<DateTime, int> bucket)
    {
        var result = new Dictionary<int, decimal>();
        foreach (var (day, revenue) in RevenueByDay(from, to))
            result[bucket(day)] = result.GetValueOrDefault(bucket(day)) + revenue;
        return result;
    }

    /// <summary>Ίδιος συνδυασμός ζωντανών + αρχειοθετημένων παραγγελιών με το Ιστορικό (HistoryViewModel.Refresh).
    /// Μένει ΜΟΝΟ για την ανάλυση «ΩΡΑ», που θέλει τις ίδιες τις παραγγελίες μιας μέρας — ένα αρχείο.</summary>
    private static List<CompletedOrder> LoadOrders(DateTime from, DateTime to)
    {
        var today = SalesStatsService.BusinessDay(DateTime.Now);
        var archived = HistoryArchiveService.LoadOrders(from, to)
            .Where(o => SalesStatsService.BusinessDay(o.PlacedAt) != today);
        // Ίδιο φίλτρο ημέρας-επιχείρησης και στις ζωντανές, όπως και στο Ιστορικό: μια παραγγελία
        // προηγούμενης μέρας που κάθεται ακόμα εδώ ανήκει στη ΔΙΚΗ ΤΗΣ μέρα του διαγράμματος, όχι στη
        // σημερινή (βλ. HistoryViewModel.Refresh).
        // CountedOrders, όχι Orders: το διάγραμμα δείχνει τζίρο, και όσες περιμένουν ακόμα κανάλι δεν
        // έχουν μπει σε αυτόν (βλ. SalesStatsService.CountedOrders).
        var live = SalesStatsService.Instance.CountedOrders
            .Where(o =>
            {
                var day = SalesStatsService.BusinessDay(o.PlacedAt);
                return day >= from.Date && day <= to.Date;
            });
        return archived.Concat(live).ToList();
    }
}
