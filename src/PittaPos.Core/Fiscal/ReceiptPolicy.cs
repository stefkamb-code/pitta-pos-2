using PittaPos.Core.Models;

namespace PittaPos.Core.Fiscal;

/// <summary>Πότε φεύγει μια απόδειξη στον πάροχο.</summary>
public enum ReceiptTiming
{
    /// <summary>Με την καταχώρηση της παραγγελίας.</summary>
    Now,
    /// <summary>Όταν έρθει και πληρώσει ο πελάτης: «ΕΚΤΥΠΩΣΗ ΠΑΡΑΣΤΑΤΙΚΟΥ» στο Ιστορικό/κινητό, ή αλλαγή σε κάρτα.</summary>
    AtPayment,
}

/// <summary>Η απόφαση για μία απόδειξη: πότε, το αργότερο πότε, και γιατί (γράφεται στο ημερολόγιο ελέγχου).</summary>
/// <param name="MaxWait">Όριο αναμονής μέσα στη μέρα· null = κανένα — περιμένει τον ταμία, και ό,τι ξεχαστεί κόβεται στο
/// κλείσιμο της ημέρας.</param>
public sealed record ReceiptDecision(ReceiptTiming Timing, TimeSpan? MaxWait, string Reason)
{
    public bool Deferred => Timing != ReceiptTiming.Now;

    /// <summary>Για την οθόνη: τι περιμένει η απόδειξη.</summary>
    public string WaitsFor => Deferred ? "κόβεται με την πληρωμή (ΕΚΤΥΠΩΣΗ ΠΑΡΑΣΤΑΤΙΚΟΥ)" : "";
}

/// <summary>
/// <b>Ο ΜΟΝΟΣ τόπος</b> που αποφασίζεται πότε κόβεται μια απόδειξη εκτός τραπεζιού — όλο το υπόλοιπο ταμείο ρωτάει εδώ.
///
/// <para>Ο διακόπτης στο παραθυράκι ΜΕΤΡΗΤΑ/ΚΑΡΤΑ αλλάζει ΜΟΝΟ το πότε, ποτέ το αν: με κλειστό διακόπτη η παραγγελία
/// καταχωρείται, τυπώνεται στην κουζίνα, διορθώνεται και ακυρώνεται χωρίς να φύγει τίποτα στον πάροχο, και η απόδειξη
/// κόβεται τη στιγμή που ο ταμίας πατά «ΕΚΤΥΠΩΣΗ ΠΑΡΑΣΤΑΤΙΚΟΥ» στο Ιστορικό (όταν έρθει ο πελάτης, ή όταν δίνει τη διανομή
/// στον διανομέα). Μέσα στη μέρα δεν κόβεται τίποτα μόνο του — ώστε η απόδειξη να μη βγει ποτέ πριν τη στιγμή που την
/// παίρνει ο πελάτης (στον έλεγχο μοιάζει με παλιά, ξαναχρησιμοποιημένη· απόφαση χρήστη 13/10/2026). Ό,τι ξεχαστεί (ούτε
/// κόπηκε ούτε ακυρώθηκε) κόβεται στο κλείσιμο της ημέρας: μένει στον τζίρο, άρα θέλει απόδειξη. Ό,τι ακυρωθεί πριν, δεν
/// κόβεται ποτέ (δεν έγινε πώληση).</para>
///
/// <para>Πάντα αυτόματα, ό,τι κι αν λέει ο διακόπτης:</para>
/// <list type="bullet">
/// <item>ΚΑΡΤΑ (ΟΡΘΙΟΣ ή ΔΙΑΝΟΜΗ) — η απόδειξη πάει μαζί με τη χρέωση: στο τερματικό του ταμείου, ή προφορτωμένη στο POS του
///   διανομέα πριν φτάσει στην πόρτα. Γι' αυτό με κλειστό διακόπτη το κουμπί ΚΑΡΤΑ σβήνει.</item>
/// <item>BOX, Wolt, e-food.</item>
/// </list>
/// <para>Ρυθμιζόμενα: ΟΡΘΙΟΣ και ΔΙΑΝΟΜΗ (τηλέφωνο) με μετρητά.</para>
/// <para>Τα τραπέζια δεν περνούν από εδώ: δελτίο 8.6 σε κάθε γύρο και απόδειξη με την πληρωμή, όπως ορίζει ο νόμος.</para>
/// </summary>
public static class ReceiptPolicy
{
    /// <param name="autoIssue">Ο διακόπτης του παραθύρου πληρωμής (ανοιχτός = απόδειξη τώρα).</param>
    public static ReceiptDecision Decide(OrderType type, string? channel, FiscalPay pay, bool autoIssue) => type switch
    {
        OrderType.Table => Now("τραπέζι: δελτίο σε κάθε γύρο, απόδειξη με την πληρωμή"),
        OrderType.Apps => Now($"{(string.IsNullOrWhiteSpace(channel) ? "εφαρμογή" : channel)}: πάντα αυτόματα"),
        OrderType.Pickup or OrderType.Delivery when pay == FiscalPay.Card =>
            Now("κάρτα: πάντα αυτόματα, μαζί με τη χρέωση (τερματικό ταμείου ή POS διανομέα)"),
        OrderType.Pickup when autoIssue => Now("όρθιος μετρητά: απόδειξη τώρα"),
        OrderType.Delivery when autoIssue => Now("διανομή μετρητά: απόδειξη τώρα"),
        OrderType.Pickup => new(ReceiptTiming.AtPayment, null, "όρθιος μετρητά: με την ΕΚΤΥΠΩΣΗ ΠΑΡΑΣΤΑΤΙΚΟΥ (διακόπτης κλειστός)"),
        OrderType.Delivery => new(ReceiptTiming.AtPayment, null, "διανομή μετρητά: με την ΕΚΤΥΠΩΣΗ ΠΑΡΑΣΤΑΤΙΚΟΥ (διακόπτης κλειστός)"),
        _ => Now("άγνωστο είδος παραγγελίας: αυτόματα"),
    };

    private static ReceiptDecision Now(string reason) => new(ReceiptTiming.Now, null, reason);
}
