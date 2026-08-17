namespace PittaPos.App.Services;

/// <summary>
/// ΕΝΑ σημείο για κάθε ακύρωση παραγγελίας, από όπου κι αν γίνει: Ιστορικό, οθόνη τραπεζιού,
/// Ζωντανές Παραγγελίες. Ό,τι σβήνεται πηγαίνει στις ΑΚΥΡΩΜΕΝΕΣ και φεύγει από παντού αλλού.
///
/// Υπήρχε ήδη ο μηχανισμός, αλλά κάθε οθόνη καθάριζε ΑΛΛΑ services και ξεχνούσε άλλα: διαγραφή από
/// το Ιστορικό άφηνε την παραγγελία στις Ζωντανές Παραγγελίες (ο διανομέας συνέχιζε να τη βλέπει),
/// ακύρωση γύρου από το τραπέζι άφηνε την είσπραξή του στα μετρητά/κάρτα της ημέρας (ο τζίρος
/// έφευγε, τα λεφτά έμεναν), και ακύρωση από τις Ζωντανές Παραγγελίες άφηνε πίσω τις εξοφλήσεις
/// και τα άτομα του τραπεζιού. Τώρα και οι τρεις οθόνες καλούν από εδώ.
/// </summary>
public static class OrderCancellationService
{
    /// <summary>
    /// Ακυρώνει ΟΛΗ την παραγγελία. Τα προϊόντα της καταγράφονται στις ακυρωμένες
    /// (SalesStatsService.RemoveOrder) και φεύγει από τζίρο, Ιστορικό, πίνακα ζωντανών παραγγελιών
    /// και — αν είναι τραπέζι — από εξοφλήσεις, άτομα και εισπράξεις.
    /// </summary>
    public static void CancelOrder(int orderNumber, string cancelledBy)
    {
        // Το τραπέζι διαβάζεται ΠΡΙΝ φύγει η παραγγελία — μετά δεν υπάρχει από πού να βγει.
        var table = TableOf(orderNumber);

        SalesStatsService.Instance.RemoveOrder(orderNumber, cancelledBy);
        RemoveFromBoard(orderNumber);
        ClearTableTraces(table, orderNumber);
    }

    /// <summary>
    /// Ακυρώνει μια παρτίδα επιλεγμένων προϊόντων (κουμπί ακύρωσης στην οθόνη τραπεζιού).
    /// <para>Όταν από μια παραγγελία φεύγουν <b>ΟΛΑ</b> της τα προϊόντα, γίνεται μία ενέργεια «ακύρωση
    /// παραγγελίας» αντί για γραμμή-γραμμή. Δεν είναι καλλωπισμός: στο δεύτερο ταμείο κάθε διαγραφή
    /// γραμμής φεύγει προς το κύριο ασύγχρονα και το τοπικό αντίγραφο ενημερώνεται αργότερα, οπότε ο
    /// έλεγχος «έμεινε τίποτα;» της τελευταίας γραμμής κοίταζε παλιά εικόνα, τον έβρισκε γεμάτο, και η
    /// παραγγελία έμενε στις Ζωντανές Παραγγελίες με την είσπραξή της άθικτη.</para>
    /// </summary>
    public static void CancelLines(IEnumerable<(int OrderNumber, int LineIndex)> lines, string cancelledBy, DateTime cancelledAt)
    {
        foreach (var group in lines.GroupBy(l => l.OrderNumber))
        {
            var order = Find(group.Key);
            // Φθίνουσα σειρά: η διαγραφή μιας γραμμής μετακινεί τους δείκτες των επόμενων.
            var indexes = group.Select(l => l.LineIndex).Distinct().OrderByDescending(i => i).ToList();
            if (order is not null && indexes.Count >= order.Lines.Count)
            {
                CancelOrder(group.Key, cancelledBy);
                continue;
            }
            foreach (var lineIndex in indexes)
                CancelLine(group.Key, lineIndex, cancelledBy, cancelledAt);
        }
    }

