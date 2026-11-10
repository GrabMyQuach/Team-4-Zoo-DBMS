using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Reports;

// HABITAT SAFETY REPORT
// Question: "Are enclosures safe, what keeps going wrong, and are problems getting fixed?"
// Uses Habitat_Inspection, Maintenance, Enclosure, Employee.
// Every count links to the inspections or work orders behind it.
[Authorize(Roles = ReportAccess.HabitatSafety)]
public class HabitatSafetyModel : RunnableReport
{
    public HabitatSafetyModel(IConfiguration config) : base(config) { }

    public override string ReportTitle => "Habitat Safety";

    public override IReadOnlyList<ReportView> Views { get; } = new[]
    {
        new ReportView("monthly", "Inspections & repairs by month", "inspection results and repair work each month."),
        new ReportView("enclosure", "Enclosure scorecard", "pass rate, open repairs and repair cost for each enclosure."),
        new ReportView("issues", "Recurring issues", "which kinds of problems inspectors keep finding."),
        new ReportView("followup", "Failed inspection follow-up", "every failed inspection and whether its repair got done."),
        new ReportView("open", "Open work orders", "repairs not finished yet, most urgent first (as of today).", UsesDates: false),
        new ReportView("insp", "Inspections", "", Hidden: true),
        new ReportView("repairs", "Work orders", "", Hidden: true)
    };

    public static readonly string[] IssueCategories =
    {
        "Structural", "Barrier/Fencing", "Water Quality", "Temperature/Climate",
        "Electrical", "Sanitation", "Vegetation/Hazard", "Other"
    };

    [BindProperty(SupportsGet = true)] public int? EnclosureId { get; set; }
    [BindProperty(SupportsGet = true)] public int? CategoryId { get; set; }
    private string? Category => CategoryId is >= 1 and <= 8 ? IssueCategories[CategoryId.Value - 1] : null;

    private List<Option> _enclosures = new();

    protected override async Task LoadFiltersAsync(MySqlConnection conn)
    {
        _enclosures = await LoadOptionsAsync(conn, "SELECT Enclosure_ID, Enclosure_Name FROM Enclosure ORDER BY Enclosure_Name");
        Filters.Add(new FilterField(nameof(EnclosureId), "Enclosure", _enclosures, EnclosureId?.ToString()));
        Filters.Add(new FilterField(nameof(CategoryId), "Issue type",
            IssueCategories.Select((c, i) => new Option(i + 1, c)).ToList(), CategoryId?.ToString()));
    }

    private string InspWhere
    {
        get
        {
            var w = "i.Inspection_Date >= @from AND i.Inspection_Date < @to";
            if (EnclosureId != null) w += " AND i.Enclosure_ID = @enclosure";
            if (Category != null) w += " AND i.Issue_Category = @category";
            return w;
        }
    }

    private string RepairWhere
    {
        get
        {
            var w = "m.Enclosure_ID IS NOT NULL AND m.Request_Date >= @from AND m.Request_Date < @to";
            if (EnclosureId != null) w += " AND m.Enclosure_ID = @enclosure";
            if (Category != null) w += " AND EXISTS (SELECT 1 FROM Habitat_Inspection i2 WHERE i2.Inspection_ID = m.Inspection_ID AND i2.Issue_Category = @category)";
            return w;
        }
    }

    private MySqlCommand Cmd(MySqlConnection conn, string sql)
    {
        var cmd = new MySqlCommand(sql, conn);
        AddDateParams(cmd);
        if (EnclosureId != null) cmd.Parameters.AddWithValue("@enclosure", EnclosureId);
        if (Category != null) cmd.Parameters.AddWithValue("@category", Category);
        return cmd;
    }

    private static readonly Dictionary<string, (string Label, string Sql)> InspKinds = new()
    {
        ["all"] = ("All inspections", "1=1"),
        ["pass"] = ("Passed", "i.Result = 'Pass'"),
        ["minor"] = ("Minor issues", "i.Result = 'Minor Issues'"),
        ["critical"] = ("Critical", "i.Result = 'Critical'"),
        ["problem"] = ("Problems found", "i.Result <> 'Pass' AND i.Issue_Category <> 'None'")
    };

