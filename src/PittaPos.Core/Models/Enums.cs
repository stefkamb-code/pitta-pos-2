namespace PittaPos.Core.Models;

/// <summary>Τύπος παραγγελίας — Βήμα 1 του wizard.</summary>
public enum OrderType
{
    /// <summary>ΔΙΑΝΟΜΗ — δικός μας διανομέας.</summary>
    Delivery,
    /// <summary>ΕΦΑΡΜΟΓΕΣ — e-food / Wolt / BOX.</summary>
    Apps,
    /// <summary>ΠΑΡΑΛΑΒΗ — από το κατάστημα.</summary>
    Pickup,
    /// <summary>ΤΡΑΠΕΖΙ — επί τόπου.</summary>
    Table,
}

/// <summary>Κανάλι αποστολής στο Live Orders Board («πέρασέ την σε»).</summary>
public enum DispatchChannel
{
    /// <summary>Ίδιος διανομέας.</summary>
    OwnDriver,
    Efood,
    Wolt,
    Box,
    /// <summary>Κάρτα Μαγαζιού.</summary>
    StoreCard,
}

/// <summary>Κατάσταση παραγγελίας στο Live Orders Board.</summary>
public enum OrderStatus
{
    /// <summary>Σε αναμονή (δεν έχει σταλεί σε κανάλι).</summary>
    Pending,
    /// <summary>Έχει σταλεί σε κανάλι.</summary>
    Dispatched,
}

/// <summary>Πώς θα πληρώσει ο πελάτης τον διανομέα — μόνο για παραγγελίες που παραδίδει δικός μας
/// διανομέας (ΔΙΑΝΟΜΗ, ή BOX που πλέον παραδίδεται κι αυτό από εμάς), ώστε να ξέρει πόσα μετρητά να
/// έχει πάνω του.</summary>
public enum PaymentMethod
{
    Cash,
    Card,
}
