using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ZooDatabase.Data;
using static ZooDatabase.Pages.Reports.LaborMath;

namespace ZooDatabase.Pages.Reports;

// STAFFING & LABOR REPORT
// Question: "Is staff time being used well, what does labor cost, and do departments have enough coverage?"
// Uses Employee_Shift, Employee, Department, Care_Task.
// Hours are PAID hours (shifts of 6+ hours have a 30-minute unpaid break). Every count links to the shifts behind it.
[Authorize(Roles = ReportAccess.Staffing)]
public class StaffingModel : RunnableReport
{
    public StaffingModel(IConfiguration config) : base(config) { }

    public override string ReportTitle => "Staffing & Labor";

    public override IReadOnlyList<ReportView> Views { get; } = new[]
    {
        new ReportView("department", "By department", "staff, paid hours, absences and labor cost for each department."),
        new ReportView("employee", "By employee", "who worked the most, who missed shifts, and overtime."),
        new ReportView("weekly", "Week by week", "paid hours, absences and labor cost each week."),
        new ReportView("overtime", "Overtime", "every employee-week over 40 paid hours."),
        new ReportView("absences", "Absence reasons", "why people missed shifts."),
        new ReportView("coverage", "Next 7 days coverage", "people scheduled per department vs. their usual daily staff (from today).", UsesDates: false),
        new ReportView("shifts", "Shifts", "", Hidden: true),
        new ReportView("tasks", "Open care tasks", "", UsesDates: false, Hidden: true)
    };

    [BindProperty(SupportsGet = true)] public int? DepartmentId { get; set; }
    [BindProperty(SupportsGet = true)] public int? EmployeeId { get; set; }
    private List<Option> _departments = new();
    private List<Option> _employees = new();

    // A shift counts toward the department it was worked in, or the person's home department
    private const string ShiftDept = "COALESCE(s.Department_ID, e.Department_ID)";
    private const string ShiftFrom = "FROM Employee_Shift s JOIN Employee e ON e.Employee_ID = s.Employee_ID";
    private const string WorkedHours = $"COALESCE(SUM(CASE WHEN s.Status = 'Worked' THEN {PaidHours} END), 0)";
    private const string WorkedCost = $"COALESCE(SUM(CASE WHEN s.Status = 'Worked' THEN {PaidHours} * {HourlyRate} END), 0)";

    private string DeptFilter => (DepartmentId != null ? $" AND {ShiftDept} = @dept" : "")
                                 + (EmployeeId != null ? " AND s.Employee_ID = @emp" : "");
    private string ShiftWhere => "s.Shift_Date >= @from AND s.Shift_Date < @to AND s.Shift_Date <= @today" + DeptFilter;