    /// <summary>
    /// Ακυρώνει ΜΙΑ γραμμή μιας παραγγελίας. Αν ήταν η τελευταία της, η παραγγελία σβήνει ολόκληρη —
    /// οπότε πρέπει να φύγει και από τον πίνακα και τις εισπράξεις, σαν ακύρωση ολόκληρης παραγγελίας.
    /// </summary>
    public static void CancelLine(int orderNumber, int lineIndex, string cancelledBy, DateTime cancelledAt)
    {
        var order = Find(orderNumber);
        var table = TableOf(order);
        // Κρίνεται ΤΩΡΑ, όχι μετά: στο δεύτερο ταμείο το RemoveLine στέλνει τη μεταβολή στο κύριο
        // ταμείο και γυρίζει αμέσως, οπότε ο τοπικός έλεγχος «έμεινε τίποτα;» θα έλεγε πάντα ναι.
        var wasLastLine = order is not null && order.Lines.Count <= 1;

        if (table is int t)
        {
            // Πρώτα η μετακίνηση των δεικτών, μετά η διαγραφή — αλλιώς η εξόφληση/χρέωση των
            // επόμενων γραμμών κολλάει σε λάθος προϊόν.
            TableSettlementService.Instance.ShiftAfterRemoval(t, orderNumber, lineIndex);
            TablePersonsService.Instance.ShiftAfterRemoval(t, orderNumber, lineIndex);
        }
        SalesStatsService.Instance.RemoveLine(orderNumber, lineIndex, cancelledBy, cancelledAt);

        if (wasLastLine)
        {
            RemoveFromBoard(orderNumber);
            TablePaymentsService.Instance.RemoveFor(orderNumber);
        }
    }

    private static CompletedOrder? Find(int orderNumber) =>
        SalesStatsService.Instance.Orders.FirstOrDefault(o => o.OrderNumber == orderNumber);

    private static int? TableOf(int orderNumber) => TableOf(Find(orderNumber));

    /// <summary>Ο αριθμός τραπεζιού της παραγγελίας, ή null αν δεν είναι τραπέζι.</summary>
    private static int? TableOf(CompletedOrder? order) =>
        order is not null && int.TryParse(order.TableNumberLabel, out var table) ? table : null;

    private static void RemoveFromBoard(int orderNumber)
    {
        var board = OrderBoardService.Instance;
        if (board.Orders.FirstOrDefault(o => o.OrderNumber == orderNumber) is { } entry)
            board.Cancel(entry);
    }

    /// <summary>
    /// Σβήνει ό,τι κρατάει το τραπέζι για μια παραγγελία: εξοφλήσεις, χρέωση σε άτομο και είσπραξη.
    /// Καλείται και όταν η παραγγελία ακυρώνεται, και όταν <b>παύει να είναι τραπέζι</b> επειδή
    /// διορθώθηκε το κανάλι της από το Ιστορικό (βλ. SalesStatsService.UpdateChannel).
    /// </summary>
    public static void ClearTableTraces(int? table, int orderNumber)
    {
        if (table is int t)
        {
            TableSettlementService.Instance.ClearOrder(t, orderNumber);
            TablePersonsService.Instance.ClearOrder(t, orderNumber);
        }
        // Χωρίς έλεγχο τραπεζιού: η είσπραξη μπορεί να υπάρχει ακόμα και όταν η παραγγελία δεν
        // φαίνεται πια για τραπέζι, και θα συνέχιζε να μετράει στα μετρητά/κάρτα της ημέρας.
        // Σε ό,τι δεν είχε είσπραξη δεν κάνει τίποτα.
        TablePaymentsService.Instance.RemoveFor(orderNumber);
    }

    /// <summary>Ο αριθμός τραπεζιού μιας παραγγελίας, ή null αν δεν είναι τραπέζι — για καλούντες που
    /// κρατούν ήδη την παραγγελία στο χέρι (βλ. SalesStatsService.UpdateChannel).</summary>
    public static int? TableNumberOf(CompletedOrder order) => TableOf(order);
}
