using System.Globalization;

namespace PittaPos.Core.Models;

/// <summary>Προσαρμογή γραμμής για customizable προϊόντα (πίττες).</summary>
public class LineCustomization
{
    public string Bread { get; set; } = "Ελληνική";
    /// <summary>Υλικά που αφαιρέθηκαν από τα περιλαμβανόμενα.</summary>
    public List<string> Removed { get; set; } = [];
    /// <summary>Έξτρα → ποσότητα (μέγιστο 10 ανά έξτρα).</summary>
    public Dictionary<string, int> Extras { get; set; } = [];
    public string Note { get; set; } = "";
    /// <summary>Διπλή πίτα — μόνο στα ΤΥΛΙΧΤΑ και ΚΛΑΣΙΚΑ ΜΙΝΙ, βλ. <see cref="PittaPos.Core.Data.MenuSeed.SupportsDoublePita"/>.</summary>
    public bool DoublePita { get; set; }
}

public class OrderLine
{
    public required string ProductId { get; init; }
    public required string Name { get; init; }
    public int Quantity { get; set; } = 1;
    /// <summary>Τιμή μονάδας μαζί με τα έξτρα.</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>Έκπτωση είδους 0–50 % (βήμα 5).</summary>
    public int DiscountPct { get; set; }
    /// <summary>Χωρίς χρέωση — μηδενίζει τη γραμμή.</summary>
    public bool NoCharge { get; set; }
    public LineCustomization? Customization { get; set; }

    public decimal Total => NoCharge ? 0m : Quantity * UnitPrice * (1 - DiscountPct / 100m);
}

public class Order
{
    public long Id { get; set; }
    public int OrderNumber { get; set; }
    public OrderType Type { get; set; }
    public int? TableNumber { get; set; }
    public Customer? Customer { get; set; }
    /// <summary>Πλατφόρμα για παραγγελίες ΕΦΑΡΜΟΓΩΝ (e-food/Wolt/BOX).</summary>
    public DispatchChannel? AppPlatform { get; set; }
    /// <summary>Ώρα παραλαβής για ΠΑΡΑΛΑΒΗ (Άμεσα/15'/30'/45').</summary>
    public string? PickupTime { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
    /// <summary>Έκπτωση παραγγελίας 0–100 %.</summary>
    public int OrderDiscountPct { get; set; }

    public DateTimeOffset PlacedAt { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DispatchChannel? SentVia { get; set; }
    public DateTimeOffset? SentAt { get; set; }

    public decimal Subtotal => Lines.Sum(l => l.Quantity * l.UnitPrice);
    public decimal LineTotal => Lines.Sum(l => l.Total);
    public decimal Total => LineTotal * (1 - OrderDiscountPct / 100m);

    private static readonly CultureInfo Greek = CultureInfo.GetCultureInfo("el-GR");

    /// <summary>Μορφή τιμών του σχεδίου: «€3,20».</summary>
    public static string FormatPrice(decimal amount) =>
        "€" + amount.ToString("0.00", Greek);
}
