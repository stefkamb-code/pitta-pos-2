using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using PittaPos.App.Services;
using PittaPos.Core.Models;
using SkiaSharp;

namespace PittaPos.App.ViewModels;

/// <summary>Ποια καρτέλα δείχνει το παράθυρο Στατιστικών.</summary>
public enum StatsMainTab { Overview, Chart }

/// <summary>Παραλλαγή προϊόντος (ψωμί/έξτρα/χωρίς) στον πίνακα στατιστικών.</summary>
public class ProductVariantStatViewModel
{
    public required int Quantity { get; init; }
    public required string Details { get; init; }
    public required decimal Revenue { get; init; }

    public string QuantityLabel => "× " + Quantity;
    public string DetailsLabel => Details.Length > 0 ? Details : "κανονική";
    public string RevenueLabel => Order.FormatPrice(Revenue);
}

/// <summary>Γραμμή προϊόντος στον πίνακα στατιστικών.</summary>
public class ProductStatViewModel
{
    public required int Rank { get; init; }
    public required string Name { get; init; }
    public required int Quantity { get; init; }
    public required decimal Revenue { get; init; }
    /// <summary>Μερίδιο στον συνολικό τζίρο (0–100).</summary>
    public required double SharePct { get; init; }
    /// <summary>Ανάλυση σε παραλλαγές — κενή όταν όλες οι πωλήσεις ήταν χωρίς προσαρμογές.</summary>
    public required IReadOnlyList<ProductVariantStatViewModel> Variants { get; init; }

    public string RankLabel => Rank + ".";
    public string QuantityLabel => "× " + Quantity;
    public string RevenueLabel => Order.FormatPrice(Revenue);
    public string ShareLabel => SharePct.ToString("0.#") + "%";
    public bool HasVariants => Variants.Count > 0;
}

/// <summary>Κάρτα τζίρου ανά κανάλι — εμφανίζεται πάντα, και με μηδενικό τζίρο. Πρωί/βράδυ πάντα μαζί, σταθερά.</summary>
public class ChannelRevenueViewModel
{
    public required string Name { get; init; }
    public required System.Windows.Media.Brush Brush { get; init; }
    public required int OrderCount { get; init; }
    public required decimal Revenue { get; init; }
    /// <summary>Μετρητά/κάρτα χωριστά μέσα στην κάρτα — μόνο για τα κανάλια που κρατούν τρόπο πληρωμής
    /// (BOX: το παραδίδει δικός μας διανομέας και εισπράττει επιτόπου). Στα e-food/Wolt το ποσό έχει ήδη
    /// πληρωθεί στην πλατφόρμα, οπότε ένας διαχωρισμός εκεί θα ήταν πάντα μηδέν.</summary>
    public bool HasPaymentSplit { get; init; }
    public decimal CashRevenue { get; init; }
    public decimal CardRevenue { get; init; }

    public string RevenueLabel => Order.FormatPrice(Revenue);
    public string CashLabel => Order.FormatPrice(CashRevenue);
    public string CardLabel => Order.FormatPrice(CardRevenue);
    public string CountLabel => OrderCount + (OrderCount == 1 ? " παραγγελία" : " παραγγελίες");
}

/// <summary>Στατιστικά ημέρας — τροφοδοτείται ζωντανά από το SalesStatsService.</summary>
public partial class StatsViewModel : ObservableObject
{
    private readonly SalesStatsService _stats = SalesStatsService.Instance;

    private readonly DispatcherTimer _clock;

    public StatsViewModel()
    {
        _stats.Changed += Refresh;
        ThemeManager.Changed += RefreshChart;
        // Δεύτερο ταμείο: το διάγραμμα παλιότερων ημερών βγαίνει από το ίδιο αρχείο με το Ιστορικό.
        HistoryArchiveService.Changed += Refresh;
        Refresh();

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => OnPropertyChanged(nameof(NowLabel));
        _clock.Start();
    }

    /// <summary>Αποσύνδεση από τα services όταν κλείσει το παράθυρο.</summary>
    public void Detach()
    {
        _stats.Changed -= Refresh;
        ThemeManager.Changed -= RefreshChart;
        HistoryArchiveService.Changed -= Refresh;
        _clock.Stop();
    }

