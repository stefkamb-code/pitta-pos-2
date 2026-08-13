using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>Τραπέζι όπως το βλέπει η εφαρμογή κινητού — ίδια δεδομένα με την κάτοψη του ταμείου.</summary>
/// <summary><paramref name="Persons"/> = πόσα άτομα δηλώθηκαν στο άνοιγμα, δηλαδή <b>πόσες αποδείξεις
/// θα κοπούν</b> στην ταμειακή. 0 = δεν ρωτήθηκε ακόμα (ή πληρώνουν μαζί) — τότε το κινητό ρωτάει.</summary>
public sealed record TableDto(int Number, bool IsOpen, decimal Total, int RoundCount, string? LastOrderTime,
    int Persons = 0);

/// <summary>Προϊόν όπως το βλέπει η εφαρμογή κινητού.
/// <para><paramref name="Ingredients"/> = τα βασικά υλικά ΤΟΥ ΣΥΓΚΕΚΡΙΜΕΝΟΥ προϊόντος, ήδη λυμένα μέσω
/// <see cref="MenuStore.IngredientsFor"/> (άδεια λίστα = προϊόν χωρίς βασικά υλικά). Πριν υπάρξει αυτό το
/// πεδίο το κινητό έδειχνε παντού τον κοινό κατάλογο του <see cref="CustomizerOptionsDto.Ingredients"/>,
/// οπότε ο σερβιτόρος ξετσέκαρε υλικά που το προϊόν δεν είχε καν και το ταμείο — που μετράει το «σκέτο»
/// με τα υλικά του προϊόντος — τύπωνε ΣΚΕΤΟ σε παραγγελία που δεν το ζήτησε.
/// Μένει nullable: παλιό κινητό αγνοεί το πεδίο, νέο κινητό σε ταμείο που δεν το στέλνει πέφτει πίσω
/// στον κοινό κατάλογο και δουλεύει όπως πριν.</para></summary>
public sealed record MenuProductDto(
    string Id,
    string Name,
    string? NameEn,
    decimal Price,
    bool Customizable,
    IReadOnlyList<string>? Ingredients = null,
    IReadOnlyList<string>? Extras = null);

/// <summary>Κατηγορία όπως τη βλέπει η εφαρμογή κινητού, μαζί με τους κανόνες της.
/// <para>Οι κανόνες ταξιδεύουν από το ταμείο αντί να τους ξέρει το κινητό: τα ίδια ονόματα κατηγοριών
/// ήταν γραμμένα και μέσα στο APK, οπότε μια μετονομασία από τη Διαχείριση Καταλόγου (ΤΥΛΙΧΤΑ → ΠΙΤΤΕΣ)
/// έκανε το κινητό να μη δείχνει ούτε επιλογή ψωμιού ούτε διπλή πίτα — και δεν διορθωνόταν χωρίς νέο APK.
/// Πηγή είναι οι ιδιότητες της κατηγορίας (MenuStore.HasBreadChoice/FuseBreadIntoName/SupportsDoublePita),
/// οι ίδιες που χρησιμοποιεί ο customizer του ταμείου, ώστε οι δύο οθόνες να μη διαφέρουν ποτέ.</para>
/// <para>Nullable: κινητό νεότερο από το ταμείο πέφτει πίσω στους δικούς του κανόνες αντί να μείνει χωρίς.</para></summary>
public sealed record MenuCategoryDto(
    string Id,
    string Name,
    List<MenuProductDto> Products,
    bool? HasBread = null,
    bool? FuseBreadIntoName = null,
    bool? SupportsDoublePita = null,
    decimal? DoublePitaPrice = null);

/// <summary>Έξτρα υλικό όπως το βλέπει η εφαρμογή κινητού.</summary>
public sealed record ExtraOptionDto(string Name, decimal Price);

