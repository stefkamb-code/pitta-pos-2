using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PittaPos.App.Services;
using PittaPos.Core.Data;

namespace PittaPos.App.Views;

/// <summary>
/// Παράθυρο μόνο για μία απόδειξη — χωρίς καμία άλλη λειτουργία του Ιστορικού (χωρίς λίστα, χωρίς
/// διαγραφή, χωρίς PIN). Χρησιμοποιείται τόσο εκτός οθόνης (σιωπηλή αυτόματη εκτύπωση) όσο και ορατό
/// (χειροκίνητη επανεκτύπωση) — και στις δύο περιπτώσεις δεν εκθέτει καμία ενέργεια ασφαλείας.
/// </summary>
public partial class ReceiptWindow : Window
{
    public ReceiptWindow(CompletedOrder order)
    {
        InitializeComponent();
        // ΟΧΙ TitleBarTheme εδώ, σε αντίθεση με τα υπόλοιπα παράθυρα: αυτό ανοίγει εκτός οθόνης για να
        // τυπωθεί και το ύψος του βγαίνει από το ίδιο του το περιεχόμενο (SizeToContent). Οτιδήποτε
        // πειράζει το πλαίσιό του πριν μετρηθεί το περιεχόμενο χαλάει το ύψος της απόδειξης — και δεν
        // βγαίνει χαρτί. Μπάρα τίτλου ούτως ή άλλως δεν βλέπει σχεδόν ποτέ κανείς.

        // ΟΛΑ τα κείμενα της απόδειξης με ΚΕΦΑΛΑΙΑ (χωρίς τόνους) — ρητή απαίτηση για ευανάγνωστο σε
        // βιαστική ματιά. Τα στοιχεία καταστήματος έρχονται από τις Ρυθμίσεις όπως τα έγραψε ο χρήστης,
        // οπότε μετατρέπονται εδώ — μόνο για το χαρτί, οι ίδιες οι ρυθμίσεις μένουν όπως γράφτηκαν.
        // Στην κορυφή δεν τυπώνεται πια τίτλος με γράμματα: μπαίνει το ΛΟΓΟΤΥΠΟ (βλ. XAML).
        var s = SettingsStore.Instance.Settings;

        // Κατάστημα + ημερομηνία/ώρα σε ΜΙΑ γραμμή (μία γραμμή λιγότερη χαρτί ανά απόδειξη). Ενώνονται
        // εδώ και όχι στο XAML, ώστε να μη μένει ορφανός διαχωριστής όταν λείπει το ένα από τα δύο.
        var headerParts = new List<string>();
        if (s.ReceiptInfo.Length > 0)
            headerParts.Add(s.ReceiptInfo);
        if (s.ReceiptShowDateTime)
            headerParts.Add(order.DateTimeLabel);
        InfoText.Text = MenuSeed.ToUpperGreek(string.Join(" · ", headerParts));
        InfoText.Visibility = headerParts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Μία μόνο γραμμή στην κορυφή: κανάλι + αριθμός, π.χ. «WOLT #3232» ή «ΔΙΑΝΟΜΗ #1046» — χωρίς
        // ετικέτες «ΤΥΠΟΣ»/«ΠΑΡΑΓΓΕΛΙΑ» (δύο γραμμές λιγότερες, και είναι το πρώτο που κοιτάει ο
        // υπάλληλος). Σε Wolt/e-food/BOX μπαίνει ο αριθμός ΤΗΣ ΠΛΑΤΦΟΡΜΑΣ αμέσως μετά τη δίεση: αυτόν
        // ψάχνει ο υπάλληλος για να αντιστοιχίσει με την εφαρμογή, όχι τον δικό μας μετρητή. Ίδιος
        // κανόνας με το Ιστορικό (βλ. CompletedOrder.DisplayNumber), ώστε να μη διαφωνούν οθόνη-χαρτί.
        // ΤΡΑΠΕΖΙ: πάνω μπαίνει ο αριθμός ΤΡΑΠΕΖΙΟΥ («ΤΡΑΠΕΖΙ #6»), όχι ο μετρητής παραγγελίας — αυτόν
        // ψάχνει ο σερβιτόρος. Πριν τύπωνε «ΤΡΑΠΕΖΙ #1047» πάνω και «ΠΕΛΑΤΗΣ ΤΡΑΠΕΖΙ 6» από κάτω:
        // δύο γραμμές που έλεγαν το ίδιο πράγμα, με τον χρήσιμο αριθμό κρυμμένο στη δεύτερη.
        // Ένας κανόνας για όλα, μέσα στο DisplayNumber: πλατφόρμα → ο κωδικός της, τραπέζι → ο αριθμός
        // τραπεζιού, ΟΡΘΙΟΣ/ΔΙΑΝΟΜΗ → η σειρά της βάρδιας («#01»). Ο ΟΡΘΙΟΣ τύπωνε πριν σκέτη τη λέξη,
        // γιατί ο τότε αριθμός ήταν ο εσωτερικός μετρητής (#1046) και δεν τον φώναζε κανείς — τώρα ο
        // αριθμός ΕΙΝΑΙ η σειρά του πελάτη στη βάρδια, άρα μπαίνει κι εκεί.
        var label = MenuSeed.ToUpperGreek(order.TypeLabel);
        var typeLine = label + " #" + order.DisplayNumber;
        // ΤΡΑΠΕΖΙ: το άτομο μπαίνει ΜΟΝΟ όταν είναι προσθήκη σε τραπέζι που έχει ήδη παραγγείλει —
        // τότε το χαρτί αφορά έναν άνθρωπο και ο σερβιτόρος πρέπει να ξέρει σε ποιον πάει. Στην πρώτη
        // παραγγελία της παρέας τυπώνεται ΕΝΑ δελτίο για όλο το τραπέζι, χωρίς καμία αναφορά σε άτομα
        // (βλ. OrderWizardViewModel.PrintTableRounds — εκεί κρίνεται ποιο από τα δύο είναι).
        if (order.Type == Core.Models.OrderType.Table && order.HasPerson)
            typeLine += " · " + order.PersonLabel;
        // Και στις ΔΥΟ άκρες του χαρτιού, με το ίδιο ακριβώς κείμενο: όταν κρεμιέται η απόδειξη, το
        // θηλύκωμα σκεπάζει τη μία — έτσι ο αριθμός διαβάζεται πάντα από την άλλη, χωρίς ξεκρέμασμα.
        TypeText.Text = typeLine;
        FooterText.Text = typeLine;
        TotalText.Text = order.TotalLabel;

        // Όνομα και τηλέφωνο μαζί στη γραμμή ΠΕΛΑΤΗΣ — μία γραμμή λιγότερη. Το «·» μπαίνει μόνο όταν
        // υπάρχουν και τα δύο (π.χ. σε ΠΑΡΑΛΑΒΗ δεν κρατάμε τηλέφωνο, θα έμενε ορφανό).
        var who = MenuSeed.ToUpperGreek(order.WhoLabel);
        WhoText.Text = order.Phone.Length > 0 ? who + " · " + order.Phone : who;

        // Wolt/e-food: τα στοιχεία πελάτη τα κρατάει η πλατφόρμα, εμείς δεν καταχωρούμε καν όνομα —
        // η γραμμή τύπωνε σκέτο «ΠΕΛΑΤΗΣ —», δηλαδή μια χαμένη γραμμή χαρτί σε κάθε τέτοια παραγγελία.
        // Το BOX εξαιρείται: εκεί παραδίδει δικός μας διανομέας και έχουμε πραγματικά στοιχεία.
        // Κρύβεται και στα ΤΡΑΠΕΖΙΑ: το «ΠΕΛΑΤΗΣ ΤΡΑΠΕΖΙ 6» επαναλάμβανε αυτούσια την επικεφαλίδα.
        // ΕΞΑΙΡΕΣΗ: e-food από τη γέφυρα που τη φέρνει ΔΙΚΟΣ ΜΑΣ διανομέας — εκεί έχουμε όνομα ΚΑΙ τηλέφωνο,
        // και ο διανομέας τα χρειάζεται (βλ. EfoodOrderReader: τηλέφωνο κρατιέται μόνο τότε). Οι e-food/Wolt
        // που περνάει ο ταμίας με το χέρι δεν έχουν τηλέφωνο (η φόρμα πελάτη φαίνεται μόνο σε ΔΙΑΝΟΜΗ/BOX),
        // οπότε τυπώνονται ακριβώς όπως πριν.
        var platformOrder = order.Type == Core.Models.OrderType.Apps && order.Channel != "BOX" && order.Phone.Length == 0;
        CustomerRow.Visibility = s.ReceiptShowCustomer && !platformOrder
            && order.Type != Core.Models.OrderType.Table && order.Who.Trim().Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        PaymentText.Text = order.PaymentMethod switch
        {
            Core.Models.PaymentMethod.Cash => "💶 ΜΕΤΡΗΤΑ",
            Core.Models.PaymentMethod.Card => "💳 ΚΑΡΤΑ",
            _ => "",
        };
        PaymentRow.Visibility = order.HasPaymentMethod ? Visibility.Visible : Visibility.Collapsed;

        // ✓ δίπλα στο ΣΥΝΟΛΟ όταν έχει ήδη πληρωθεί με κάρτα — ο διανομέας το βλέπει στο χαρτί που
        // κρατάει και ξέρει αμέσως ότι δεν εισπράττει, χωρίς να ψάχνει τη γραμμή «ΠΛΗΡΩΜΗ» πιο πάνω.
        PaidMarkText.Visibility = order.PaymentMethod == Core.Models.PaymentMethod.Card
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Στοιχεία παράδοσης — τρεις ξεχωριστές γραμμές, η κάθε μία κρύβεται μόνη της αν είναι κενή
        // (π.χ. συχνά δεν υπάρχουν σχόλια) αντί να κρύβονται/φαίνονται όλα μαζί σαν ένα μπλοκ.
        AddressText.Text = MenuSeed.ToUpperGreek(order.DeliveryAddress);
        AddressBlock.Visibility = order.DeliveryAddress.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Όροφος και σχόλια σε μία γραμμή, με ετικέτα που λέει μόνο ό,τι πραγματικά υπάρχει — αλλιώς
        // τύπωνε ετικέτα «ΣΧΟΛΙΑ» με κενό από κάτω, ή δέσμευε τέσσερις γραμμές για δύο μικρές φράσεις.
        var floor = MenuSeed.ToUpperGreek(order.DeliveryFloor);
        var notes = MenuSeed.ToUpperGreek(order.DeliveryNotes);
        var labels = new List<string>();
        var values = new List<string>();
        if (floor.Length > 0)
        {
            labels.Add("ΟΡΟΦΟΣ");
            values.Add(floor);
        }
        if (notes.Length > 0)
        {
            labels.Add("ΣΧΟΛΙΑ");
            values.Add(notes);
        }
        FloorNotesLabel.Text = string.Join(" / ", labels);
        FloorNotesText.Text = string.Join(" · ", values);
        FloorNotesBlock.Visibility = values.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Αν δεν έμεινε τίποτα ορατό μέσα στο μπλοκ (τυπικά σε ΤΡΑΠΕΖΙ: ούτε πελάτης, ούτε πληρωμή,
        // ούτε διεύθυνση), το πλαίσιο τύπωνε ΔΥΟ οριζόντιες γραμμές με κενό ανάμεσά τους — δηλαδή δύο
        // γραμμές χαρτί που δεν έλεγαν τίποτα. Μένει μία, σαν απλός διαχωριστής κεφαλίδας/προϊόντων.
        var hasInfo = CustomerRow.Visibility == Visibility.Visible
            || PaymentRow.Visibility == Visibility.Visible
            || AddressBlock.Visibility == Visibility.Visible
            || FloorNotesBlock.Visibility == Visibility.Visible;
        if (!hasInfo)
        {
            InfoBlock.BorderThickness = new Thickness(0, 0, 0, 2);
            InfoBlock.Padding = new Thickness(0);
            InfoBlock.Margin = new Thickness(0, 0, 0, 8);
        }

        // Αν η ρύθμιση απόδειξης δεν θέλει λεπτομέρειες ανά γραμμή (ψωμί/έξτρα/σχόλιο), τις αδειάζουμε
        // εδώ αντί να χρειαστεί δεύτερο binding σε ολόκληρο ViewModel μόνο γι' αυτό. Όνομα προϊόντος και
        // λεπτομέρειες (σχόλια/έξτρα/χωρίς) γράφονται με ΚΕΦΑΛΑΙΑ μόνο εδώ, στην ίδια την απόδειξη — για
        // ευανάγνωστο σε βιαστική ματιά από τον υπάλληλο, χωρίς να αλλάζει πώς φαίνονται αλλού (Ιστορικό/
        // Στατιστικά συνεχίζουν να δείχνουν τα δεδομένα όπως καταχωρήθηκαν).
        LinesList.ItemsSource = (s.ReceiptShowDetails ? order.Lines : order.Lines.Select(l => l with { Details = "" }))
            // NameForPrint: αν το προϊόν έχει δικό του «όνομα εκτύπωσης» στη Διαχείριση Καταλόγου,
            // το χαρτί τυπώνει εκείνο· αλλιώς ό,τι δείχνει και η οθόνη.
            .Select(l => l with { Name = MenuSeed.ToUpperGreek(l.NameForPrint), Details = MenuSeed.ToUpperGreek(l.Details) })
            .ToList();

        // Τέσσερις ανεξάρτητες ρυθμίσεις μεγέθους (όχι μία κοινή κλίμακα) — ώστε π.χ. να μεγαλώνει ο
        // τίτλος χωρίς να αλλάζουν οι γραμμές παραγγελίας. Ο τίτλος/σύνολο παίρνουν απευθείας την τιμή
        // της ρύθμισης· η ετικέτα «ΣΥΝΟΛΟ» και οι λεπτομέρειες προϊόντων ακολουθούν σε μικρότερη αναλογία
        // ως προς το αρχικό τους μέγεθος σχεδίασης, ώστε να μη γίνουν ποτέ μεγαλύτερες από το κύριο κείμενο.
        // Ορίζονται ΠΡΙΝ μπουν οι γραμμές στο δέντρο — τα Loaded handlers παρακάτω τα διαβάζουν.
        // Λεπτομέρειες (έξτρα/χωρίς) και τιμή ακολουθούν ΑΝΑΛΟΓΙΚΑ το όνομα του προϊόντος — μία ρύθμιση
        // κινεί ολόκληρη τη γραμμή, κρατώντας πάντα την ίδια ιεραρχία: όνομα > έξτρα > τιμή.
        // Το όνομα του προϊόντος βγαίνει ΜΕΓΑΛΥΤΕΡΟ από τη ρύθμιση (×1,2) και έντονο (βλ. XAML): είναι
        // αυτό που διαβάζει βιαστικά ο ψήστης πάνω από τη σχάρα. Οι λεπτομέρειες (έξτρα/χωρίς) και η
        // τιμή κρεμιούνται από ΑΥΤΟ, οπότε μεγαλώνουν αναλογικά και η ιεραρχία μένει ίδια:
        // όνομα > έξτρα > τιμή.
        _itemsFontSize = s.ReceiptItemsFontSize * 1.2;
        _detailsFontSize = _itemsFontSize * (10.5 / 13.0);
        _priceFontSize = s.ReceiptItemsFontSize * 0.75;

        // Το «μέγεθος τίτλου» των Ρυθμίσεων κουμαντάρει πλέον το ΠΛΑΤΟΣ του λογότυπου: 15 (η
        // προεπιλογή) = 200 μονάδες, δηλαδή περίπου 5 εκατοστά στο χαρτί. Έτσι μεγαλώνει/μικραίνει
        // από την ίδια ρύθμιση που μεγάλωνε τον τίτλο, χωρίς νέα ρύθμιση στην οθόνη.
        LogoImage.Width = s.ReceiptTitleFontSize * (200.0 / 15.0);

        TotalText.FontSize = s.ReceiptTotalFontSize;
        PaidMarkText.FontSize = s.ReceiptTotalFontSize;
        TotalLabelText.FontSize = s.ReceiptTotalFontSize * (17.0 / 20.0);

        // Ανάλυση ΦΠΑ κάτω από το ΣΥΝΟΛΟ — μικρότερη από αυτό: δεν είναι το ποσό που πληρώνει ο
        // πελάτης, είναι τα δύο νούμερα για την ταμειακή μηχανή.
        var vat = VatBreakdownService.Split(order);
        var vatFontSize = s.ReceiptTotalFontSize * 0.78;
        foreach (var tb in new[] { VatReducedLabel, VatReducedAmount, VatStandardLabel, VatStandardAmount })
            tb.FontSize = vatFontSize;
        VatReducedAmount.Text = vat.ReducedLabel;
        VatStandardAmount.Text = vat.StandardLabel;
        // Παραγγελία μόνο με φαγητό δεν χρειάζεται γραμμή «ΜΕ 24% 0,00» — και αντίστροφα.
        VatReducedRow.Visibility = vat.HasReduced ? Visibility.Visible : Visibility.Collapsed;
        VatStandardRow.Visibility = vat.HasStandard ? Visibility.Visible : Visibility.Collapsed;
        VatBlock.Visibility = vat.HasReduced || vat.HasStandard ? Visibility.Visible : Visibility.Collapsed;

        InfoText.FontSize = s.ReceiptMetaFontSize;
        // ΠΕΛΑΤΗΣ/ΠΛΗΡΩΜΗ στο απλό μέγεθος meta (όχι μεγεθυμένο): είναι δευτερεύουσα πληροφορία δίπλα
        // στα προϊόντα — μεγεθυμένα ΚΑΙ έντονα τραβούσαν το μάτι περισσότερο απ' την ίδια την παραγγελία.
        WhoLabelText.FontSize = s.ReceiptMetaFontSize;
        WhoText.FontSize = s.ReceiptMetaFontSize;
        PaymentText.FontSize = s.ReceiptMetaFontSize;
        // Ίδιο μέγεθος πάνω και κάτω: είναι ο ίδιος αριθμός, για να διαβάζεται κρεμασμένη η απόδειξη
        // από όποια άκρη μείνει ακάλυπτη.
        // Κρέμονται από το «Λοιπά» και ΟΧΙ από το «Λογότυπο» (όπως πριν): η ρύθμιση του λογότυπου
        // πρέπει να αλλάζει ΜΟΝΟ την εικόνα, αλλιώς μίκραινε μαζί της και το «ΟΡΘΙΟΣ #01». Λίγο
        // μεγαλύτερα από τα υπόλοιπα meta (15 έναντι 13 στο αρχικό σχέδιο) — ο αριθμός διαβάζεται
        // από απόσταση.
        TypeText.FontSize = s.ReceiptMetaFontSize * (15.0 / 13.0);
        FooterText.FontSize = s.ReceiptMetaFontSize * (15.0 / 13.0);

        UpdateLayout();
    }

    /// <summary>Μέγεθος για όνομα/τιμή προϊόντος — από τη ρύθμιση ReceiptItemsFontSize.</summary>
    private double _itemsFontSize;

    /// <summary>Μέγεθος για τις λεπτομέρειες (ψωμί/έξτρα/χωρίς) — ίδια αναλογία με το αρχικό σχέδιο
    /// (10.5 έναντι 13), ώστε να μη γίνουν ποτέ μεγαλύτερες από το ίδιο το όνομα του προϊόντος.</summary>
    private double _detailsFontSize;

    /// <summary>Μέγεθος για την τιμή της γραμμής — μικρότερο από το όνομα: στην κουζίνα μετράει τι
    /// είναι το προϊόν, το ποσό αφορά το ταμείο και δεν χρειάζεται να τραβάει το μάτι το ίδιο.</summary>
    private double _priceFontSize;

    /// <summary>
    /// Το πραγματικό μέγεθος μπαίνει ΕΔΩ, όταν το κάθε TextBlock μπαίνει στο δέντρο.
    ///
    /// Παλιότερα γινόταν με διάσχιση του visual tree μέσα στον constructor — που ΔΕΝ δούλευε ποτέ: οι
    /// γραμμές του ItemsControl δεν έχουν δημιουργηθεί ακόμα πριν εμφανιστεί το παράθυρο (δοκιμασμένο:
    /// 0 TextBlocks πριν το Show, 3 μετά), οπότε η διάσχιση δεν έβρισκε τίποτα και η απόδειξη τυπωνόταν
    /// πάντα στο αρχικό 13 — ό,τι κι αν έλεγε η ρύθμιση. Με το Loaded δεν παίζει ρόλο ο χρονισμός.
    /// </summary>
    private void ItemText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock tb)
            tb.FontSize = _itemsFontSize;
    }

    /// <summary>Ίδια λογική με το ItemText_Loaded, για την τιμή της γραμμής.</summary>
    private void PriceText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock tb)
            tb.FontSize = _priceFontSize;
    }

    /// <summary>Ίδια λογική με το ItemText_Loaded, συν το έντονο στις λέξεις-κλειδιά.</summary>
    private void DetailsText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock tb)
            return;
        tb.FontSize = _detailsFontSize;
        HighlightModifierKeywords(tb);
    }

    /// <summary>Ξαναγράφει το κείμενο μιας «λεπτομέρειας» γραμμής προϊόντος (π.χ. «ΧΩΡΙΣ ΚΡΕΜΜΥΔΙ»,
    /// «ΜΟΝΟ ΜΕ:») σε Runs, ώστε να είναι έντονο ΜΟΝΟ το «ΧΩΡΙΣ»/«ΜΟΝΟ ΜΕ:» και όχι το υπόλοιπο κείμενο
    /// (π.χ. το ίδιο το υλικό) — ο ταμίας ζήτησε ρητά μόνο τη λέξη-κλειδί έντονη, όχι όλη τη γραμμή.
    /// Καλείται μία φορά ανά TextBlock, αφού πρώτα εντοπιστεί ως «λεπτομέρεια» (βλ. ScaleItemLines).</summary>
    private static void HighlightModifierKeywords(TextBlock tb)
    {
        var text = tb.Text;
        if (string.IsNullOrEmpty(text))
            return;
        tb.Text = "";

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            // Έντονα: οι επικεφαλίδες «ΧΩΡΙΣ:» / «ΜΟΝΟ ΜΕ:» ΚΑΙ τα έξτρα («+ ΤΖΑΤΖΙΚΙ») — αυτά είναι
            // που πρέπει να πιάσει το μάτι του ψήστη, γιατί προσθέτουν δουλειά. Τα υλικά κάτω από το
            // «ΧΩΡΙΣ:» μένουν κανονικά (ρητή απαίτηση: καθαρό λεπτό μαύρο στο ίδιο το υλικό).
            var line = lines[i];
            var isHeading = line.Equals("ΧΩΡΙΣ:", StringComparison.Ordinal)
                || line.Equals("ΜΟΝΟ ΜΕ:", StringComparison.Ordinal);
            var isExtra = line.StartsWith("+", StringComparison.Ordinal);
            tb.Inlines.Add(new Run(line)
            {
                FontWeight = isHeading || isExtra ? FontWeights.Bold : FontWeights.Normal,
            });
            if (i < lines.Length - 1)
                tb.Inlines.Add(new LineBreak());
        }
    }
}
