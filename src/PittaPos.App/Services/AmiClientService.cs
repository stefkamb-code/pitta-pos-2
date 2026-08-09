using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows;

namespace PittaPos.App.Services;

/// <summary>
/// Συνδέεται σαν client στο Asterisk Manager Interface (AMI) του Grandstream UCM, ώστε να «ακούει»
/// σε πραγματικό χρόνο πότε χτυπάει το τηλέφωνο και να ενημερώνει το <see cref="IncomingCallService"/>
/// με τον αριθμό του καλούντος — βλ. docs/incoming-call-el.md για τη ρύθμιση στο UCM (Integrations →
/// AMI). Κρατά μόνιμη σύνδεση με αυτόματη επανασύνδεση αν πέσει το δίκτυο/το UCM.
/// </summary>
public static class AmiClientService
{
    public static void Start()
    {
        if (SettingsStore.Instance.Settings.UcmHost.Trim().Length == 0)
            return; // δεν έχει ρυθμιστεί αναγνώριση κλήσεων — ανενεργό
        _ = RunForeverAsync();
    }

    /// <summary>Τελευταίο καταγεγραμμένο πρόβλημα — ώστε μια μόνιμα χαλασμένη ρύθμιση (π.χ. λάθος IP)
    /// να μη γεμίσει το αρχείο καταγραφής με την ίδια γραμμή κάθε 5 δευτερόλεπτα. Γράφεται μία φορά
    /// όταν αλλάζει η αιτία, και μία φορά όταν αποκαθίσταται η σύνδεση.</summary>
    private static string? _lastProblem;

    private static void Report(string? problem)
    {
        if (problem == _lastProblem)
            return;
        _lastProblem = problem;
        AppLog.Write("ami", problem is null
            ? "Η σύνδεση με το τηλεφωνικό κέντρο αποκαταστάθηκε."
            : "Πρόβλημα σύνδεσης με το τηλεφωνικό κέντρο: " + problem);
    }

    private static async Task RunForeverAsync()
    {
        while (true)
        {
            try
            {
                await ConnectAndListenAsync();
                // Επέστρεψε ομαλά = έκλεισε η σύνδεση από την άλλη πλευρά· δεν είναι σφάλμα καθαυτό,
                // αλλά αν συμβαίνει συνέχεια αξίζει να φαίνεται.
                Report("η σύνδεση έκλεισε από το κέντρο");
            }
            catch (Exception ex)
            {
                // Έπεσε η σύνδεση (δίκτυο/UCM οριστικά ή προσωρινά κάτω) — ξαναδοκίμασε σε λίγο,
                // δεν πρέπει να ρίξει το ταμείο. Χωρίς καταγραφή, μια λάθος ρύθμιση (IP/κωδικός)
                // σήμαινε ότι η αναγνώριση κλήσεων απλώς «δεν δούλευε» χωρίς κανένα ίχνος πουθενά.
                Report(ex.Message);
            }
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task ConnectAndListenAsync()
    {
        var s = SettingsStore.Instance.Settings;
        using var client = new TcpClient();
        await client.ConnectAsync(s.UcmHost.Trim(), s.AmiPort);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        // Πρώτη γραμμή: banner του AMI (π.χ. "Asterisk Call Manager/x.x") — απλά τη διαβάζουμε.
        await reader.ReadLineAsync();

        await writer.WriteLineAsync("Action: Login");
        await writer.WriteLineAsync("Username: " + s.AmiUsername.Trim());
        await writer.WriteLineAsync("Secret: " + s.AmiPassword);
        await writer.WriteLineAsync();

        var loggedIn = false;
        while (true)
        {
            var block = await ReadBlockAsync(reader);
            if (block is null)
                return; // η σύνδεση έκλεισε — ξαναπροσπάθησε από το RunForeverAsync

            // Η απάντηση στο Login: λάθος όνομα/κωδικός δίνει «Response: Error». Χωρίς αυτόν τον έλεγχο
            // η εφαρμογή ξανασυνδεόταν επ' άπειρον κάθε 5 δευτερόλεπτα χωρίς να πει ποτέ ΓΙΑΤΙ δεν
            // δουλεύει η αναγνώριση κλήσεων.
            if (!loggedIn && block.TryGetValue("Response", out var response))
            {
                loggedIn = true;
                if (!response.Equals("Success", StringComparison.OrdinalIgnoreCase))
                {
                    Report("απορρίφθηκε η σύνδεση (" + response + ": " +
                        block.GetValueOrDefault("Message", "χωρίς λεπτομέρειες") + ")");
                    return;
                }
                Report(null);
            }

            HandleBlock(block);
        }
    }

    /// <summary>
    /// Το AMI στέλνει «blocks» από γραμμές «Header: Value», τερματισμένα με μία κενή γραμμή —
    /// τόσο για την απάντηση του login όσο και για κάθε event (νέα κλήση, αλλαγή κατάστασης, κ.λπ.).
    /// </summary>
    private static async Task<Dictionary<string, string>?> ReadBlockAsync(StreamReader reader)
    {
        var block = new Dictionary<string, string>();
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
                return null;
            if (line.Length == 0)
                return block;
            var idx = line.IndexOf(':');
            if (idx <= 0)
                continue;
            block[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
    }

    /// <summary>
    /// Νέο κανάλι/αλλαγή κατάστασης με αριθμό καλούντος — το ίδιο το IncomingCallService αγνοεί ήδη
    /// σύντομους αριθμούς (εσωτερικά τηλέφωνα, 3-4 ψηφία) και διπλά events για την ίδια κλήση, οπότε
    /// εδώ δεν χρειάζεται λεπτομερές φιλτράρισμα — αρκεί να πιάνουμε νωρίς κάθε πιθανό σήμα κλήσης.
    /// </summary>
    private static void HandleBlock(Dictionary<string, string> block)
    {
        if (!block.TryGetValue("Event", out var evt) || evt is not ("Newchannel" or "Newstate"))
            return;
        if (!block.TryGetValue("CallerIDNum", out var caller) || caller.Length == 0)
            return;

        var dispatcher = Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(() => IncomingCallService.Instance.ReportRinging(caller));
    }
}