/// <summary>Κοινές επιλογές customizer (ίδιες για κάθε customizable προϊόν) — ψωμί/υλικά/έξτρα.
/// DoublePitaPrices: χρέωση διπλής πίτας ανά κατηγορία (βλ. MenuSeed.SupportsDoublePita/MenuStore.DoublePitaPrices)
/// — μόνο οι κατηγορίες που την υποστηρίζουν έχουν κλειδί εδώ, το κινητό δείχνει την επιλογή μόνο γι' αυτές.</summary>
public sealed record CustomizerOptionsDto(
    IReadOnlyList<string> Breads,
    IReadOnlyList<string> Ingredients,
    IReadOnlyList<ExtraOptionDto> Extras,
    IReadOnlyDictionary<string, decimal> DoublePitaPrices,
    /// <summary>Η συντομογραφία κάθε ψωμιού («Αραβική» → «ΑΡ.»), όπως τη βγάζει το ταμείο
    /// (MenuSeed.BreadAbbreviation) — μπαίνει μπροστά στο όνομα της γραμμής στις κατηγορίες που
    /// χωνεύουν το ψωμί. Ταξιδεύει αντί να την υπολογίζει το κινητό: αν αλλάξουν τα ψωμιά ή οι
    /// συντομογραφίες τους, το καλάθι του σερβιτόρου δεν γράφει άλλα από την απόδειξη.</summary>
    IReadOnlyDictionary<string, string>? BreadAbbreviations = null);

public sealed record OrderLineRequest(
    string ProductId,
    int Quantity,
    string? Bread = null,
    List<string>? RemovedIngredients = null,
    Dictionary<string, int>? Extras = null,
    string? Note = null,
    bool DoublePita = false,
    /// <summary>Σε ποιο άτομο του τραπεζιού χρεώνεται η γραμμή (0-based: 0 = «Α»). null = σε κανέναν
    /// ακόμα — παλιό APK, ή τραπέζι που πληρώνει μαζί. Το μαγαζί κόβει ΜΙΑ ΑΠΟΔΕΙΞΗ ΑΝΑ ΑΤΟΜΟ στην
    /// ταμειακή του, γι' αυτό το κινητό ρωτά «πόσα άτομα;» μόλις ανοίξει το τραπέζι και ο σερβιτόρος
    /// γράφει την παραγγελία ανά άτομο (βλ. TablePersonsService).</summary>
    int? Person = null);

/// <summary><paramref name="PrintNow"/> = να τυπωθεί δελτίο κουζίνας τώρα. Σε τραπέζι που παραγγέλνει
/// ανά άτομο, το κινητό στέλνει <c>false</c> για κάθε άτομο εκτός από το τελευταίο: οι παραγγελίες
/// καταχωρούνται κανονικά μία-μία (μία απόδειξη ανά άτομο), αλλά η κουζίνα παίρνει <b>ΕΝΑ</b> δελτίο
/// με όλο το τραπέζι μαζί, όταν κλείσει και το τελευταίο άτομο. Προεπιλογή true, ώστε παλιό APK και
/// κάθε άλλη παραγγελία να τυπώνουν όπως πάντα.</summary>
public sealed record SubmitOrderRequest(int Table, string Pin, List<OrderLineRequest> Lines, string? Note = null,
    bool PrintNow = true);

/// <summary>Μία γραμμή από ήδη καταχωρημένη παραγγελία τραπεζιού.</summary>
/// <summary><paramref name="Person"/> = σε ποιο άτομο του τραπεζιού χρεώνεται (0-based, null = σε
/// κανέναν). Έτσι η καρτέλα του τραπεζιού — και στο κινητό και στο ταμείο — δείχνει «ΑΤΟΜΟ Α: αυτά,
/// ΑΤΟΜΟ Β: αυτά», δηλαδή ακριβώς τι θα γραφτεί σε κάθε απόδειξη.</summary>
public sealed record TableOrderLineDto(int LineIndex, string Name, int Quantity, decimal Revenue, string Details,
    bool IsSettled, int? Person = null);

