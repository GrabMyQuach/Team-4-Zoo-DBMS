using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Reports;

// HABITAT SAFETY & MAINTENANCE REPORT
// Question: "Are enclosures safe, which ones keep failing inspection, and how fast do we fix problems?"
// Uses Habitat_Inspection, Maintenance (linked by Inspection_ID), Enclosure, Employee.
[Authorize(Roles = ReportAccess.HabitatSafety)]
public class HabitatSafetyModel : ReportPageModel
{
    public HabitatSafetyModel(IConfiguration config) : base(config) { }

    public static readonly string[] IssueCategories =
    {
        "Structural", "Barrier/Fencing", "Water Quality", "Temperature/Climate",
        "Electrical", "Sanitation", "Vegetation/Hazard", "Other"
    };

    // ---- Filters ----
    [BindProperty(SupportsGet = true)] public int? EnclosureId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Category { get; set; }

    public List<Option> EnclosureOptions { get; set; } = new();

    // ---- Results ----
    public int Inspections { get; set; }
    public int Passed { get; set; }
    public int Minor { get; set; }
    public int Critical { get; set; }
    public decimal PassRate => Pct(Passed, Inspections);

    public int RepairsRequested { get; set; }
    public int RepairsCompleted { get; set; }
    public decimal? AvgDaysToFix { get; set; }
    public decimal RepairCost { get; set; }
    public int OpenUrgent => OpenWork.Count(w => w.Priority is "Emergency" or "High");

    public List<EnclosureRow> ByEnclosure { get; set; } = new();
    public List<CategoryRow> ByCategory { get; set; } = new();
    public List<FollowUpRow> FollowUps { get; set; } = new();
    public List<MonthRow> Months { get; set; } = new();
    public List<WorkRow> OpenWork { get; set; } = new();

