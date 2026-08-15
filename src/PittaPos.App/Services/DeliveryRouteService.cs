using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PittaPos.App.Services;

/// <summary>Ένα σημείο στη διαδρομή — το κατάστημα ή μια διεύθυνση παράδοσης.</summary>
public sealed record RouteStop(string Label, string Address, double Lat, double Lon);

public sealed class DeliveryRouteResult
{
    public RouteStop? Shop { get; set; }
    /// <summary>Οι στάσεις παράδοσης, στη βέλτιστη σειρά επίσκεψης (ή στην αρχική σειρά, αν απέτυχε η βελτιστοποίηση).</summary>
    public List<RouteStop> Stops { get; set; } = [];
    /// <summary>Σημεία της γραμμής διαδρομής (lat, lon) για σχεδίαση πάνω στον χάρτη.</summary>
    public List<(double Lat, double Lon)> Geometry { get; set; } = [];
    /// <summary>Διευθύνσεις που δεν εντοπίστηκαν στον χάρτη — δείχνονται στον χρήστη ξεχωριστά.</summary>
    public List<string> FailedAddresses { get; } = [];
}

/// <summary>
/// Γεωκωδικοποίηση + autocomplete διεύθυνσης + βελτιστοποιημένη διαδρομή πολλαπλών στάσεων για τον
/// Χάρτη Διανομής. Δύο υποστηριζόμενοι πάροχοι:
/// - Χωρίς κλειδί (προεπιλογή): Nominatim/OpenStreetMap + OSRM δημόσιος server — δωρεάν, αλλά με
///   πολιτική χρήσης (π.χ. Nominatim θέλει &lt;= 1 αίτημα/δευτ.) και όχι πάντα καλή ποιότητα αναζήτησης
///   σε ελληνικούς δρόμους.
/// - Με κλειδί (SettingsStore.Settings.GoogleMapsApiKey): Google Geocoding/Places/Directions API —
///   καλύτερη ποιότητα αναζήτησης, με κόστος πέρα από το μηνιαίο δωρεάν όριο της Google.
/// </summary>
public static class DeliveryRouteService
{
    // ConnectCallback αναγκάζει IPv4 (αγνοεί τυχόν IPv6 διευθύνσεις της DNS) — δοκιμασμένο ότι σε δίκτυα με
    // «σπασμένο»/μαύρη τρύπα IPv6 routing (π.χ. προβληματικό δρομολογητή ISP, αρκετά συχνό σε ελληνικές
    // συνδέσεις), το προεπιλεγμένο dual-stack HttpClient προσπαθεί πρώτα IPv6, κολλάει χωρίς να πέφτει ποτέ
    // σε timeout/RST, και καταναλώνει ΟΛΟΚΛΗΡΟ το παρακάτω Timeout (12s) πριν καν προλάβει να δοκιμάσει
    // IPv4 — μετρημένο με αυτό ακριβώς το σενάριο: 12000ms αποτυχία χωρίς αυτό, 484ms επιτυχία με αυτό.
    // Χωρίς αυτό, ο Χάρτης Διανομής έμενε άδειος (ούτε η πινέζα του καταστήματος δεν πρόλαβε να λυθεί)
    // σε τέτοια δίκτυα, χωρίς κανένα ορατό σφάλμα — η γεωκωδικοποίηση απλά "χανόταν" σιωπηλά στο timeout.
    private static readonly SocketsHttpHandler Handler = new()
    {
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, AddressFamily.InterNetwork, cancellationToken);
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    private static readonly HttpClient Http = new(Handler) { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    static DeliveryRouteService()
    {
        // Το Nominatim απαιτεί αναγνωρίσιμο User-Agent (πολιτική χρήσης) — χωρίς αυτό μπλοκάρει τα αιτήματα.
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("PittaPos-DeliveryMap/1.0 (+stefkamb@gmail.com)");
    }

    private static string? GoogleKey
    {
        get
        {
            var key = SettingsStore.Instance.Settings.GoogleMapsApiKey.Trim();
            return key.Length > 0 ? key : null;
        }
    }

    /// <summary>Ώστε ο Χάρτης Διανομής να δείχνει με ποιον πάροχο δουλεύει (Google Maps JS ή Leaflet/OSM).</summary>
    public static bool UsingGoogle => GoogleKey is not null;

    private static string Fmt(double d) => d.ToString(CultureInfo.InvariantCulture);

    // Επιβάλλει <=1 αίτημα/δευτ. προς το nominatim.openstreetmap.org (πολιτική χρήσης) μετρώντας τον
    // πραγματικό χρόνο από την ΑΠΟΣΤΟΛΗ του προηγούμενου αιτήματος — όχι ένα σταθερό delay μετά από κάθε
    // απάντηση (όπως πριν), που σπαταλούσε επιπλέον χρόνο πάνω στον ήδη περασμένο χρόνο απόκρισης δικτύου
    // και άφηνε ένα εντελώς άχρηστο τελικό delay μετά την τελευταία διεύθυνση μιας παρτίδας γεωκωδικοποίησης
    // (βλ. GeocodeNominatimCachedAsync/GeocodeDeliveryCachedAsync — έναν χάρτη με πολλές νέες διευθύνσεις
    // παράδοσης τον καθυστερούσε αισθητά περισσότερο απ' όσο χρειάζεται το ίδιο το Nominatim).
    private static readonly SemaphoreSlim NominatimGate = new(1, 1);
    private static DateTime _lastNominatimRequestUtc = DateTime.MinValue;
    private const int NominatimMinGapMs = 1100;

    private static async Task ThrottleNominatimAsync()
    {
        await NominatimGate.WaitAsync();
        try
        {
            var wait = NominatimMinGapMs - (DateTime.UtcNow - _lastNominatimRequestUtc).TotalMilliseconds;
            if (wait > 0)
                await Task.Delay((int)wait);
            _lastNominatimRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            NominatimGate.Release();
        }
    }

    // =========================================================================================
    // Γεωκωδικοποίηση μίας διεύθυνσης
    // =========================================================================================

    /// <summary>Διεύθυνση σε συντεταγμένες· null αν δεν βρέθηκε ή αν κάτι πήγε στραβά με το δίκτυο.</summary>
    public static Task<(double Lat, double Lon)?> GeocodeAsync(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return Task.FromResult<(double, double)?>(null);
        return GoogleKey is { } key ? GeocodeGoogleAsync(address, key) : GeocodeNominatimAsync(address);
    }

    private sealed record NominatimResult(
        [property: JsonPropertyName("lat")] string Lat,
        [property: JsonPropertyName("lon")] string Lon);

    // «Οκτωμβρίου» αντί για «Οκτωβρίου» είναι από τα πιο κοινά ελληνικά ορθογραφικά λάθη σε διευθύνσεις
    // (υπερδιόρθωση κατ' αναλογία με Σεπτέμβριος/Νοέμβριος/Δεκέμβριος, που όντως έχουν «μ») — δοκιμασμένο
    // ότι το Nominatim έχει το «Οκτωμβρίου» καταχωρημένο μόνο σε ελάχιστα σημεία εκτός Αττικής, άρα με
    // αυτή την ορθογραφία μια πραγματική διεύθυνση «28ης Οκτωμβρίου» στην Αθήνα δεν βρίσκεται καθόλου.
    private static readonly Regex OctobriouTypo = new("οκτωμβρ", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static async Task<(double Lat, double Lon)?> GeocodeNominatimAsync(string address, string? viewbox = null)
    {
        try
        {
            var query = OctobriouTypo.Replace(address, "οκτωβρ");
            var url = "https://nominatim.openstreetmap.org/search?format=json&limit=1&countrycodes=gr&"
                + (viewbox is not null ? viewbox + "&" : "") + "q=" + Uri.EscapeDataString(query);
            await ThrottleNominatimAsync();
            var results = await Http.GetFromJsonAsync<List<NominatimResult>>(url);
            var first = results?.FirstOrDefault();
            if (first is null)
                return null;
            if (!double.TryParse(first.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
                return null;
            if (!double.TryParse(first.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                return null;
            return (lat, lon);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record GoogleLatLng(
        [property: JsonPropertyName("lat")] double Lat,
        [property: JsonPropertyName("lng")] double Lng);

    private sealed record GoogleGeometry(
        [property: JsonPropertyName("location")] GoogleLatLng Location);

    private sealed record GoogleGeocodeResult(
        [property: JsonPropertyName("geometry")] GoogleGeometry Geometry);

    private sealed record GoogleGeocodeResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("results")] List<GoogleGeocodeResult>? Results);

    private static async Task<(double Lat, double Lon)?> GeocodeGoogleAsync(string address, string key)
    {
        try
        {
            var url = "https://maps.googleapis.com/maps/api/geocode/json?region=gr&language=el&address="
                + Uri.EscapeDataString(address) + "&key=" + Uri.EscapeDataString(key);
            var resp = await Http.GetFromJsonAsync<GoogleGeocodeResponse>(url);
            var loc = resp?.Results?.FirstOrDefault()?.Geometry.Location;
            return loc is null ? null : (loc.Lat, loc.Lng);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // =========================================================================================
    // Προτάσεις διεύθυνσης (autocomplete) — Βήμα 2 στοιχείων πελάτη
    // =========================================================================================

    /// <summary>Μια πρόταση διεύθυνσης για το autocomplete στα στοιχεία πελάτη (Βήμα 2). Στο Nominatim
    /// έρχεται ήδη πλήρως χωρισμένη στα πεδία της φόρμας· στο Google έρχεται μόνο με Display+PlaceId —
    /// τα υπόλοιπα πεδία γεμίζουν μόνο όταν επιλεγεί (βλ. ResolveGooglePlaceAsync), γιατί η Place
    /// Details κλήση χρεώνεται/κοστίζει πολύ περισσότερο από μια απλή πρόταση στη λίστα.</summary>
    public sealed record AddressSuggestion(string Display, string Street, string HouseNumber, string Area,
        string PostalCode, string? PlaceId = null);

    public static Task<List<AddressSuggestion>> SuggestAddressesAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3)
            return Task.FromResult(new List<AddressSuggestion>());
        return GoogleKey is { } key
            ? SuggestAddressesGoogleAsync(query.Trim(), key)
            : SuggestAddressesNominatimAsync(query.Trim());
    }

    private sealed record NominatimAddress(
        [property: JsonPropertyName("road")] string? Road,
        [property: JsonPropertyName("house_number")] string? HouseNumber,
        [property: JsonPropertyName("suburb")] string? Suburb,
        [property: JsonPropertyName("city_district")] string? CityDistrict,
        [property: JsonPropertyName("town")] string? Town,
        [property: JsonPropertyName("village")] string? Village,
        [property: JsonPropertyName("municipality")] string? Municipality,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("postcode")] string? Postcode);

    private sealed record NominatimFullResult(
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("address")] NominatimAddress? Address,
        [property: JsonPropertyName("lat")] string? Lat,
        [property: JsonPropertyName("lon")] string? Lon);

    // Πλαίσιο Αττικής (Αθήνα + γύρω δήμοι) — μόνο fallback όταν δεν έχει οριστεί/γεωκωδικοποιηθεί ακόμα η
    // διεύθυνση καταστήματος (βλ. BuildRadiusViewbox παρακάτω για το κανονικό, στενό πλαίσιο γύρω από το
    // μαγαζί). Nominatim viewbox: αριστερά,πάνω,δεξιά,κάτω (lon_min,lat_max,lon_max,lat_min) — bounded=1
    // το κάνει σκληρό φίλτρο, όχι απλή προτίμηση.
    private const string AthensViewbox = "viewbox=23.55,38.15,23.95,37.85&bounded=1";

    // Ακτίνα γύρω από το κατάστημα μέσα στην οποία περιορίζονται οι προτάσεις διεύθυνσης — πιο στενό
    // πλαίσιο από ολόκληρη την Αττική σημαίνει λιγότερα/πιο σχετικά αποτελέσματα από το Nominatim, άρα
    // πιο γρήγορη και πιο «σωστή» αυτόματη συμπλήρωση (δεν προτείνει διευθύνσεις έξω από την περιοχή
    // παράδοσης). Αρκετά γενναιόδωρη ώστε να καλύπτει σίγουρα όλη τη συνηθισμένη ζώνη διανομής.
    private const double DeliveryRadiusKm = 10;

    /// <summary>Nominatim viewbox γύρω από ένα σημείο, ±radiusKm και στους δύο άξονες.</summary>
    private static string BuildRadiusViewbox(double lat, double lon, double radiusKm)
    {
        var dLat = radiusKm / 111.0;
        var dLon = radiusKm / (111.0 * Math.Cos(lat * Math.PI / 180));
        return "viewbox=" + Fmt(lon - dLon) + "," + Fmt(lat + dLat) + "," + Fmt(lon + dLon) + "," + Fmt(lat - dLat) + "&bounded=1";
    }

    // Συντεταγμένες του καταστήματος (SettingsStore.Settings.ShopAddress) — μοιράζεται το ίδιο
    // _osmGeocodeCache με τη γεωκωδικοποίηση του Χάρτη Διανομής (βλ. GeocodeNominatimCachedAsync
    // παρακάτω) αντί για ξεχωριστό cache μίας τιμής: πριν, το άνοιγμα του autocomplete και το άνοιγμα
    // του χάρτη γεωκωδικοποιούσαν το ίδιο κατάστημα ο καθένας ξεχωριστά (δύο πραγματικά αιτήματα προς
    // Nominatim για την ίδια ακριβώς διεύθυνση, ~1s χαμένο τη δεύτερη φορά) — τώρα όποιο ζητηθεί πρώτο
    // γεμίζει το cache και για τους δύο.
    /// <summary>Δημόσιο επίσης για τον Χάρτη Διανομής (βλ. DeliveryMapWindow.ShowShopPinQuicklyAsync) — έτσι
    /// το παράθυρο δείχνει την πινέζα του καταστήματος αμέσως μόλις ανοίξει, χωρίς να περιμένει να
    /// γεωκωδικοποιηθούν όλες οι παραδόσεις· ίδιο cache, άρα κανένα επιπλέον αίτημα προς το Nominatim.</summary>
    public static async Task<(double Lat, double Lon)?> GetShopCoordsAsync()
    {
        // 1) Το σημείο που έδειξε ο ίδιος ο ταμίας στον χάρτη — υπερισχύει πάντα, και δουλεύει ακόμα κι
        //    όταν το πεδίο διεύθυνσης είναι κενό ή η διεύθυνση δεν βρίσκεται πουθενά.
        if (AddressPointsService.Instance.Get(AddressPointsService.ShopPointKey) is { } pinned)
            return (pinned.Lat, pinned.Lon);

        // 2) Αλλιώς, η γραμμένη διεύθυνση (ίδιο cache/σημεία με τις παραδόσεις).
        var address = SettingsStore.Instance.Settings.ShopAddress.Trim();
        return address.Length == 0 ? null : await GeocodeNominatimCachedAsync(address);
    }

    /// <summary>Απόσταση σε km μεταξύ δύο σημείων (τύπος Haversine) — μόνο για ταξινόμηση προτάσεων
    /// από την πιο κοντινή στο κατάστημα, δεν χρειάζεται ακρίβεια δρόμου.</summary>
    private static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>Ο καλών πρέπει να κάνει δικό του debounce πριν καλέσει αυτό (βλ. OrderWizardViewModel) —
    /// σκόπιμα ΧΩΡΙΣ ThrottleNominatimAsync εδώ (σε αντίθεση με τη γεωκωδικοποίηση παρακάτω): αυτό είναι
    /// ζωντανό autocomplete όσο πληκτρολογεί ο ταμίας, το κοινό gate 1.1s με τυχόν παράλληλη
    /// γεωκωδικοποίηση παραγγελιών (π.χ. συγχρονισμός Χάρτη Διανομής) θα καθυστερούσε αισθητά την
    /// εμφάνιση προτάσεων — δοκιμασμένο ότι το UI debounce (110ms) μόνο του αρκεί, δεν παραβιάζεται στην
    /// πράξη το όριο του Nominatim από ανθρώπινη ταχύτητα πληκτρολόγησης.</summary>
    private static async Task<List<AddressSuggestion>> SuggestAddressesNominatimAsync(string query)
    {
        try
        {
            // Στενό πλαίσιο γύρω από το μαγαζί (όχι όλη η Αττική) όταν έχουμε τις συντεταγμένες του —
            // πιο γρήγορο, πιο σχετικό Nominatim αποτέλεσμα, καμία πρόταση έξω από την ακτίνα διανομής.
            var shop = await GetShopCoordsAsync();
            var viewbox = shop is { } sc ? BuildRadiusViewbox(sc.Lat, sc.Lon, DeliveryRadiusKm) : AthensViewbox;

            // layer=address αποκλείει μαγαζιά/επιχειρήσεις/τοποθεσίες (POI layer) — μόνο δρόμοι/περιοχές/
            // διευθύνσεις, όχι «Φούρνος Χ, Μαγνησίας 12, ...». Χωρίς αυτό το Nominatim ανακατεύει POI
            // αποτελέσματα μέσα στις προτάσεις.
            var url = "https://nominatim.openstreetmap.org/search?format=json&addressdetails=1&limit=8&countrycodes=gr&layer=address&"
                + viewbox + "&q=" + Uri.EscapeDataString(query);
            // Κοντό timeout (3s) αντί για το κοινό 12s του client — αυτό τρέχει ενώ ο ταμίας πληκτρολογεί.
            // Αν το Nominatim αργεί, δεν έχει νόημα να περιμένει 12" ολόκληρα: καλύτερα να αποτύχει
            // γρήγορα και να προλάβει το Photon (fallback παρακάτω), που είναι φτιαγμένο για ζωντανή
            // αναζήτηση. Ίδια λογική με το SuggestAddressesPhotonAsync.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var results = await Http.GetFromJsonAsync<List<NominatimFullResult>>(url, cts.Token);
            if (results is null || results.Count == 0)
            {
                // Το Nominatim δεν κάνει καθόλου prefix-ταίριασμα σε μισοτελειωμένη τελευταία λέξη
                // (δοκιμασμένο: πάντα 0 αποτελέσματα, π.χ. «28ης Οκτ» ενώ «28ης» ή η πλήρης «28ης
                // Οκτωβρίου» δουλεύουν) — το Photon (photon.komoot.io, άλλος δωρεάν geocoder πάνω σε
                // OpenStreetMap, φτιαγμένος για ζωντανή αναζήτηση καθώς πληκτρολογεί κάποιος) καταλαβαίνει
                // μισοτελειωμένες λέξεις πολύ καλύτερα — δοκιμασμένο ότι βρίσκει ακριβώς αυτές τις
                // περιπτώσεις. Fallback μόνο εδώ, το Nominatim μένει πρώτη επιλογή γιατί έχει πιο σταθερή
                // δομημένη ανάλυση διεύθυνσης (addressdetails).
                return await SuggestAddressesPhotonAsync(query, shop);
            }

            var tokens = QueryTokens(query);

            return results.Select(r =>
            {
                var street = r.Address?.Road ?? r.DisplayName.Split(',')[0];
                var area = r.Address?.Suburb ?? r.Address?.CityDistrict ?? r.Address?.Town
                    ?? r.Address?.Village ?? r.Address?.Municipality ?? r.Address?.City ?? "";
                var houseNumber = r.Address?.HouseNumber ?? "";
                // Σύντομη εμφάνιση («Μαγνησίας 12, Βύρωνας») αντί για το ωμό display_name του Nominatim,
                // που συχνά κουβαλάει ολόκληρη την αλυσίδα διοικητικών ενοτήτων μέχρι Τ.Κ./χώρα.
                var streetWithNumber = houseNumber.Length > 0 ? street.Trim() + " " + houseNumber : street.Trim();
                var shortDisplay = area.Length > 0 ? streetWithNumber + ", " + area : streetWithNumber;
                var suggestion = new AddressSuggestion(shortDisplay, street.Trim(), houseNumber, area, r.Address?.Postcode ?? "");

                var distance = shop is { } s
                    && double.TryParse(r.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                    && double.TryParse(r.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                        ? DistanceKm(s.Lat, s.Lon, lat, lon)
                        : double.MaxValue;
                return (Suggestion: suggestion, Distance: distance,
                    Score: MatchScore(suggestion, tokens, r.Address?.Suburb, r.Address?.CityDistrict,
                        r.Address?.Town, r.Address?.Village, r.Address?.Municipality, r.Address?.City));
            })
            .Where(x => x.Suggestion.Street.Length > 0)
            // Πρώτα πόσο ταιριάζει με ό,τι γράφτηκε (κυρίως η περιοχή), ΜΕΤΑ η απόσταση από το μαγαζί —
            // η απόσταση μόνη της έβγαζε πρώτη ομώνυμη οδό σε λάθος περιοχή (βλ. MatchScore).
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Distance)
            .Select(x => x.Suggestion)
            .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Πόσο ταιριάζει μια πρόταση με ό,τι πληκτρολόγησε ο ταμίας. Χωρίς αυτό, οι προτάσεις έμπαιναν σε
    /// σειρά ΜΟΝΟ με βάση την απόσταση από το μαγαζί, οπότε γράφοντας «οδός + περιοχή» μαζί έβγαινε
    /// πρώτη μια ομώνυμη οδός σε ΑΛΛΗ, πιο κοντινή περιοχή — ενώ ο ταμίας είχε ήδη γράψει ρητά ποια
    /// περιοχή θέλει. Η περιοχή βαραίνει πολύ περισσότερο από τον δρόμο: ο δρόμος ταιριάζει σχεδόν
    /// πάντα (γι' αυτό επέστρεψε το αποτέλεσμα), η περιοχή είναι αυτή που ξεχωρίζει τα ομώνυμα.
    /// </summary>
    /// <param name="areaNames">ΟΛΑ τα ονόματα περιοχής που γύρισε ο geocoder, όχι μόνο αυτό που τελικά
    /// εμφανίζεται. Κρίσιμο: για την «Ερμού, Δάφνη» το Photon γυρίζει district «Υμηττός» και city «Δήμος
    /// Δάφνης - Υμηττού», και κρατάμε για εμφάνιση μόνο το district — οπότε αν βαθμολογούσαμε μόνο αυτό,
    /// η λέξη «Δάφνη» που έγραψε ο ταμίας δεν θα μπορούσε ΠΟΤΕ να ταιριάξει. Το ίδιο ισχύει στο Nominatim
    /// (suburb έναντι municipality/city).</param>
    private static int MatchScore(AddressSuggestion s, IReadOnlyList<string> queryTokens, params string?[] areaNames)
    {
        var areaHaystack = Core.Data.MenuSeed.ToUpperGreek(
            string.Join(" ", areaNames.Where(a => !string.IsNullOrWhiteSpace(a))));
        var street = Core.Data.MenuSeed.ToUpperGreek(s.Street);
        var score = 0;

        foreach (var token in queryTokens)
        {
            if (token.All(char.IsDigit))
            {
                if (s.HouseNumber.Length > 0 && s.HouseNumber == token)
                    score += 2;
                continue;
            }
            if (token.Length < 3)
                continue;
            // Δρόμος και περιοχή μετράνε ΙΣΟΤΙΜΑ και ΑΘΡΟΙΣΤΙΚΑ (όχι else-if): μόνο έτσι κερδίζει αυτό
            // που ταιριάζει ΚΑΙ στα δύο. Δοκιμασμένο με αληθινά δεδομένα geocoder — αν βάραινε μόνο η
            // περιοχή, το «Ερμού 14 Δάφνη» έβγαζε πρώτο το «Έλλης 14, Δάφνη» (σωστή περιοχή, λάθος
            // δρόμος)· αν βάραινε μόνο ο δρόμος, ξαναγυρνάγαμε στο αρχικό πρόβλημα (ομώνυμη οδός σε
            // λάθος περιοχή). Ο αριθμός μετράει λιγότερο — ο geocoder συχνά γυρίζει τον δρόμο χωρίς αυτόν.
            if (areaHaystack.Contains(token, StringComparison.Ordinal))
                score += 5;
            if (street.Contains(token, StringComparison.Ordinal))
                score += 5;
        }
        return score;
    }

    /// <summary>Χωρίζει ό,τι πληκτρολογήθηκε σε λέξεις-κλειδιά (κεφαλαία, χωρίς τόνους) για το MatchScore.</summary>
    private static List<string> QueryTokens(string query) =>
        Core.Data.MenuSeed.ToUpperGreek(query)
            .Split([' ', ',', '.', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private sealed record PhotonProperties(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("street")] string? Street,
        [property: JsonPropertyName("housenumber")] string? HouseNumber,
        [property: JsonPropertyName("district")] string? District,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("postcode")] string? Postcode);

    private sealed record PhotonSuggestFeature(
        [property: JsonPropertyName("properties")] PhotonProperties Properties);

    private sealed record PhotonSuggestResponse(
        [property: JsonPropertyName("features")] List<PhotonSuggestFeature>? Features);

    /// <summary>Δεύτερος δωρεάν, χωρίς κλειδί geocoder (photon.komoot.io) — καλείται μόνο όταν το
    /// Nominatim δεν βρίσκει τίποτα για μια μισοτελειωμένη λέξη (βλ. σχόλιο στο SuggestAddressesNominatimAsync).
    /// Το <paramref name="bias"/> (συντεταγμένες καταστήματος) δίνεται στο Photon ως προτίμηση θέσης
    /// (lat/lon/zoom) — όχι σκληρό όριο περιοχής σαν το viewbox του Nominatim, το Photon δεν το υποστηρίζει
    /// ως τέτοιο, αλλά βοηθά να προτιμηθούν κοντινά αποτελέσματα. Ξεχωριστό, ΚΟΝΤΟ timeout (3s) αντί για
    /// το κοινό 12s του Http client — δοκιμασμένο ότι το δωρεάν δημόσιο photon.komoot.io μπορεί να κρεμάσει
    /// ολόκληρα δευτερόλεπτα (μέχρι τα πλήρη 12s) όταν είναι αργό/φορτωμένο, κάνοντας το autocomplete να
    /// φαίνεται εντελώς παγωμένο ενώ ο ταμίας γράφει· καλύτερα να αποτύχει γρήγορα (καμία πρόταση) παρά να
    /// κάνει τον ταμία να περιμένει 12" σε κάθε μισοτελειωμένη λέξη.</summary>
    private static async Task<List<AddressSuggestion>> SuggestAddressesPhotonAsync(string query, (double Lat, double Lon)? bias)
    {
        try
        {
            var url = "https://photon.komoot.io/api/?limit=8&lang=default&q=" + Uri.EscapeDataString(query);
            if (bias is { } b)
                url += "&lat=" + Fmt(b.Lat) + "&lon=" + Fmt(b.Lon) + "&zoom=14";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var resp = await Http.GetFromJsonAsync<PhotonSuggestResponse>(url, JsonOpts, cts.Token);
            var features = resp?.Features ?? [];

            var tokens = QueryTokens(query);

            return features.Select(f =>
            {
                var p = f.Properties;
                var street = (p.Street ?? p.Name ?? "").Trim();
                var area = (p.District ?? p.City ?? "").Trim();
                var houseNumber = (p.HouseNumber ?? "").Trim();
                var streetWithNumber = houseNumber.Length > 0 ? street + " " + houseNumber : street;
                var shortDisplay = area.Length > 0 ? streetWithNumber + ", " + area : streetWithNumber;
                return (Suggestion: new AddressSuggestion(shortDisplay, street, houseNumber, area, p.Postcode ?? ""),
                    p.District, p.City);
            })
            .Where(x => x.Suggestion.Street.Length > 0)
            // Ίδια λογική με το Nominatim (βλ. MatchScore): προτεραιότητα σε ό,τι ταιριάζει με την
            // περιοχή που γράφτηκε. Το OrderByDescending είναι σταθερό, οπότε σε ισοβαθμία διατηρείται
            // η σειρά σχετικότητας που έδωσε το ίδιο το Photon.
            .OrderByDescending(x => MatchScore(x.Suggestion, tokens, x.District, x.City))
            .Select(x => x.Suggestion)
            .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private sealed record GooglePrediction(
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("place_id")] string PlaceId);

    private sealed record GoogleAutocompleteResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("predictions")] List<GooglePrediction>? Predictions);

    private static async Task<List<AddressSuggestion>> SuggestAddressesGoogleAsync(string query, string key)
    {
        try
        {
            var url = "https://maps.googleapis.com/maps/api/place/autocomplete/json?components=country:gr&language=el&input="
                + Uri.EscapeDataString(query) + "&key=" + Uri.EscapeDataString(key);
            var resp = await Http.GetFromJsonAsync<GoogleAutocompleteResponse>(url);
            return (resp?.Predictions ?? [])
                .Take(5)
                .Select(p => new AddressSuggestion(p.Description, "", "", "", "", p.PlaceId))
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private sealed record GoogleAddressComponent(
        [property: JsonPropertyName("long_name")] string LongName,
        [property: JsonPropertyName("types")] List<string> Types);

    private sealed record GoogleDetailsResult(
        [property: JsonPropertyName("address_components")] List<GoogleAddressComponent>? AddressComponents);

    private sealed record GoogleDetailsResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("result")] GoogleDetailsResult? Result);

    /// <summary>Καλείται μόνο όταν ο ταμίας επιλέξει μια πρόταση Google από τη λίστα — γεμίζει τα
    /// ξεχωριστά πεδία (Οδός/Αριθμός/Περιοχή/Τ.Κ.) από τα address_components της Place Details.</summary>
    public static async Task<AddressSuggestion?> ResolveGooglePlaceAsync(string placeId)
    {
        var key = GoogleKey;
        if (key is null)
            return null;
        try
        {
            var url = "https://maps.googleapis.com/maps/api/place/details/json?language=el&fields=address_component&place_id="
                + Uri.EscapeDataString(placeId) + "&key=" + Uri.EscapeDataString(key);
            var resp = await Http.GetFromJsonAsync<GoogleDetailsResponse>(url);
            var comps = resp?.Result?.AddressComponents ?? [];

            string Find(params string[] types) =>
                comps.FirstOrDefault(c => c.Types.Any(types.Contains))?.LongName ?? "";

            var street = Find("route");
            var number = Find("street_number");
            var area = Find("sublocality", "sublocality_level_1", "locality",
                "administrative_area_level_3", "administrative_area_level_2");
            var postal = Find("postal_code");
            var display = (street + " " + number).Trim();
            return new AddressSuggestion(display.Length > 0 ? display : area, street, number, area, postal);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // =========================================================================================
    // Διαδρομή πολλαπλών στάσεων
    // =========================================================================================

    /// <summary>
    /// Γεωκωδικοποιεί το κατάστημα + κάθε παράδοση, μετά ζητά τη βέλτιστη σειρά επίσκεψης (γύρος με
    /// αφετηρία/επιστροφή το κατάστημα, αν έχει δοθεί διεύθυνση καταστήματος).
    /// </summary>
    /// <param name="onStopGeocoded">Καλείται αμέσως μόλις γεωκωδικοποιηθεί επιτυχώς μία παράδοση (με τη
    /// σειρά προτεραιότητας του <paramref name="deliveries"/>, δηλαδή τη σειρά καταχώρησης — παλαιότερη
    /// πρώτη), πριν ολοκληρωθούν όλες — έτσι ο Χάρτης Διανομής μπορεί να δείχνει τις πινέζες μία-μία καθώς
    /// έρχονται αντί να περιμένει το σύνολο + τη βελτιστοποίηση διαδρομής (βλ. DeliveryMapWindow.SyncPinsAsync).
    /// Μόνο για το OSM/Nominatim μονοπάτι (throttled, άρα αργό με πολλές νέες διευθύνσεις) — το Google
    /// μονοπάτι δεν έχει τέτοιο περιορισμό, χτίζει τη σελίδα μία φορά με όλα μέσα, αγνοεί την παράμετρο.</param>
    public static Task<DeliveryRouteResult> BuildRouteAsync(
        string shopAddress, IReadOnlyList<(string Label, string Address)> deliveries,
        Func<RouteStop, int, Task>? onStopGeocoded = null)
    {
        return GoogleKey is { } key
            ? BuildRouteGoogleAsync(shopAddress, deliveries, key)
            : BuildRouteOsmAsync(shopAddress, deliveries, onStopGeocoded);
    }

    /// <summary>Διαδρομή οδήγησης ανάμεσα σε δύο συγκεκριμένα σημεία, χωρίς βελτιστοποίηση πολλαπλών
    /// στάσεων — για το «δείξε μου τη διαδρομή» όταν ο ταμίας πατά μια συγκεκριμένη πινέζα στον Χάρτη
    /// Διανομής (βλ. DeliveryMapWindow).</summary>
    public static Task<List<(double Lat, double Lon)>> GetRouteGeometryAsync(
        (double Lat, double Lon) from, (double Lat, double Lon) to)
    {
        return GoogleKey is { } key
            ? GetRouteGeometryGoogleAsync(from, to, key)
            : GetRouteGeometryOsmAsync(from, to);
    }

    private sealed record OsrmRouteResponse(
        [property: JsonPropertyName("routes")] List<OsrmTrip>? Routes);

    private static async Task<List<(double Lat, double Lon)>> GetRouteGeometryOsmAsync(
        (double Lat, double Lon) from, (double Lat, double Lon) to)
    {
        try
        {
            // http:// όχι https:// -- σε αυτό το δίκτυο το TLS handshake προς router.project-osrm.org
            // αποτυγχάνει (δοκιμασμένο, SEC_E_ILLEGAL_MESSAGE), αλλά το απλό HTTP δουλεύει κανονικά.
            var url = "http://router.project-osrm.org/route/v1/driving/"
                + Fmt(from.Lon) + "," + Fmt(from.Lat) + ";" + Fmt(to.Lon) + "," + Fmt(to.Lat)
                + "?geometries=geojson&overview=full";
            var resp = await Http.GetFromJsonAsync<OsrmRouteResponse>(url, JsonOpts);
            var coords = resp?.Routes?.FirstOrDefault()?.Geometry.Coordinates;
            return coords?.Select(c => (c[1], c[0])).ToList() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static async Task<List<(double Lat, double Lon)>> GetRouteGeometryGoogleAsync(
        (double Lat, double Lon) from, (double Lat, double Lon) to, string key)
    {
        try
        {
            var url = "https://maps.googleapis.com/maps/api/directions/json?mode=driving&origin="
                + Fmt(from.Lat) + "," + Fmt(from.Lon) + "&destination=" + Fmt(to.Lat) + "," + Fmt(to.Lon)
                + "&key=" + Uri.EscapeDataString(key);
            var resp = await Http.GetFromJsonAsync<GoogleDirectionsResponse>(url);
            var route = resp?.Routes?.FirstOrDefault();
            return route is null ? [] : DecodePolyline(route.OverviewPolyline?.Points ?? "");
        }
        catch (Exception)
        {
            return [];
        }
    }

    private sealed record OsrmWaypoint(
        [property: JsonPropertyName("waypoint_index")] int WaypointIndex);

    private sealed record OsrmGeometry(
        [property: JsonPropertyName("coordinates")] List<List<double>> Coordinates);

    private sealed record OsrmTrip(
        [property: JsonPropertyName("geometry")] OsrmGeometry Geometry);

    private sealed record OsrmTripResponse(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("trips")] List<OsrmTrip>? Trips,
        [property: JsonPropertyName("waypoints")] List<OsrmWaypoint>? Waypoints);

    // Cache γεωκωδικοποιημένων διευθύνσεων (μόνο επιτυχημένων) για όσο τρέχει η εφαρμογή — έτσι το
    // άνοιγμα/ανανέωση του Χάρτη Διανομής δεν ξαναρωτάει το Nominatim (ούτε ξαναπερνάει από το 1.1s
    // per-request όριο, βλ. ThrottleNominatimAsync) για διευθύνσεις που έχουν ήδη λυθεί σε προηγούμενο
    // άνοιγμα· μόνο οι καινούριες παραγγελίες περιμένουν πραγματικό αίτημα. Οι αποτυχίες δεν αποθηκεύονται,
    // ώστε το κουμπί "Ανανέωση" να μπορεί να ξαναδοκιμάσει μια διεύθυνση που απέτυχε λόγω παροδικού
    // προβλήματος δικτύου.
    private static readonly Dictionary<string, (double Lat, double Lon)?> _osmGeocodeCache = new();

    private static async Task<(double Lat, double Lon)?> GeocodeNominatimCachedAsync(string address)
    {
        // Και το κατάστημα περνάει από τα δικά μας σημεία: αν η διεύθυνσή του δεν εντοπίζεται σωστά (ή
        // καθόλου) στον χάρτη, ο ταμίας τη δείχνει μία φορά και η πινέζα του μαγαζιού μένει εκεί.
        if (AddressPointsService.Instance.Get(address) is { } known)
            return (known.Lat, known.Lon);

        if (_osmGeocodeCache.TryGetValue(address, out var cached))
            return cached;
        var geo = await GeocodeNominatimAsync(address);
        if (geo is { } g)
        {
            _osmGeocodeCache[address] = g;
            AddressPointsService.Instance.Set(address, g.Lat, g.Lon, manual: false);
        }
        return geo;
    }

    /// <summary>Σαν το GeocodeNominatimCachedAsync, αλλά για διευθύνσεις παράδοσης: αν η πλήρης αναζήτηση
    /// (οδός+αριθμός, περιοχή) αποτύχει, ξαναδοκιμάζει μόνο με οδό+αριθμό — δοκιμασμένο ότι συχνά η
    /// γειτονιά που δίνει ο πελάτης δεν ταιριάζει ακριβώς με το διοικητικό όριο του OSM για εκείνο το
    /// σημείο (π.χ. οδός σε γειτονικό δήμο), οπότε το φιλτράρισμα ανά περιοχή αποκλείει σιωπηλά μια σωστή
    /// διεύθυνση. Το δεύτερο αίτημα περιορίζεται γεωγραφικά σε ακτίνα γύρω από το κατάστημα (viewbox, ίδια
    /// λογική με το autocomplete) αντί για φιλτράρισμα με όνομα περιοχής — έτσι βρίσκει τον σωστό δρόμο
    /// κοντά στο μαγαζί, όχι μια ομώνυμη οδό σε τελείως άλλη πόλη (βλ. σχόλιο στο CleanAddressForGeocoding
    /// στο DeliveryMapWindow για το πόσο επικίνδυνο είναι αυτό χωρίς κανέναν γεωγραφικό περιορισμό).</summary>
    private static async Task<(double Lat, double Lon)?> GeocodeDeliveryCachedAsync(
        string address, (double Lat, double Lon)? shopCoords)
    {
        // ΠΡΩΤΑ τα δικά μας σημεία: μια διεύθυνση που έχει ήδη εντοπιστεί — και κυρίως μία που τη
        // διόρθωσε ο ταμίας πάνω στον χάρτη — δεν ξαναρωτιέται ποτέ σε geocoder (βλ. AddressPointsService).
        if (AddressPointsService.Instance.Get(address) is { } known)
            return (known.Lat, known.Lon);

        if (_osmGeocodeCache.TryGetValue(address, out var cached))
            return cached;

        var geo = await GeocodeNominatimAsync(address);

        if (geo is null && shopCoords is { } shop)
        {
            var commaIdx = address.IndexOf(',');
            if (commaIdx > 0)
            {
                var streetOnly = address[..commaIdx].Trim();
                var viewbox = BuildRadiusViewbox(shop.Lat, shop.Lon, DeliveryRadiusKm);
                var fallback = await GeocodeNominatimAsync(streetOnly, viewbox);
                if (fallback is { } f && DistanceKm(shop.Lat, shop.Lon, f.Lat, f.Lon) <= DeliveryRadiusKm)
                    geo = f;
            }
        }

        if (geo is { } g)
        {
            _osmGeocodeCache[address] = g;
            // Ό,τι βρέθηκε μένει γραμμένο στο μαγαζί (manual: false — ο ταμίας μπορεί να το διορθώσει
            // από πάνω): την επόμενη φορά η πινέζα μπαίνει ακαριαία, χωρίς internet και χωρίς αναμονή.
            AddressPointsService.Instance.Set(address, g.Lat, g.Lon, manual: false);
        }
        return geo;
    }

    private static async Task<DeliveryRouteResult> BuildRouteOsmAsync(
        string shopAddress, IReadOnlyList<(string Label, string Address)> deliveries,
        Func<RouteStop, int, Task>? onStopGeocoded = null)
    {
        var result = new DeliveryRouteResult();

        // Ίδια σειρά με το GetShopCoordsAsync: πρώτα το σημείο που έδειξε ο ταμίας, μετά η διεύθυνση.
        // Χωρίς αυτό, το καρφιτσωμένο μαγαζί φαινόταν στο άνοιγμα του χάρτη αλλά εξαφανιζόταν μόλις
        // υπήρχε έστω μία παράδοση (οπότε ο κώδικας περνούσε από εδώ).
        RouteStop? shop = null;
        var shopCoordsNow = await GetShopCoordsAsync();
        if (shopCoordsNow is { } sc)
            shop = new RouteStop("Κατάστημα", shopAddress, sc.Lat, sc.Lon);
        else if (!string.IsNullOrWhiteSpace(shopAddress))
            result.FailedAddresses.Add("Κατάστημα: " + shopAddress);

        var shopCoords = shop is { } s0 ? (s0.Lat, s0.Lon) : ((double Lat, double Lon)?)null;
        var stops = new List<RouteStop>();
        foreach (var (label, address) in deliveries)
        {
            var geo = await GeocodeDeliveryCachedAsync(address, shopCoords);
            if (geo is { } g)
            {
                var stop = new RouteStop(label, address, g.Lat, g.Lon);
                stops.Add(stop);
                if (onStopGeocoded is not null)
                    await onStopGeocoded(stop, stops.Count);
            }
            else
                result.FailedAddresses.Add(label + ": " + address);
        }

        result.Shop = shop;
        if (stops.Count == 0)
            return result;
        if (stops.Count == 1 && shop is null)
        {
            result.Stops = stops;
            return result;
        }

        var points = shop is not null ? new List<RouteStop> { shop } : [];
        points.AddRange(stops);

        try
        {
            var coordParam = string.Join(";", points.Select(p => Fmt(p.Lon) + "," + Fmt(p.Lat)));
            // http:// όχι https:// -- βλ. σχόλιο στο GetRouteGeometryOsmAsync.
            var url = $"http://router.project-osrm.org/trip/v1/driving/{coordParam}"
                + "?geometries=geojson&overview=full&roundtrip=true" + (shop is not null ? "&source=first" : "");
            var trip = await Http.GetFromJsonAsync<OsrmTripResponse>(url, JsonOpts);
            if (trip?.Code == "Ok" && trip.Waypoints is { Count: > 0 } waypoints && trip.Trips is { Count: > 0 })
            {
                result.Stops = waypoints
                    .Select((wp, inputIndex) => (wp.WaypointIndex, inputIndex))
                    .OrderBy(x => x.WaypointIndex)
                    .Select(x => points[x.inputIndex])
                    .Where(p => shop is null || p != shop) // το κατάστημα δείχνεται ξεχωριστά, όχι σαν «στάση»
                    .ToList();
                result.Geometry = trip.Trips[0].Geometry.Coordinates.Select(c => (c[1], c[0])).ToList();
                return result;
            }
        }
        catch (Exception)
        {
            // OSRM μη διαθέσιμο/απέτυχε — γύρνα τα σημεία χωρίς βελτιστοποιημένη σειρά/γραμμή διαδρομής,
            // καλύτερα να δείξει έστω τα pins παρά τίποτα.
        }

        result.Stops = stops;
        return result;
    }

    private sealed record GoogleOverviewPolyline(
        [property: JsonPropertyName("points")] string Points);

    private sealed record GoogleRoute(
        [property: JsonPropertyName("waypoint_order")] List<int>? WaypointOrder,
        [property: JsonPropertyName("overview_polyline")] GoogleOverviewPolyline? OverviewPolyline);

    private sealed record GoogleDirectionsResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("routes")] List<GoogleRoute>? Routes);

    private static async Task<DeliveryRouteResult> BuildRouteGoogleAsync(
        string shopAddress, IReadOnlyList<(string Label, string Address)> deliveries, string key)
    {
        var result = new DeliveryRouteResult();

        RouteStop? shop = null;
        if (!string.IsNullOrWhiteSpace(shopAddress))
        {
            var geo = await GeocodeGoogleAsync(shopAddress, key);
            if (geo is { } g)
                shop = new RouteStop("Κατάστημα", shopAddress, g.Lat, g.Lon);
            else
                result.FailedAddresses.Add("Κατάστημα: " + shopAddress);
        }

        var stops = new List<RouteStop>();
        foreach (var (label, address) in deliveries)
        {
            // Ίδια σειρά με το OSM μονοπάτι: πρώτα τα σημεία που ξέρει το μαγαζί (βλ. AddressPointsService),
            // και μόνο για άγνωστη διεύθυνση ρωτιέται η Google — που χρεώνεται κιόλας ανά αίτημα.
            if (AddressPointsService.Instance.Get(address) is { } known)
            {
                stops.Add(new RouteStop(label, address, known.Lat, known.Lon));
                continue;
            }
            var geo = await GeocodeGoogleAsync(address, key);
            if (geo is { } g)
            {
                stops.Add(new RouteStop(label, address, g.Lat, g.Lon));
                AddressPointsService.Instance.Set(address, g.Lat, g.Lon, manual: false);
            }
            else
                result.FailedAddresses.Add(label + ": " + address);
        }

        result.Shop = shop;
        if (stops.Count == 0)
            return result;
        if (stops.Count == 1 && shop is null)
        {
            result.Stops = stops;
            return result;
        }

        // Χωρίς διεύθυνση καταστήματος: η πρώτη παράδοση γίνεται αφετηρία/τέλος του γύρου (καλύτερο
        // από τίποτα — δεν έχουμε άλλο σταθερό σημείο εκκίνησης).
        var origin = shop ?? stops[0];
        var waypointStops = shop is not null ? stops : stops.Skip(1).ToList();

        try
        {
            var waypointsParam = waypointStops.Count > 0
                ? "&waypoints=optimize:true|" + string.Join("|", waypointStops.Select(s => Fmt(s.Lat) + "," + Fmt(s.Lon)))
                : "";
            var url = "https://maps.googleapis.com/maps/api/directions/json?mode=driving&origin="
                + Fmt(origin.Lat) + "," + Fmt(origin.Lon) + "&destination=" + Fmt(origin.Lat) + "," + Fmt(origin.Lon)
                + waypointsParam + "&key=" + Uri.EscapeDataString(key);
            var resp = await Http.GetFromJsonAsync<GoogleDirectionsResponse>(url);
            var route = resp?.Routes?.FirstOrDefault();
            if (resp?.Status == "OK" && route is not null)
            {
                var orderedWaypoints = (route.WaypointOrder ?? Enumerable.Range(0, waypointStops.Count).ToList())
                    .Where(i => i >= 0 && i < waypointStops.Count)
                    .Select(i => waypointStops[i])
                    .ToList();
                result.Stops = shop is not null
                    ? orderedWaypoints
                    : new List<RouteStop> { stops[0] }.Concat(orderedWaypoints).ToList();
                result.Geometry = DecodePolyline(route.OverviewPolyline?.Points ?? "");
                return result;
            }
        }
        catch (Exception)
        {
            // Directions API μη διαθέσιμο/απέτυχε — γύρνα τα σημεία χωρίς βελτιστοποιημένη σειρά/γραμμή.
        }

        result.Stops = stops;
        return result;
    }

    /// <summary>Αποκωδικοποιεί το «encoded polyline» format της Google (Directions API overview_polyline).</summary>
    private static List<(double Lat, double Lon)> DecodePolyline(string encoded)
    {
        var points = new List<(double, double)>();
        if (string.IsNullOrEmpty(encoded))
            return points;

        var index = 0;
        var lat = 0;
        var lng = 0;
        while (index < encoded.Length)
        {
            var result = 1;
            var shift = 0;
            int b;
            do { b = encoded[index++] - 63 - 1; result += b << shift; shift += 5; } while (b >= 0x1f);
            lat += (result & 1) != 0 ? ~(result >> 1) : result >> 1;

            result = 1;
            shift = 0;
            do { b = encoded[index++] - 63 - 1; result += b << shift; shift += 5; } while (b >= 0x1f);
            lng += (result & 1) != 0 ? ~(result >> 1) : result >> 1;

            points.Add((lat * 1e-5, lng * 1e-5));
        }
        return points;
    }
}
