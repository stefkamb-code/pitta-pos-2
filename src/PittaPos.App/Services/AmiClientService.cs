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
        {
            // Γράφεται, γιατί «δεν δουλεύει η αναγνώριση κλήσεων» και «δεν έχει ρυθμιστεί καθόλου»
            // είναι δύο εντελώς διαφορετικά πράγματα — και μέχρι τώρα φαίνονταν ακριβώς ίδια: σιωπή.
            AppLog.Write("ami", "Η αναγνώριση κλήσεων είναι ΑΝΕΝΕΡΓΗ: δεν έχει οριστεί IP τηλεφωνικού κέντρου.");
            return;
        }
        AppLog.Write("ami", $"Σύνδεση στο τηλεφωνικό κέντρο {SettingsStore.Instance.Settings.UcmHost.Trim()}:{SettingsStore.Instance.Settings.AmiPort}…");
        _ = RunForeverAsync();
    }

    /// <summary>
    /// ΔΟΚΙΜΗ με τα στοιχεία που βλέπει ο χρήστης στην οθόνη, χωρίς να χρειαστεί επανεκκίνηση.
    ///
    /// <para>Απαντά στο πραγματικό ερώτημα — «γιατί δεν δουλεύει;» — με σειρά: απαντά η IP/θύρα;
    /// δέχτηκε τον κωδικό; και, όσο ακούει, <b>έρχεται κάτι όταν χτυπήσει το τηλέφωνο;</b> Το τελευταίο
    /// είναι που έλειπε: αν το UCM στέλνει γεγονότα με άλλο όνομα απ' όσα περιμένουμε, μέχρι τώρα δεν
    /// υπήρχε κανένας τρόπος να το δει κανείς — ούτε καν ότι έφτασε κάτι.</para>
    /// </summary>
    public static async Task<string> TestAsync(string host, int port, string user, string password,
        TimeSpan listenFor, IProgress<string>? progress = null)
    {
        host = host.Trim();
        if (host.Length == 0)
            return "Δεν έχει οριστεί IP τηλεφωνικού κέντρου.";

        try
        {
            using var client = new TcpClient();
            progress?.Report($"Συνδέομαι στο {host}:{port}…");
            var connect = client.ConnectAsync(host, port);
            if (await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(6))) != connect)
                return $"✕ Δεν απαντά το {host}:{port} — λάθος IP ή θύρα, ή το AMI δεν είναι ενεργό στο UCM.";
            await connect;

            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

            var banner = await reader.ReadLineAsync();
            progress?.Report("Απάντησε: " + (banner ?? "(τίποτα)"));

            await writer.WriteLineAsync("Action: Login");
            await writer.WriteLineAsync("Username: " + user.Trim());
            await writer.WriteLineAsync("Secret: " + password);
            await writer.WriteLineAsync();

            var login = await ReadBlockAsync(reader);
            if (login is null)
                return "✕ Το κέντρο έκλεισε τη σύνδεση χωρίς απάντηση — συνήθως η IP του ταμείου δεν είναι στις επιτρεπόμενες (Permitted IP) του AMI χρήστη.";
            if (!login.GetValueOrDefault("Response", "").Equals("Success", StringComparison.OrdinalIgnoreCase))
                return "✕ Απορρίφθηκε: " + login.GetValueOrDefault("Message", "λάθος χρήστης ή κωδικός AMI");

            // Ρητό αίτημα για γεγονότα: σε κάποια firmware η σύνδεση περνάει αλλά δεν στέλνεται τίποτα
            // μέχρι να ζητηθεί — και τότε όλα μοιάζουν σωστά ενώ δεν έρχεται ποτέ κλήση.
            await writer.WriteLineAsync("Action: Events");
            await writer.WriteLineAsync("EventMask: on");
            await writer.WriteLineAsync();

            progress?.Report("✓ Συνδέθηκα. ΧΤΥΠΑ ΤΩΡΑ ΤΟ ΤΗΛΕΦΩΝΟ ΤΟΥ ΜΑΓΑΖΙΟΥ…");

            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var callers = new List<string>();
            var deadline = DateTime.UtcNow + listenFor;
            while (DateTime.UtcNow < deadline)
            {
                var read = ReadBlockAsync(reader);
                if (await Task.WhenAny(read, Task.Delay(deadline - DateTime.UtcNow)) != read)
                    break;
                var block = await read;
                if (block is null)
                    break;
                if (!block.TryGetValue("Event", out var evt))
                    continue;
                seen[evt] = seen.GetValueOrDefault(evt) + 1;
                var caller = block.GetValueOrDefault("CallerIDNum", "");
                if (caller.Length >= 6 && !callers.Contains(caller))
                    callers.Add(caller);
            }

            if (seen.Count == 0)
                return "✓ Η σύνδεση δουλεύει, αλλά ΔΕΝ ήρθε κανένα γεγονός. Αν χτύπησε το τηλέφωνο, ο χρήστης AMI δεν έχει δικαίωμα «Call» στο UCM.";

            var summary = string.Join(", ", seen.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key}×{kv.Value}"));
            return callers.Count > 0
                ? $"✓ ΟΛΑ ΚΑΛΑ — ήρθαν αριθμοί: {string.Join(", ", callers)}\n({summary})"
                : $"✓ Η σύνδεση δουλεύει και έρχονται γεγονότα, αλλά κανένα με αριθμό καλούντος.\n({summary})";
        }
        catch (Exception ex)
        {
            return "✕ " + ex.Message;
        }
    }

    /// <summary>Μετά από τόση σιωπή ρωτάμε το κέντρο «είσαι εκεί;» (Action: Ping). Όταν δεν χτυπάει τηλέφωνο δεν
    /// έρχεται τίποτα, οπότε χωρίς ερώτηση το ήσυχο κέντρο δεν ξεχωρίζει από το νεκρό.</summary>
    private static readonly TimeSpan PingAfter = TimeSpan.FromSeconds(30);

    /// <summary>Τόση σιωπή παρά τα Ping σημαίνει νεκρή σύνδεση. Αν το κέντρο ή το δίκτυο πέσουν χωρίς να κλείσει
    /// κανονικά η σύνδεση (π.χ. διακοπή ρεύματος), το ταμείο δεν ειδοποιείται ποτέ: περίμενε για πάντα, χωρίς καμία
    /// γραμμή στο αρχείο, και η αναγνώριση κλήσεων έμενε σταματημένη μέχρι να ξανανοίξει η εφαρμογή.</summary>
    private static readonly TimeSpan DeadAfter = TimeSpan.FromSeconds(75);

    /// <summary>Τα εσωτερικά τηλέφωνα έχουν 3-4 ψηφία· αριθμός πελάτη δεν είναι ποτέ τόσο σύντομος.</summary>
    private const int ExtensionMaxDigits = 5;

    /// <summary>Οι κλήσεις σε εξέλιξη που ξεκίνησαν από εσωτερικό τηλέφωνο (Linkedid) — βλ. <see cref="HandleBlock"/>.</summary>
    private static readonly HashSet<string> FromExtension = [];

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
        var banner = reader.ReadLineAsync();
        if (await Task.WhenAny(banner, Task.Delay(DeadAfter)) != banner)
            throw Abandon(banner, "το κέντρο δέχτηκε τη σύνδεση αλλά δεν απαντά");

        await writer.WriteLineAsync("Action: Login");
        await writer.WriteLineAsync("Username: " + s.AmiUsername.Trim());
        await writer.WriteLineAsync("Secret: " + s.AmiPassword);
        await writer.WriteLineAsync();

        var loggedIn = false;
        var lastHeard = DateTime.UtcNow;
        while (true)
        {
            var read = ReadBlockAsync(reader);
            while (await Task.WhenAny(read, Task.Delay(PingAfter)) != read)
            {
                if (DateTime.UtcNow - lastHeard > DeadAfter)
                    throw Abandon(read, "το κέντρο σταμάτησε να απαντά");
                await writer.WriteLineAsync("Action: Ping");
                await writer.WriteLineAsync();
            }

            var block = await read;
            if (block is null)
                return; // η σύνδεση έκλεισε — ξαναπροσπάθησε από το RunForeverAsync
            lastHeard = DateTime.UtcNow;

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
                // Ίδιος λόγος με τη ΔΟΚΙΜΗ: σε κάποια firmware δεν στέλνεται τίποτα μέχρι να ζητηθεί.
                await writer.WriteLineAsync("Action: Events");
                await writer.WriteLineAsync("EventMask: on");
                await writer.WriteLineAsync();
            }

            HandleBlock(block);
        }
    }

    /// <summary>Η ανάγνωση που μένει μετέωρη αποτυγχάνει μόλις κλείσει η σύνδεση. Το ξέρουμε ήδη και γράφεται μία
    /// φορά από το <see cref="Report"/> — χωρίς αυτό θα έμπαινε και σαν σφάλμα «unobserved task» στο αρχείο.</summary>
    private static IOException Abandon(Task read, string reason)
    {
        _ = read.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        return new IOException(reason);
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
    /// εδώ φιλτράρονται μόνο οι κλήσεις που ξεκίνησαν από το μαγαζί — αρκεί να πιάνουμε νωρίς κάθε πιθανό σήμα κλήσης.
    /// </summary>
    private static void HandleBlock(Dictionary<string, string> block)
    {
        if (!block.TryGetValue("Event", out var evt))
            return;
        var dispatcher = Application.Current?.Dispatcher;
        var unique = block.GetValueOrDefault("Uniqueid", "");
        var linked = block.GetValueOrDefault("Linkedid", "");

        // Η κλήση τελείωσε (ο πελάτης έκλεισε, απαντημένη ή όχι): φεύγει η κάρτα της. Μετράει ΜΟΝΟ το κλείσιμο του
        // πρώτου καναλιού της κλήσης (Uniqueid == Linkedid) — όταν χτυπάνε πολλά εσωτερικά και το σηκώσει ένα, τα
        // υπόλοιπα κλείνουν κι αυτά με Hangup, ενώ η κλήση συνεχίζεται· εκείνα δεν πρέπει να κλείσουν την κάρτα.
        if (evt == "Hangup")
        {
            if (unique.Length > 0 && unique == linked)
            {
                FromExtension.Remove(unique);
                dispatcher?.BeginInvoke(() => IncomingCallService.Instance.ReportHangup(unique));
            }
            return;
        }

        if (evt is not ("Newchannel" or "Newstate"))
            return;
        if (!block.TryGetValue("CallerIDNum", out var caller) || caller.Length == 0)
            return;

        // Κλήση που ξεκίνησε από εσωτερικό τηλέφωνο του μαγαζιού (σύντομος αριθμός στο πρώτο της κανάλι) δεν είναι
        // εισερχόμενη: το κανάλι προς τη γραμμή έχει τον αριθμό που κάλεσαν και θα έβγαζε κάρτα σαν να χτυπούσε το
        // τηλέφωνο. Μένει σημειωμένη ως το τέλος της, γιατί και το πρώτο κανάλι μπορεί να πάρει μετά τον αριθμό του
        // μαγαζιού. Σημειώνεται ΜΟΝΟ ό,τι ξέρουμε σίγουρα: στις εισερχόμενες δεν αλλάζει τίποτα.
        if (unique.Length > 0 && unique == linked && caller.Count(char.IsDigit) is > 0 and <= ExtensionMaxDigits)
            FromExtension.Add(unique);
        if (FromExtension.Contains(linked))
            return;

        // Ποια κλήση είναι: όλα τα κανάλια της ίδιας κλήσης έχουν το ίδιο Linkedid (το Uniqueid του πρώτου).
        var callId = linked.Length > 0 ? linked : block.GetValueOrDefault("Uniqueid");
        dispatcher?.BeginInvoke(() => IncomingCallService.Instance.ReportRinging(caller, callId));
    }
}