    // ---- Καρτέλα «ΔΙΑΓΡΑΜΜΑ»: πωλήσεις ανά ώρα/ημέρα/μήνα, με επιλογή ημερομηνίας ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOverviewTab))]
    [NotifyPropertyChangedFor(nameof(ShowChartTab))]
    private StatsMainTab _mainTab = StatsMainTab.Overview;

    public bool ShowOverviewTab => MainTab == StatsMainTab.Overview;
    public bool ShowChartTab => MainTab == StatsMainTab.Chart;

    [RelayCommand] private void SelectOverviewTab() => MainTab = StatsMainTab.Overview;

    [RelayCommand]
    private void SelectChartTab()
    {
        MainTab = StatsMainTab.Chart;
        RefreshChart();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHourGranularity))]
    [NotifyPropertyChangedFor(nameof(IsDayGranularity))]
    [NotifyPropertyChangedFor(nameof(IsMonthGranularity))]
    [NotifyPropertyChangedFor(nameof(IsYearGranularity))]
    [NotifyPropertyChangedFor(nameof(IsCustomGranularity))]
    [NotifyPropertyChangedFor(nameof(ShowAnchorPicker))]
    [NotifyPropertyChangedFor(nameof(ShowRangePicker))]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    private ChartGranularity _granularity = ChartGranularity.Hour;

    public bool IsHourGranularity => Granularity == ChartGranularity.Hour;
    public bool IsDayGranularity => Granularity == ChartGranularity.Day;
    public bool IsMonthGranularity => Granularity == ChartGranularity.Month;
    public bool IsYearGranularity => Granularity == ChartGranularity.Year;
    public bool IsCustomGranularity => Granularity == ChartGranularity.Custom;

    /// <summary>Ένα κουτί ημερομηνίας + βελάκια (ΩΡΑ/ΗΜΕΡΑ/ΜΗΝΑΣ/ΧΡΟΝΟΣ) ή δύο κουτιά Από/Έως (ΠΡΟΣΑΡΜΟΣΜΕΝΟ).</summary>
    public bool ShowAnchorPicker => !IsCustomGranularity;
    public bool ShowRangePicker => IsCustomGranularity;

    /// <summary>Η σύγκριση με πέρσι δεν έχει νόημα στη ΧΡΟΝΟΣ (ήδη δείχνει ολόκληρη δεκαετία μαζί).</summary>
    public bool CanCompare => !IsYearGranularity;

    partial void OnGranularityChanged(ChartGranularity value) => RefreshChart();

    [RelayCommand] private void SelectHourGranularity() => Granularity = ChartGranularity.Hour;
    [RelayCommand] private void SelectDayGranularity() => Granularity = ChartGranularity.Day;
    [RelayCommand] private void SelectMonthGranularity() => Granularity = ChartGranularity.Month;
    [RelayCommand] private void SelectYearGranularity() => Granularity = ChartGranularity.Year;
    [RelayCommand] private void SelectCustomGranularity() => Granularity = ChartGranularity.Custom;

    /// <summary>Σύγκριση με την ίδια περίοδο πέρσι — δεύτερη σειρά μπλε, δίπλα στην κόκκινη τρέχουσα.</summary>
    [ObservableProperty]
    private bool _compareEnabled;

    partial void OnCompareEnabledChanged(bool value) => RefreshChart();

    [RelayCommand] private void ToggleCompare() => CompareEnabled = !CompareEnabled;

    /// <summary>Μέρα (ώρα), μήνας (ημέρα), έτος (μήνας) ή δεκαετία (χρόνος) που δείχνει το διάγραμμα — επιλέγεται και από ημερολόγιο.</summary>
    [ObservableProperty]
    private DateTime _chartAnchor = DateTime.Now;

    partial void OnChartAnchorChanged(DateTime value) => RefreshChart();

    /// <summary>Εύρος «ΠΡΟΣΑΡΜΟΣΜΕΝΟ» — ελεύθερη επιλογή Από/Έως από ημερολόγιο, μία μπάρα ανά ημέρα.</summary>
    [ObservableProperty]
    private DateTime _customFromDate = DateTime.Now.AddDays(-6);

    [ObservableProperty]
    private DateTime _customToDate = DateTime.Now;

    partial void OnCustomFromDateChanged(DateTime value) => RefreshChart();
    partial void OnCustomToDateChanged(DateTime value) => RefreshChart();

    [RelayCommand]
    private void ChartPrev()
    {
        if (Granularity == ChartGranularity.Custom)
        {
            var span = (CustomToDate.Date - CustomFromDate.Date).Days + 1;
            CustomFromDate = CustomFromDate.AddDays(-span);
            CustomToDate = CustomToDate.AddDays(-span);
            return;
        }
        ChartAnchor = Granularity switch
        {
            ChartGranularity.Hour => ChartAnchor.AddDays(-1),
            ChartGranularity.Day => ChartAnchor.AddMonths(-1),
            ChartGranularity.Month => ChartAnchor.AddYears(-1),
            _ => ChartAnchor.AddYears(-10),
        };
    }

    [RelayCommand]
    private void ChartNext()
    {
        if (Granularity == ChartGranularity.Custom)
        {
            var span = (CustomToDate.Date - CustomFromDate.Date).Days + 1;
            CustomFromDate = CustomFromDate.AddDays(span);
            CustomToDate = CustomToDate.AddDays(span);
            return;
        }
        ChartAnchor = Granularity switch
        {
            ChartGranularity.Hour => ChartAnchor.AddDays(1),
            ChartGranularity.Day => ChartAnchor.AddMonths(1),
            ChartGranularity.Month => ChartAnchor.AddYears(1),
            _ => ChartAnchor.AddYears(10),
        };
    }

    [RelayCommand]
    private void ChartToday()
    {
        ChartAnchor = DateTime.Now;
        CustomFromDate = DateTime.Now.AddDays(-6);
        CustomToDate = DateTime.Now;
    }

    public ISeries[] Series { get; private set; } = [];
    public Axis[] XAxes { get; private set; } = [];
    public Axis[] YAxes { get; private set; } = [];
    public string ChartAnchorLabel { get; private set; } = "";
    public string ChartTotalLabel { get; private set; } = Order.FormatPrice(0);
    public bool ChartNoData { get; private set; } = true;
    public bool ChartHasData => !ChartNoData;

    /// <summary>Διαβάζει χρώμα από το ενεργό θέμα (ώστε να δείχνει σωστά και στο μαύρο).</summary>
    private static SKColor ThemeColor(string key, byte fallbackR, byte fallbackG, byte fallbackB)
    {
        if (System.Windows.Application.Current?.Resources[key] is System.Windows.Media.SolidColorBrush brush)
        {
            var c = brush.Color;
            return new SKColor(c.R, c.G, c.B);
        }
        return new SKColor(fallbackR, fallbackG, fallbackB);
    }

    private void RefreshChart()
    {
        var data = Granularity == ChartGranularity.Custom
            ? SalesChartService.BuildRange(CustomFromDate, CustomToDate)
            : SalesChartService.Build(Granularity, ChartAnchor);
        var values = data.Select(d => (double)d.Revenue).ToArray();
        var labels = data.Select(d => d.Label).ToArray();

        var accent = ThemeColor("Accent", 0xec, 0x30, 0x13);
        var accentLight = accent.WithAlpha(90);
        var ink = ThemeColor("Neutral600", 0x6b, 0x66, 0x63);
        var gridLine = ThemeColor("Divider", 0xe3, 0xe1, 0xe0).WithAlpha(160);
        var isComparing = CompareEnabled && CanCompare;

        var currentSeries = new ColumnSeries<double>
        {
            // «Φέτος» έχει νόημα μόνο δίπλα σε «Πέρσι» — αλλιώς (π.χ. ΧΡΟΝΟΣ με πολλά έτη μαζί) μπερδεύει.
            Name = isComparing ? "Φέτος" : "Τζίρος",
            Values = values,
            Rx = 6,
            Ry = 6,
            MaxBarWidth = 42,
            // Ανοιχτό στην κορυφή, γεμάτο accent στη βάση — πιο ζωντανό από ενιαίο χρώμα.
            Fill = new LinearGradientPaint(accentLight, accent, new SKPoint(0.5f, 0), new SKPoint(0.5f, 1)),
            YToolTipLabelFormatter = point => Order.FormatPrice((decimal)point.Model),
            XToolTipLabelFormatter = point => labels.Length > point.Index ? labels[point.Index] : "",
            AnimationsSpeed = TimeSpan.FromMilliseconds(500),
        };

        // Γραμμή πάνω από τις κορυφές των φετινών μπαρών, με κόκκινες τελείες σε κάθε σημείο.
        var trendLine = new LineSeries<double>
        {
            Name = "Τάση",
            Values = values,
            Fill = null,
            Stroke = new SolidColorPaint(accent) { StrokeThickness = 2 },
            GeometryFill = new SolidColorPaint(accent),
            GeometryStroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1.5f },
            GeometrySize = 9,
            LineSmoothness = 0.4,
            YToolTipLabelFormatter = point => Order.FormatPrice((decimal)point.Model),
            XToolTipLabelFormatter = point => labels.Length > point.Index ? labels[point.Index] : "",
            AnimationsSpeed = TimeSpan.FromMilliseconds(500),
        };

        if (isComparing)
        {
            var compareValues = (Granularity == ChartGranularity.Custom
                    ? SalesChartService.BuildRangeCompareValues(CustomFromDate, CustomToDate)
                    : SalesChartService.BuildCompareValues(Granularity, ChartAnchor))
                .Select(r => (double)r)
                .ToArray();
            // Ίδιο μήκος με τη φετινή σειρά, ώστε να ταιριάζουν θέση-με-θέση στο ίδιο διάγραμμα.
            if (compareValues.Length != values.Length)
                Array.Resize(ref compareValues, values.Length);

            var blue = new SKColor(0x2f, 0x6f, 0xed);
            var blueLight = blue.WithAlpha(90);
            var compareSeries = new ColumnSeries<double>
            {
                Name = "Πέρσι",
                Values = compareValues,
                Rx = 6,
                Ry = 6,
                MaxBarWidth = 42,
                Fill = new LinearGradientPaint(blueLight, blue, new SKPoint(0.5f, 0), new SKPoint(0.5f, 1)),
                YToolTipLabelFormatter = point => Order.FormatPrice((decimal)point.Model),
                XToolTipLabelFormatter = point => labels.Length > point.Index ? labels[point.Index] : "",
                AnimationsSpeed = TimeSpan.FromMilliseconds(500),
            };
            Series = [currentSeries, compareSeries, trendLine];
        }
        else
        {
            Series = [currentSeries, trendLine];
        }

        XAxes =
        [
            new Axis
            {
                Labels = labels,
                TextSize = 11,
                // Πολλές μπάρες (π.χ. μεγάλο ΠΡΟΣΑΡΜΟΣΜΕΝΟ διάστημα) — γυρίζει τις ετικέτες για να μη μπερδεύονται.
                LabelsRotation = labels.Length > 15 ? 60 : 0,
                LabelsPaint = new SolidColorPaint(ink),
                SeparatorsPaint = null,
            },
        ];

        YAxes =
        [
            new Axis
            {
                Labeler = value => Order.FormatPrice((decimal)value),
                MinLimit = 0,
                TextSize = 11,
                LabelsPaint = new SolidColorPaint(ink),
                SeparatorsPaint = new SolidColorPaint(gridLine) { StrokeThickness = 1 },
            },
        ];

        var total = data.Sum(d => d.Revenue);
        ChartTotalLabel = Order.FormatPrice(total);
        ChartNoData = total == 0;

        ChartAnchorLabel = Granularity switch
        {
            ChartGranularity.Hour => ChartAnchor.ToString("dddd d MMMM yyyy", Greek),
            ChartGranularity.Day => ChartAnchor.ToString("MMMM yyyy", Greek),
            ChartGranularity.Month => ChartAnchor.ToString("yyyy", Greek),
            _ => SalesChartService.DecadeStart(ChartAnchor) + "–" + (SalesChartService.DecadeStart(ChartAnchor) + 9),
        };

        OnPropertyChanged(nameof(Series));
        OnPropertyChanged(nameof(XAxes));
        OnPropertyChanged(nameof(YAxes));
        OnPropertyChanged(nameof(ChartTotalLabel));
        OnPropertyChanged(nameof(ChartNoData));
        OnPropertyChanged(nameof(ChartHasData));
        OnPropertyChanged(nameof(ChartAnchorLabel));
        OnPropertyChanged(nameof(ChartLegendPosition));
    }

    /// <summary>Λεζάντα (Φέτος/Πέρσι) μόνο όταν είναι ενεργή η σύγκριση — μία σειρά δεν τη χρειάζεται.</summary>
    public LegendPosition ChartLegendPosition => CompareEnabled && CanCompare ? LegendPosition.Top : LegendPosition.Hidden;

    private static readonly System.Globalization.CultureInfo Greek =
        System.Globalization.CultureInfo.GetCultureInfo("el-GR");

    /// <summary>Ζωντανή μέρα + ώρα στο header.</summary>
    public string NowLabel => DateTime.Now.ToString("dddd d MMMM yyyy · HH:mm:ss", Greek);

    public string RevenueLabel { get; private set; } = Order.FormatPrice(0);
    public string AvgOrderLabel { get; private set; } = Order.FormatPrice(0);

    public string MorningRevenueLabel { get; private set; } = Order.FormatPrice(0);
    public string MorningCountLabel { get; private set; } = "0 παραγγελίες";
    public string EveningRevenueLabel { get; private set; } = Order.FormatPrice(0);
    public string EveningCountLabel { get; private set; } = "0 παραγγελίες";

    /// <summary>Οι παραγγελίες που περιμένουν ακόμα κανάλι στις Ζωντανές — δεν μετράνε σε καμία βάρδια
    /// (βλ. SalesStatsService.CountedOrders), αλλά φαίνονται εδώ ώστε να μη «λείπουν» λεφτά αναπάντητα.</summary>
    public string PendingRevenueLabel { get; private set; } = Order.FormatPrice(0);
    public string PendingCountLabel { get; private set; } = "0 παραγγελίες";
    public bool HasPending { get; private set; }

    public IReadOnlyList<ProductStatViewModel> ProductStats { get; private set; } = [];
    /// <summary>Οι κάρτες καναλιού της ΠΡΩΙΝΗΣ και της ΒΡΑΔΙΝΗΣ βάρδιας, χωριστά.</summary>
    public IReadOnlyList<ChannelRevenueViewModel> MorningChannelRevenues { get; private set; } = [];
    public IReadOnlyList<ChannelRevenueViewModel> EveningChannelRevenues { get; private set; } = [];

    public bool NoData { get; private set; } = true;
    public bool HasData => !NoData;

    /// <summary>Το όνομα του προϊόντος όπως το λέει ο ΚΑΤΑΛΟΓΟΣ — καθαρό, χωρίς ψωμί και «ΔΙΠΛΗ ΠΙΤΑ»
    /// μπροστά. Αν το προϊόν έχει διαγραφεί (ή η παραγγελία είναι παλιά, χωρίς ProductId), πέφτει πίσω
    /// στο όνομα που πουλήθηκε περισσότερο — κάτι είναι πάντα καλύτερο από κενή γραμμή.</summary>
    private static string ProductDisplayName(IEnumerable<SoldLine> lines)
    {
        var list = lines.ToList();
        var id = list[0].ProductId;
        if (id.Length > 0)
        {
            var product = MenuStore.Instance.Categories
                .SelectMany(c => c.Products)
                .FirstOrDefault(p => p.Id == id);
            if (product is not null)
                return product.Name;
        }
        return list.GroupBy(l => l.Name).OrderByDescending(g => g.Sum(l => l.Quantity)).First().Key;
    }

    /// <summary>Πώς γράφεται μια παραλλαγή: ό,τι διαφέρει από το σκέτο όνομα του προϊόντος (π.χ. «ΕΛ.»,
    /// «ΔΙΠΛΗ ΠΙΤΑ») μπροστά, και μετά οι λεπτομέρειες (χωρίς κρεμμύδι, + αλλαντικά).</summary>
    private static string VariantLabel(string lineName, string details, string productName)
    {
        var prefix = lineName == productName ? "" : lineName;
        if (prefix.Length == 0)
            return details;
        return details.Length == 0 ? prefix : prefix + " · " + details;
    }

    /// <summary>Δείχνει η λίστα ΚΑΤΗΓΟΡΙΕΣ αντί για προϊόντα; Ίδιες στήλες, άλλη ομαδοποίηση — «πόσο
    /// πούλησαν οι ΠΙΤΤΕΣ συνολικά» είναι άλλη ερώτηση από «ποιο προϊόν πάει καλύτερα».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ByProduct))]
    [NotifyPropertyChangedFor(nameof(BreakdownHeader))]
    private bool _byCategory;

    public bool ByProduct => !ByCategory;

    /// <summary>Το κουμπί γράφει πάντα τι δείχνει η λίστα τώρα — ένα κουμπί, οι επιλογές από μέσα.</summary>
    public string BreakdownHeader => (ByCategory ? "ΑΝΑ ΚΑΤΗΓΟΡΙΑ" : "ΑΝΑ ΠΡΟΪΟΝ") + " ▾";

    /// <summary>Ανοιχτό/κλειστό το μενού επιλογής.</summary>
    [ObservableProperty]
    private bool _showBreakdownMenu;

    [RelayCommand] private void ToggleBreakdownMenu() => ShowBreakdownMenu = !ShowBreakdownMenu;

    partial void OnByCategoryChanged(bool value)
    {
        ShowBreakdownMenu = false;
        Refresh();
    }

    // Το κλείσιμο του μενού γίνεται ΕΔΩ και όχι μόνο στο OnByCategoryChanged: αν ξαναδιαλέξεις αυτό
    // που ήδη βλέπεις, η τιμή δεν αλλάζει, δεν σηκώνεται event, και το μενού έμενε ανοιχτό.
    [RelayCommand]
    private void ShowByProduct()
    {
        ByCategory = false;
        ShowBreakdownMenu = false;
    }

    [RelayCommand]
    private void ShowByCategory()
    {
        ByCategory = true;
        ShowBreakdownMenu = false;
    }

    /// <summary>Πωλήσεις ανά κατηγορία καταλόγου. Η ανάλυση από κάτω είναι τα ΠΡΟΪΟΝΤΑ της κατηγορίας —
    /// έτσι η ίδια οθόνη απαντά και «πόσο έκαναν οι ΜΕΡΙΔΕΣ» και «ποια μερίδα τράβηξε».</summary>
    private static List<ProductStatViewModel> BuildCategoryStats(List<CompletedOrder> orders, decimal revenue)
    {
        var categoryOf = MenuStore.Instance.Categories
            .SelectMany(c => c.Products.Select(p => (p.Id, Category: c.Name)))
            .ToDictionary(x => x.Id, x => x.Category);

        return orders
            .SelectMany(o => o.Lines)
            .GroupBy(l => categoryOf.GetValueOrDefault(l.ProductId, "— ΕΚΤΟΣ ΚΑΤΑΛΟΓΟΥ —"))
            .Select(g => (
                Name: g.Key,
                Quantity: g.Sum(l => l.Quantity),
                Revenue: g.Sum(l => l.Revenue),
                Products: g.GroupBy(l => l.ProductId.Length > 0 ? l.ProductId : l.Name)
                    .Select(p => (Name: ProductDisplayName(p), Quantity: p.Sum(l => l.Quantity), Revenue: p.Sum(l => l.Revenue)))
                    .OrderByDescending(p => p.Quantity)
                    .ToList()))
            .OrderByDescending(c => c.Revenue)
            .Select((c, i) => new ProductStatViewModel
            {
                Rank = i + 1,
                Name = c.Name,
                Quantity = c.Quantity,
                Revenue = c.Revenue,
                SharePct = revenue == 0 ? 0 : (double)(c.Revenue / revenue * 100),
                Variants = c.Products.Select(p => new ProductVariantStatViewModel
                {
                    Quantity = p.Quantity,
                    Details = p.Name,
                    Revenue = p.Revenue,
                }).ToList(),
            })
            .ToList();
    }

    private void Refresh()
    {
        // ΟΧΙ _stats.Orders: όσες κάθονται ακόμα στην αναμονή του πίνακα δεν έχουν μπει στον τζίρο
        // (βλ. SalesStatsService.CountedOrders) — μετράνε μόλις περάσουν σε κανάλι.
        var orders = _stats.CountedOrders;
        var awaiting = _stats.AwaitingChannelOrders;

        var revenue = orders.Sum(o => o.Total);
        RevenueLabel = Order.FormatPrice(revenue);
        AvgOrderLabel = Order.FormatPrice(orders.Count == 0 ? 0 : revenue / orders.Count);

        // Ομαδοποίηση ανά ΠΡΟΪΟΝ, όχι ανά όνομα γραμμής. Το όνομα κουβαλάει τις προσαρμογές —
        // ψωμί μπροστά («ΕΛ. Κοτόπουλο» / «ΑΡ. Κοτόπουλο») και «ΔΙΠΛΗ ΠΙΤΑ» — οπότε η ίδια πίττα
        // κοτόπουλο σπάει σε τρία-τέσσερα «προϊόντα» και δεν βλέπεις ποτέ πόσες πούλησες συνολικά.
        // Κλειδί το ProductId· οι παραλλαγές (χωρίς κρεμμύδι, + αλλαντικά) μένουν από κάτω, αναλυτικά.
        ProductStats = ByCategory ? BuildCategoryStats(orders, revenue) : orders
            .SelectMany(o => o.Lines)
            .GroupBy(l => l.ProductId.Length > 0 ? l.ProductId : l.Name)
            .Select(g =>
            {
                // Μία φορά ανά προϊόν: το ProductDisplayName σαρώνει ολόκληρο τον κατάλογο, δεν έχει
                // νόημα να ξανατρέξει για κάθε παραλλαγή της ίδιας γραμμής.
                var name = ProductDisplayName(g);
                return (
                    Name: name,
                    Quantity: g.Sum(l => l.Quantity),
                    Revenue: g.Sum(l => l.Revenue),
                    // Η παραλλαγή κρατά ΚΑΙ το όνομα της γραμμής: το ψωμί και η «ΔΙΠΛΗ ΠΙΤΑ» ζουν μέσα
                    // στο όνομα, όχι στις λεπτομέρειες — αλλιώς θα εξαφανίζονταν από την ανάλυση.
                    Variants: g.GroupBy(l => (l.Name, l.Details))
                        .Select(v => (
                            Details: VariantLabel(v.Key.Name, v.Key.Details, name),
                            Quantity: v.Sum(l => l.Quantity),
                            Revenue: v.Sum(l => l.Revenue)))
                        .OrderByDescending(v => v.Quantity)
                        .ToList());
            })
            .OrderByDescending(p => p.Quantity)
            .ThenByDescending(p => p.Revenue)
            .Select((p, i) => new ProductStatViewModel
            {
                Rank = i + 1,
                Name = p.Name,
                Quantity = p.Quantity,
                Revenue = p.Revenue,
                SharePct = revenue == 0 ? 0 : (double)(p.Revenue / revenue * 100),
                // Ανάλυση μόνο όταν υπάρχει κάποια προσαρμογή — αλλιώς η γραμμή αρκεί
                Variants = p.Variants.Any(v => v.Details.Length > 0)
                    ? p.Variants.Select(v => new ProductVariantStatViewModel
                    {
                        Quantity = v.Quantity,
                        Details = v.Details,
                        Revenue = v.Revenue,
                    }).ToList()
                    : [],
            })
            .ToList();

        var morning = orders.Where(o => !o.IsEveningShift).ToList();
        var evening = orders.Where(o => o.IsEveningShift).ToList();

        // Δύο ξεχωριστές σειρές καρτών, μία ανά βάρδια: ο ταμίας συγκρίνει «ΤΡΑΠΕΖΙ πρωί» με
        // «ΤΡΑΠΕΖΙ βράδυ» διαβάζοντας κάθετα, αντί να ψάχνει δύο ψιλά νούμερα μέσα σε κάθε κάρτα.
        MorningChannelRevenues = BuildChannelRevenues(morning);
        EveningChannelRevenues = BuildChannelRevenues(evening);
        MorningRevenueLabel = Order.FormatPrice(morning.Sum(o => o.Total));
        MorningCountLabel = CountLabel(morning.Count);
        EveningRevenueLabel = Order.FormatPrice(evening.Sum(o => o.Total));
        EveningCountLabel = CountLabel(evening.Count);

        PendingRevenueLabel = Order.FormatPrice(awaiting.Sum(o => o.Total));
        PendingCountLabel = CountLabel(awaiting.Count);
        HasPending = awaiting.Count > 0;

        NoData = orders.Count == 0;

        OnPropertyChanged(nameof(RevenueLabel));
        OnPropertyChanged(nameof(AvgOrderLabel));
        OnPropertyChanged(nameof(ProductStats));
        OnPropertyChanged(nameof(MorningChannelRevenues));
        OnPropertyChanged(nameof(EveningChannelRevenues));
        OnPropertyChanged(nameof(NoData));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(MorningRevenueLabel));
        OnPropertyChanged(nameof(MorningCountLabel));
        OnPropertyChanged(nameof(EveningRevenueLabel));
        OnPropertyChanged(nameof(EveningCountLabel));
        OnPropertyChanged(nameof(PendingRevenueLabel));
        OnPropertyChanged(nameof(PendingCountLabel));
        OnPropertyChanged(nameof(HasPending));

        // Το διάγραμμα ξαναδιαβάζει αρχεία ιστορικού (ακριβό για ΜΗΝΑΣ/ΧΡΟΝΟΣ) — μόνο όταν
        // είναι ορατό, ώστε μια νέα παραγγελία στην κίνηση να μην ξανασαρώνει αρχεία άδικα.
        if (MainTab == StatsMainTab.Chart)
            RefreshChart();
    }

    private static string CountLabel(int count) => count + (count == 1 ? " παραγγελία" : " παραγγελίες");

    /// <summary>Σταθερή σειρά καναλιών όπως στο ταμείο — πάντα και τα έξι, και με μηδέν. Πρωί/βράδυ πάντα μαζί.</summary>
    private static List<ChannelRevenueViewModel> BuildChannelRevenues(List<CompletedOrder> orders)
    {
        // Από το ενεργό θέμα, ώστε να φαίνεται σωστά και στο μαύρο
        var ink = System.Windows.Application.Current.Resources["Ink"] as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.Black;

        (string Name, System.Windows.Media.Brush Brush, Func<CompletedOrder, bool> Match, bool SplitsPayment)[] channels =
        [
            ("ΔΙΑΝΟΜΗ", ink, o => o.Type == OrderType.Delivery, false),
            ("ΟΡΘΙΟ", ink, o => o.Type == OrderType.Pickup, false),
            ("ΤΡΑΠΕΖΙ", ink, o => o.Type == OrderType.Table, false),
            ("e-food", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd3, 0x2f, 0x2f)),
                o => o.Channel == "e-food", false),
            ("Wolt", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x15, 0x65, 0xc0)),
                o => o.Channel == "Wolt", false),
            ("BOX", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xb8, 0x86, 0x0b)),
                o => o.Channel == "BOX", true),
        ];

        return channels.Select(c =>
        {
            var matched = orders.Where(c.Match).ToList();
            return new ChannelRevenueViewModel
            {
                Name = c.Name,
                Brush = c.Brush,
                OrderCount = matched.Count,
                Revenue = matched.Sum(o => o.Total),
                HasPaymentSplit = c.SplitsPayment,
                // Χωρίς δηλωμένο τρόπο πληρωμής (παλιά ή διορθωμένη παραγγελία) μετράει ως μετρητά —
                // έτσι τα δύο νούμερα αθροίζουν πάντα στον τζίρο της κάρτας από πάνω.
                CashRevenue = c.SplitsPayment
                    ? matched.Where(o => o.PaymentMethod != Core.Models.PaymentMethod.Card).Sum(o => o.Total) : 0m,
                CardRevenue = c.SplitsPayment
                    ? matched.Where(o => o.PaymentMethod == Core.Models.PaymentMethod.Card).Sum(o => o.Total) : 0m,
            };
        }).ToList();
    }
}
