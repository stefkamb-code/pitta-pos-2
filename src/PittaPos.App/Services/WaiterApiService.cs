using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PittaPos.Core.Data;
using PittaPos.Core.Models;

namespace PittaPos.App.Services;

/// <summary>
/// Ενσωματωμένος HTTP server (Kestrel) μέσα στο ίδιο exe του ταμείου, ώστε ο σερβιτόρος να βλέπει
/// τα τραπέζια + το μενού από το κινητό του και να στέλνει παραγγελίες — μόνο μέσα στο WiFi του
/// μαγαζιού (ο router δεν προωθεί αυτή τη θύρα προς το internet, δεν χρειάζεται τίποτα επιπλέον).
/// Όλη η πρόσβαση στα stores γίνεται πάνω στο Dispatcher του WPF, γιατί οι συλλογές τους (π.χ. το
/// ObservableCollection του OrderBoardService) δεν είναι ασφαλείς από άλλο thread.
/// </summary>
public static class WaiterApiService
{
    public const int Port = AppIdentity.ApiPort;

    /// <summary>Διεύθυνση για τις ρυθμίσεις του Android app (IP:θύρα στο τοπικό δίκτυο του μαγαζιού).</summary>
    public static string GetLanAddress()
    {
        try
        {
            var ip = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !System.Net.IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .FirstOrDefault();
            return ip is null ? "(δεν βρέθηκε τοπική διεύθυνση)" : $"http://{ip}:{Port}";
        }
        catch (Exception)
        {
            return "(σφάλμα εύρεσης διεύθυνσης)";
        }
    }