/// <summary>Ένας γύρος παραγγελίας για το τραπέζι — ό,τι έχει ήδη παραγγελθεί.</summary>
public sealed record TableOrderDto(int OrderNumber, string TimeLabel, decimal Total, List<TableOrderLineDto> Lines, string Note = "");

/// <summary>Εξόφληση ενός προϊόντος ξεχωριστά (π.χ. πλήρωσε μόνο ένας από την παρέα) — PIN όπως στις παραγγελίες.</summary>
/// <param name="Method">«cash» ή «card», όπως τα κουμπιά του ταμείου. null = παλιότερο APK που δεν
/// ρωτούσε τρόπο πληρωμής — θεωρείται μετρητά, δηλαδή ό,τι ίσχυε πριν.</param>
public sealed record SettleLineRequest(string Pin, int OrderNumber, int LineIndex, string? Method = null);

/// <summary>«Πόσα άτομα;» από το κινητό — ένα άτομο = μία απόδειξη στην ταμειακή του μαγαζιού.</summary>
public sealed record SetPersonsRequest(string Pin, int Count);

/// <summary>Πληρωμή/κλείσιμο ολόκληρου τραπεζιού από το κινητό.</summary>
/// <param name="Method">«cash» ή «card» — ό,τι έμεινε ανεξόφλητο θεωρείται ότι πληρώθηκε τώρα έτσι,
/// ακριβώς όπως το «ΠΛΗΡΩΜΗ ΥΠΟΛΟΙΠΟΥ ΚΑΙ ΚΛΕΙΣΙΜΟ» του ταμείου. null = παλιότερο APK → μετρητά.</param>
public sealed record CloseTableRequest(string Pin, string? Method = null);

/// <summary>Ειδοποίηση εισερχόμενης κλήσης από το τηλεφωνικό κέντρο (Grandstream UCM Event Notification).</summary>
public sealed record IncomingCallRequest(string? Caller);

// ---- Συγχρονισμός δεύτερου ταμείου (βλ. RemoteSync, docs/two-tills-el.md) ----

/// <summary>Κοινές ρυθμίσεις καταστήματος ανάμεσα στα δύο ταμεία — ό,τι έχει νόημα να ταιριάζει
/// (διεύθυνση/θέμα/γλώσσα/email/απόδειξη/κωδικοί) εκτός από PrinterName/NetworkMode/HostAddress, που
/// μένουν σκόπιμα τοπικά ανά υπολογιστή (βλ. SettingsStore.BuildSharedSettingsDto/ApplySharedSettingsDto).</summary>
public sealed record SharedSettingsDto(
    string Theme, string Language, string Pin, bool IsEveningShift,
    string ShopAddress, string GoogleMapsApiKey,
    string UcmHost, int AmiPort, string AmiUsername, string AmiPassword,
    string SmtpHost, int SmtpPort, string SmtpUser, string SmtpPassword, string ReportEmail,
    List<StaffPin> CancelStaffPins,
    string ReceiptTitle, string ReceiptInfo, string ReceiptFooter,
    bool ReceiptShowDateTime, bool ReceiptShowCustomer, bool ReceiptShowDetails,
    double ReceiptTitleFontSize, double ReceiptItemsFontSize, double ReceiptTotalFontSize, double ReceiptMetaFontSize);