    public string FilterText
    {
        get
        {
            var parts = new List<string>();
            if (EnclosureId != null) parts.Add("Enclosure: " + EnclosureOptions.FirstOrDefault(o => o.Id == EnclosureId)?.Name);
            if (!string.IsNullOrEmpty(Category)) parts.Add("Issue: " + Category);
            return string.Join(" · ", parts);
        }
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnGetExcelAsync()
    {
        await LoadAsync();
        var subtitle = $"{DateRangeText}{(FilterText == "" ? "" : " · " + FilterText)} · Generated {DateTime.Now:MMM d, yyyy h:mm tt}";

        var xl = new ExcelReport("Habitat Safety & Maintenance Report", subtitle)
            .AddSheet("Summary", new[] { "Measure", "Value" }, new[]
            {
                new object?[] { "Inspections", Inspections },
                new object?[] { "Pass rate (%)", PassRate },
                new object?[] { "Minor issues", Minor },
                new object?[] { "Critical failures", Critical },
                new object?[] { "Repairs requested", RepairsRequested },
                new object?[] { "Repairs completed", RepairsCompleted },
                new object?[] { "Average days to fix", AvgDaysToFix },
                new object?[] { "Repair cost ($)", RepairCost },
                new object?[] { "Open high/emergency work orders (today)", OpenUrgent }
            })
            .AddSheet("By Enclosure", new[] { "Enclosure", "Status", "Inspections", "Pass Rate (%)", "Minor", "Critical",
                                              "Last Inspected", "Days Since", "Open Repairs", "Repair Cost ($)" },
                ByEnclosure.Select(e => new object?[] { e.Name, e.Status, e.Inspections, e.PassRate, e.Minor, e.Critical,
                                                        e.LastInspected, e.DaysSince, e.OpenRepairs, e.RepairCost }),
                row => (int)(row[5] ?? 0) > 0 || (string?)row[1] != "Open" ? ExcelReport.Red
                     : row[7] == null || (int)row[7]! > 30 ? ExcelReport.Yellow : null)
            .AddSheet("Issues by Category", new[] { "Category", "Findings", "Critical", "Enclosures Affected", "Last Seen" },
                ByCategory.Select(c => new object?[] { c.Category, c.Findings, c.Critical, c.Enclosures, c.LastSeen }))
            .AddSheet("Follow-up", new[] { "Inspected", "Enclosure", "Result", "Category", "Findings", "Inspector",
                                           "Work Order", "Priority", "Repair Status", "Days to Fix / Open" },
                FollowUps.Select(f => new object?[] { f.Date, f.Enclosure, f.Result, f.Category, f.Findings, f.Inspector,
                                                      f.MaintenanceId, f.Priority, f.RepairStatus, f.Days }),
                row => (string?)row[2] == "Critical" ? ExcelReport.Red : ExcelReport.Yellow)
            .AddSheet("Monthly", new[] { "Month", "Inspections", "Minor", "Critical", "Repairs Requested", "Repairs Completed", "Repair Cost ($)" },
                Months.Select(m => new object?[] { MonthName(m.YearMonth), m.Inspections, m.Minor, m.Critical, m.Requested, m.Completed, m.Cost }))
            .AddSheet("Open Work Orders", new[] { "Work Order", "Enclosure", "Description", "Priority", "Status", "Requested", "Days Open", "Assigned To" },
                OpenWork.Select(w => new object?[] { w.Id, w.Enclosure, w.Description, w.Priority, w.Status, w.Requested, w.DaysOpen, w.AssignedTo }),
                row => (string?)row[3] is "Emergency" or "High" ? ExcelReport.Red : null);

        return File(xl.ToBytes(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"habitat-safety-report-{DateTime.Today:yyyy-MM-dd}.xlsx");
    }

    private async Task LoadAsync()
    {
        ResolveDates();
        if (!string.IsNullOrEmpty(Category) && !IssueCategories.Contains(Category)) Category = null;

        await using var conn = await OpenAsync();
        EnclosureOptions = await LoadOptionsAsync(conn, "SELECT Enclosure_ID, Enclosure_Name FROM Enclosure ORDER BY Enclosure_Name");

        // Inspection filters (alias i) and repair filters (alias m)
        var inspWhere = "i.Inspection_Date >= @from AND i.Inspection_Date < @to";
        if (EnclosureId != null) inspWhere += " AND i.Enclosure_ID = @enclosure";
        if (Category != null) inspWhere += " AND i.Issue_Category = @category";

        var repairWhere = "m.Enclosure_ID IS NOT NULL AND m.Request_Date >= @from AND m.Request_Date < @to";
        if (EnclosureId != null) repairWhere += " AND m.Enclosure_ID = @enclosure";
        if (Category != null) repairWhere += " AND EXISTS (SELECT 1 FROM Habitat_Inspection i2 WHERE i2.Inspection_ID = m.Inspection_ID AND i2.Issue_Category = @category)";

        MySqlCommand Cmd(string sql)
        {
            var cmd = new MySqlCommand(sql, conn);
            AddDateParams(cmd);
            if (EnclosureId != null) cmd.Parameters.AddWithValue("@enclosure", EnclosureId);
            if (Category != null) cmd.Parameters.AddWithValue("@category", Category);
            return cmd;
        }

        // 1. Inspection totals
        await using (var cmd = Cmd($@"
            SELECT COUNT(*), SUM(i.Result = 'Pass'), SUM(i.Result = 'Minor Issues'), SUM(i.Result = 'Critical')
            FROM Habitat_Inspection i WHERE {inspWhere}"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                Inspections = Int(r, 0); Passed = Int(r, 1); Minor = Int(r, 2); Critical = Int(r, 3);
            }
        }

        // 2. Repair totals: requested in period, how many got finished, how long it took, what it cost
        await using (var cmd = Cmd($@"
            SELECT COUNT(*),
                   SUM(m.Status = 'Completed'),
                   AVG(CASE WHEN m.Status = 'Completed' THEN DATEDIFF(m.Completion_Date, m.Request_Date) END),
                   SUM(COALESCE(m.Cost, 0))
            FROM Maintenance m WHERE {repairWhere}"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                RepairsRequested = Int(r, 0);
                RepairsCompleted = Int(r, 1);
                AvgDaysToFix = DecOrNull(r, 2) is decimal d ? Math.Round(d, 1) : null;
                RepairCost = Dec(r, 3);
            }
        }

        // 3. Enclosure scorecard. LEFT JOINs to subquery totals keep enclosures with no inspections in the list.
        var enclosureFilter = EnclosureId != null ? "WHERE e.Enclosure_ID = @enclosure" : "";
        await using (var cmd = Cmd($@"
            SELECT e.Enclosure_Name, e.Status,
                   COALESCE(ins.Total, 0), COALESCE(ins.Passed, 0), COALESCE(ins.Minor, 0), COALESCE(ins.Critical, 0),
                   (SELECT MAX(x.Inspection_Date) FROM Habitat_Inspection x WHERE x.Enclosure_ID = e.Enclosure_ID),
                   (SELECT COUNT(*) FROM Maintenance o WHERE o.Enclosure_ID = e.Enclosure_ID
                        AND COALESCE(o.Status, 'Open') NOT IN ('Completed', 'Cancelled')),
                   COALESCE(rep.Cost, 0)
            FROM Enclosure e
            LEFT JOIN (SELECT i.Enclosure_ID, COUNT(*) AS Total, SUM(i.Result = 'Pass') AS Passed,
                              SUM(i.Result = 'Minor Issues') AS Minor, SUM(i.Result = 'Critical') AS Critical
                       FROM Habitat_Inspection i WHERE {inspWhere}
                       GROUP BY i.Enclosure_ID) ins ON ins.Enclosure_ID = e.Enclosure_ID
            LEFT JOIN (SELECT m.Enclosure_ID, SUM(COALESCE(m.Cost, 0)) AS Cost
                       FROM Maintenance m WHERE {repairWhere}
                       GROUP BY m.Enclosure_ID) rep ON rep.Enclosure_ID = e.Enclosure_ID
            {enclosureFilter}"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var row = new EnclosureRow
                {
                    Name = r.GetString(0),
                    Status = r.GetString(1),
                    Inspections = Int(r, 2),
                    Passed = Int(r, 3),
                    Minor = Int(r, 4),
                    Critical = Int(r, 5),
                    LastInspected = DateOrNull(r, 6),
                    OpenRepairs = Int(r, 7),
                    RepairCost = Dec(r, 8)
                };
                row.DaysSince = row.LastInspected == null ? null : (DateTime.Today - row.LastInspected.Value.Date).Days;
                ByEnclosure.Add(row);
            }
        }
        // Worst first: closed, then most critical, then lowest pass rate
        ByEnclosure = ByEnclosure
            .OrderByDescending(e => e.Status != "Open")
            .ThenByDescending(e => e.Critical)
            .ThenBy(e => e.Inspections == 0 ? 101 : e.PassRate)
            .ThenBy(e => e.Name)
            .ToList();

        // 4. What kinds of problems keep coming up
        await using (var cmd = Cmd($@"
            SELECT i.Issue_Category, COUNT(*), SUM(i.Result = 'Critical'), COUNT(DISTINCT i.Enclosure_ID), MAX(i.Inspection_Date)
            FROM Habitat_Inspection i
            WHERE {inspWhere} AND i.Result <> 'Pass' AND i.Issue_Category <> 'None'
            GROUP BY i.Issue_Category
            ORDER BY COUNT(*) DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                ByCategory.Add(new CategoryRow
                {
                    Category = r.GetString(0),
                    Findings = Int(r, 1),
                    Critical = Int(r, 2),
                    Enclosures = Int(r, 3),
                    LastSeen = r.GetDateTime(4)
                });
            }
        }

        // 5. Follow-up: every failed inspection and the work order (if any) raised from it
        await using (var cmd = Cmd($@"
            SELECT i.Inspection_Date, e.Enclosure_Name, i.Result, i.Issue_Category, i.Findings,
                   CONCAT(emp.First_Name, ' ', emp.Last_Name),
                   m.Maintenance_ID, m.Priority, m.Status,
                   CASE WHEN m.Maintenance_ID IS NULL THEN NULL
                        WHEN m.Status = 'Completed' THEN DATEDIFF(m.Completion_Date, m.Request_Date)
                        ELSE DATEDIFF(CURDATE(), m.Request_Date) END
            FROM Habitat_Inspection i
            JOIN Enclosure e ON e.Enclosure_ID = i.Enclosure_ID
            LEFT JOIN Employee emp ON emp.Employee_ID = i.Inspector_Employee_ID
            LEFT JOIN Maintenance m ON m.Inspection_ID = i.Inspection_ID
            WHERE {inspWhere} AND i.Result <> 'Pass'
            ORDER BY FIELD(i.Result, 'Critical', 'Minor Issues'), i.Inspection_Date DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var status = Str(r, 8);
                FollowUps.Add(new FollowUpRow
                {
                    Date = r.GetDateTime(0),
                    Enclosure = r.GetString(1),
                    Result = r.GetString(2),
                    Category = r.GetString(3),
                    Findings = Str(r, 4),
                    Inspector = Str(r, 5),
                    MaintenanceId = r.IsDBNull(6) ? null : r.GetInt32(6),
                    Priority = Str(r, 7),
                    RepairStatus = r.IsDBNull(6) ? "No work order" : (status ?? "Open"),
                    Days = r.IsDBNull(9) ? null : Int(r, 9)
                });
            }
        }

        // 6. Month by month: inspections and repairs come from different tables, so run both and merge by month
        var months = new SortedDictionary<string, MonthRow>();
        MonthRow Month(string key) => months.TryGetValue(key, out var m) ? m : months[key] = new MonthRow { YearMonth = key };

        await using (var cmd = Cmd($@"
            SELECT DATE_FORMAT(i.Inspection_Date, '%Y-%m'), COUNT(*), SUM(i.Result = 'Minor Issues'), SUM(i.Result = 'Critical')
            FROM Habitat_Inspection i WHERE {inspWhere}
            GROUP BY DATE_FORMAT(i.Inspection_Date, '%Y-%m')"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var m = Month(r.GetString(0));
                m.Inspections = Int(r, 1); m.Minor = Int(r, 2); m.Critical = Int(r, 3);
            }
        }
        await using (var cmd = Cmd($@"
            SELECT DATE_FORMAT(m.Request_Date, '%Y-%m'), COUNT(*), SUM(m.Status = 'Completed'), SUM(COALESCE(m.Cost, 0))
            FROM Maintenance m WHERE {repairWhere}
            GROUP BY DATE_FORMAT(m.Request_Date, '%Y-%m')"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var m = Month(r.GetString(0));
                m.Requested = Int(r, 1); m.Completed = Int(r, 2); m.Cost = Dec(r, 3);
            }
        }
        Months = months.Values.ToList();

        // 7. Open work orders right now (not limited to the date range: open work is open work)
        var openFilter = EnclosureId != null ? "AND m.Enclosure_ID = @enclosure" : "";
        await using (var cmd = Cmd($@"
            SELECT m.Maintenance_ID, e.Enclosure_Name, m.Description, m.Priority, COALESCE(m.Status, 'Open'), m.Request_Date,
                   DATEDIFF(CURDATE(), m.Request_Date), CONCAT(emp.First_Name, ' ', emp.Last_Name)
            FROM Maintenance m
            JOIN Enclosure e ON e.Enclosure_ID = m.Enclosure_ID
            LEFT JOIN Employee emp ON emp.Employee_ID = m.Employee_ID
            WHERE COALESCE(m.Status, 'Open') NOT IN ('Completed', 'Cancelled') {openFilter}
            ORDER BY FIELD(m.Priority, 'Emergency', 'High', 'Normal', 'Low'), m.Request_Date"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                OpenWork.Add(new WorkRow
                {
                    Id = r.GetInt32(0),
                    Enclosure = r.GetString(1),
                    Description = Str(r, 2),
                    Priority = r.GetString(3),
                    Status = r.GetString(4),
                    Requested = DateOrNull(r, 5),
                    DaysOpen = r.IsDBNull(6) ? null : Int(r, 6),
                    AssignedTo = Str(r, 7)
                });
            }
        }
    }

    public class EnclosureRow
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public int Inspections { get; set; }
        public int Passed { get; set; }
        public int Minor { get; set; }
        public int Critical { get; set; }
        public decimal PassRate => Pct(Passed, Inspections);
        public DateTime? LastInspected { get; set; }
        public int? DaysSince { get; set; }
        public int OpenRepairs { get; set; }
        public decimal RepairCost { get; set; }
    }

