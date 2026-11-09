using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Reports;

// FEEDING & NUTRITION REPORT
// Question: "Are animals being fed on schedule, are they eating, and is feed running low?"
// Uses Feeding_Log, Feeding_Schedule, Feed, Purchase_Order, Animal, Keeper.
// Every count links to the Feeding_Log rows (or purchase orders) behind it.
[Authorize(Roles = ReportAccess.Feeding)]
public class FeedingModel : RunnableReport
{
    public FeedingModel(IConfiguration config) : base(config) { }

    public override string ReportTitle => "Feeding & Nutrition";

    public override IReadOnlyList<ReportView> Views { get; } = new[]
    {
        new ReportView("weekly", "Feedings by week", "logged feedings each week and how many were completed."),
        new ReportView("enclosure", "By enclosure", "feeding completion for each enclosure, lowest first."),
        new ReportView("keeper", "By keeper", "feeding completion for each keeper's schedules, lowest first."),
        new ReportView("concerns", "Appetite concerns", "animals refusing or missing meals in the last 7 days of the range."),
        new ReportView("supply", "Feed supply", "stock on hand, days left, use and cost in the range, open orders."),
        new ReportView("logs", "Feeding logs", "", Hidden: true),
        new ReportView("orders", "Purchase orders", "", UsesDates: false, Hidden: true)
    };

    [BindProperty(SupportsGet = true)] public int? EnclosureId { get; set; }
    [BindProperty(SupportsGet = true)] public int? KeeperId { get; set; }

    private List<Option> _enclosures = new();
    private List<Option> _keepers = new();

    private DateTime RecentStart => EndDate.AddDays(-6);

    private static readonly Dictionary<string, (string Label, string Sql)> Kinds = new()
    {
        ["all"] = ("All feedings", "1=1"),
        ["completed"] = ("Completed", "fl.Status = 'Completed'"),
        ["partial"] = ("Partial", "fl.Status = 'Partial'"),
        ["refused"] = ("Refused", "fl.Status = 'Refused'"),
        ["missed"] = ("Missed", "fl.Status = 'Missed'"),
        ["problem"] = ("Refused or missed", "fl.Status IN ('Refused', 'Missed')")
    };

    protected override async Task LoadFiltersAsync(MySqlConnection conn)
    {
        _enclosures = await LoadOptionsAsync(conn, "SELECT Enclosure_ID, Enclosure_Name FROM Enclosure ORDER BY Enclosure_Name");
        _keepers = await LoadOptionsAsync(conn, @"
            SELECT k.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name)
            FROM Keeper k JOIN Employee e ON e.Employee_ID = k.Employee_ID ORDER BY e.Last_Name");
        Filters.Add(new FilterField(nameof(EnclosureId), "Enclosure", _enclosures, EnclosureId?.ToString()));
        Filters.Add(new FilterField(nameof(KeeperId), "Keeper", _keepers, KeeperId?.ToString()));
    }

    private string FilterSql()
    {
        var f = "";
        if (EnclosureId != null) f += " AND a.Enclosure_ID = @enclosure";
        if (KeeperId != null) f += " AND fs.Keeper_Employee_ID = @keeper";
        return f;
    }

    private string LogWhere => "fl.Feeding_Date >= @from AND fl.Feeding_Date < @to" + FilterSql();

    private const string LogFrom = @"
        FROM Feeding_Log fl
        JOIN Feeding_Schedule fs ON fs.Schedule_ID = fl.Schedule_ID
        JOIN Animal a ON a.Animal_ID = fs.Animal_ID";

    private const string Counts = @"
        COUNT(*), SUM(fl.Status = 'Completed'), SUM(fl.Status = 'Partial'),
        SUM(fl.Status = 'Refused'), SUM(fl.Status = 'Missed')";