/// <summary>Ολόκληρο το μενού για συγχρονισμό με το δεύτερο ταμείο. Πριν στέλνονταν ΜΟΝΟ οι
/// κατηγορίες: τα κοινά έξτρα και οι χρεώσεις διπλής πίτας δεν έφταναν ποτέ, οπότε στο δεύτερο ταμείο
/// έλειπαν (ή έμεναν στις αρχικές τιμές) και ό,τι επεξεργαζόταν εκεί ο ταμίας χανόταν στο επόμενο
/// άνοιγμα — δεν αποθηκεύεται τοπικά στο δεύτερο ταμείο.</summary>
/// <param name="Ingredients">Ο κοινός κατάλογος βασικών υλικών (MenuStore.Ingredients) — προεπιλογή
/// για προϊόντα που δεν έχουν ρητή δική τους λίστα (βλ. MenuStore.IngredientsFor). Πρόσφατη προσθήκη
/// (default []) για παλιά αιτήματα από πριν υπάρξει η δυνατότητα — βλ. ApplySyncDto.</param>
public sealed record MenuSyncDto(
    List<MenuCategory> Categories,
    List<ExtraItem> Extras,
    Dictionary<string, decimal> DoublePitaPrices,
    List<string>? Ingredients = null);

public sealed record TableSyncRequest(int Table);
public sealed record TableLayoutSyncRequest(int Table, double X, double Y);
public sealed record TableCountSyncRequest(int Count);
/// <param name="Unit">Ποιο τεμάχιο της γραμμής εξοφλείται· -1 (ή αν λείπει, από παλιότερη έκδοση)
/// σημαίνει ΟΛΗ η γραμμή — βλ. TableSettlementService.</param>
/// <param name="Method">«cash»/«card» — χωρίς αυτό, οι εξοφλήσεις του δεύτερου ταμείου θα μετρούσαν
/// όλες ως μετρητά στην αναφορά ημέρας.</param>
/// <param name="Amount">Το ποσό της είσπραξης, για το ημερολόγιο πληρωμών (TablePaymentsService).</param>
public sealed record TableSettleSyncRequest(int Table, int OrderNumber, int LineIndex, int Unit = -1,
    string Method = "cash", decimal Amount = 0);
public sealed record TableOrderSyncRequest(int Table, int OrderNumber);
public sealed record TablePersonsCountRequest(int Table, int Count);
public sealed record TablePersonAssignRequest(int Table, int OrderNumber, int LineIndex, int Unit, int Person);
public sealed record TableShiftSyncRequest(int Table, int OrderNumber, int RemovedIndex);
public sealed record OrderNumberRequest(int OrderNumber, string CancelledBy = "");
public sealed record OrderNumberLineRequest(int OrderNumber, int LineIndex);
public sealed record OrderNumberPaymentMethodRequest(int OrderNumber, PittaPos.Core.Models.PaymentMethod? PaymentMethod);
public sealed record OrderNumberChannelRequest(int OrderNumber, PittaPos.Core.Models.OrderType Type, string? Channel);
public sealed record BoardChannelRequest(int OrderNumber, string Channel);
public sealed record CustomerUpsertRequest(string Name, string Phone, string Address, string StreetNumber = "",
    string Area = "", string PostalCode = "", string Floor = "", string Notes = "");
public sealed record CustomerOrderLineDto(string Name, int Quantity);
public sealed record CustomerRecordOrderRequest(string Name, string Phone, string Address, string StreetNumber,
    string Area, string PostalCode, string Floor, string Notes, decimal Total, List<CustomerOrderLineDto> Lines);
public sealed record CustomerMemoRequest(string Name, string Phone, string Address, string Memo);
public sealed record CustomerRemoveAddressRequest(string Name, string Phone, string Address,
    string OtherAddress, string OtherArea, string OtherNumber = "");
public sealed record CustomerRemoveMainAddressRequest(string Name, string Phone, string Address);
public sealed record CustomerAddAddressRequest(string Name, string Phone, string Address,
    string NewAddress, string NewNumber, string NewArea, string NewPostalCode, string NewFloor);

/// <summary>Μία κλήση στην ουρά αναμονής, όπως τη στέλνει το host στο δεύτερο ταμείο.</summary>
public sealed record IncomingCallEntry(int Id, int Position, string Phone);
public sealed record DismissIncomingCallRequest(int Id);