    public class CategoryRow
    {
        public string Category { get; set; } = "";
        public int Findings { get; set; }
        public int Critical { get; set; }
        public int Enclosures { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public class FollowUpRow
    {
        public DateTime Date { get; set; }
        public string Enclosure { get; set; } = "";
        public string Result { get; set; } = "";
        public string Category { get; set; } = "";
        public string? Findings { get; set; }
        public string? Inspector { get; set; }
        public int? MaintenanceId { get; set; }
        public string? Priority { get; set; }
        public string RepairStatus { get; set; } = "";
        public int? Days { get; set; }
        // Critical problems should be fixed within a week; anything with no work order needs attention
        public bool Late => MaintenanceId == null || (Result == "Critical" && Days > 7);
    }

    public class MonthRow
    {
        public string YearMonth { get; set; } = "";
        public int Inspections { get; set; }
        public int Minor { get; set; }
        public int Critical { get; set; }
        public int Requested { get; set; }
        public int Completed { get; set; }
        public decimal Cost { get; set; }
    }

    public class WorkRow
    {
        public int Id { get; set; }
        public string Enclosure { get; set; } = "";
        public string? Description { get; set; }
        public string Priority { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime? Requested { get; set; }
        public int? DaysOpen { get; set; }
        public string? AssignedTo { get; set; }
    }
}
