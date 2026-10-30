using System.Globalization;
using System.Text.Json.Nodes;
using PittaPos.Core.Efood;
using PittaPos.Core.Models;

namespace PittaPos.Core.Box;

/// <summary>Μια παραγγελία Box μεταφρασμένη σε ό,τι καταλαβαίνει το ταμείο.</summary>
/// <param name="Order">Γραμμές, πελάτης, σημειώσεις — ίδια μορφή με μια παραγγελία e-food.</param>
/// <param name="Payment">Όπως θα τη σημείωνε ο ταμίας σε BOX: μετρητά = τα εισπράττει δικός μας διανομέας (ή το μαγαζί στην
/// παραλαβή)· κάρτα = πληρωμένη ΜΕΣΑ στην εφαρμογή της Box (απάντηση χρήστη, 21/10/2026).</param>
/// <param name="PaidInApp">Δεν περνάει ευρώ από εμάς: η απόδειξη γράφει «μέσω πλατφόρμας» και ΤΙΠΟΤΑ δεν προφορτώνεται στο
/// POS του διανομέα — ο πελάτης έχει ήδη πληρώσει.</param>
/// <param name="IsTest">Δοκιμαστική της Box: ΔΕΝ μπαίνει στα έσοδα ούτε στην ΑΑΔΕ.</param>
/// <param name="ItemsTotal">Ό,τι λέει η Box για τα προϊόντα (orderItemsPrice) — για έλεγχο, όχι για τον τζίρο.</param>
/// <param name="Street">Η διεύθυνση σε κομμάτια, για το πελατολόγιο — όπως θα τη γράφαμε στη φόρμα πελάτη.</param>
public sealed record BoxImport(EfoodImport Order, PaymentMethod Payment, bool PaidInApp, bool IsTest, decimal ItemsTotal,
    string Street, string StreetNumber, string Area, string PostalCode, string Comments);

/// <summary>
/// Μεταφράζει μια παραγγελία της Box (webhook, τεκμηρίωση 21/10/2026) σε παραγγελία ταμείου. Τη φέρνει στη μορφή του
/// e-food και τη διαβάζει ο <see cref="EfoodOrderReader"/> — ίδιοι κανόνες για ψωμί, «χωρίς», έξτρα, ΑΝΤΙΣΤΟΙΧΙΣΗ και
/// δελτίο, ώστε η Box να μη διαφέρει ποτέ από το e-food ή από ό,τι περνάει ο ταμίας:
///
/// <list type="bullet">
/// <item>Τιμές σε λεπτά → ευρώ. Το <c>finalPrice</c> είναι ανά τεμάχιο και περιέχει ήδη τις επιλογές (το
///   <c>orderItemsPrice</c> της Box = Σ finalPrice × ποσότητα + προσφορές) — οι επιλογές δεν ξαναμετρούν.</item>
/// <item>Κάθε προϊόν και επιλογή φέρνει το <c>friendlyId</c> της Box — αυτό είναι το κλειδί της ΑΝΤΙΣΤΟΙΧΙΣΗΣ.</item>
/// <item>Χωρίς <c>daasInfo</c> τη φέρνει δικός μας διανομέας (Δάφνη): τηλέφωνο, διεύθυνση, όροφος, κουδούνι στο χαρτί.
///   Με <c>daasInfo</c> τη φέρνει κούριερ της Box — ό,τι πληρωθεί πάει στη Box, όχι σε εμάς.</item>
/// <item>Φιλοδώρημα και μεταφορικά δεν είναι τζίρος (το ταμείο δεν έχει μεταφορικά) — φαίνονται στο χαρτί.</item>
/// </list>
///
/// <para><b>ΠΟΤΕ δεν πετάει εξαίρεση, ΠΟΤΕ δεν χάνει κάτι</b>, όπως ο <see cref="EfoodOrderReader"/>.</para>
/// </summary>
public static class BoxOrderReader
{
    /// <summary>Το όνομα στο χαρτί («ΔΙΑΝΟΜΗ Box», «Προϊόν Box»).</summary>
    public const string Platform = "Box";

    public static BoxImport Map(JsonObject box, IEfoodMenu menu) => Map(box, menu, EfoodContext.Empty);