    private MySqlCommand Cmd(MySqlConnection conn, string sql)
    {
        var cmd = new MySqlCommand(sql, conn);
        AddDateParams(cmd);
        cmd.Parameters.AddWithValue("@recent", RecentStart);
        if (EnclosureId != null) cmd.Parameters.AddWithValue("@enclosure", EnclosureId);
        if (KeeperId != null) cmd.Parameters.AddWithValue("@keeper", KeeperId);
        return cmd;
    }

    protected override Task<ReportTable> BuildAsync(MySqlConnection conn) => CurrentView.Key switch
    {
        "enclosure" => GroupedAsync(conn, "e", "e.Enclosure_ID", "e.Enclosure_Name", "JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID", "Enclosure"),
        "keeper" => GroupedAsync(conn, "k", "COALESCE(fs.Keeper_Employee_ID, 0)",
                                 "COALESCE(CONCAT(k.First_Name, ' ', k.Last_Name), 'Unassigned')",
                                 "LEFT JOIN Employee k ON k.Employee_ID = fs.Keeper_Employee_ID", "Keeper"),
        "concerns" => ConcernsAsync(conn),
        "supply" => SupplyAsync(conn),
        "logs" => LogsAsync(conn),
        "orders" => OrdersAsync(conn),
        _ => WeeklyAsync(conn)
    };

    // The five status columns + completion rate, each count linking to its feeding logs
    private Cell[] CountCells(System.Data.Common.DbDataReader r, int start, string key)
    {
        int logged = Int(r, start), done = Int(r, start + 1);
        return new[]
        {
            N(logged, Drill("logs", key, "all")), N(done, Drill("logs", key, "completed")),
            N(Int(r, start + 2), Drill("logs", key, "partial")), N(Int(r, start + 3), Drill("logs", key, "refused")),
            N(Int(r, start + 4), Drill("logs", key, "missed")), PctCell(Pct(done, logged))
        };
    }

    private static Col[] CountCols(string first) => new[]
    {
        new Col(first), new Col("Logged", true), new Col("Completed", true), new Col("Partial", true),
        new Col("Refused", true), new Col("Missed", true), new Col("Completion", true)
    };

    // ===== Feedings by week =====
    private async Task<ReportTable> WeeklyAsync(MySqlConnection conn)
    {
        var t = Table(CountCols("Week"));
        int[] sum = new int[5];
        await using var cmd = Cmd(conn, $@"
            SELECT YEARWEEK(fl.Feeding_Date, 1), MIN(fl.Feeding_Date), {Counts}
            {LogFrom} WHERE {LogWhere}
            GROUP BY YEARWEEK(fl.Feeding_Date, 1) ORDER BY 1");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            for (int i = 0; i < 5; i++) sum[i] += Int(r, 2 + i);
            var cells = new List<Cell> { T(WeekLabel(r.GetDateTime(1))) };
            cells.AddRange(CountCells(r, 2, "w:" + r.GetValue(0)));
            t.Rows.Add(R(cells, Pct(Int(r, 3), Int(r, 2)) < 90 ? "warn" : null));
        }
        t.Total = R(new[]
        {
            T("Total"), N(sum[0], Drill("logs", "all", "all")), N(sum[1], Drill("logs", "all", "completed")),
            N(sum[2], Drill("logs", "all", "partial")), N(sum[3], Drill("logs", "all", "refused")),
            N(sum[4], Drill("logs", "all", "missed")), PctCell(Pct(sum[1], sum[0]))
        });
        t.Note = "Yellow: under 90% completed. Click any number to see the feeding logs it counts.";
        return t;
    }

    // ===== By enclosure / by keeper =====
    private async Task<ReportTable> GroupedAsync(MySqlConnection conn, string keyType, string idSql, string nameSql, string join, string label)
    {
        var t = Table(CountCols(label));
        await using var cmd = Cmd(conn, $@"
            SELECT {idSql}, {nameSql}, {Counts}
            {LogFrom} {join}
            WHERE {LogWhere}
            GROUP BY 1, 2
            ORDER BY SUM(fl.Status = 'Completed') / COUNT(*), 2");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = Convert.ToInt32(r.GetValue(0));
            var cells = new List<Cell> { T(r.GetString(1)) };
            cells.AddRange(CountCells(r, 2, $"{keyType}:{id}"));
            var link = keyType == "e" ? $"/Animals/EnclosureDetails/{id}" : id > 0 ? $"/Staff/EmployeeDetails/{id}" : null;
            t.Rows.Add(R(cells, Pct(Int(r, 3), Int(r, 2)) < 90 ? "warn" : null, link));
        }
        t.Note = "Yellow: under 90% completed. Lowest completion is listed first.";
        return t;
    }

