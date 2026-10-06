namespace PittaPos.Core.Fiscal;

/// <summary>
/// Ένας πάροχος ηλεκτρονικής τιμολόγησης. Τον διαλέγει το μαγαζί — το ταμείο πρέπει να μιλά με όλους, οπότε όλο το
/// υπόλοιπο ταμείο ξέρει ΜΟΝΟ αυτό το interface· κάθε πάροχος είναι ένας μικρός «μεταφραστής» από κάτω.
/// </summary>
public interface IFiscalProvider
{
    string Name { get; }

    /// <summary>Κόβει το παραστατικό. Για κάρτα στέλνει και το ποσό στο τερματικό και γυρίζει όταν εγκριθεί η πληρωμή.</summary>
    Task<FiscalResult> IssueAsync(FiscalDocument doc, CancellationToken ct = default);

    /// <summary>Ακυρώνει ένα δελτίο παραγγελίας τραπεζιού (π.χ. πιάτο που δεν βγήκε ποτέ).</summary>
    Task<FiscalResult> CancelOrderSlipAsync(string mark, CancellationToken ct = default);
}

/// <summary>Διαλέγει τον «μεταφραστή» από το όνομα του παρόχου στις Ρυθμίσεις του μαγαζιού.</summary>
public static class FiscalProviders
{
    /// <summary>Το όνομα του εικονικού παρόχου — για δοκιμές χωρίς κανέναν πραγματικό πάροχο.</summary>
    public const string Simulated = "Δοκιμή χωρίς πάροχο";

    /// <summary>Όσοι έχουν οδηγό. Οι υπόλοιποι της λίστας του site προστίθενται όταν τους διαλέξει κάποιο μαγαζί.</summary>
    public static readonly IReadOnlyList<string> Supported = ["Wrapp", Simulated];

    public static IFiscalProvider Create(FiscalSettings s, HttpClient http) => s.Provider switch
    {
        "Wrapp" => new WrappProvider(s, http),
        Simulated => new SimulatedProvider(),
        "" => new UnsupportedProvider("", "Δεν έχει οριστεί πάροχος στις Ρυθμίσεις του μαγαζιού."),
        var other => new UnsupportedProvider(other, $"Ο πάροχος «{other}» δεν έχει συνδεθεί ακόμα με το ταμείο."),
    };
}

/// <summary>Πάροχος χωρίς οδηγό ακόμα: δεν κόβει τίποτα και το λέει καθαρά.</summary>
public sealed class UnsupportedProvider(string name, string reason) : IFiscalProvider
{
    public string Name => name;
    public Task<FiscalResult> IssueAsync(FiscalDocument doc, CancellationToken ct = default) => Task.FromResult(FiscalResult.Fail(reason));
    public Task<FiscalResult> CancelOrderSlipAsync(string mark, CancellationToken ct = default) => Task.FromResult(FiscalResult.Fail(reason));
}