    protected override async Task LoadFiltersAsync(MySqlConnection conn)
    {
        _departments = await LoadOptionsAsync(conn, "SELECT Department_ID, Department_Name FROM Department ORDER BY Department_Name");
        Filters.Add(new FilterField(nameof(DepartmentId), "Department", _departments, DepartmentId?.ToString()));
        _employees = await LoadOptionsAsync(conn, @"
            SELECT Employee_ID, CONCAT(First_Name, ' ', Last_Name) FROM Employee ORDER BY First_Name, Last_Name");
        Filters.Add(new FilterField(nameof(EmployeeId), "Employee", _employees, EmployeeId?.ToString()));
    }

    private MySqlCommand Cmd(MySqlConnection conn, string sql)
    {
        var cmd = new MySqlCommand(sql, conn);
        AddDateParams(cmd);
        if (DepartmentId != null) cmd.Parameters.AddWithValue("@dept", DepartmentId);
        if (EmployeeId != null) cmd.Parameters.AddWithValue("@emp", EmployeeId);
        return cmd;
    }

    // ===== Key findings =====
    // Target: absences under 5% of scheduled shifts.
    protected override async Task AnalyzeAsync(MySqlConnection conn)
    {
        var (n, p) = await CompareAsync(() => Cmd(conn, $@"
            SELECT {WorkedHours}, {WorkedCost}, SUM(s.Status = 'Absent'), COUNT(*),
                   (SELECT COUNT(*) FROM (
                        SELECT s.Employee_ID FROM Employee_Shift s JOIN Employee e ON e.Employee_ID = s.Employee_ID
                        WHERE {ShiftWhere} AND s.Status = 'Worked'
                        GROUP BY s.Employee_ID, YEARWEEK(s.Shift_Date, 1) HAVING SUM({PaidHours}) > 40) ot)
            {ShiftFrom} WHERE {ShiftWhere}"));
        decimal absNow = Rate(n[2], n[3]), absPrev = Rate(p[2], p[3]);
        Kpis.Add(new Kpi("Paid hours", Math.Round(n[0]), Math.Round(p[0]), HigherIsBetter: null));
        Kpis.Add(new Kpi("Labor cost", n[1], p[1], "money", HigherIsBetter: null));
        Kpis.Add(new Kpi("Absence rate", absNow, p[3] == 0 ? null : absPrev, "pct", HigherIsBetter: false));
        Kpis.Add(new Kpi("Overtime weeks", n[4], p[4], HigherIsBetter: false));

        if (n[3] == 0)
        {
            Findings.Add(new Finding("No shifts were recorded in this period.", "bad"));
            return;
        }

        Findings.Add(new Finding(
            $"{absNow:0.#}% of shifts were missed" + (p[3] > 0 ? Versus(absNow, absPrev, v => v.ToString("0.#") + "%") : "")
            + (absNow < 5 ? ". That's within the 5% target." : ". That's above the 5% target."),
            absNow < 5 ? "good" : "bad"));

        if (DepartmentId == null && EmployeeId == null)
        {
            await using var cmd = Cmd(conn, $@"
                SELECT d.Department_Name, SUM(s.Status = 'Absent'), COUNT(*)
                {ShiftFrom} JOIN Department d ON d.Department_ID = {ShiftDept}
                WHERE {ShiftWhere}
                GROUP BY d.Department_ID, d.Department_Name HAVING COUNT(*) >= 10 AND SUM(s.Status = 'Absent') > 0
                ORDER BY SUM(s.Status = 'Absent') / COUNT(*) DESC LIMIT 1");
            foreach (var r in await RowsAsync(cmd))
                Findings.Add(new Finding($"{r[0]} had the highest absence rate: {Rate(Convert.ToDecimal(r[1]), Convert.ToDecimal(r[2])):0.#}% ({Convert.ToInt32(r[1])} of {Convert.ToInt32(r[2])} shifts).", "bad"));
        }

        await using (var cmd = Cmd(conn, $@"
            SELECT COALESCE(NULLIF(TRIM(s.Absence_Reason), ''), 'No reason given'), COUNT(*)
            {ShiftFrom} WHERE {ShiftWhere} AND s.Status = 'Absent' GROUP BY 1 ORDER BY 2 DESC LIMIT 1"))
            foreach (var r in await RowsAsync(cmd))
                Findings.Add(new Finding($"Most common reason for missing a shift: {r[0]} ({Plural(Convert.ToInt32(r[1]), "time", "times")}).", "info"));

        if (n[4] > 0)
            Findings.Add(new Finding($"{Plural(n[4], "employee-week went", "employee-weeks went")} over 40 paid hours. See Overtime.", "bad"));

        await using (var cmd = Cmd(conn, @"
            SELECT COUNT(*) FROM Care_Task WHERE Status IN ('Open', 'In Progress') AND Due_At < @now"))
        {
            var late = (await FirstRowAsync(cmd))[0];
            Findings.Add(late > 0
                ? new Finding($"{Plural(late, "care task is", "care tasks are")} past due right now, a sign of too few people on shift.", "bad")
                : new Finding("No care tasks are past due right now.", "good"));
        }
    }

    protected override Task<ReportTable> BuildAsync(MySqlConnection conn) => CurrentView.Key switch
    {
        "employee" => EmployeesAsync(conn),
        "weekly" => WeeklyAsync(conn),
        "overtime" => OvertimeAsync(conn),
        "absences" => AbsencesAsync(conn),
        "coverage" => CoverageAsync(conn),
        "shifts" => ShiftsAsync(conn),
        "tasks" => TasksAsync(conn),
        _ => DepartmentsAsync(conn)
    };

    // ===== By department =====
    private async Task<ReportTable> DepartmentsAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Department"), new Col("Staff", true), new Col("Shifts", true), new Col("Paid hours", true),
                      new Col("Absences", true), new Col("Absence rate", true), new Col("Labor cost", true), new Col("Open tasks", true));
        int sShifts = 0, sAbs = 0; decimal sHours = 0, sCost = 0;
        await using var cmd = Cmd(conn, $@"
            SELECT d.Department_ID, d.Department_Name,
                   COUNT(DISTINCT CASE WHEN s.Status = 'Worked' THEN s.Employee_ID END),
                   SUM(s.Status = 'Worked'), {WorkedHours}, SUM(s.Status = 'Absent'), {WorkedCost},
                   (SELECT COUNT(*) FROM Care_Task ct JOIN Employee te ON te.Employee_ID = ct.Assigned_Employee_ID
                     WHERE te.Department_ID = d.Department_ID AND ct.Status IN ('Open', 'In Progress'))
            {ShiftFrom}
            JOIN Department d ON d.Department_ID = {ShiftDept}
            WHERE {ShiftWhere}
            GROUP BY d.Department_ID, d.Department_Name
            ORDER BY 5 DESC");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = r.GetInt32(0);
            var key = "d:" + id;
            int shifts = Int(r, 3), abs = Int(r, 5);
            decimal hours = Math.Round(Dec(r, 4), 1), cost = Math.Round(Dec(r, 6), 2);
            sShifts += shifts; sAbs += abs; sHours += hours; sCost += cost;
            var rate = Pct(abs, shifts + abs);
            t.Rows.Add(R(new[]
            {
                T(r.GetString(1)), N(Int(r, 2), Drill("shifts", key, "worked")), N(shifts, Drill("shifts", key, "worked")),
                Dec1(hours), N(abs, Drill("shifts", key, "absent")), PctCell(rate),
                Money(cost, Drill("shifts", key, "worked")), N(Int(r, 7), Drill("tasks", key))
            }, rate >= 10 ? "warn" : null, $"/Staff/DepartmentDetails/{id}"));
        }
        t.Total = R(new[]
        {
            T("Total"), T(""), N(sShifts, Drill("shifts", "all", "worked")), Dec1(sHours),
            N(sAbs, Drill("shifts", "all", "absent")), PctCell(Pct(sAbs, sShifts + sAbs)), Money(sCost, Drill("shifts", "all", "worked")), T("")
        });
        t.Note = "Yellow: absence rate of 10% or more. Hours are paid hours (30-minute unpaid break on shifts of 6+ hours). Open tasks are as of today.";
        return t;
    }

    // ===== By employee =====
    private async Task<ReportTable> EmployeesAsync(MySqlConnection conn)
    {
        var overtime = await OvertimeWeeksAsync(conn);
        var t = Table(new Col("Employee"), new Col("Department"), new Col("Job"), new Col("Shifts", true), new Col("Paid hours", true),
                      new Col("Absences", true), new Col("Overtime weeks", true), new Col("Labor cost", true));
        await using var cmd = Cmd(conn, $@"
            SELECT e.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name), d.Department_Name, e.Job_Position,
                   SUM(s.Status = 'Worked'), {WorkedHours}, SUM(s.Status = 'Absent'), {WorkedCost}
            {ShiftFrom}
            LEFT JOIN Department d ON d.Department_ID = e.Department_ID
            WHERE {ShiftWhere}
            GROUP BY e.Employee_ID, e.First_Name, e.Last_Name, d.Department_Name, e.Job_Position
            ORDER BY 6 DESC");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = r.GetInt32(0);
            var key = "e:" + id;
            int abs = Int(r, 6), ot = overtime.GetValueOrDefault(id);
            t.Rows.Add(R(new[]
            {
                T(r.GetString(1)), T(Str(r, 2)), T(Str(r, 3)), N(Int(r, 4), Drill("shifts", key, "worked")),
                Dec1(Math.Round(Dec(r, 5), 1)), N(abs, Drill("shifts", key, "absent")), N(ot),
                Money(Math.Round(Dec(r, 7), 2), Drill("shifts", key, "worked"))
            }, abs >= 3 || ot > 0 ? "warn" : null, $"/Staff/EmployeeDetails/{id}"));
        }
        t.Note = "Yellow: 3+ absences, or worked overtime. Click a name to open the employee.";
        return t;
    }

    private async Task<Dictionary<int, int>> OvertimeWeeksAsync(MySqlConnection conn)
    {
        var map = new Dictionary<int, int>();
        await using var cmd = Cmd(conn, $@"
            SELECT s.Employee_ID, COUNT(*) FROM (
                SELECT s.Employee_ID, YEARWEEK(s.Shift_Date, 1) AS wk
                {ShiftFrom}
                WHERE {ShiftWhere} AND s.Status = 'Worked'
                GROUP BY s.Employee_ID, wk
                HAVING SUM({PaidHours}) > 40) s
            GROUP BY s.Employee_ID");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) map[r.GetInt32(0)] = Int(r, 1);
        return map;
    }

    // ===== Week by week =====
    private async Task<ReportTable> WeeklyAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Week"), new Col("Shifts", true), new Col("Paid hours", true), new Col("Absences", true),
                      new Col("Absence rate", true), new Col("Labor cost", true));
        await using var cmd = Cmd(conn, $@"
            SELECT YEARWEEK(s.Shift_Date, 1), MIN(s.Shift_Date), SUM(s.Status = 'Worked'), {WorkedHours}, SUM(s.Status = 'Absent'), {WorkedCost}
            {ShiftFrom}
            WHERE {ShiftWhere}
            GROUP BY YEARWEEK(s.Shift_Date, 1)
            ORDER BY 1");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var key = "w:" + r.GetValue(0);
            int shifts = Int(r, 2), abs = Int(r, 4);
            var rate = Pct(abs, shifts + abs);
            t.Rows.Add(R(new[]
            {
                T(WeekLabel(r.GetDateTime(1))), N(shifts, Drill("shifts", key, "worked")), Dec1(Math.Round(Dec(r, 3), 1)),
                N(abs, Drill("shifts", key, "absent")), PctCell(rate), Money(Math.Round(Dec(r, 5), 2), Drill("shifts", key, "worked"))
            }, rate >= 10 ? "warn" : null));
        }
        t.Note = "Yellow: absence rate of 10% or more.";
        return t;
    }

    // ===== Overtime =====
    private async Task<ReportTable> OvertimeAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Employee"), new Col("Week"), new Col("Shifts", true), new Col("Paid hours", true), new Col("Overtime hours", true));
        await using var cmd = Cmd(conn, $@"
            SELECT e.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name), YEARWEEK(s.Shift_Date, 1), MIN(s.Shift_Date),
                   COUNT(*), SUM({PaidHours})
            {ShiftFrom}
            WHERE {ShiftWhere} AND s.Status = 'Worked'
            GROUP BY e.Employee_ID, e.First_Name, e.Last_Name, YEARWEEK(s.Shift_Date, 1)
            HAVING SUM({PaidHours}) > 40
            ORDER BY 6 DESC");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = r.GetInt32(0);
            var hours = Math.Round(Dec(r, 5), 1);
            t.Rows.Add(R(new[]
            {
                T(r.GetString(1)), T(WeekLabel(r.GetDateTime(3))),
                N(Int(r, 4), Drill("shifts", $"ew:{id}:{r.GetValue(2)}", "worked")), Dec1(hours), Dec1(hours - 40)
            }, hours - 40 >= 8 ? "bad" : "warn", $"/Staff/EmployeeDetails/{id}"));
        }
        t.EmptyText = "Nobody went over 40 paid hours in a week.";
        t.Note = "Red: 8+ overtime hours in one week. Click Shifts to see that week's shifts.";
        return t;
    }

    // ===== Absence reasons =====
    private async Task<ReportTable> AbsencesAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Reason"), new Col("Absences", true), new Col("People", true), new Col("Share", true));
        var rows = new List<(string Reason, int Count, int People)>();
        await using (var cmd = Cmd(conn, $@"
            SELECT COALESCE(NULLIF(TRIM(s.Absence_Reason), ''), 'No reason given'), COUNT(*), COUNT(DISTINCT s.Employee_ID)
            {ShiftFrom}
            WHERE {ShiftWhere} AND s.Status = 'Absent'
            GROUP BY 1 ORDER BY 2 DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) rows.Add((r.GetString(0), Int(r, 1), Int(r, 2)));

        int total = rows.Sum(x => x.Count);
        foreach (var x in rows)
            t.Rows.Add(R(new[] { T(x.Reason), N(x.Count, Drill("shifts", "r:" + x.Reason, "absent")), N(x.People), PctCell(Pct(x.Count, total)) }));
        if (rows.Count > 0) t.Total = R(new[] { T("Total"), N(total, Drill("shifts", "all", "absent")), T(""), PctCell(100) });
        t.EmptyText = "No absences in this date range.";
        return t;
    }

    // ===== Next 7 days coverage =====
    private async Task<ReportTable> CoverageAsync(MySqlConnection conn)
    {
        var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(i)).ToList();
        var cols = new List<Col> { new("Department"), new("Usual per day", true) };
        cols.AddRange(days.Select(d => new Col(d.ToString("ddd M/d"), true)));
        var t = Table(cols.ToArray());

        var depts = new Dictionary<int, string>();
        var usual = new Dictionary<int, decimal>();
        await using (var cmd = Cmd(conn, $@"
            SELECT d.Department_ID, d.Department_Name, COUNT(*) / COUNT(DISTINCT s.Shift_Date)
            {ShiftFrom}
            JOIN Department d ON d.Department_ID = {ShiftDept}
            WHERE s.Status = 'Worked' AND s.Shift_Date BETWEEN @today - INTERVAL 28 DAY AND @today - INTERVAL 1 DAY {DeptFilter}
            GROUP BY d.Department_ID, d.Department_Name"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) { depts[r.GetInt32(0)] = r.GetString(1); usual[r.GetInt32(0)] = Math.Round(Dec(r, 2), 1); }

        var scheduled = new Dictionary<(int, DateTime), int>();
        await using (var cmd = Cmd(conn, $@"
            SELECT d.Department_ID, d.Department_Name, s.Shift_Date, COUNT(*)
            {ShiftFrom}
            JOIN Department d ON d.Department_ID = {ShiftDept}
            WHERE s.Status <> 'Absent' AND s.Shift_Date BETWEEN @today AND @today + INTERVAL 6 DAY {DeptFilter}
            GROUP BY d.Department_ID, d.Department_Name, s.Shift_Date"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) { depts[r.GetInt32(0)] = r.GetString(1); scheduled[(r.GetInt32(0), r.GetDateTime(2).Date)] = Int(r, 3); }

        foreach (var (id, name) in depts.OrderBy(x => x.Value))
        {
            var u = usual.GetValueOrDefault(id);
            var cells = new List<Cell> { T(name), Dec1(u) };
            bool shortDay = false;
            foreach (var d in days)
            {
                var n = scheduled.GetValueOrDefault((id, d));
                bool low = u > 0 && n < u * 0.75m;
                shortDay |= low;
                var link = n == 0 ? null : Drill("shifts", $"dd:{id}:{d:yyyy-MM-dd}", "scheduled");
                cells.Add(new Cell { Text = low ? $"{n} (low)" : n.ToString(), Value = n, Link = link });
            }
            t.Rows.Add(R(cells, shortDay ? "warn" : null, $"/Staff/DepartmentDetails/{id}"));
        }
        t.EmptyText = "No shifts are scheduled for the next 7 days.";
        t.Note = "Usual = average people working per day over the last 28 days. \"(low)\" means under 75% of usual; those rows are yellow.";
        return t;
    }

    // ===== Source records: shifts =====
    private async Task<ReportTable> ShiftsAsync(MySqlConnection conn)
    {
        var kind = Key2 ?? "worked";
        var (type, text) = SplitKey(Key);
        bool future = type == "dd";
        var where = future ? "1=1" + DeptFilter : ShiftWhere;
        var title = new List<string>();
        switch (kind)
        {
            case "absent": where += " AND s.Status = 'Absent'"; title.Add("Absences"); break;
            case "scheduled": where += " AND s.Status <> 'Absent'"; title.Add("Scheduled shifts"); break;
            default: where += " AND s.Status = 'Worked'"; title.Add("Worked shifts"); break;
        }

        var cmdText = "";
        object? k = null, k2 = null;
        switch (type)
        {
            case "d": where += $" AND {ShiftDept} = @k"; k = text; title.Add(OptionName(_departments, IntOrNull(text)) ?? "department"); break;
            case "e": where += " AND s.Employee_ID = @k"; k = text; break;
            case "w": where += " AND YEARWEEK(s.Shift_Date, 1) = @k"; k = text; break;
            case "r":
                where += text == "No reason given"
                    ? " AND NULLIF(TRIM(s.Absence_Reason), '') IS NULL"
                    : " AND TRIM(s.Absence_Reason) = @k";
                k = text;
                title.Add(text);
                break;
            case "ew":
                var p = text.Split(':');
                where += " AND s.Employee_ID = @k AND YEARWEEK(s.Shift_Date, 1) = @k2";
                k = p[0]; k2 = p.Length > 1 ? p[1] : "";
                break;
            case "dd":
                var q = text.Split(':');
                where += $" AND {ShiftDept} = @k AND s.Shift_Date = @k2";
                k = q[0]; k2 = q.Length > 1 ? q[1] : "";
                title.Add(OptionName(_departments, IntOrNull(q[0])) ?? "department");
                if (DateTime.TryParse(k2 as string, out var day)) title.Add(day.ToString("ddd, MMM d"));
                break;
        }

        var t = Table(new Col("Date"), new Col("Employee"), new Col("Department"), new Col("Start"), new Col("End"),
                      new Col("Paid hours", true), new Col("Status"), new Col("Absence reason"), new Col("Cost", true));
        cmdText = $@"
            SELECT s.Shift_Date, e.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name), d.Department_Name,
                   s.Start_Time, s.End_Time, {PaidHours}, s.Status, s.Absence_Reason,
                   CASE WHEN s.Status = 'Worked' THEN {PaidHours} * {HourlyRate} ELSE 0 END
            {ShiftFrom}
            LEFT JOIN Department d ON d.Department_ID = {ShiftDept}
            WHERE {where}
            ORDER BY s.Shift_Date DESC, e.Last_Name
            LIMIT 500";
        await using var cmd = Cmd(conn, cmdText);
        if (k != null) cmd.Parameters.AddWithValue("@k", k);
        if (k2 != null) cmd.Parameters.AddWithValue("@k2", k2);
        await using var r = await cmd.ExecuteReaderAsync();
        decimal hours = 0, cost = 0;
        string? person = null;
        DateTime? firstDay = null;
        while (await r.ReadAsync())
        {
            person ??= r.GetString(2);
            firstDay = r.GetDateTime(0);
            var status = Str(r, 7);
            decimal h = status == "Worked" ? Dec(r, 6) : 0, c = Math.Round(Dec(r, 9), 2);
            hours += h; cost += c;
            t.Rows.Add(R(new[]
            {
                D(r.GetDateTime(0)), T(r.GetString(2), $"/Staff/EmployeeDetails/{r.GetInt32(1)}"), T(Str(r, 3)),
                T(Str(r, 4)), T(Str(r, 5)), status == "Worked" ? Dec1(h) : T("—"), T(status), T(Str(r, 8)), Money(c)
            }, status == "Absent" ? "warn" : null));
        }
        if (t.Rows.Count > 0 && kind != "absent")
            t.Total = R(new[] { T("Total"), T(""), T(""), T(""), T(""), Dec1(hours), T(""), T(""), Money(cost) });
        if ((type == "e" || type == "ew") && person != null) title.Add(person);
        if ((type == "w" || type == "ew") && firstDay != null) title.Add(WeekLabel(firstDay.Value));
        ResultTitle = string.Join(" · ", title);
        t.Note = "Paid hours subtract a 30-minute unpaid break on shifts of 6+ hours. Click a name to open the employee. Shows up to 500 shifts.";
        return t;
    }

    // ===== Source records: a department's open care tasks =====
    private async Task<ReportTable> TasksAsync(MySqlConnection conn)
    {
        var (_, text) = SplitKey(Key);
        var t = Table(new Col("Task"), new Col("Animal"), new Col("Assigned to"), new Col("Priority"), new Col("Due"), new Col("Status"));
        await using var cmd = new MySqlCommand(@"
            SELECT ct.Title, ct.Animal_ID, a.Animal_Name, CONCAT(e.First_Name, ' ', e.Last_Name), ct.Priority, ct.Due_At, ct.Status
            FROM Care_Task ct
            JOIN Employee e ON e.Employee_ID = ct.Assigned_Employee_ID
            LEFT JOIN Animal a ON a.Animal_ID = ct.Animal_ID
            WHERE e.Department_ID = @k AND ct.Status IN ('Open', 'In Progress')
            ORDER BY (ct.Due_At < @now) DESC, FIELD(ct.Priority, 'Urgent', 'High', 'Normal', 'Low'), ct.Due_At", conn);
        cmd.Parameters.AddWithValue("@k", IntOrNull(text) ?? 0);
        cmd.Parameters.AddWithValue("@now", DateTime.Now);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var due = DateOrNull(r, 5);
            t.Rows.Add(R(new[]
            {
                T(Str(r, 0)), r.IsDBNull(1) ? T("—") : T(Str(r, 2), $"/Animals/Details/{r.GetInt32(1)}"),
                T(Str(r, 3)), T(Str(r, 4)), T(due?.ToString("MMM d, h:mm tt") ?? "—"), T(Str(r, 6))
            }, due != null && due < DateTime.Now ? "bad" : null));
        }
        ResultTitle = "Open care tasks: " + (OptionName(_departments, IntOrNull(text)) ?? "department");
        t.EmptyText = "No open tasks.";
        t.Note = "Red: past due.";
        return t;
    }
}