    // ===== Appetite concerns (last 7 days of the range) =====
    private async Task<ReportTable> ConcernsAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Animal"), new Col("Species"), new Col("Enclosure"), new Col("Health"),
                      new Col("Refused", true), new Col("Missed", true), new Col("Partial", true), new Col("Last problem"));
        await using var cmd = Cmd(conn, $@"
            SELECT a.Animal_ID, a.Animal_Name, s.Common_Name, e.Enclosure_Name, a.Health_Status,
                   SUM(fl.Status = 'Refused'), SUM(fl.Status = 'Missed'), SUM(fl.Status = 'Partial'),
                   MAX(CASE WHEN fl.Status IN ('Refused', 'Missed') THEN fl.Feeding_Date END)
            {LogFrom}
            JOIN Species s ON s.Species_ID = a.Species_ID
            JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
            WHERE fl.Feeding_Date >= @recent AND fl.Feeding_Date < @to {FilterSql()}
            GROUP BY a.Animal_ID, a.Animal_Name, s.Common_Name, e.Enclosure_Name, a.Health_Status
            HAVING SUM(fl.Status IN ('Refused', 'Missed')) >= 2
                OR SUM(fl.Status IN ('Refused', 'Missed', 'Partial')) >= 3
            ORDER BY SUM(fl.Status IN ('Refused', 'Missed')) DESC");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = r.GetInt32(0);
            var key = "a:" + id;
            int refused = Int(r, 5), missed = Int(r, 6);
            t.Rows.Add(R(new[]
            {
                T(r.GetString(1)), T(r.GetString(2)), T(r.GetString(3)), T(Str(r, 4)),
                N(refused, Drill("logs", key, "refused-recent")), N(missed, Drill("logs", key, "missed-recent")),
                N(Int(r, 7), Drill("logs", key, "partial-recent")), D(DateOrNull(r, 8))
            }, refused + missed >= 3 ? "bad" : "warn", $"/Animals/Details/{id}"));
        }
        ResultTitle = $"Appetite concerns: {RecentStart:MMM d} to {EndDate:MMM d, yyyy}";
        t.EmptyText = "No animal refused or missed meals repeatedly in the last 7 days of the range.";
        t.Note = "Listed if an animal refused or missed 2+ meals, or had 3+ problem meals, in the last 7 days of the range. Red: 3 or more refused or missed.";
        return t;
    }

    // ===== Feed supply =====
    private async Task<ReportTable> SupplyAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Feed"), new Col("Supplier"), new Col("In stock", true), new Col("Daily use", true),
                      new Col("Days left", true), new Col("Used in range", true), new Col("Cost in range", true),
                      new Col("Open orders", true), new Col("Status"));
        await using var cmd = Cmd(conn, @"
            SELECT f.Feed_ID, f.Feed_Name, sup.Supplier_Name, f.Unit, f.Quantity_In_Stock, f.Reorder_Level, f.Unit_Cost, f.Expiration_Date,
                   (SELECT COALESCE(SUM(fs.Quantity), 0) FROM Feeding_Schedule fs JOIN Animal a ON a.Animal_ID = fs.Animal_ID
                     WHERE fs.Feed_ID = f.Feed_ID AND fs.Is_Active = 1 AND a.Is_Active = 1),
                   (SELECT COALESCE(SUM(COALESCE(fl.Quantity_Given, IF(fl.Status = 'Completed', fs.Quantity, 0))), 0)
                     FROM Feeding_Log fl JOIN Feeding_Schedule fs ON fs.Schedule_ID = fl.Schedule_ID
                     WHERE fs.Feed_ID = f.Feed_ID AND fl.Feeding_Date >= @from AND fl.Feeding_Date < @to),
                   (SELECT COUNT(*) FROM Purchase_Order po WHERE po.Feed_ID = f.Feed_ID AND po.Status IN ('Draft', 'Ordered'))
            FROM Feed f
            LEFT JOIN Supplier sup ON sup.Supplier_ID = f.Supplier_ID
            ORDER BY f.Feed_Name");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = r.GetInt32(0);
            var unit = r.GetString(3);
            decimal stock = Dec(r, 4), reorder = Dec(r, 5), daily = Dec(r, 8), used = Dec(r, 9);
            decimal? unitCost = DecOrNull(r, 6);
            var expires = DateOrNull(r, 7);
            decimal? daysLeft = daily > 0 ? Math.Round(stock / daily, 1) : null;

            var problems = new List<string>();
            if (daysLeft != null && daysLeft < 7) problems.Add("Under 7 days left");
            else if (reorder > 0 && stock <= reorder) problems.Add("Below reorder level");
            if (expires != null && expires <= DateTime.Today.AddDays(14))
                problems.Add(expires < DateTime.Today ? "Expired" : "Expires within 14 days");

            t.Rows.Add(R(new[]
            {
                T(r.GetString(1)), T(Str(r, 2)),
                T($"{stock:0.##} {unit}"), T($"{daily:0.##} {unit}"), Dec1(daysLeft),
                new Cell { Text = $"{used:0.##} {unit}", Value = used, Link = used == 0 ? null : Drill("logs", "f:" + id, "all") },
                unitCost == null ? T("—") : Money(Math.Round(used * unitCost.Value, 2)),
                N(Int(r, 10), Drill("orders", "f:" + id)),
                T(problems.Count == 0 ? "OK" : string.Join("; ", problems))
            }, problems.Any(p => p.StartsWith("Under") || p == "Expired") ? "bad" : problems.Count > 0 ? "warn" : null));
        }
        t.Note = "Stock and days left are as of today. Click Used in range to see the feedings, or Open orders to see the purchase orders.";
        return t;
    }

    // ===== Source records: feeding logs =====
    private async Task<ReportTable> LogsAsync(MySqlConnection conn)
    {
        var kind = Key2 ?? "all";
        bool recent = kind.EndsWith("-recent");
        if (recent) kind = kind[..^7];
        if (!Kinds.ContainsKey(kind)) kind = "all";

        var where = (recent ? "fl.Feeding_Date >= @recent AND fl.Feeding_Date < @to" + FilterSql() : LogWhere)
                    + " AND " + Kinds[kind].Sql;
        var title = new List<string> { Kinds[kind].Label };
        var (type, text) = SplitKey(Key);
        object? k = null;
        switch (type)
        {
            case "w": where += " AND YEARWEEK(fl.Feeding_Date, 1) = @k"; k = text; break;
            case "e": where += " AND a.Enclosure_ID = @k"; k = text; title.Add(OptionName(_enclosures, IntOrNull(text)) ?? "enclosure"); break;
            case "k":
                where += text == "0" ? " AND fs.Keeper_Employee_ID IS NULL" : " AND fs.Keeper_Employee_ID = @k";
                k = text;
                title.Add(text == "0" ? "Unassigned" : OptionName(_keepers, IntOrNull(text)) ?? "keeper");
                break;
            case "a": where += " AND a.Animal_ID = @k"; k = text; break;
            case "f": where += " AND fs.Feed_ID = @k"; k = text; break;
        }

        var t = Table(new Col("Date"), new Col("Animal"), new Col("Feed"), new Col("Scheduled", true),
                      new Col("Given", true), new Col("Status"), new Col("Keeper"));
        await using var cmd = Cmd(conn, $@"
            SELECT fl.Feeding_Date, a.Animal_ID, a.Animal_Name, f.Feed_Name, fs.Quantity, fs.Unit, fl.Quantity_Given, fl.Status,
                   CONCAT(k.First_Name, ' ', k.Last_Name)
            {LogFrom}
            JOIN Feed f ON f.Feed_ID = fs.Feed_ID
            LEFT JOIN Employee k ON k.Employee_ID = fs.Keeper_Employee_ID
            WHERE {where}
            ORDER BY fl.Feeding_Date DESC, a.Animal_Name
            LIMIT 500");
        if (k != null) cmd.Parameters.AddWithValue("@k", k);
        await using var r = await cmd.ExecuteReaderAsync();
        string? first = null, feed = null;
        while (await r.ReadAsync())
        {
            first ??= r.GetString(2);
            feed ??= r.GetString(3);
            var status = r.GetString(7);
            var unit = Str(r, 5) ?? "";
            var given = DecOrNull(r, 6);
            t.Rows.Add(R(new[]
            {
                D(r.GetDateTime(0)), T(r.GetString(2), $"/Animals/Details/{r.GetInt32(1)}"), T(r.GetString(3)),
                T($"{Dec(r, 4):0.##} {unit}"), T(given == null ? "—" : $"{given:0.##} {unit}"), T(status), T(Str(r, 8) ?? "Unassigned")
            }, status is "Refused" or "Missed" ? "bad" : status == "Partial" ? "warn" : null));
        }
        if (type == "a" && first != null) title.Add(first);
        if (type == "f" && feed != null) title.Add(feed);
        if (type == "w" && t.Rows.Count > 0) title.Add(WeekLabel((DateTime)t.Rows[^1].Cells[0].Value!));
        ResultTitle = "Feeding logs: " + string.Join(" · ", title) + (recent ? $" ({RecentStart:MMM d} to {EndDate:MMM d})" : "");
        t.Note = "Red: refused or missed. Yellow: partial. Click an animal's name to open its page. Shows up to 500 logs.";
        return t;
    }

    // ===== Source records: open purchase orders for a feed =====
    private async Task<ReportTable> OrdersAsync(MySqlConnection conn)
    {
        var (_, text) = SplitKey(Key);
        var t = Table(new Col("Order #"), new Col("Feed"), new Col("Ordered"), new Col("Quantity", true),
                      new Col("Total cost", true), new Col("Expected"), new Col("Status"), new Col("Created by"));
        await using var cmd = new MySqlCommand(@"
            SELECT po.PO_ID, f.Feed_Name, po.Order_Date, po.Quantity, f.Unit, po.Total_Cost, po.Expected_Delivery, po.Status, po.Auto_Generated
            FROM Purchase_Order po JOIN Feed f ON f.Feed_ID = po.Feed_ID
            WHERE po.Feed_ID = @k AND po.Status IN ('Draft', 'Ordered')
            ORDER BY po.Order_Date DESC", conn);
        cmd.Parameters.AddWithValue("@k", IntOrNull(text) ?? 0);
        await using var r = await cmd.ExecuteReaderAsync();
        string? feed = null;
        while (await r.ReadAsync())
        {
            feed ??= r.GetString(1);
            bool auto = !r.IsDBNull(8) && Convert.ToBoolean(r.GetValue(8));
            t.Rows.Add(R(new[]
            {
                T("#" + r.GetInt32(0), "/Shop/Inventory"), T(r.GetString(1)), D(DateOrNull(r, 2)),
                T($"{Dec(r, 3):0.##} {Str(r, 4)}"), Money(Dec(r, 5)), D(DateOrNull(r, 6)), T(Str(r, 7)),
                T(auto ? "Automatic (low stock)" : "Staff")
            }));
        }
        ResultTitle = "Open purchase orders" + (feed == null ? "" : ": " + feed);
        t.EmptyText = "No open orders for this feed.";
        t.Note = "Orders are managed on the Inventory page.";
        return t;
    }
}