    public static async void Start()
    {
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://0.0.0.0:{Port}");
            builder.Logging.ClearProviders();
            var app = builder.Build();

            app.MapGet("/", () => "Πίττα του Παππού — API σερβιτόρου ενεργό.");
            app.MapGet("/api/tables", () => Results.Json(OnUi(GetTables)));
            app.MapGet("/api/tables/{table:int}/orders", (int table) => Results.Json(OnUi(() => GetTableOrders(table))));
            app.MapGet("/api/menu", () => Results.Json(OnUi(GetMenu)));
            // OnUi όπως όλα τα υπόλοιπα: διαβάζει τον κατάλογο (κατηγορίες/έξτρα) που μπορεί να τον
            // αλλάζει εκείνη τη στιγμή ο ταμίας από τη Διαχείριση — χωρίς αυτό, μια ταυτόχρονη
            // επεξεργασία μενού και ένα άνοιγμα προϊόντος από το κινητό μπορούσαν να συμπέσουν.
            app.MapGet("/api/customizer-options", () => Results.Json(OnUi(GetCustomizerOptions)));
            app.MapPost("/api/orders", async (HttpContext ctx) =>
            {
                var req = await ctx.Request.ReadFromJsonAsync<SubmitOrderRequest>();
                if (req is null)
                    return Results.BadRequest(new { error = "Άκυρο αίτημα" });
                var (status, body) = await SubmitOrderAsync(req);
                return Results.Json(body, statusCode: status);
            });
            app.MapPost("/api/tables/{table:int}/settle", async (HttpContext ctx, int table) =>
            {
                var req = await ctx.Request.ReadFromJsonAsync<SettleLineRequest>();
                if (req is null)
                    return Results.BadRequest(new { error = "Άκυρο αίτημα" });
                var (status, body) = OnUi(() => SettleLine(table, req));
                return Results.Json(body, statusCode: status);
            });
            app.MapPost("/api/tables/{table:int}/close", async (HttpContext ctx, int table) =>
            {
                var req = await ctx.Request.ReadFromJsonAsync<CloseTableRequest>();
                if (req is null)
                    return Results.BadRequest(new { error = "Άκυρο αίτημα" });
                var (status, body) = OnUi(() => CloseTable(table, req));
                return Results.Json(body, statusCode: status);
            });

            // «Πόσα άτομα;» — η ερώτηση που κάνει ο σερβιτόρος μόλις ανοίξει το τραπέζι. Ένα άτομο =
            // μία απόδειξη στην ταμειακή, οπότε αυτός ο αριθμός ΕΙΝΑΙ ο αριθμός των αποδείξεων.
            app.MapPost("/api/tables/{table:int}/persons", async (HttpContext ctx, int table) =>
            {
                var req = await ctx.Request.ReadFromJsonAsync<SetPersonsRequest>();
                if (req is null)
                    return Results.BadRequest(new { error = "Άκυρο αίτημα" });
                if (!SettingsStore.Instance.VerifyPin(req.Pin))
                    return Results.Json(new { error = "Λάθος κωδικός" }, statusCode: 401);
                OnUi(() => { TablePersonsService.Instance.SetCount(table, req.Count); return 0; });
                return Results.Json(new { persons = OnUi(() => TablePersonsService.Instance.CountFor(table)) });
            });

            // Δικλείδα: ο σερβιτόρος βγήκε στη μέση της σειράς των ατόμων, οπότε δεν ήρθε ποτέ το
            // «τελευταίο άτομο» που θα τύπωνε. Ό,τι έχει μείνει στην ουρά βγαίνει τώρα, να μη μείνει
            // η κουζίνα χωρίς δελτίο για φαγητό που έχει ήδη καταχωρηθεί.
            app.MapPost("/api/tables/{table:int}/print-pending", (int table) =>
            {
                OnUi(() => { FlushPendingPrint(table); return 0; });
                return Results.Ok();
            });

            // Δέχεται την ειδοποίηση «χτυπάει το τηλέφωνο» από το Event Notification του Grandstream
            // UCM — GET με ?caller=NUMBER (πιο εύκολο να ρυθμιστεί στο UCM) ή POST με JSON.
            app.MapGet("/api/incoming-call", (HttpContext ctx) =>
            {
                var number = ctx.Request.Query["caller"].ToString();
                if (number.Length == 0)
                    number = ctx.Request.Query["number"].ToString();
                OnUi(() => { IncomingCallService.Instance.ReportRinging(number); return 0; });
                return Results.Ok();
            });
            app.MapPost("/api/incoming-call", async (HttpContext ctx) =>
            {
                var req = await ctx.Request.ReadFromJsonAsync<IncomingCallRequest>();
                OnUi(() => { IncomingCallService.Instance.ReportRinging(req?.Caller ?? ""); return 0; });
                return Results.Ok();
            });

            MapSyncEndpoints(app);

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            // Αποτυχία εκκίνησης του server (π.χ. κατειλημμένη θύρα) δεν πρέπει να ρίξει το ταμείο —
            // αλλά χωρίς αυτό ο server, ο σερβιτόρος ΚΑΙ το δεύτερο ταμείο δεν θα συνδεθούν ποτέ,
            // χωρίς κανένα ίχνος στην οθόνη· τουλάχιστον ας μείνει καταγεγραμμένο.
            AppLog.Write("waiter-api", $"Αποτυχία εκκίνησης server στη θύρα {Port}: {ex}");
        }
    }

    /// <summary>
    /// Endpoints για το δεύτερο ταμείο (client — βλ. RemoteSync, docs/two-tills-el.md). Δουλεύουν πάνω
    /// στα ίδια stores που ήδη υπάρχουν — όταν αυτή η μηχανή είναι το κύριο ταμείο (host), τα stores
    /// λειτουργούν ακριβώς όπως πάντα (τοπικά JSON), οπότε αυτά τα endpoints απλά εκθέτουν την ήδη
    /// υπάρχουσα λογική τους μέσω HTTP.
    /// </summary>
    private static void MapSyncEndpoints(WebApplication app)
    {
        // ---- τραπέζια ----
        app.MapGet("/api/sync/table-status", () =>
            Results.Json(OnUi(() => TableStatusService.Instance.OpenSince.ToDictionary(kv => kv.Key, kv => kv.Value))));
        app.MapPost("/api/sync/table-status/open", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<TableSyncRequest>();
            if (req is not null) OnUi(() => { TableStatusService.Instance.MarkOpen(req.Table); return 0; });
            return Results.Ok();
        });
        app.MapPost("/api/sync/table-status/close", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<TableSyncRequest>();
            if (req is not null) OnUi(() => { TableStatusService.Instance.MarkClosed(req.Table); return 0; });
            return Results.Ok();
        });
        app.MapPost("/api/sync/table-status/close-all", () =>
        {
            OnUi(() => { TableStatusService.Instance.CloseAll(); return 0; });
            return Results.Ok();
        });

        // ---- αριθμός τραπεζιών ----
        app.MapGet("/api/sync/table-count", () => Results.Json(OnUi(() => SettingsStore.Instance.Settings.TableCount)));
        app.MapPost("/api/sync/table-count", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<TableCountSyncRequest>();
            if (req is not null) OnUi(() => { SettingsStore.Instance.SetTableCount(req.Count); return 0; });
            return Results.Ok();
        });

        // ---- κοινές ρυθμίσεις καταστήματος (όλα εκτός εκτυπωτή/δικτύου, βλ. SharedSettingsDto) ----
        app.MapGet("/api/sync/settings", () => Results.Json(OnUi(() => SettingsStore.Instance.BuildSharedSettingsDto())));
        app.MapPost("/api/sync/settings", async (HttpContext ctx) =>
        {
            var dto = await ctx.Request.ReadFromJsonAsync<SharedSettingsDto>();
            if (dto is not null) OnUi(() => { SettingsStore.Instance.ApplySharedSettingsDto(dto); return 0; });
            return Results.Ok();
        });

        // ---- διάταξη τραπεζιών (κάτοψη) ----
        app.MapGet("/api/sync/table-layout", () =>
            Results.Json(OnUi(() => Enumerable.Range(1, SettingsStore.Instance.Settings.TableCount)
                .ToDictionary(n => n, n => { var p = TableLayoutService.Instance.GetPosition(n); return new[] { p.X, p.Y }; }))));
        app.MapPost("/api/sync/table-layout", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<TableLayoutSyncRequest>();
            if (req is not null) OnUi(() => { TableLayoutService.Instance.SetPosition(req.Table, req.X, req.Y); return 0; });
            return Results.Ok();
        });

        // ---- εξοφλήσεις τραπεζιών ----
        app.MapGet("/api/sync/table-settlements", () =>
            Results.Json(OnUi(() => TableSettlementService.Instance.Snapshot())));
        app.MapPost("/api/sync/table-settlements/settle", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<TableSettleSyncRequest>();
            if (req is not null)
            {
                var method = req.Method == "card" ? PaymentMethod.Card : PaymentMethod.Cash;
                OnUi(() => req.Unit < 0
                    ? TableSettlementService.Instance.Settle(req.Table, req.OrderNumber, req.LineIndex, method, req.Amount)
                    : TableSettlementService.Instance.Settle(req.Table, req.OrderNumber, req.LineIndex, req.Unit, method, req.Amount));
            }
            return Results.Ok();
        });
        app.MapPost("/api/sync/table-settlements/clear-order", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<TableOrderSyncRequest>();
            if (req is not null) OnUi(() => { TableSettlementService.Instance.ClearOrder(req.Table, req.OrderNumber); return 0; });
            return Results.Ok();
        });
        app.MapPost("/api/sync/table-settlements/shift-after-removal", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<TableShiftSyncRequest>();
            if (req is not null) OnUi(() => { TableSettlementService.Instance.ShiftAfterRemoval(req.Table, req.OrderNumber, req.RemovedIndex); return 0; });
            return Results.Ok();
        });

        // ---- άτομα ανά τραπέζι (πόσες αποδείξεις και ποιος πήρε τι) ----
        // Ίδιο μοτίβο με τις εξοφλήσεις από πάνω: το δεύτερο ταμείο δεν κρατά δική του αλήθεια, στέλνει
        // τη μεταβολή εδώ και μετά ξαναδιαβάζει — αλλιώς οι δύο ταμειακές θα έδειχναν άλλα ποσά ανά άτομο.
        app.MapGet("/api/sync/table-persons", () =>
            Results.Json(OnUi(() => TablePersonsService.Instance.Snapshot())));
        app.MapPost("/api/sync/table-persons/count", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<TablePersonsCountRequest>();
            if (req is not null) OnUi(() => { TablePersonsService.Instance.SetCount(req.Table, req.Count); return 0; });
            return Results.Ok();
        });
        app.MapPost("/api/sync/table-persons/assign", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<TablePersonAssignRequest>();
            if (req is not null)
                OnUi(() => { TablePersonsService.Instance.Assign(req.Table, req.OrderNumber, req.LineIndex, req.Unit, req.Person); return 0; });
            return Results.Ok();
        });
        app.MapPost("/api/sync/table-persons/clear-order", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<TableOrderSyncRequest>();
            if (req is not null) OnUi(() => { TablePersonsService.Instance.ClearOrder(req.Table, req.OrderNumber); return 0; });
            return Results.Ok();
        });
        app.MapPost("/api/sync/table-persons/shift-after-removal", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<TableShiftSyncRequest>();
            if (req is not null) OnUi(() => { TablePersonsService.Instance.ShiftAfterRemoval(req.Table, req.OrderNumber, req.RemovedIndex); return 0; });
            return Results.Ok();
        });

        // ---- στατιστικά/ιστορικό ημέρας ----
        app.MapGet("/api/sync/orders", () => Results.Json(OnUi(() => SalesStatsService.Instance.Orders.ToList())));
        app.MapPost("/api/sync/orders", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<CompletedOrder>();
            if (req is null)
                return Results.Ok();
            var number = OnUi(() => SalesStatsService.Instance.Record(req));
            return Results.Json(number);
        });
        // ---- εκτύπωση για λογαριασμό του δεύτερου ταμείου ----
        // Ένας εκτυπωτής στο μαγαζί, δεμένος σε ΑΥΤΟΝ τον υπολογιστή. Το δεύτερο ταμείο δεν τον βλέπει
        // καθόλου, οπότε στέλνει εδώ την απόδειξη και την τυπώνει το κύριο ταμείο. Αν εδώ είναι κλειστό,
        // η απόδειξη μπαίνει στην ουρά του δεύτερου (PendingSyncService) και ξαναστέλνεται μόλις ανοίξει.
        app.MapPost("/api/sync/print/order", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<CompletedOrder>();
            if (req is null)
                return Results.Ok();
            OnUi(() => { ReceiptPrinter.PrintOrder(req); return 0; });
            return Results.Ok();
        });

        app.MapPost("/api/sync/orders/clear", () =>
        {
            OnUi(() => { SalesStatsService.Instance.Clear(); return 0; });
            return Results.Ok();
        });
        app.MapPost("/api/sync/orders/remove-line", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<OrderNumberLineRequest>();
            if (req is not null) OnUi(() => { SalesStatsService.Instance.RemoveLine(req.OrderNumber, req.LineIndex); return 0; });
            return Results.Ok();
        });
        app.MapPost("/api/sync/orders/remove", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<OrderNumberRequest>();
            if (req is not null) OnUi(() => { SalesStatsService.Instance.RemoveOrder(req.OrderNumber, req.CancelledBy); return 0; });
            return Results.Ok();
        });
        app.MapPost("/api/sync/orders/payment-method", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<OrderNumberPaymentMethodRequest>();
            if (req is not null) OnUi(() => { SalesStatsService.Instance.UpdatePaymentMethod(req.OrderNumber, req.PaymentMethod); return 0; });
            return Results.Ok();
        });
        app.MapPost("/api/sync/orders/channel", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<OrderNumberChannelRequest>();
            if (req is not null) OnUi(() => { SalesStatsService.Instance.UpdateChannel(req.OrderNumber, req.Type, req.Channel); return 0; });
            return Results.Ok();
        });

        // ---- ζωντανές παραγγελίες (Live Orders Board) ----
        app.MapGet("/api/sync/board", () => Results.Json(OnUi(() => OrderBoardService.Instance.Orders.ToList())));
        app.MapPost("/api/sync/board", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<BoardOrder>();
            if (req is not null) OnUi(() => { OrderBoardService.Instance.Add(req); return 0; });
            return Results.Ok();
        });
        app.MapPost("/api/sync/board/dispatch", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<BoardChannelRequest>();
            if (req is not null)
                OnUi(() =>
                {
                    var order = OrderBoardService.Instance.Orders.FirstOrDefault(o => o.OrderNumber == req.OrderNumber);
                    if (order is not null) OrderBoardService.Instance.Dispatch(order, req.Channel);
                    return 0;
                });
            return Results.Ok();
        });
        app.MapPost("/api/sync/board/reassign", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<BoardChannelRequest>();
            if (req is not null)
                OnUi(() =>
                {
                    var order = OrderBoardService.Instance.Orders.FirstOrDefault(o => o.OrderNumber == req.OrderNumber);
                    if (order is not null) OrderBoardService.Instance.Reassign(order, req.Channel);
                    return 0;
                });
            return Results.Ok();
        });
        app.MapPost("/api/sync/board/revert", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<OrderNumberRequest>();
            if (req is not null)
                OnUi(() =>
                {
                    var order = OrderBoardService.Instance.Orders.FirstOrDefault(o => o.OrderNumber == req.OrderNumber);
                    if (order is not null) OrderBoardService.Instance.RevertToPending(order);
                    return 0;
                });
            return Results.Ok();
        });
        app.MapPost("/api/sync/board/clear", () =>
        {
            OnUi(() => { OrderBoardService.Instance.Clear(); return 0; });
            return Results.Ok();
        });
        app.MapPost("/api/sync/board/cancel", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<OrderNumberRequest>();
            if (req is not null)
                OnUi(() =>
                {
                    var order = OrderBoardService.Instance.Orders.FirstOrDefault(o => o.OrderNumber == req.OrderNumber);
                    if (order is not null) OrderBoardService.Instance.Cancel(order);
                    return 0;
                });
            return Results.Ok();
        });

        // ---- πελάτες ----
        app.MapGet("/api/sync/customers", () => Results.Json(OnUi(() => CustomerStore.Instance.All.ToList())));
        app.MapPost("/api/sync/customers/upsert", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<CustomerUpsertRequest>();
            if (req is not null)
                OnUi(() => { CustomerStore.Instance.Upsert(req.Name, req.Phone, req.Address, req.StreetNumber,
                    req.Area, req.PostalCode, req.Floor, req.Notes); return 0; });
            return Results.Ok();
        });
        app.MapPost("/api/sync/customers/record-order", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<CustomerRecordOrderRequest>();
            if (req is not null)
                OnUi(() =>
                {
                    CustomerStore.Instance.RecordOrder(req.Name, req.Phone, req.Address, req.StreetNumber,
                        req.Area, req.PostalCode, req.Floor, req.Notes,
                        req.Total, req.Lines.Select(l => (l.Name, l.Quantity)).ToList());
                    return 0;
                });
            return Results.Ok();
        });
        app.MapPost("/api/sync/customers/memo", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<CustomerMemoRequest>();
            if (req is not null)
                OnUi(() =>
                {
                    var customer = CustomerStore.Instance.Find(req.Name, req.Phone, req.Address);
                    if (customer is not null) CustomerStore.Instance.SetMemo(customer, req.Memo);
                    return 0;
                });
            return Results.Ok();
        });
        app.MapPost("/api/sync/customers/remove-address", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<CustomerRemoveAddressRequest>();
            if (req is not null)
                OnUi(() =>
                {
                    var customer = CustomerStore.Instance.Find(req.Name, req.Phone, req.Address);
                    var other = customer?.OtherAddresses.FirstOrDefault(a =>
                        string.Equals(a.Address.Trim(), req.OtherAddress.Trim(), StringComparison.OrdinalIgnoreCase)
                        && string.Equals(a.StreetNumber.Trim(), req.OtherNumber.Trim(), StringComparison.OrdinalIgnoreCase)
                        && string.Equals(a.Area.Trim(), req.OtherArea.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (customer is not null && other is not null) CustomerStore.Instance.RemoveOtherAddress(customer, other);
                    return 0;
                });
            return Results.Ok();
        });
        app.MapPost("/api/sync/customers/remove-main-address", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<CustomerRemoveMainAddressRequest>();
            if (req is not null)
                OnUi(() =>
                {
                    var customer = CustomerStore.Instance.Find(req.Name, req.Phone, req.Address);
                    if (customer is not null) CustomerStore.Instance.RemoveMainAddress(customer);
                    return 0;
                });
            return Results.Ok();
        });
        app.MapPost("/api/sync/customers/add-address", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<CustomerAddAddressRequest>();
            if (req is not null)
                OnUi(() =>
                {
                    var customer = CustomerStore.Instance.Find(req.Name, req.Phone, req.Address);
                    if (customer is not null)
                        CustomerStore.Instance.AddOtherAddress(customer, req.NewAddress, req.NewNumber, req.NewArea, req.NewPostalCode, req.NewFloor);
                    return 0;
                });
            return Results.Ok();
        });

        // ---- μενού (πλήρες: κατηγορίες + έξτρα + χρεώσεις διπλής πίτας — όχι το DTO του /api/menu) ----
        app.MapGet("/api/sync/menu", () => Results.Json(OnUi(() => MenuStore.Instance.BuildSyncDto())));
        app.MapPost("/api/sync/menu", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<MenuSyncDto>();
            if (req is not null) OnUi(() => { MenuStore.Instance.ReplaceAll(req); return 0; });
            return Results.Ok();
        });

        // ---- ουρά εισερχόμενων κλήσεων ----
        app.MapGet("/api/sync/incoming-calls", () =>
            Results.Json(OnUi(() => IncomingCallService.Instance.Snapshot())));
        app.MapPost("/api/sync/incoming-calls/dismiss", async (HttpContext ctx) =>
        {
            var req = await ctx.Request.ReadFromJsonAsync<DismissIncomingCallRequest>();
            if (req is not null) OnUi(() => { IncomingCallService.Instance.Dismiss(req.Id); return 0; });
            return Results.Ok();
        });
    }

    /// <summary>Τρέχει τη δουλειά πάνω στο UI thread — τα Kestrel handlers τρέχουν σε thread pool threads.</summary>
    private static T OnUi<T>(Func<T> func)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return func();
        return dispatcher.Invoke(func);
    }

    private static List<TableDto> GetTables()
    {
        var orders = SalesStatsService.Instance.Orders.Where(o => o.Type == OrderType.Table).ToList();
        var openSince = TableStatusService.Instance.OpenSince;

        var list = new List<TableDto>();
        foreach (var n in Enumerable.Range(1, SettingsStore.Instance.Settings.TableCount))
        {
            var isOpen = openSince.TryGetValue(n, out var since);
            var mine = isOpen
                ? orders.Where(o => o.Who == "Τραπέζι " + n && o.PlacedAt >= since).ToList()
                : [];
            list.Add(new TableDto(n, isOpen, OutstandingTotal(n, mine), mine.Count,
                mine.Count > 0 ? mine.Max(o => o.PlacedAt).ToString("HH:mm") : null,
                TablePersonsService.Instance.CountFor(n)));
        }
        return list;
    }

    /// <summary>Άθροισμα ό,τι δεν έχει εξοφληθεί ακόμα — ανά ΤΕΜΑΧΙΟ, όχι ανά γραμμή: από 3 ίδιες
    /// πίττες μπορεί να έχει πληρωθεί μόνο η μία (βλ. TableSettlementService.SettledUnits).</summary>
    private static decimal OutstandingTotal(int table, List<CompletedOrder> orders) => orders.Sum(o =>
        o.Lines.Select((l, i) => (l, i)).Sum(x =>
        {
            var units = Math.Max(1, x.l.Quantity);
            var unpaid = units - TableSettlementService.Instance.SettledUnits(table, o.OrderNumber, x.i, units);
            // Με την έκπτωση παραγγελίας μέσα — ίδιος υπολογισμός με το ταμείο.
            return x.l.Revenue / units * unpaid * (1 - o.OrderDiscountPct / 100m);
        }));

    // Τα υλικά στέλνονται ήδη λυμένα ανά προϊόν (IngredientsFor), όχι ως «null = τα κοινά»: το κινητό
    // δεν χρειάζεται να ξέρει τον κανόνα προεπιλογής και μια μελλοντική αλλαγή του δεν θα απαιτεί νέο APK.
    private static List<MenuCategoryDto> GetMenu() =>
        MenuStore.Instance.Categories.Select(c => new MenuCategoryDto(c.Id, c.Name,
            c.Products.Select(p => new MenuProductDto(p.Id, p.Name, p.NameEn, p.Price, p.Customizable,
                MenuStore.Instance.IngredientsFor(p), ExtraNamesFor(p))).ToList(),
            MenuStore.Instance.HasBreadChoice(c.Name),
            MenuStore.Instance.FuseBreadIntoName(c.Name),
            MenuStore.Instance.SupportsDoublePita(c.Name),
            MenuStore.Instance.DoublePitaPriceFor(c.Name))).ToList();

    /// <summary>Τα έξτρα ΤΟΥ προϊόντος, με τη σειρά που τα έχει το ίδιο (Product.ExtraNames, οριζόμενη
    /// με σύρσιμο από τη Διαχείριση Καταλόγου). Το κινητό έδειχνε τον κοινό κατάλογο σε όλα: αγνοούσε
    /// και τη σειρά και τον περιορισμό, οπότε ένα προϊόν με τρία επιτρεπτά έξτρα εμφάνιζε και τα 21.
    /// Ονόματα που δεν υπάρχουν πια στον κατάλογο πετιούνται — το κινητό αντλεί τιμές από εκεί.</summary>
    private static List<string> ExtraNamesFor(Product product)
    {
        var catalog = MenuStore.Instance.Extras;
        return product.ExtraNames is { } names
            ? names.Where(n => catalog.Any(e => e.Name == n)).ToList()
            : catalog.Select(e => e.Name).ToList();
    }

    private static CustomizerOptionsDto GetCustomizerOptions() => new(
        MenuSeed.BreadOptions,
        // Ο κοινός κατάλογος υλικών (όχι πια σταθερός στον κώδικα). Μένει εδώ ως εφεδρεία για προϊόν που
        // ήρθε χωρίς δικά του υλικά· τα ανά προϊόν υλικά ταξιδεύουν πλέον στο MenuProductDto.Ingredients.
        MenuStore.Instance.Ingredients,
        MenuStore.Instance.Extras.Select(e => new ExtraOptionDto(e.Name, e.Price)).ToList(),
        MenuStore.Instance.Categories.Where(c => MenuStore.Instance.SupportsDoublePita(c.Name))
            .ToDictionary(c => c.Name, c => MenuStore.Instance.DoublePitaPriceFor(c.Name)),
        MenuSeed.BreadOptions.ToDictionary(b => b, MenuSeed.BreadAbbreviation));

    /// <summary>Ό,τι έχει ήδη παραγγελθεί στο τραπέζι από τότε που άνοιξε — για την οθόνη λεπτομερειών του κινητού.</summary>
    private static List<TableOrderDto> GetTableOrders(int table)
    {
        if (!TableStatusService.Instance.OpenSince.TryGetValue(table, out var since))
            return [];
        return SalesStatsService.Instance.Orders
            .Where(o => o.Type == OrderType.Table && o.Who == "Τραπέζι " + table && o.PlacedAt >= since)
            .OrderBy(o => o.PlacedAt)
            .Select(o => new TableOrderDto(o.OrderNumber, o.TimeLabel, o.Total,
                o.Lines.Select((l, i) => new TableOrderLineDto(i, l.Name, l.Quantity, l.Revenue, l.Details,
                    // Πληρωμένη = πληρωμένα ΟΛΑ της τα τεμάχια. Ο παλιός έλεγχος κοίταζε το κλειδί
                    // «παραγγελία:γραμμή», που δεν γράφεται πια (οι εξοφλήσεις είναι ανά τεμάχιο),
                    // οπότε στο κινητό η γραμμή έμενε για πάντα απλήρωτη.
                    TableSettlementService.Instance.SettledUnits(table, o.OrderNumber, i, Math.Max(1, l.Quantity))
                        >= Math.Max(1, l.Quantity),
                    // Το άτομο του ΠΡΩΤΟΥ τεμαχίου: μια γραμμή γεννιέται πάντα από έναν γύρο ενός
                    // ατόμου, οπότε όλα της τα τεμάχια έχουν το ίδιο (βλ. AssignPersons).
                    TablePersonsService.Instance.PersonFor(table, o.OrderNumber, i, 0))).ToList(), o.Note))
            .ToList();
    }

    /// <summary>Εξοφλεί ξεχωριστά ένα προϊόν από ανοιχτό τραπέζι (π.χ. πλήρωσε μόνο ένας από την παρέα).</summary>
    private static (int Status, object Body) SettleLine(int table, SettleLineRequest req)
    {
        if (!SettingsStore.Instance.VerifyPin(req.Pin))
            return (401, new { error = "Λάθος κωδικός" });

        var order = SalesStatsService.Instance.Orders
            .FirstOrDefault(o => o.Type == OrderType.Table && o.Who == "Τραπέζι " + table && o.OrderNumber == req.OrderNumber);
        if (order is null || req.LineIndex < 0 || req.LineIndex >= order.Lines.Count)
            return (400, new { error = "Άκυρη γραμμή" });

        // Το κινητό δεν ρωτάει τρόπο πληρωμής — θεωρείται μετρητά. Το ποσό όμως πρέπει να περάσει, αλλιώς
        // η είσπραξη δεν θα μετρούσε καθόλου στον διαχωρισμό μετρητά/κάρτα της αναφοράς ημέρας.
        // Εξοφλείται ολόκληρη η γραμμή (το κινητό δεν έχει επιλογή ανά τεμάχιο).
        var settledLine = order.Lines[req.LineIndex];
        TableSettlementService.Instance.Settle(table, req.OrderNumber, req.LineIndex,
            PaymentMethod.Cash, settledLine.Revenue);
        AutoCloseIfNothingOwed(table);
        return (200, new { ok = true });
    }

    /// <summary>
    /// Αν μηδενίστηκε το οφειλόμενο (όλα εξοφλήθηκαν), κλείνει αυτόματα το τραπέζι — αλλιώς έμενε
    /// «ανοιχτό» με €0,00 και καμία εκκρεμότητα, κάτι που μπερδεύει (φαίνεται σαν σφάλμα).
    /// </summary>
    private static void AutoCloseIfNothingOwed(int table)
    {
        if (!TableStatusService.Instance.OpenSince.TryGetValue(table, out var since))
            return;
        var orders = SalesStatsService.Instance.Orders
            .Where(o => o.Type == OrderType.Table && o.Who == "Τραπέζι " + table && o.PlacedAt >= since)
            .ToList();
        if (orders.Count > 0 && OutstandingTotal(table, orders) == 0)
            TableStatusService.Instance.MarkClosed(table);
    }

    /// <summary>Πληρωμή/κλείσιμο ολόκληρου τραπεζιού από το κινητό — ίδια ενέργεια με το «✕ ΠΛΗΡΩΜΗ ΤΡΑΠΕΖΙΟΥ» του ταμείου.</summary>
    private static (int Status, object Body) CloseTable(int table, CloseTableRequest req)
    {
        if (!SettingsStore.Instance.VerifyPin(req.Pin))
            return (401, new { error = "Λάθος κωδικός" });
        if (!TableStatusService.Instance.OpenSince.ContainsKey(table))
            return (400, new { error = "Το τραπέζι δεν είναι ανοιχτό" });

        TableStatusService.Instance.MarkClosed(table);
        return (200, new { ok = true });
    }

    /// <summary>
    /// Ασύγχρονο (όχι απλή OnUi κλήση σαν τα υπόλοιπα endpoints) ώστε στο δεύτερο ταμείο να ΠΕΡΙΜΕΝΕΙ
    /// την επιβεβαίωση του host για τον τελικό αριθμό παραγγελίας (βλ.
    /// SalesStatsService.RecordAndConfirmNumberAsync) πριν τυπώσει — αλλιώς η απόδειξη μπορεί να δείξει
    /// αριθμό διαφορετικό από αυτόν που τελικά καταγράφηκε στο Ιστορικό. Το χτίσιμο της παραγγελίας
    /// (BuildTableOrder) και η εκτύπωση γίνονται μέσω OnUi (χρειάζονται UI thread) ξεχωριστά, με το
    /// δικτυακό round-trip ΑΝΑΜΕΣΑ τους σε κανονικό await — όχι μέσα σε OnUi/Dispatcher.Invoke, για να
    /// μην μπλοκάρεται το UI thread περιμένοντας δίκτυο.
    /// </summary>
    private static async Task<(int Status, object Body)> SubmitOrderAsync(SubmitOrderRequest req)
    {
        // Κύριο ταμείο: ΟΛΑ μέσα σε μία κλήση στο UI thread — «πάρε αριθμό», καταχώρηση και εκτύπωση
        // αδιαίρετα. Αν χωριστούν, μια παραγγελία που ολοκληρώνει ο ταμίας (τρέχει κι αυτή στο UI
        // thread) μπορεί να παρεμβληθεί ανάμεσα στο «πάρε αριθμό» και στην καταχώρηση και να πάρει τον
        // ίδιο αριθμό — ακριβώς το σενάριο «ήρθε παραγγελία από το κινητό, πρόσθεσα κι εγώ κάτι στο
        // τραπέζι, έσβησε η πρώτη» (σπάνιο, γι' αυτό δεν αναπαράγεται σε κάθε δοκιμή).
        if (!RemoteSync.IsClient)
            return OnUi(() =>
            {
                var built = BuildTableOrder(req);
                if (built.Order is not { } order)
                    return (built.Status, built.Body);
                var number = SalesStatsService.Instance.Record(order);
                if (number != order.OrderNumber)
                    order = SalesStatsService.WithOrderNumber(order, number);
                // ΜΕΤΑ την οριστικοποίηση του αριθμού: τα άτομα κλειδώνονται με κλειδί
                // «παραγγελία:γραμμή:τεμάχιο», οπότε με τον προσωρινό αριθμό θα κρέμονταν στο κενό.
                AssignPersons(req.Table, order, built.Persons);
                // Τελευταία παραγγελία του γύρου (αυτή που τυπώνει): όποιος δηλώθηκε αλλά τελικά δεν
                // πήρε τίποτα φεύγει από το τραπέζι — ίδιος κανόνας με το ταμείο. Μόνο στο τέλος, γιατί
                // ενδιάμεσα τα επόμενα άτομα δεν έχουν έρθει ακόμα και θα κόβαμε το τραπέζι στη μέση.
                if (req.PrintNow)
                    TablePersonsService.Instance.ShrinkToWhoOrdered(req.Table);
                // Η ΕΚΤΥΠΩΣΗ ΔΕΝ ΚΡΑΤΑΕΙ ΤΗΝ ΑΠΑΝΤΗΣΗ. Πριν τυπωνόταν εδώ, μέσα στην κλήση, οπότε ο
                // σερβιτόρος περίμενε στο κινητό όσο δούλευε ο εκτυπωτής — δευτερόλεπτα, με το κουμπί
                // «...». Με μία παραγγελία ανά ΑΤΟΜΟ αυτό συμβαίνει 3-4 φορές σε κάθε τραπέζι και έγινε
                // αμέσως αισθητό. Η παραγγελία είναι ήδη καταχωρημένη σε αυτό το σημείο· το δελτίο
                // μπαίνει στην ουρά του UI thread και τυπώνεται αμέσως μετά, με την ίδια σειρά.
                PrintOrQueue(req.Table, order, req.PrintNow);
                return (200, (object)new { order.OrderNumber });
            });

        // Δεύτερο ταμείο: ο αριθμός αποφασίζεται στο host, οπότε το round-trip γίνεται με κανονικό
        // await ΑΝΑΜΕΣΑ στο χτίσιμο και στην εκτύπωση — έξω από το UI thread, να μη «παγώνει» η οθόνη.
        var clientBuilt = OnUi(() => BuildTableOrder(req));
        if (clientBuilt.Order is not { } clientOrder)
            return (clientBuilt.Status, clientBuilt.Body);

        var finalNumber = await SalesStatsService.Instance.RecordAndConfirmNumberAsync(clientOrder);
        if (finalNumber != clientOrder.OrderNumber)
            clientOrder = SalesStatsService.WithOrderNumber(clientOrder, finalNumber);

        OnUi(() => { AssignPersons(req.Table, clientOrder, clientBuilt.Persons); return 0; });
        if (req.PrintNow)
            OnUi(() => { TablePersonsService.Instance.ShrinkToWhoOrdered(req.Table); return 0; });
        OnUi(() => { PrintOrQueue(req.Table, clientOrder, req.PrintNow); return 0; });
        return (200, new { clientOrder.OrderNumber });
    }

    /// <summary><paramref name="req"/> → έτοιμη παραγγελία. Το <c>Persons</c> που επιστρέφεται είναι
    /// παράλληλο με τις γραμμές της παραγγελίας (ΟΧΙ με τις γραμμές του αιτήματος — κάποιες μπορεί να
    /// αγνοηθούν) και λέει σε ποιο άτομο χρεώνεται η καθεμιά.</summary>
    private static (int Status, object Body, CompletedOrder? Order, List<int?> Persons) BuildTableOrder(SubmitOrderRequest req)
    {
        if (!SettingsStore.Instance.VerifyPin(req.Pin))
            return (401, new { error = "Λάθος κωδικός" }, null, []);
        if (req.Table < 1 || req.Table > SettingsStore.Instance.Settings.TableCount)
            return (400, new { error = "Άκυρο τραπέζι" }, null, []);

        var products = MenuStore.Instance.Categories.SelectMany(c => c.Products).ToDictionary(p => p.Id);
        var categoryOf = MenuStore.Instance.Categories
            .SelectMany(c => c.Products.Select(p => (p.Id, Category: c.Name)))
            .ToDictionary(x => x.Id, x => x.Category);
        var extraPrices = MenuStore.Instance.Extras.ToDictionary(e => e.Name, e => e.Price);
        var lines = new List<SoldLine>();
        var persons = new List<int?>();
        var skipped = 0;
        decimal total = 0;
        foreach (var l in req.Lines ?? [])
        {
            // Το κινητό μπορεί να κρατάει παλιότερο αντίγραφο του μενού: αν το προϊόν διαγράφηκε στο
            // ενδιάμεσο, η γραμμή απλώς αγνοείται. Καταγράφεται, γιατί αλλιώς η παραγγελία περνάει
            // ελλιπής χωρίς να το πάρει κανείς είδηση (ο σερβιτόρος βλέπει «ΟΚ», λείπει ένα προϊόν).
            if (l.Quantity <= 0 || !products.TryGetValue(l.ProductId, out var p))
            {
                skipped++;
                continue;
            }

            var extras = (l.Extras ?? []).Where(e => e.Value > 0).ToList();
            // Ίδια λογική κατηγορίας με τον customizer του ταμείου (βλ. CustomizerViewModel.Add), ώστε
            // να μη διαφέρει ανάλογα με το από πού ήρθε η παραγγελία.
            var category = categoryOf.GetValueOrDefault(p.Id, "");
            var bread = string.IsNullOrWhiteSpace(l.Bread) ? MenuSeed.BreadOptions[0] : l.Bread;
            var doublePita = l.DoublePita && MenuStore.Instance.SupportsDoublePita(category);
            var doublePitaSurcharge = doublePita ? MenuStore.Instance.DoublePitaPriceFor(category) : 0m;
            var unitPrice = p.Price + extras.Sum(e => e.Value * extraPrices.GetValueOrDefault(e.Key)) + doublePitaSurcharge;
            var lineTotal = unitPrice * l.Quantity;
            total += lineTotal;
            var name = doublePita
                ? MenuSeed.ComposeDoublePitaName(p.Name, category, bread)
                : p.Customizable && MenuStore.Instance.HasBreadChoice(category) && MenuStore.Instance.FuseBreadIntoName(category)
                    ? MenuSeed.ComposeCustomizedName(p.Name, bread)
                    : p.Name;
            lines.Add(new SoldLine(name, l.Quantity, lineTotal, BuildDetails(p, l, extras, category, bread),
                ProductId: p.Id));
            persons.Add(l.Person);
        }
        if (skipped > 0)
            AppLog.Write("waiter-api",
                $"Τραπέζι {req.Table}: αγνοήθηκαν {skipped} γραμμές (προϊόν που δεν υπάρχει πια στο μενού " +
                $"ή μηδενική ποσότητα) — καταχωρήθηκαν {lines.Count}.");

        if (lines.Count == 0)
            return (400, new { error = "Άδεια παραγγελία" }, null, []);

        // Πρέπει να ανοίξει ΠΡΙΝ καταγραφεί η παραγγελία — αλλιώς η ώρα ανοίγματος μπορεί να βγει
        // (λόγω I/O) ελάχιστα μετά το PlacedAt της ίδιας της παραγγελίας και να μη μετρήσει στο σύνολο.
        TableStatusService.Instance.MarkOpen(req.Table);

        // Το κινητό στέλνει μία παραγγελία ανά άτομο· όταν όλες οι γραμμές είναι του ίδιου, η παραγγελία
        // ΕΙΝΑΙ αυτού του ατόμου και το κρατάμε πάνω της για το Ιστορικό (βλ. CompletedOrder.TablePerson).
        var distinctPersons = persons.Where(p => p is >= 0).Distinct().ToList();

        var order = new CompletedOrder
        {
            OrderNumber = NextOrderNumber(),
            Type = OrderType.Table,
            Who = "Τραπέζι " + req.Table,
            TablePerson = distinctPersons.Count == 1 ? distinctPersons[0] : null,
            // Σε ποιο άνοιγμα του τραπεζιού ανήκει (το MarkOpen από πάνω το έχει ήδη εξασφαλίσει) —
            // έτσι το Ιστορικό μαζεύει όλα τα άτομα της ΙΔΙΑΣ παρέας σε μία εγγραφή.
            TableOpenedAt = TableStatusService.Instance.OpenSince.TryGetValue(req.Table, out var openedAt)
                ? openedAt
                : null,
            Total = total,
            Lines = lines,
            Note = req.Note?.Trim() ?? "",
            IsEveningShift = SettingsStore.Instance.Settings.IsEveningShift,
        };
        return (200, new { }, order, persons);
    }

    /// <summary>
    /// Χρεώνει κάθε ΤΕΜΑΧΙΟ της παραγγελίας στο άτομο που το παρήγγειλε — μία απόδειξη ανά άτομο.
    /// Ανά τεμάχιο και όχι ανά γραμμή, γιατί έτσι δουλεύει και η εξόφληση (βλ. TableSettlementService):
    /// αν ο Β πάρει δύο ίδιες πίττες, είναι δύο τεμάχια στη μερίδα του Β.
    /// Γραμμή χωρίς άτομο (παλιό APK, ή τραπέζι που πληρώνει μαζί) μένει αχρέωτη και φαίνεται ως τέτοια
    /// στο ταμείο — δεν μαντεύουμε άτομο, θα έβγαζε λάθος ποσά σε απόδειξη.
    /// </summary>
    /// <summary>Καταχωρημένες παραγγελίες τραπεζιού που περιμένουν να τυπωθούν μαζί, ανά τραπέζι.
    /// Μόνο στη μνήμη: αν πέσει το ταμείο στη μέση, οι παραγγελίες είναι ήδη καταγεγραμμένες και το
    /// δελτίο ξανατυπώνεται από το Ιστορικό — δεν χάνεται πώληση, μόνο ένα χαρτί.</summary>
    private static readonly Dictionary<int, List<CompletedOrder>> PendingPrints = [];

    /// <summary>
    /// Τυπώνει, ή κρατά για αργότερα. Το τραπέζι που παραγγέλνει ανά άτομο στέλνει μία παραγγελία για
    /// τον καθένα (μία απόδειξη ανά άτομο), αλλά η κουζίνα πρέπει να πάρει <b>ΕΝΑ</b> δελτίο με όλο το
    /// τραπέζι — αλλιώς ο ψήστης παίρνει 4 χαρτιά για το ίδιο τραπέζι και τα ψήνει σε 4 δόσεις.
    /// <para>Το δελτίο βγαίνει ΑΚΡΙΒΩΣ όπως πριν: σκέτα προϊόντα, χωρίς αναφορά σε άτομα — αυτά
    /// αφορούν τις αποδείξεις της ταμειακής, όχι την κουζίνα.</para>
    /// </summary>
    private static void PrintOrQueue(int table, CompletedOrder order, bool printNow)
    {
        if (!printNow)
        {
            if (!PendingPrints.TryGetValue(table, out var waiting))
                PendingPrints[table] = waiting = [];
            waiting.Add(order);
            return;
        }

        var all = PendingPrints.Remove(table, out var pending) ? pending : [];
        all.Add(order);
        var ticket = all.Count == 1 ? order : MergeForPrinting(all);
        // Η εκτύπωση δεν κρατάει την απάντηση: ο σερβιτόρος περίμενε στο κινητό όσο δούλευε ο εκτυπωτής.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ReceiptPrinter.PrintOrder(ticket));
    }

    /// <summary>Τυπώνει ό,τι έχει μείνει στην ουρά για το τραπέζι — δικλείδα για την περίπτωση που ο
    /// σερβιτόρος βγήκε στη μέση της σειράς των ατόμων και δεν ήρθε ποτέ το «τελευταίο άτομο».</summary>
    private static void FlushPendingPrint(int table)
    {
        if (!PendingPrints.TryGetValue(table, out var waiting) || waiting.Count == 0)
            return;
        PendingPrints.Remove(table);
        var ticket = waiting.Count == 1 ? waiting[0] : MergeForPrinting(waiting);
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ReceiptPrinter.PrintOrder(ticket));
    }

    /// <summary>Ένα εικονικό «μαζεμένο» δελτίο για εκτύπωση — ΔΕΝ καταχωρείται πουθενά, οι πραγματικές
    /// παραγγελίες έχουν ήδη γραφτεί ξεχωριστά (τζίρος, ιστορικό και άτομα μένουν ανέπαφα).</summary>
    private static CompletedOrder MergeForPrinting(List<CompletedOrder> orders)
    {
        var last = orders[^1];
        return new CompletedOrder
        {
            OrderNumber = last.OrderNumber,
            Type = last.Type,
            Who = last.Who,
            Total = orders.Sum(o => o.Total),
            Lines = orders.SelectMany(o => o.Lines).ToList(),
            Note = string.Join(" · ", orders.Select(o => o.Note).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct()),
            IsEveningShift = last.IsEveningShift,
        };
    }

    private static void AssignPersons(int table, CompletedOrder order, List<int?> persons)
    {
        for (var i = 0; i < order.Lines.Count && i < persons.Count; i++)
        {
            if (persons[i] is not { } person || person < 0)
                continue;
            var units = Math.Max(1, order.Lines[i].Quantity);
            for (var u = 0; u < units; u++)
                TablePersonsService.Instance.Assign(table, order.OrderNumber, i, u, person);
        }
    }

    /// <summary>Ίδιο στυλ κειμένου με τον customizer του ταμείου — κάθε ιδιαιτερότητα σε δική της γραμμή.
    /// Το ψωμί εδώ μπαίνει μόνο αν η κατηγορία το θέλει σε ξεχωριστή γραμμή (π.χ. ΜΕΡΙΔΕΣ) — στα ΤΥΛΙΧΤΑ
    /// είναι ήδη μέσα στο όνομα (βλ. BuildTableOrder) και δεν επαναλαμβάνεται εδώ.</summary>
    private static string BuildDetails(Product p, OrderLineRequest l, List<KeyValuePair<string, int>> extras,
        string category, string bread)
    {
        if (!p.Customizable)
            return "";

        var note = l.Note?.Trim() ?? "";
        var breadLine = MenuStore.Instance.HasBreadChoice(category) && !MenuStore.Instance.FuseBreadIntoName(category) ? bread : "";

        var mods = new List<string>();
        mods.AddRange(MenuSeed.DescribeRemovedIngredients(
            l.RemovedIngredients ?? [], MenuStore.Instance.IngredientsFor(p)));
        mods.AddRange(extras.Select(e => "+ " + e.Key + (e.Value > 1 ? " ×" + e.Value : "")));

        return string.Join("\n", new[] { breadLine, note, string.Join("\n", mods) }.Where(s => s.Length > 0));
    }

    /// <summary>Μία μόνο πηγή αλήθειας πλέον (βλ. SalesStatsService.NextOrderNumber) — πριν υπολόγιζε τον
    /// ίδιο τύπο «max+1» ξεχωριστά εδώ, που μπορούσε να συγκρουστεί με το wizard του ταμείου όταν ο
    /// ταμίας κρατούσε δεσμευμένο αριθμό από νωρίτερα (βλ. σχόλιο εκεί για το πραγματικό συμβάν).</summary>
    private static int NextOrderNumber() => SalesStatsService.Instance.NextOrderNumber();
}
