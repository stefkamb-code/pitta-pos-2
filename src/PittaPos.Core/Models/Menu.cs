namespace PittaPos.Core.Models;

/// <summary>Κατηγορία μενού (ΠΙΤΤΕΣ, ΜΕΡΙΔΕΣ, …). Επεξεργάσιμη από τη Διαχείριση Καταλόγου.</summary>
public class MenuCategory
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public List<Product> Products { get; set; } = [];

    // Οι τρεις παρακάτω συμπεριφορές είναι ΙΔΙΟΤΗΤΕΣ ΤΗΣ ΚΑΤΗΓΟΡΙΑΣ, όχι του ονόματός της. Παλιότερα
    // κρίνονταν συγκρίνοντας κυριολεκτικά το όνομα (π.χ. == "ΤΥΛΙΧΤΑ") — έτσι, μόλις το μαγαζί
    // μετονόμασε την κατηγορία σε «ΠΙΤΤΕΣ», έπαψαν σιωπηλά να δουλεύουν η επιλογή ψωμιού και η διπλή
    // πίτα σε ΟΛΑ τα προϊόντα της, χωρίς κανένα μήνυμα. Τώρα αποθηκεύονται μέσα στο menu.json και
    // ρυθμίζονται από τη Διαχείριση Καταλόγου, οπότε η μετονομασία δεν σπάει πια τίποτα.
    // null = δεν έχει αποφασιστεί ακόμα· το MenuStore το γεμίζει μία φορά από τους παλιούς κανόνες
    // ονόματος (βλ. MenuSeed/MenuStore.MigrateCategoryFlags), ώστε να μην αλλάξει τίποτα σε υπάρχοντα
    // καταστήματα.

    /// <summary>Αν τα προϊόντα της κατηγορίας ρωτούν ψωμί (Ελληνική/Αραβική/Ψωμί) στον customizer.</summary>
    public bool? HasBread { get; set; }

    /// <summary>Αν το ψωμί μπαίνει μέσα στο όνομα («ΑΡ. Κοτόπουλο») αντί σε ξεχωριστή γραμμή από κάτω.</summary>
    public bool? FuseBreadIntoName { get; set; }

    /// <summary>Αν προσφέρεται η επιλογή «διπλή πίτα» (με τη δική της χρέωση ανά κατηγορία).</summary>
    public bool? SupportsDoublePita { get; set; }
}

public class Product
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Προαιρετικό αγγλικό όνομα (δεύτερη γραμμή στο tile).</summary>
    public string? NameEn { get; set; }

    /// <summary>
    /// Όνομα ΜΟΝΟ για το χαρτί. Κενό ή null σημαίνει «τύπωσε ό,τι λέει και η οθόνη».
    ///
    /// Υπάρχει γιατί τα δύο κοινά δεν είναι ίδια: στην οθόνη ο ταμίας θέλει το πλήρες όνομα για να
    /// μην μπερδέψει δύο παρόμοια προϊόντα, ενώ στην απόδειξη μετράει το σύντομο που καταλαβαίνει ο
    /// ψήστης — και το χαρτί έχει μόλις 80mm πλάτος.
    /// </summary>
    public string? PrintName { get; set; }

    /// <summary>Το όνομα που πρέπει να τυπωθεί: το ειδικό αν έχει οριστεί, αλλιώς το κανονικό.</summary>
    public string NameForPrint => string.IsNullOrWhiteSpace(PrintName) ? Name : PrintName!;
    /// <summary>Περιγραφή/υλικά όπως στον κατάλογο.</summary>
    public string? Description { get; set; }
    /// <summary>Κανονική τιμή (όρθιο/τραπέζι/διανομή).</summary>
    public decimal Price { get; set; }
    /// <summary>Τιμή εφαρμογών (e-food/Wolt/BOX) — null = ίδια με την κανονική.</summary>
    public decimal? DeliveryPrice { get; set; }
    /// <summary>Αν ανοίγει customizer (ψωμί/υλικά/έξτρα) — «με τα υλικά της επιλογής σας».</summary>
    public bool Customizable { get; set; }
    /// <summary>Ονόματα από το κοινό MenuSeed.Extras που επιτρέπονται σε αυτό το προϊόν — null = όλα
    /// (προεπιλογή, ώστε παλιά προϊόντα να μη χάσουν έξτρα όταν προστέθηκε αυτό το πεδίο).</summary>
    public List<string>? ExtraNames { get; set; }
}

/// <summary>Έξτρα υλικό με τιμή (0 = δωρεάν). Επεξεργάσιμο από τη Διαχείριση Καταλόγου.</summary>
public class ExtraItem
{
    public required string Name { get; init; }
    public decimal Price { get; set; }
}
