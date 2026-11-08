using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Reports;

// FEEDING & NUTRITION COMPLIANCE REPORT
// Question: "Are animals being fed on schedule, who is refusing food, and what does feed cost?"
// Uses Feeding_Log, Feeding_Schedule, Feed, Animal, Enclosure, Employee, Purchase_Order.
[Authorize(Roles = ReportAccess.Feeding)]
public class FeedingModel : ReportPageModel
{
    public FeedingModel(IConfiguration config) : base(config) { }

    // ---- Filters ----
    [BindProperty(SupportsGet = true)] public int? EnclosureId { get; set; }
    [BindProperty(SupportsGet = true)] public int? KeeperId { get; set; }

    public List<Option> EnclosureOptions { get; set; } = new();
    public List<Option> KeeperOptions { get; set; } = new();

    // ---- Results ----
    public StatusCounts Totals { get; set; } = new();
    public int ExpectedFeedings { get; set; }
    public int NotLogged => Math.Max(0, ExpectedFeedings - Totals.Logged);
    public decimal FeedCostInPeriod { get; set; }

    public List<GroupRow> ByEnclosure { get; set; } = new();
    public List<GroupRow> ByKeeper { get; set; } = new();
    public List<WeekRow> Weeks { get; set; } = new();
    public List<ConcernRow> Concerns { get; set; } = new();
    public List<FeedRow> Feeds { get; set; } = new();

    public DateTime RecentStart => EndDate.AddDays(-6);