    public static BoxImport Map(JsonObject box, IEfoodMenu menu, EfoodContext context)
    {
        var order = EfoodOrderReader.Map(ToEfoodShape(box), menu, context, Platform);
        // Προγραμματισμένη: πρώτο απ' όλα στο χαρτί — η κουζίνα δεν πρέπει να την ψήσει τώρα.
        if (Slot(box) is { Length: > 0 } slot)
            order = order with { Notes = order.Notes.Length > 0 ? slot + " · " + order.Notes : slot };

        var address = box["address"] as JsonObject;
        var paidInApp = IsCard(box) || BoxCourier(box);
        return new BoxImport(
            Order: order,
            Payment: paidInApp ? PaymentMethod.Card : PaymentMethod.Cash,
            PaidInApp: paidInApp,
            IsTest: box["isTestOrder"] is JsonValue test && test.TryGetValue(out bool isTest) && isTest,
            ItemsTotal: Cents(box["orderItemsPrice"]),
            Street: Text(address?["street"]),
            StreetNumber: Text(address?["streetNo"]),
            Area: Text(address?["city"]),
            PostalCode: Text(address?["postalCode"]),
            Comments: Text(box["comments"]));
    }

    /// <summary>
    /// Η παραγγελία της Box στη μορφή του e-food, ώστε να τη διαβάσει ο <see cref="EfoodOrderReader"/>: ποσά σε ευρώ, ο
    /// πελάτης με τη διεύθυνση σε κομμάτια, προϊόντα με τις επιλογές τους ως materials (με 0 — είναι ήδη μέσα στην τιμή).
    /// </summary>
    public static JsonObject ToEfoodShape(JsonObject box)
    {
        var takeaway = Text(box["orderShippingType"]).Equals("takeAway", StringComparison.OrdinalIgnoreCase);
        var address = box["address"] as JsonObject;
        var user = box["user"] as JsonObject;
        var deliveryFee = Cents(box["deliveryFee"]);
        return new JsonObject
        {
            ["id"] = Text(box["orderId"]),
            ["short_code"] = Text(box["simplifiedId"]),
            ["type"] = takeaway ? "takeaway" : "delivery",
            ["payment_type"] = Text(box["paymentType"]).ToLowerInvariant(),
            // Όπως το price του e-food: ό,τι πληρώνει ο πελάτης για προϊόντα και μεταφορικά — όχι το φιλοδώρημα.
            ["price"] = Cents(box["orderItemsPrice"]) + deliveryFee,
            ["delivery_fee"] = deliveryFee,
            ["tip"] = Cents(box["tip"]),
            ["customer"] = new JsonObject
            {
                ["name"] = Text(user?["firstName"]),
                ["surname"] = Text(user?["lastName"]),
                ["telephone"] = LocalPhone(Text(box["orderContactPhone"])),
                ["street"] = Text(address?["street"]),
                ["street_number"] = Text(address?["streetNo"]),
                ["area"] = Text(address?["city"]),
                ["postal_code"] = Text(address?["postalCode"]),
                ["floor"] = Text(address?["floor"]),
                ["doorbell"] = Text(address?["nameAtBell"]),
                ["notes"] = Text(box["comments"]),
            },
            ["transport_method"] = new JsonObject
            {
                ["key"] = takeaway ? "takeaway" : "delivery",
                ["delivery_provider"] = BoxCourier(box) ? "platform_delivery" : "vendor_delivery",
            },
            ["products"] = new JsonArray((box["products"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(p => (JsonNode?)Product(p, Cents(p["finalPrice"]), Count(p["quantity"]))).ToArray()),
            ["offers"] = new JsonArray((box["offers"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(o => (JsonNode?)new JsonObject
                {
                    ["name"] = Text(o["name"]),
                    ["price"] = Cents(o["price"]),
                    ["iteration"] = Math.Max(1, Count(o["quantity"])),
                    ["products"] = new JsonArray((o["products"] as JsonArray ?? []).OfType<JsonObject>()
                        .Select(p => (JsonNode?)Product(p, Cents(p["price"]), 1)).ToArray()),
                }).ToArray()),
        };
    }

    /// <summary>Κούριερ της Box (DaaS): το λέει μόνο η παρουσία του <c>daasInfo</c> — δεν υπάρχει άλλο πεδίο.</summary>
    private static bool BoxCourier(JsonObject box) => box["daasInfo"] is JsonObject;

    /// <summary>«+306912345678» → «6912345678», όπως το γράφει ο ταμίας και όπως το ψάχνει το πελατολόγιο. Η Box λέει
    /// ότι η μορφή «διαφέρει» (κάποιοι με πρόθεμα χώρας)· ό,τι δεν είναι ελληνικό κινητό/σταθερό μένει όπως ήρθε.</summary>
    private static string LocalPhone(string phone)
    {
        var digits = new string(phone.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        foreach (var prefix in new[] { "+30", "0030" })
            if (digits.StartsWith(prefix, StringComparison.Ordinal) && digits.Length == prefix.Length + 10 && digits[prefix.Length..].All(char.IsAsciiDigit))
                return digits[prefix.Length..];
        return phone;
    }

    private static bool IsCard(JsonObject box) => Text(box["paymentType"]).Equals("card", StringComparison.OrdinalIgnoreCase);

    private static JsonObject Product(JsonObject product, decimal unitPrice, int quantity) => new()
    {
        ["name"] = Text(product["name"]),
        ["price"] = unitPrice,
        ["quantity"] = Math.Max(1, quantity),
        ["notes"] = Text(product["comments"]),
        ["integrator_id"] = Text(product["friendlyId"]),
        ["materials"] = new JsonArray((product["selections"] as JsonArray ?? []).OfType<JsonObject>()
            .SelectMany(s => (s["optionsSelected"] as JsonArray ?? []).OfType<JsonObject>())
            .Select(o => (JsonNode?)new JsonObject
            {
                ["name"] = Text(o["title"]),
                ["integrator_id"] = Text(o["friendlyId"]),
                ["quantity"] = 1,
                ["price"] = 0m,
            }).ToArray()),
    };

    /// <summary>«ΓΙΑ ΤΙΣ 21:00–21:30» για προγραμματισμένη (κενό για «όσο πιο σύντομα»), σε ώρα Ελλάδας· με ημερομηνία όταν
    /// δεν είναι η ίδια μέρα με την παραγγελία.</summary>
    private static string Slot(JsonObject box)
    {
        if (!Time(box["timeSlotStart"], out var start))
            return "";
        var text = "ΓΙΑ ΤΙΣ " + start.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (Time(box["timeSlotEnd"], out var end) && end > start)
            text += "–" + end.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (Time(box["createdAt"], out var created) && created.Date != start.Date)
            text += " (" + start.ToString("d/M", CultureInfo.InvariantCulture) + ")";
        return text;
    }

    private static bool Time(JsonNode? node, out DateTime local)
    {
        local = default;
        var text = Text(node);
        if (text.Length == 0 || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return false;
        local = TimeZoneInfo.ConvertTime(parsed, Athens).DateTime;
        return true;
    }

    private static readonly TimeZoneInfo Athens = FindAthens();

    private static TimeZoneInfo FindAthens()
    {
        foreach (var id in new[] { "Europe/Athens", "GTB Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }

    /// <summary>Ποσό της Box (λεπτά, ακέραιος — ή κείμενο) σε ευρώ. 0 όταν λείπει.</summary>
    private static decimal Cents(JsonNode? node)
    {
        if (node is not JsonValue value)
            return 0m;
        // Από το δίκτυο έρχεται JsonElement (δέχεται κάθε τύπο)· ένας αριθμός φτιαγμένος στον κώδικα δέχεται μόνο τον δικό του.
        if (value.TryGetValue(out decimal m))
            return m / 100m;
        if (value.TryGetValue(out long l))
            return l / 100m;
        if (value.TryGetValue(out int i))
            return i / 100m;
        if (value.TryGetValue(out double d))
            return (decimal)d / 100m;
        if (value.TryGetValue(out string? s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed / 100m;
        return 0m;
    }

    private static int Count(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue(out int i))
                return i;
            if (value.TryGetValue(out double d))
                return (int)Math.Round(d);
            if (value.TryGetValue(out string? s) && int.TryParse(s, out var parsed))
                return parsed;
        }
        return 0;
    }

    /// <summary>Κείμενο από οποιαδήποτε τιμή (string/αριθμός) — κενό για null ή ό,τι δεν είναι τιμή.</summary>
    private static string Text(JsonNode? node)
    {
        if (node is not JsonValue value)
            return "";
        if (value.TryGetValue(out string? s))
            return s?.Trim() ?? "";
        if (value.TryGetValue(out long l))
            return l.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue(out double d))
            return d.ToString(CultureInfo.InvariantCulture);
        return "";
    }
}
