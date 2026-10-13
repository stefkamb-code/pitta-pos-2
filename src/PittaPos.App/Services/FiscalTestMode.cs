using System.IO;
using PittaPos.Core.Fiscal;

namespace PittaPos.App.Services;

/// <summary>
/// Δοκιμή του παρόχου ΑΑΔΕ στην πράξη, χωρίς πάροχο και χωρίς site: ανάβει ΜΟΝΟ με το αρχείο <see cref="MarkerFile"/> δίπλα
/// στο exe (όπως το <see cref="DemoMode"/> — το setup του μαγαζιού δεν το έχει ποτέ). Τότε το ταμείο δουλεύει με τον
/// «Δοκιμή χωρίς πάροχο» (δεν στέλνει τίποτα πουθενά, ΜΑΡΚ από 9…), με ένα τερματικό ταμείου και ένα διανομέα, και η
/// απόδειξη βγαίνει σε παράθυρο στην οθόνη αντί για τον εκτυπωτή. Μαζί με το demo, όλα μένουν στο demo-data.
/// </summary>
public static class FiscalTestMode
{
    public const string MarkerFile = "pittapos-fiscal-test.txt";

    public static bool IsOn { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, MarkerFile));

    public static FiscalSettings Settings { get; } = new(
        FiscalProviders.Simulated, "sandbox", "", "",
        [new FiscalTerminal("Ταμείο", "Viva", "", "T-TAMEIO"), new FiscalTerminal("Διανομή", "Viva", "", "T-DIANOMI")],
        "same", "", "999999999");

    /// <summary>Ένας για όλη τη συνεδρία — με αναμονή «τερματικού» στην κάρτα, όπως στο αληθινό.</summary>
    public static IFiscalProvider Provider { get; } = new SimulatedProvider();
}