    public string FilterText
    {
        get
        {
            var parts = new List<string>();
            if (EnclosureId != null) parts.Add("Enclosure: " + EnclosureOptions.FirstOrDefault(o => o.Id == EnclosureId)?.Name);
            if (KeeperId != null) parts.Add("Keeper: " + KeeperOptions.FirstOrDefault(o => o.Id == KeeperId)?.Name);
            return string.Join(" · ", parts);
        }
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnGetExcelAsync()
    {
        await LoadAsync();
        var subtitle = $"{DateRangeText}{(FilterText == "" ? "" : " · " + FilterText)} · Generated {DateTime.Now:MMM d, yyyy h:mm tt}";
        string? Flag(object?[] row) => (decimal)(row[2] ?? 0m) < 95 ? ExcelReport.Yellow : null;

        var xl = new ExcelReport("Feeding & Nutrition Report", subtitle)
            .AddSheet("Summary", new[] { "Measure", "Value" }, new[]
            {
                new object?[] { "Scheduled feedings in period", ExpectedFeedings },
                new object?[] { "Feedings logged", Totals.Logged },
                new object?[] { "Not logged", NotLogged },
                new object?[] { "Completion rate (%)", Totals.CompletionRate },
                new object?[] { "Partial", Totals.Partial },
                new object?[] { "Refused", Totals.Refused },
                new object?[] { "Missed", Totals.Missed },
                new object?[] { "Animals with appetite concerns (last 7 days)", Concerns.Count },
                new object?[] { "Feed cost in period ($)", FeedCostInPeriod }
            })
            .AddSheet("By Enclosure", new[] { "Enclosure", "Logged", "Completion (%)", "Partial", "Refused", "Missed" },
                ByEnclosure.Select(g => new object?[] { g.Name, g.Counts.Logged, g.Counts.CompletionRate, g.Counts.Partial, g.Counts.Refused, g.Counts.Missed }), Flag)
            .AddSheet("By Keeper", new[] { "Keeper", "Logged", "Completion (%)", "Partial", "Refused", "Missed" },
                ByKeeper.Select(g => new object?[] { g.Name, g.Counts.Logged, g.Counts.CompletionRate, g.Counts.Partial, g.Counts.Refused, g.Counts.Missed }), Flag)
            .AddSheet("Weekly Trend", new[] { "Week Starting", "Logged", "Completion (%)", "Refused", "Missed" },
                Weeks.Select(w => new object?[] { w.WeekStart, w.Counts.Logged, w.Counts.CompletionRate, w.Counts.Refused, w.Counts.Missed }))
            .AddSheet("Appetite Concerns", new[] { "Animal", "Species", "Enclosure", "Health", "Refused", "Missed", "Partial", "Last Problem" },
                Concerns.Select(c => new object?[] { c.Animal, c.Species, c.Enclosure, c.HealthStatus, c.Refused, c.Missed, c.Partial, c.LastProblem }),
                _ => ExcelReport.Red)
            .AddSheet("Feed Supply", new[] { "Feed", "Supplier", "Unit", "In Stock", "Daily Use", "Days of Supply", "Reorder Level",
                                             "Unit Cost ($)", "Cost per Day ($)", "Cost in Period ($)", "Expires", "Open Orders", "Status" },
                Feeds.Select(f => new object?[] { f.Name, f.Supplier, f.Unit, f.InStock, f.DailyUse, f.DaysOfSupply, f.ReorderLevel,
                                                  f.UnitCost, f.CostPerDay, f.CostInPeriod, f.Expires, f.OpenOrders, f.StatusText }),
                row => (string?)row[12] == "OK" ? null : ExcelReport.Yellow);

        return File(xl.ToBytes(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"feeding-report-{DateTime.Today:yyyy-MM-dd}.xlsx");
    }

    private async Task LoadAsync()
    {
        ResolveDates();
        await using var conn = await OpenAsync();

        EnclosureOptions = await LoadOptionsAsync(conn, "SELECT Enclosure_ID, Enclosure_Name FROM Enclosure ORDER BY Enclosure_Name");
        KeeperOptions = await LoadOptionsAsync(conn, @"
            SELECT k.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name)
            FROM Keeper k JOIN Employee e ON e.Employee_ID = k.Employee_ID ORDER BY e.Last_Name");

        // Conditions every feeding-log query shares
        var filters = "";
        if (EnclosureId != null) filters += " AND a.Enclosure_ID = @enclosure";
        if (KeeperId != null) filters += " AND fs.Keeper_Employee_ID = @keeper";
        var where = "fl.Feeding_Date >= @from AND fl.Feeding_Date < @to" + filters;

        // Counting each outcome with SUM(condition) gives one row of totals
        const string counts = @"
            COUNT(*),
            SUM(fl.Status = 'Completed'),
            SUM(fl.Status = 'Partial'),
            SUM(fl.Status = 'Refused'),
            SUM(fl.Status = 'Missed')";

        const string from = @"
            FROM Feeding_Log fl
            JOIN Feeding_Schedule fs ON fs.Schedule_ID = fl.Schedule_ID
            JOIN Animal a ON a.Animal_ID = fs.Animal_ID";

        MySqlCommand Cmd(string sql)
        {
            var cmd = new MySqlCommand(sql, conn);
            AddDateParams(cmd);
            cmd.Parameters.AddWithValue("@recent", RecentStart);
            if (EnclosureId != null) cmd.Parameters.AddWithValue("@enclosure", EnclosureId);
            if (KeeperId != null) cmd.Parameters.AddWithValue("@keeper", KeeperId);
            return cmd;
        }

        // 1. Totals + feed cost (quantity actually given x cost per unit)
        await using (var cmd = Cmd($@"
            SELECT {counts},
                   SUM(COALESCE(fl.Quantity_Given, IF(fl.Status = 'Completed', fs.Quantity, 0)) * COALESCE(f.Unit_Cost, 0))
            {from}
            JOIN Feed f ON f.Feed_ID = fs.Feed_ID
            WHERE {where}"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                Totals = ReadCounts(r, 0);
                FeedCostInPeriod = Math.Round(Dec(r, 5), 2);
            }
        }

        // 2. How many feedings SHOULD have happened: active schedules x days in the period
        await using (var cmd = Cmd($@"
            SELECT COUNT(*)
            FROM Feeding_Schedule fs
            JOIN Animal a ON a.Animal_ID = fs.Animal_ID
            WHERE fs.Is_Active = 1 AND a.Is_Active = 1 {filters}"))
        {
            ExpectedFeedings = Convert.ToInt32(await cmd.ExecuteScalarAsync()) * DayCount;
        }

        // 3. By enclosure
        await using (var cmd = Cmd($@"
            SELECT e.Enclosure_Name, {counts}
            {from}
            JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
            WHERE {where}
            GROUP BY e.Enclosure_ID, e.Enclosure_Name"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                ByEnclosure.Add(new GroupRow { Name = r.GetString(0), Counts = ReadCounts(r, 1) });
        }
        ByEnclosure = ByEnclosure.OrderBy(g => g.Counts.CompletionRate).ToList();

        // 4. By keeper (the keeper on each feeding schedule)
        await using (var cmd = Cmd($@"
            SELECT COALESCE(CONCAT(k.First_Name, ' ', k.Last_Name), 'Unassigned'), {counts}
            {from}
            LEFT JOIN Employee k ON k.Employee_ID = fs.Keeper_Employee_ID
            WHERE {where}
            GROUP BY fs.Keeper_Employee_ID, k.First_Name, k.Last_Name"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                ByKeeper.Add(new GroupRow { Name = r.GetString(0), Counts = ReadCounts(r, 1) });
        }
        ByKeeper = ByKeeper.OrderBy(g => g.Counts.CompletionRate).ToList();

        // 5. Week-by-week trend (YEARWEEK groups dates into Monday-to-Sunday weeks)
        await using (var cmd = Cmd($@"
            SELECT MIN(fl.Feeding_Date), {counts}
            {from}
            WHERE {where}
            GROUP BY YEARWEEK(fl.Feeding_Date, 1)
            ORDER BY MIN(fl.Feeding_Date)"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                Weeks.Add(new WeekRow { WeekStart = r.GetDateTime(0), Counts = ReadCounts(r, 1) });
        }

        // 6. Appetite concerns: refusing or missing 2+ feedings in the last 7 days of the period.
        //    Food refusal is one of the earliest signs that an animal is unwell.
        await using (var cmd = Cmd($@"
            SELECT a.Animal_Name, s.Common_Name, e.Enclosure_Name, a.Health_Status,
                   SUM(fl.Status = 'Refused'), SUM(fl.Status = 'Missed'), SUM(fl.Status = 'Partial'),
                   MAX(CASE WHEN fl.Status IN ('Refused', 'Missed') THEN fl.Feeding_Date END)
            {from}
            JOIN Species s ON s.Species_ID = a.Species_ID
            JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
            WHERE fl.Feeding_Date >= @recent AND fl.Feeding_Date < @to {filters}
            GROUP BY a.Animal_ID, a.Animal_Name, s.Common_Name, e.Enclosure_Name, a.Health_Status
            HAVING SUM(fl.Status IN ('Refused', 'Missed')) >= 2
                OR SUM(fl.Status IN ('Refused', 'Missed', 'Partial')) >= 3
            ORDER BY SUM(fl.Status IN ('Refused', 'Missed')) DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                Concerns.Add(new ConcernRow
                {
                    Animal = r.GetString(0),
                    Species = r.GetString(1),
                    Enclosure = r.GetString(2),
                    HealthStatus = Str(r, 3),
                    Refused = Int(r, 4),
                    Missed = Int(r, 5),
                    Partial = Int(r, 6),
                    LastProblem = DateOrNull(r, 7)
                });
            }
        }

        // 7. Feed supply and cost (zoo-wide stock, period usage)
        await using (var cmd = Cmd(@"
            SELECT f.Feed_Name, sup.Supplier_Name, f.Unit, f.Quantity_In_Stock, f.Reorder_Level, f.Unit_Cost, f.Expiration_Date,
                   (SELECT COALESCE(SUM(fs.Quantity), 0) FROM Feeding_Schedule fs
                     JOIN Animal a ON a.Animal_ID = fs.Animal_ID
                     WHERE fs.Feed_ID = f.Feed_ID AND fs.Is_Active = 1 AND a.Is_Active = 1) AS Daily_Use,
                   (SELECT COALESCE(SUM(COALESCE(fl.Quantity_Given, IF(fl.Status = 'Completed', fs.Quantity, 0))), 0)
                     FROM Feeding_Log fl JOIN Feeding_Schedule fs ON fs.Schedule_ID = fl.Schedule_ID
                     WHERE fs.Feed_ID = f.Feed_ID AND fl.Feeding_Date >= @from AND fl.Feeding_Date < @to) AS Used,
                   (SELECT COUNT(*) FROM Purchase_Order po
                     WHERE po.Feed_ID = f.Feed_ID AND po.Status IN ('Draft', 'Ordered')) AS Open_Orders
            FROM Feed f
            LEFT JOIN Supplier sup ON sup.Supplier_ID = f.Supplier_ID
            ORDER BY f.Feed_Name"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var row = new FeedRow
                {
                    Name = r.GetString(0),
                    Supplier = Str(r, 1),
                    Unit = r.GetString(2),
                    InStock = Dec(r, 3),
                    ReorderLevel = Dec(r, 4),
                    UnitCost = DecOrNull(r, 5),
                    Expires = DateOrNull(r, 6),
                    DailyUse = Dec(r, 7),
                    Used = Dec(r, 8),
                    OpenOrders = Int(r, 9)
                };
                row.DaysOfSupply = row.DailyUse > 0 ? Math.Round(row.InStock / row.DailyUse, 1) : null;
                row.CostPerDay = row.UnitCost == null ? null : Math.Round(row.DailyUse * row.UnitCost.Value, 2);
                row.CostInPeriod = row.UnitCost == null ? null : Math.Round(row.Used * row.UnitCost.Value, 2);

                var problems = new List<string>();
                if (row.DaysOfSupply != null && row.DaysOfSupply < 7) problems.Add("Under 7 days left");
                else if (row.ReorderLevel > 0 && row.InStock <= row.ReorderLevel) problems.Add("Below reorder level");
                if (row.Expires != null && row.Expires <= DateTime.Today.AddDays(14))
                    problems.Add(row.Expires < DateTime.Today ? "Expired" : "Expires within 14 days");
                row.StatusText = problems.Count == 0 ? "OK" : string.Join("; ", problems);
                Feeds.Add(row);
            }
        }
    }

    private static StatusCounts ReadCounts(System.Data.Common.DbDataReader r, int start) => new()
    {
        Logged = Int(r, start),
        Completed = Int(r, start + 1),
        Partial = Int(r, start + 2),
        Refused = Int(r, start + 3),
        Missed = Int(r, start + 4)
    };

    public class StatusCounts
    {
        public int Logged { get; set; }
        public int Completed { get; set; }
        public int Partial { get; set; }
        public int Refused { get; set; }
        public int Missed { get; set; }
        public decimal CompletionRate => Pct(Completed, Logged);
    }

    public class GroupRow
    {
        public string Name { get; set; } = "";
        public StatusCounts Counts { get; set; } = new();
    }

    public class WeekRow
    {
        public DateTime WeekStart { get; set; }
        public StatusCounts Counts { get; set; } = new();
    }

    public class ConcernRow
    {
        public string Animal { get; set; } = "";
        public string Species { get; set; } = "";
        public string Enclosure { get; set; } = "";
        public string? HealthStatus { get; set; }
        public int Refused { get; set; }
        public int Missed { get; set; }
        public int Partial { get; set; }
        public DateTime? LastProblem { get; set; }
    }

    public class FeedRow
    {
        public string Name { get; set; } = "";
        public string? Supplier { get; set; }
        public string Unit { get; set; } = "";
        public decimal InStock { get; set; }
        public decimal ReorderLevel { get; set; }
        public decimal? UnitCost { get; set; }
        public DateTime? Expires { get; set; }
        public decimal DailyUse { get; set; }
        public decimal Used { get; set; }
        public int OpenOrders { get; set; }
        public decimal? DaysOfSupply { get; set; }
        public decimal? CostPerDay { get; set; }
        public decimal? CostInPeriod { get; set; }
        public string StatusText { get; set; } = "OK";
    }
}
