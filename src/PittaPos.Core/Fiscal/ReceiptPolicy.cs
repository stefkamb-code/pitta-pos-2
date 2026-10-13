using PittaPos.Core.Models;

namespace PittaPos.Core.Fiscal;

/// <summary>Πότε φεύγει μια απόδειξη στον πάροχο.</summary>
public enum ReceiptTiming
{
    /// <summary>Με την καταχώρηση της παραγγελίας.</summary>
    Now,
    /// <summary>Όταν η διανομή περάσει σε διανομέα (Ζωντανές → «ΠΕΡΑΣΕ ΤΗΝ ΣΕ») — η στιγμή που ξεκινά η αποστολή και που
    /// η παραγγελία μπαίνει στον τζίρο.</summary>
    AtDispatch,
    /// <summary>Όταν πληρώσει ο πελάτης στο ταμείο: «🧾 ΑΠΟΔΕΙΞΗ» στο Ιστορικό/κινητό, ή αλλαγή σε κάρτα.</summary>
    AtPayment,
}

/// <summary>Η απόφαση για μία απόδειξη: πότε, το αργότερο πότε, και γιατί (γράφεται στο ημερολόγιο ελέγχου).</summary>
public sealed record ReceiptDecision(ReceiptTiming Timing, TimeSpan MaxWait, string Reason)
{
    public bool Deferred => Timing != ReceiptTiming.Now;

    /// <summary>Για την οθόνη: τι περιμένει η απόδειξη.</summary>
    public string WaitsFor => Timing switch
    {
        ReceiptTiming.AtDispatch => "κόβεται όταν φύγει με τον διανομέα",
        ReceiptTiming.AtPayment => "κόβεται με την πληρωμή (🧾 ΑΠΟΔΕΙΞΗ)",
        _ => "",
    };
}

/// <summary>
/// <b>Ο ΜΟΝΟΣ τόπος</b> που αποφασίζεται πότε κόβεται μια απόδειξη εκτός τραπεζιού — όλο το υπόλοιπο ταμείο ρωτάει εδώ.
///
/// <para>Ο διακόπτης «Αυτόματη έκδοση απόδειξης» αλλάζει ΜΟΝΟ το πότε, ποτέ το αν: με κλειστό διακόπτη η παραγγελία
/// καταχωρείται, τυπώνεται στην κουζίνα, διορθώνεται και ακυρώνεται χωρίς να φύγει τίποτα στον πάροχο, και η απόδειξη
/// κόβεται τη στιγμή της αποστολής/πληρωμής — και σε κάθε περίπτωση το αργότερο σε <see cref="ReceiptDecision.MaxWait"/>
/// ή στο κλείσιμο της ημέρας. Ό,τι ακυρωθεί πριν, δεν κόβεται ποτέ (δεν έγινε πώληση).</para>
///
/// <para>Πάντα αυτόματα, ό,τι κι αν λέει ο διακόπτης:</para>
/// <list type="bullet">
/// <item>ΟΡΘΙΟΣ με κάρτα — η απόδειξη και η χρέωση στο τερματικό πάνε μαζί (διασύνδεση POS–παρόχου).</item>
/// <item>BOX, Wolt, e-food.</item>
/// </list>
/// <para>Ρυθμιζόμενα: ΔΙΑΝΟΜΗ (τηλέφωνο), μετρητά ή κάρτα — σε κάρτα η απόδειξη προφορτώνεται στο POS του διανομέα τη στιγμή
/// που φεύγει, πριν χρεωθεί ο πελάτης στην πόρτα. ΟΡΘΙΟΣ με μετρητά.</para>
/// <para>Τα τραπέζια δεν περνούν από εδώ: δελτίο 8.6 σε κάθε γύρο και απόδειξη με την πληρωμή, όπως ορίζει ο νόμος.</para>
/// </summary>
public static class ReceiptPolicy
{
    /// <summary>ΟΡΘΙΟΣ με μετρητά: ο πελάτης είναι στο μαγαζί — όσο να πάρει την παραγγελία του ή να αλλάξει γνώμη για τον τρόπο.</summary>
    public static readonly TimeSpan CounterMaxWait = TimeSpan.FromHours(1);

    /// <summary>ΔΙΑΝΟΜΗ: κόβεται όταν φύγει με τον διανομέα· αυτό είναι το δίχτυ αν δεν περαστεί ποτέ στις Ζωντανές.</summary>
    public static readonly TimeSpan PhoneMaxWait = TimeSpan.FromHours(2);

    /// <param name="autoIssue">Ο διακόπτης «Αυτόματη έκδοση απόδειξης» του μαγαζιού.</param>
    public static ReceiptDecision Decide(OrderType type, string? channel, FiscalPay pay, bool autoIssue) => type switch
    {
        OrderType.Table => Now("τραπέζι: δελτίο σε κάθε γύρο, απόδειξη με την πληρωμή"),
        OrderType.Apps => Now($"{(string.IsNullOrWhiteSpace(channel) ? "εφαρμογή" : channel)}: πάντα αυτόματα"),
        OrderType.Pickup when pay == FiscalPay.Card => Now("όρθιος με κάρτα: πάντα αυτόματα, μαζί με τη χρέωση στο τερματικό"),
        OrderType.Pickup when autoIssue => Now("όρθιος μετρητά: αυτόματη έκδοση"),
        OrderType.Pickup => new(ReceiptTiming.AtPayment, CounterMaxWait, "όρθιος μετρητά: με την πληρωμή (αυτόματη έκδοση κλειστή)"),
        OrderType.Delivery when autoIssue => Now("διανομή: αυτόματη έκδοση"),
        OrderType.Delivery => new(ReceiptTiming.AtDispatch, PhoneMaxWait, "διανομή: όταν φύγει με τον διανομέα (αυτόματη έκδοση κλειστή)"),
        _ => Now("άγνωστο είδος παραγγελίας: αυτόματα"),
    };

    private static ReceiptDecision Now(string reason) => new(ReceiptTiming.Now, TimeSpan.Zero, reason);
}