    private static readonly Dictionary<string, (string Label, string Sql)> RepairKinds = new()
    {
        ["all"] = ("All work orders", "1=1"),
        ["done"] = ("Completed", "m.Status = 'Completed'"),
        ["cost"] = ("With a cost", "COALESCE(m.Cost, 0) > 0"),
        ["open"] = ("Not finished", "COALESCE(m.Status, 'Open') NOT IN ('Completed', 'Cancelled')")
    };

    // ===== Key findings =====
    // Target: at least 90% of inspections pass.
    protected override async Task AnalyzeAsync(MySqlConnection conn)
    {
        var (n, p) = await CompareAsync(() => Cmd(conn, $@"
            SELECT (SELECT COUNT(*) FROM Habitat_Inspection i WHERE {InspWhere}),
                   (SELECT SUM(i.Result = 'Pass') FROM Habitat_Inspection i WHERE {InspWhere}),
                   (SELECT SUM(i.Result = 'Critical') FROM Habitat_Inspection i WHERE {InspWhere}),
                   (SELECT SUM(COALESCE(m.Cost, 0)) FROM Maintenance m WHERE {RepairWhere})"));
        decimal passNow = Rate(n[1], n[0]), passPrev = Rate(p[1], p[0]);
        Kpis.Add(new Kpi("Inspections", n[0], p[0], HigherIsBetter: null));
        Kpis.Add(new Kpi("Pass rate", passNow, p[0] == 0 ? null : passPrev, "pct"));
        Kpis.Add(new Kpi("Critical findings", n[2], p[2], HigherIsBetter: false));
        Kpis.Add(new Kpi("Repair cost", n[3], p[3], "money", HigherIsBetter: null));

        if (n[0] > 0)
        {
            Findings.Add(new Finding(
                $"{passNow:0.#}% of inspections passed" + (p[0] > 0 ? Versus(passNow, passPrev, v => v.ToString("0.#") + "%") : "")
                + (passNow >= 90 ? ". That meets the 90% target." : ". That's below the 90% target."),
                passNow >= 90 ? "good" : "bad"));

            await using (var cmd = Cmd(conn, $@"
                SELECT i.Issue_Category, COUNT(*), COUNT(DISTINCT i.Enclosure_ID), SUM(i.Result = 'Critical')
                FROM Habitat_Inspection i WHERE {InspWhere} AND i.Result <> 'Pass' AND i.Issue_Category <> 'None'
                GROUP BY i.Issue_Category ORDER BY COUNT(*) DESC LIMIT 1"))
                foreach (var r in await RowsAsync(cmd))
                    Findings.Add(new Finding($"The most common problem was {r[0]}: found {Plural(Convert.ToInt32(r[1]), "time", "times")} in {Plural(Convert.ToInt32(r[2]), "enclosure", "enclosures")}"
                        + (Convert.ToInt32(r[3]) > 0 ? $", {Convert.ToInt32(r[3])} of them critical." : "."), "bad"));

            if (EnclosureId == null)
            {
                await using var cmd = Cmd(conn, $@"
                    SELECT e.Enclosure_Name, COUNT(*) FROM Habitat_Inspection i JOIN Enclosure e ON e.Enclosure_ID = i.Enclosure_ID
                    WHERE {InspWhere} AND i.Result <> 'Pass'
                    GROUP BY e.Enclosure_ID, e.Enclosure_Name HAVING COUNT(*) >= 2 ORDER BY COUNT(*) DESC LIMIT 1");
                foreach (var r in await RowsAsync(cmd))
                    Findings.Add(new Finding($"{r[0]} failed {Plural(Convert.ToInt32(r[1]), "inspection", "inspections")}, the most of any enclosure.", "bad"));
            }
        }
        else Findings.Add(new Finding("No inspections were recorded in this period.", "bad"));

        await using (var cmd = Cmd(conn, $@"
            SELECT COUNT(*), SUM(m.Priority = 'Emergency') FROM Maintenance m
            WHERE m.Enclosure_ID IS NOT NULL AND COALESCE(m.Status, 'Open') NOT IN ('Completed', 'Cancelled')
              AND m.Request_Date < @today - INTERVAL 14 DAY {(EnclosureId != null ? "AND m.Enclosure_ID = @enclosure" : "")}"))
        {
            var r = await FirstRowAsync(cmd);
            Findings.Add(r[0] > 0
                ? new Finding($"{Plural(r[0], "repair has", "repairs have")} been open for more than 14 days" + (r[1] > 0 ? $", including {Plural(r[1], "emergency", "emergencies")}." : ".") + " See Open work orders.", "bad")
                : new Finding("No enclosure repair has been waiting more than 14 days.", "good"));
        }

        await using (var cmd = Cmd(conn, $@"
            SELECT GROUP_CONCAT(e.Enclosure_Name ORDER BY e.Enclosure_Name SEPARATOR ', ') FROM Enclosure e
            WHERE e.Status = 'Closed' {(EnclosureId != null ? "AND e.Enclosure_ID = @enclosure" : "")}"))
            foreach (var r in await RowsAsync(cmd))
                if (r[0] != null) Findings.Add(new Finding($"Closed right now for safety: {r[0]}.", "bad"));

        await using (var cmd = Cmd(conn, $@"
            SELECT COUNT(*) FROM Enclosure e
            WHERE NOT EXISTS (SELECT 1 FROM Habitat_Inspection i WHERE i.Enclosure_ID = e.Enclosure_ID AND i.Inspection_Date >= @today - INTERVAL 30 DAY)
              {(EnclosureId != null ? "AND e.Enclosure_ID = @enclosure" : "")}"))
        {
            var stale = (await FirstRowAsync(cmd))[0];
            if (stale > 0) Findings.Add(new Finding($"{Plural(stale, "enclosure hasn't", "enclosures haven't")} been inspected in the last 30 days.", "bad"));
        }
    }

    protected override Task<ReportTable> BuildAsync(MySqlConnection conn) => CurrentView.Key switch
    {
        "enclosure" => EnclosuresAsync(conn),
        "issues" => IssuesAsync(conn),
        "followup" => FollowUpAsync(conn),
        "open" => OpenWorkAsync(conn),
        "insp" => InspectionsAsync(conn),
        "repairs" => RepairsAsync(conn),
        _ => MonthlyAsync(conn)
    };

    // ===== Inspections & repairs by month =====
    private async Task<ReportTable> MonthlyAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Month"), new Col("Inspections", true), new Col("Minor issues", true), new Col("Critical", true),
                      new Col("Repairs requested", true), new Col("Repairs completed", true), new Col("Repair cost", true));
        var months = new SortedDictionary<string, MonthAgg>();
        MonthAgg M(string ym) => months.TryGetValue(ym, out var x) ? x : months[ym] = new MonthAgg();
        await using (var cmd = Cmd(conn, $@"
            SELECT DATE_FORMAT(i.Inspection_Date, '%Y-%m'), COUNT(*), SUM(i.Result = 'Minor Issues'), SUM(i.Result = 'Critical')
            FROM Habitat_Inspection i WHERE {InspWhere} GROUP BY 1"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                var m = M(r.GetString(0));
                m.Insp = Int(r, 1); m.Minor = Int(r, 2); m.Crit = Int(r, 3);
            }
        await using (var cmd = Cmd(conn, $@"
            SELECT DATE_FORMAT(m.Request_Date, '%Y-%m'), COUNT(*), SUM(m.Status = 'Completed'), SUM(COALESCE(m.Cost, 0))
            FROM Maintenance m WHERE {RepairWhere} GROUP BY 1"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                var m = M(r.GetString(0));
                m.Req = Int(r, 1); m.Done = Int(r, 2); m.Cost = Dec(r, 3);
            }

        foreach (var (ym, m) in months)
        {
            var key = "m:" + ym;
            t.Rows.Add(R(new[]
            {
                T(MonthName(ym)), N(m.Insp, Drill("insp", key, "all")), N(m.Minor, Drill("insp", key, "minor")),
                N(m.Crit, Drill("insp", key, "critical")), N(m.Req, Drill("repairs", key, "all")),
                N(m.Done, Drill("repairs", key, "done")), Money(m.Cost, Drill("repairs", key, "cost"))
            }, m.Crit > 0 ? "bad" : null));
        }
        var all = months.Values;
        t.Total = R(new[]
        {
            T("Total"), N(all.Sum(m => m.Insp), Drill("insp", "all", "all")), N(all.Sum(m => m.Minor), Drill("insp", "all", "minor")),
            N(all.Sum(m => m.Crit), Drill("insp", "all", "critical")), N(all.Sum(m => m.Req), Drill("repairs", "all", "all")),
            N(all.Sum(m => m.Done), Drill("repairs", "all", "done")), Money(all.Sum(m => m.Cost), Drill("repairs", "all", "cost"))
        });
        t.Note = "Red: a month with a critical finding. Repairs are counted in the month they were requested.";
        return t;
    }

    // ===== Enclosure scorecard =====
    private async Task<ReportTable> EnclosuresAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Enclosure"), new Col("Status"), new Col("Inspections", true), new Col("Pass rate", true),
                      new Col("Minor", true), new Col("Critical", true), new Col("Last inspected"),
                      new Col("Open repairs", true), new Col("Repair cost", true));
        await using var cmd = Cmd(conn, $@"
            SELECT e.Enclosure_ID, e.Enclosure_Name, e.Status,
                   COALESCE(ins.Total, 0), COALESCE(ins.Passed, 0), COALESCE(ins.Minor, 0), COALESCE(ins.Critical, 0),
                   (SELECT MAX(x.Inspection_Date) FROM Habitat_Inspection x WHERE x.Enclosure_ID = e.Enclosure_ID),
                   (SELECT COUNT(*) FROM Maintenance o WHERE o.Enclosure_ID = e.Enclosure_ID
                        AND COALESCE(o.Status, 'Open') NOT IN ('Completed', 'Cancelled')),
                   COALESCE(rep.Cost, 0)
            FROM Enclosure e
            LEFT JOIN (SELECT i.Enclosure_ID, COUNT(*) AS Total, SUM(i.Result = 'Pass') AS Passed,
                              SUM(i.Result = 'Minor Issues') AS Minor, SUM(i.Result = 'Critical') AS Critical
                       FROM Habitat_Inspection i WHERE {InspWhere} GROUP BY i.Enclosure_ID) ins ON ins.Enclosure_ID = e.Enclosure_ID
            LEFT JOIN (SELECT m.Enclosure_ID, SUM(COALESCE(m.Cost, 0)) AS Cost
                       FROM Maintenance m WHERE {RepairWhere} GROUP BY m.Enclosure_ID) rep ON rep.Enclosure_ID = e.Enclosure_ID
            {(EnclosureId != null ? "WHERE e.Enclosure_ID = @enclosure" : "")}
            ORDER BY COALESCE(ins.Critical, 0) DESC, COALESCE(ins.Passed, 0) / NULLIF(ins.Total, 0), e.Enclosure_Name");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = r.GetInt32(0);
            var key = "e:" + id;
            int total = Int(r, 3), passed = Int(r, 4), critical = Int(r, 6), open = Int(r, 8);
            var last = DateOrNull(r, 7);
            bool stale = last == null || last < DateTime.Today.AddDays(-30);
            t.Rows.Add(R(new[]
            {
                T(r.GetString(1)), T(Str(r, 2)), N(total, Drill("insp", key, "all")),
                total == 0 ? T("—") : PctCell(Pct(passed, total)),
                N(Int(r, 5), Drill("insp", key, "minor")), N(critical, Drill("insp", key, "critical")),
                D(last), N(open, Drill("repairs", key, "open-ever")), Money(Dec(r, 9), Drill("repairs", key, "cost"))
            }, critical > 0 ? "bad" : open > 0 || stale ? "warn" : null, $"/Animals/EnclosureDetails/{id}"));
        }
        t.Note = "Red: had a critical finding. Yellow: has open repairs, or not inspected in 30+ days. Open repairs are as of today.";
        return t;
    }

    // ===== Recurring issues =====
    private async Task<ReportTable> IssuesAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Issue type"), new Col("Times found", true), new Col("Critical", true),
                      new Col("Enclosures affected", true), new Col("Last seen"));
        await using var cmd = Cmd(conn, $@"
            SELECT i.Issue_Category, COUNT(*), SUM(i.Result = 'Critical'), COUNT(DISTINCT i.Enclosure_ID), MAX(i.Inspection_Date)
            FROM Habitat_Inspection i
            WHERE {InspWhere} AND i.Result <> 'Pass' AND i.Issue_Category <> 'None'
            GROUP BY i.Issue_Category ORDER BY COUNT(*) DESC");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var cat = r.GetString(0);
            var idx = Array.IndexOf(IssueCategories, cat) + 1;
            var key = idx > 0 ? "c:" + idx : "cn:" + cat;
            int crit = Int(r, 2);
            t.Rows.Add(R(new[]
            {
                T(cat), N(Int(r, 1), Drill("insp", key, "problem")), N(crit, Drill("insp", key, "critical")),
                N(Int(r, 3), Drill("insp", key, "problem")), D(r.GetDateTime(4))
            }, crit > 0 ? "bad" : null));
        }
        t.EmptyText = "No problems were found in this date range.";
        t.Note = "Red: at least one critical finding of this type.";
        return t;
    }

    // ===== Failed inspection follow-up =====
    private async Task<ReportTable> FollowUpAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Date"), new Col("Enclosure"), new Col("Result"), new Col("Issue type"), new Col("Findings"),
                      new Col("Inspector"), new Col("Work order"), new Col("Repair status"), new Col("Days", true));
        await using var cmd = Cmd(conn, $@"
            SELECT i.Inspection_Date, e.Enclosure_ID, e.Enclosure_Name, i.Result, i.Issue_Category, i.Findings,
                   CONCAT(emp.First_Name, ' ', emp.Last_Name), m.Maintenance_ID, m.Priority, m.Status,
                   CASE WHEN m.Maintenance_ID IS NULL THEN NULL
                        WHEN m.Status = 'Completed' THEN DATEDIFF(m.Completion_Date, m.Request_Date)
                        ELSE DATEDIFF(@today, m.Request_Date) END
            FROM Habitat_Inspection i
            JOIN Enclosure e ON e.Enclosure_ID = i.Enclosure_ID
            LEFT JOIN Employee emp ON emp.Employee_ID = i.Inspector_Employee_ID
            LEFT JOIN Maintenance m ON m.Inspection_ID = i.Inspection_ID
            WHERE {InspWhere} AND i.Result <> 'Pass'
            ORDER BY FIELD(i.Result, 'Critical', 'Minor Issues'), i.Inspection_Date DESC");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            bool hasOrder = !r.IsDBNull(7);
            var status = hasOrder ? (Str(r, 9) ?? "Open") : "No work order";
            var result = r.GetString(3);
            t.Rows.Add(R(new[]
            {
                D(r.GetDateTime(0)), T(r.GetString(2), $"/Animals/EnclosureDetails/{r.GetInt32(1)}"), T(result),
                T(Str(r, 4)), T(Str(r, 5)), T(Str(r, 6)),
                hasOrder ? T($"#{r.GetInt32(7)} ({Str(r, 8)})", $"/Staff/MaintenanceEdit/{r.GetInt32(7)}") : T("—"),
                T(status), r.IsDBNull(10) ? T("—") : N(Int(r, 10))
            }, status == "Completed" ? null : result == "Critical" ? "bad" : "warn"));
        }
        t.EmptyText = "Every inspection in this range passed.";
        t.Note = "Red: critical and not fixed yet. Yellow: minor and not fixed yet. Days = days to fix, or days open so far.";
        return t;
    }

    // ===== Open work orders (as of today) =====
    private async Task<ReportTable> OpenWorkAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Work order"), new Col("Enclosure"), new Col("Description"), new Col("Priority"),
                      new Col("Status"), new Col("Requested"), new Col("Days open", true), new Col("Assigned to"));
        await using var cmd = Cmd(conn, $@"
            SELECT m.Maintenance_ID, e.Enclosure_ID, e.Enclosure_Name, m.Description, m.Priority, COALESCE(m.Status, 'Open'),
                   m.Request_Date, DATEDIFF(@today, m.Request_Date), CONCAT(emp.First_Name, ' ', emp.Last_Name)
            FROM Maintenance m
            JOIN Enclosure e ON e.Enclosure_ID = m.Enclosure_ID
            LEFT JOIN Employee emp ON emp.Employee_ID = m.Employee_ID
            WHERE COALESCE(m.Status, 'Open') NOT IN ('Completed', 'Cancelled')
                  {(EnclosureId != null ? "AND m.Enclosure_ID = @enclosure" : "")}
            ORDER BY FIELD(m.Priority, 'Emergency', 'High', 'Normal', 'Low'), m.Request_Date");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var priority = Str(r, 4);
            int days = Int(r, 7);
            t.Rows.Add(R(new[]
            {
                T("#" + r.GetInt32(0), $"/Staff/MaintenanceEdit/{r.GetInt32(0)}"),
                T(r.GetString(2), $"/Animals/EnclosureDetails/{r.GetInt32(1)}"), T(Str(r, 3)), T(priority), T(Str(r, 5)),
                D(DateOrNull(r, 6)), N(days), T(Str(r, 8) ?? "Unassigned")
            }, priority == "Emergency" ? "bad" : priority == "High" || days > 14 ? "warn" : null));
        }
        t.EmptyText = "No open work orders.";
        t.Note = "Red: emergency. Yellow: high priority, or open more than 14 days. Click a work order to update it.";
        return t;
    }

    // ===== Source records: inspections =====
    private async Task<ReportTable> InspectionsAsync(MySqlConnection conn)
    {
        var kind = InspKinds.ContainsKey(Key2 ?? "") ? Key2! : "all";
        var where = InspWhere + " AND " + InspKinds[kind].Sql;
        var title = new List<string> { InspKinds[kind].Label };
        var (type, text) = SplitKey(Key);
        object? k = null;
        switch (type)
        {
            case "m": where += " AND DATE_FORMAT(i.Inspection_Date, '%Y-%m') = @k"; k = text; title.Add(MonthName(text)); break;
            case "e": where += " AND i.Enclosure_ID = @k"; k = text; title.Add(OptionName(_enclosures, IntOrNull(text)) ?? "enclosure"); break;
            case "c":
                var idx = IntOrNull(text) ?? 0;
                k = idx is >= 1 and <= 8 ? IssueCategories[idx - 1] : "";
                where += " AND i.Issue_Category = @k";
                title.Add((string)k);
                break;
            case "cn": where += " AND i.Issue_Category = @k"; k = text; title.Add(text); break;
        }

        var t = Table(new Col("Date"), new Col("Enclosure"), new Col("Result"), new Col("Issue type"),
                      new Col("Findings"), new Col("Inspector"), new Col("Work order"));
        await using var cmd = Cmd(conn, $@"
            SELECT i.Inspection_Date, e.Enclosure_ID, e.Enclosure_Name, i.Result, i.Issue_Category, i.Findings,
                   CONCAT(emp.First_Name, ' ', emp.Last_Name), m.Maintenance_ID, m.Status
            FROM Habitat_Inspection i
            JOIN Enclosure e ON e.Enclosure_ID = i.Enclosure_ID
            LEFT JOIN Employee emp ON emp.Employee_ID = i.Inspector_Employee_ID
            LEFT JOIN Maintenance m ON m.Inspection_ID = i.Inspection_ID
            WHERE {where}
            ORDER BY i.Inspection_Date DESC
            LIMIT 500");
        if (k != null) cmd.Parameters.AddWithValue("@k", k);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var result = r.GetString(3);
            t.Rows.Add(R(new[]
            {
                D(r.GetDateTime(0)), T(r.GetString(2), $"/Animals/EnclosureDetails/{r.GetInt32(1)}"), T(result),
                T(Str(r, 4)), T(Str(r, 5)), T(Str(r, 6)),
                r.IsDBNull(7) ? T("—") : T($"#{r.GetInt32(7)} ({Str(r, 8) ?? "Open"})", $"/Staff/MaintenanceEdit/{r.GetInt32(7)}")
            }, result == "Critical" ? "bad" : result == "Minor Issues" ? "warn" : null));
        }
        ResultTitle = "Inspections: " + string.Join(" · ", title);
        t.Note = "Red: critical. Yellow: minor issues. Shows up to 500 inspections.";
        return t;
    }

    // ===== Source records: work orders =====
    private async Task<ReportTable> RepairsAsync(MySqlConnection conn)
    {
        var kind = Key2 ?? "all";
        bool ever = kind.EndsWith("-ever");     // open repairs: as of today, ignoring the date range
        if (ever) kind = kind[..^5];
        if (!RepairKinds.ContainsKey(kind)) kind = "all";

        var where = (ever
                ? "m.Enclosure_ID IS NOT NULL" + (EnclosureId != null ? " AND m.Enclosure_ID = @enclosure" : "")
                : RepairWhere)
            + " AND " + RepairKinds[kind].Sql;
        var title = new List<string> { RepairKinds[kind].Label };
        var (type, text) = SplitKey(Key);
        object? k = null;
        switch (type)
        {
            case "m": where += " AND DATE_FORMAT(m.Request_Date, '%Y-%m') = @k"; k = text; title.Add(MonthName(text)); break;
            case "e": where += " AND m.Enclosure_ID = @k"; k = text; title.Add(OptionName(_enclosures, IntOrNull(text)) ?? "enclosure"); break;
        }

        var t = Table(new Col("Work order"), new Col("Enclosure"), new Col("Description"), new Col("Priority"),
                      new Col("Status"), new Col("Requested"), new Col("Completed"), new Col("Cost", true));
        await using var cmd = Cmd(conn, $@"
            SELECT m.Maintenance_ID, e.Enclosure_ID, e.Enclosure_Name, m.Description, m.Priority, COALESCE(m.Status, 'Open'),
                   m.Request_Date, m.Completion_Date, m.Cost
            FROM Maintenance m
            JOIN Enclosure e ON e.Enclosure_ID = m.Enclosure_ID
            WHERE {where}
            ORDER BY m.Request_Date DESC
            LIMIT 500");
        if (k != null) cmd.Parameters.AddWithValue("@k", k);
        await using var r = await cmd.ExecuteReaderAsync();
        decimal total = 0;
        while (await r.ReadAsync())
        {
            var cost = Dec(r, 8);
            total += cost;
            var status = r.GetString(5);
            t.Rows.Add(R(new[]
            {
                T("#" + r.GetInt32(0), $"/Staff/MaintenanceEdit/{r.GetInt32(0)}"),
                T(r.GetString(2), $"/Animals/EnclosureDetails/{r.GetInt32(1)}"), T(Str(r, 3)), T(Str(r, 4)), T(status),
                D(DateOrNull(r, 6)), DateOrNull(r, 7) == null ? T("—") : D(DateOrNull(r, 7)), Money(cost)
            }, status is "Completed" or "Cancelled" ? null : Str(r, 4) == "Emergency" ? "bad" : "warn"));
        }
        if (t.Rows.Count > 0)
            t.Total = R(new[] { T("Total"), T(""), T(""), T(""), T(""), T(""), T(""), Money(total) });
        ResultTitle = "Work orders: " + string.Join(" · ", title) + (ever ? " (as of today)" : "");
        t.Note = "Yellow: not finished. Red: emergency not finished. Click a work order to open it.";
        return t;
    }

    private class MonthAgg
    {
        public int Insp, Minor, Crit, Req, Done;
        public decimal Cost;
    }
}
