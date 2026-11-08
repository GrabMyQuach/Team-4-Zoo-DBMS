using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Reports;

// STAFFING & LABOR REPORT
// Question: "Is staff time being used efficiently, what does labor cost, and do departments have enough coverage?"
// Uses Employee_Shift, Employee, Department, Care_Task.
[Authorize(Roles = ReportAccess.Staffing)]
public class StaffingModel : ReportPageModel
{
    public StaffingModel(IConfiguration config) : base(config) { }

    // Salaried pay is stored per year; turn it into an hourly rate (52 weeks x 40 hours)
    private const string HourlyRate =
        "CASE WHEN e.Pay_Type = 'Salary' AND e.Pay_Rate > 500 THEN e.Pay_Rate / 2080 ELSE COALESCE(e.Pay_Rate, 0) END";

    // A shift counts toward the department it was worked in, or the person's home department
    private const string ShiftDept = "COALESCE(s.Department_ID, e.Department_ID)";

    [BindProperty(SupportsGet = true)] public int? DepartmentId { get; set; }
    public List<Option> DepartmentOptions { get; set; } = new();

    // ---- Results ----
    public decimal Hours { get; set; }
    public decimal LaborCost { get; set; }
    public int ShiftsWorked { get; set; }
    public int Absences { get; set; }
    public int StaffWorking { get; set; }
    public decimal AbsenceRate => Pct(Absences, ShiftsWorked + Absences);
    public decimal OvertimeHours => Overtime.Sum(o => o.Hours - 40);

    public List<DeptRow> ByDepartment { get; set; } = new();
    public List<PersonRow> People { get; set; } = new();
    public List<WeekRow> Weeks { get; set; } = new();
    public List<OvertimeRow> Overtime { get; set; } = new();
    public List<ReasonRow> Reasons { get; set; } = new();

    // Next 7 days: scheduled headcount per department per day vs. their usual daily headcount
    public List<DateTime> NextDays { get; set; } = new();
    public List<CoverageRow> Coverage { get; set; } = new();

    public string FilterText => DepartmentId == null ? "" :
        "Department: " + DepartmentOptions.FirstOrDefault(o => o.Id == DepartmentId)?.Name;

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnGetExcelAsync()
    {
        await LoadAsync();
        var subtitle = $"{DateRangeText}{(FilterText == "" ? "" : " · " + FilterText)} · Generated {DateTime.Now:MMM d, yyyy h:mm tt}";

        var xl = new ExcelReport("Staffing & Labor Report", subtitle)
            .AddSheet("Summary", new[] { "Measure", "Value" }, new[]
            {
                new object?[] { "Hours worked", Hours },
                new object?[] { "Estimated labor cost ($)", LaborCost },
                new object?[] { "Shifts worked", ShiftsWorked },
                new object?[] { "Staff who worked", StaffWorking },
                new object?[] { "Absences", Absences },
                new object?[] { "Absence rate (%)", AbsenceRate },
                new object?[] { "Overtime hours (over 40/week)", OvertimeHours }
            })
            .AddSheet("By Department", new[] { "Department", "Staff", "Shifts", "Hours", "Hours per Person", "Absences", "Absence Rate (%)", "Labor Cost ($)", "Cost per Hour ($)", "Open Tasks" },
                ByDepartment.Select(d => new object?[] { d.Name, d.Staff, d.Shifts, d.Hours, d.HoursPerPerson, d.Absences, d.AbsenceRate, d.Cost, d.CostPerHour, d.OpenTasks }),
                row => (decimal)(row[6] ?? 0m) >= 10 ? ExcelReport.Yellow : null)
            .AddSheet("By Employee", new[] { "Employee", "Department", "Job", "Shifts", "Hours", "Absences", "Overtime Weeks", "Open Tasks", "Labor Cost ($)" },
                People.Select(p => new object?[] { p.Name, p.Department, p.Job, p.Shifts, p.Hours, p.Absences, p.OvertimeWeeks, p.OpenTasks, p.Cost }),
                row => (int)(row[5] ?? 0) >= 3 || (int)(row[6] ?? 0) > 0 ? ExcelReport.Yellow : null)
            .AddSheet("Weekly Trend", new[] { "Week Starting", "Shifts", "Hours", "Absences", "Absence Rate (%)", "Labor Cost ($)" },
                Weeks.Select(w => new object?[] { w.WeekStart, w.Shifts, w.Hours, w.Absences, w.AbsenceRate, w.Cost }))
            .AddSheet("Overtime", new[] { "Employee", "Week Starting", "Hours", "Overtime Hours" },
                Overtime.Select(o => new object?[] { o.Name, o.WeekStart, o.Hours, o.Hours - 40 }))
            .AddSheet("Absence Reasons", new[] { "Reason", "Absences" },
                Reasons.Select(r => new object?[] { r.Reason, r.Count }))
            .AddSheet("Next 7 Days", new[] { "Department", "Usual Daily Staff" }.Concat(NextDays.Select(d => d.ToString("ddd MMM d"))).ToArray(),
                Coverage.Select(c => new object?[] { c.Department, c.UsualDaily }.Concat(c.Scheduled.Select(n => (object?)n)).ToArray()));

        return File(xl.ToBytes(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"staffing-report-{DateTime.Today:yyyy-MM-dd}.xlsx");
    }

    private async Task LoadAsync()
    {
        ResolveDates();
        await using var conn = await OpenAsync();
        DepartmentOptions = await LoadOptionsAsync(conn, "SELECT Department_ID, Department_Name FROM Department ORDER BY Department_Name");

        var deptFilter = DepartmentId != null ? $" AND {ShiftDept} = @dept" : "";
        var where = "s.Shift_Date >= @from AND s.Shift_Date < @to AND s.Shift_Date <= CURDATE()" + deptFilter;
        const string from = "FROM Employee_Shift s JOIN Employee e ON e.Employee_ID = s.Employee_ID";

        MySqlCommand Cmd(string sql)
        {
            var cmd = new MySqlCommand(sql, conn);
            AddDateParams(cmd);
            cmd.Parameters.AddWithValue("@dept", DepartmentId);
            return cmd;
        }

        // 1. Totals
        await using (var cmd = Cmd($@"
            SELECT COALESCE(SUM(CASE WHEN s.Status = 'Worked' THEN s.Hours END), 0),
                   COALESCE(SUM(CASE WHEN s.Status = 'Worked' THEN s.Hours * {HourlyRate} END), 0),
                   SUM(s.Status = 'Worked'), SUM(s.Status = 'Absent'),
                   COUNT(DISTINCT CASE WHEN s.Status = 'Worked' THEN s.Employee_ID END)
            {from} WHERE {where}"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                Hours = Math.Round(Dec(r, 0), 1);
                LaborCost = Math.Round(Dec(r, 1), 2);
                ShiftsWorked = Int(r, 2);
                Absences = Int(r, 3);
                StaffWorking = Int(r, 4);
            }
        }

        // 2. By department, with open tasks as a workload signal
        await using (var cmd = Cmd($@"
            SELECT d.Department_Name,
                   COUNT(DISTINCT CASE WHEN s.Status = 'Worked' THEN s.Employee_ID END),
                   SUM(s.Status = 'Worked'),
                   COALESCE(SUM(CASE WHEN s.Status = 'Worked' THEN s.Hours END), 0),
                   SUM(s.Status = 'Absent'),
                   COALESCE(SUM(CASE WHEN s.Status = 'Worked' THEN s.Hours * {HourlyRate} END), 0),
                   (SELECT COUNT(*) FROM Care_Task t JOIN Employee te ON te.Employee_ID = t.Assigned_Employee_ID
                     WHERE te.Department_ID = d.Department_ID AND t.Status IN ('Open', 'In Progress'))
            {from}
            JOIN Department d ON d.Department_ID = {ShiftDept}
            WHERE {where}
            GROUP BY d.Department_ID, d.Department_Name
            ORDER BY 4 DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                ByDepartment.Add(new DeptRow
                {
                    Name = r.GetString(0),
                    Staff = Int(r, 1),
                    Shifts = Int(r, 2),
                    Hours = Math.Round(Dec(r, 3), 1),
                    Absences = Int(r, 4),
                    Cost = Math.Round(Dec(r, 5), 2),
                    OpenTasks = Int(r, 6)
                });
            }
        }

        // 3. Overtime: any employee-week over 40 hours worked
        await using (var cmd = Cmd($@"
            SELECT CONCAT(e.First_Name, ' ', e.Last_Name), MIN(s.Shift_Date), SUM(s.Hours), e.Employee_ID
            {from}
            WHERE {where} AND s.Status = 'Worked'
            GROUP BY e.Employee_ID, e.First_Name, e.Last_Name, YEARWEEK(s.Shift_Date, 1)
            HAVING SUM(s.Hours) > 40
            ORDER BY SUM(s.Hours) DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                Overtime.Add(new OvertimeRow(r.GetString(0), r.GetDateTime(1), Math.Round(Dec(r, 2), 1), r.GetInt32(3)));
        }

        // 4. By employee: who is carrying the most, who is missing shifts
        await using (var cmd = Cmd($@"
            SELECT e.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name), d.Department_Name, e.Job_Position,
                   SUM(s.Status = 'Worked'),
                   COALESCE(SUM(CASE WHEN s.Status = 'Worked' THEN s.Hours END), 0),
                   SUM(s.Status = 'Absent'),
                   COALESCE(SUM(CASE WHEN s.Status = 'Worked' THEN s.Hours * {HourlyRate} END), 0),
                   (SELECT COUNT(*) FROM Care_Task t WHERE t.Assigned_Employee_ID = e.Employee_ID
                      AND t.Status IN ('Open', 'In Progress'))
            {from}
            LEFT JOIN Department d ON d.Department_ID = e.Department_ID
            WHERE {where}
            GROUP BY e.Employee_ID, e.First_Name, e.Last_Name, d.Department_Name, e.Job_Position
            ORDER BY 6 DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var id = r.GetInt32(0);
                People.Add(new PersonRow
                {
                    EmployeeId = id,
                    Name = r.GetString(1),
                    Department = Str(r, 2),
                    Job = Str(r, 3),
                    Shifts = Int(r, 4),
                    Hours = Math.Round(Dec(r, 5), 1),
                    Absences = Int(r, 6),
                    Cost = Math.Round(Dec(r, 7), 2),
                    OpenTasks = Int(r, 8),
                    OvertimeWeeks = Overtime.Count(o => o.EmployeeId == id)
                });
            }
        }

        // 5. Weekly trend
        await using (var cmd = Cmd($@"
            SELECT MIN(s.Shift_Date),
                   SUM(s.Status = 'Worked'),
                   COALESCE(SUM(CASE WHEN s.Status = 'Worked' THEN s.Hours END), 0),
                   SUM(s.Status = 'Absent'),
                   COALESCE(SUM(CASE WHEN s.Status = 'Worked' THEN s.Hours * {HourlyRate} END), 0)
            {from}
            WHERE {where}
            GROUP BY YEARWEEK(s.Shift_Date, 1)
            ORDER BY MIN(s.Shift_Date)"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                Weeks.Add(new WeekRow
                {
                    WeekStart = r.GetDateTime(0),
                    Shifts = Int(r, 1),
                    Hours = Math.Round(Dec(r, 2), 1),
                    Absences = Int(r, 3),
                    Cost = Math.Round(Dec(r, 4), 2)
                });
            }
        }

        // 6. Why people are absent
        await using (var cmd = Cmd($@"
            SELECT COALESCE(NULLIF(TRIM(s.Absence_Reason), ''), 'No reason given'), COUNT(*)
            {from}
            WHERE {where} AND s.Status = 'Absent'
            GROUP BY 1
            ORDER BY 2 DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) Reasons.Add(new ReasonRow(r.GetString(0), Int(r, 1)));
        }

        // 7. Coverage for the next 7 days (always from today, not the date filter).
        //    "Usual" = average people working per day in that department over the last 28 days.
        for (int i = 0; i < 7; i++) NextDays.Add(DateTime.Today.AddDays(i));

        var usual = new Dictionary<string, decimal>();
        await using (var cmd = Cmd($@"
            SELECT d.Department_Name, COUNT(*) / COUNT(DISTINCT s.Shift_Date)
            {from}
            JOIN Department d ON d.Department_ID = {ShiftDept}
            WHERE s.Status = 'Worked' AND s.Shift_Date BETWEEN CURDATE() - INTERVAL 28 DAY AND CURDATE() - INTERVAL 1 DAY {deptFilter}
            GROUP BY d.Department_ID, d.Department_Name"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) usual[r.GetString(0)] = Math.Round(Dec(r, 1), 1);
        }

        var scheduled = new Dictionary<(string, DateTime), int>();
        await using (var cmd = Cmd($@"
            SELECT d.Department_Name, s.Shift_Date, COUNT(*)
            {from}
            JOIN Department d ON d.Department_ID = {ShiftDept}
            WHERE s.Status <> 'Absent' AND s.Shift_Date BETWEEN CURDATE() AND CURDATE() + INTERVAL 6 DAY {deptFilter}
            GROUP BY d.Department_ID, d.Department_Name, s.Shift_Date"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) scheduled[(r.GetString(0), r.GetDateTime(1).Date)] = Int(r, 2);
        }

        foreach (var dept in usual.Keys.Union(scheduled.Keys.Select(k => k.Item1)).OrderBy(n => n))
        {
            Coverage.Add(new CoverageRow
            {
                Department = dept,
                UsualDaily = usual.TryGetValue(dept, out var u) ? u : 0,
                Scheduled = NextDays.Select(d => scheduled.TryGetValue((dept, d), out var n) ? n : 0).ToList()
            });
        }
    }

    public class DeptRow
    {
        public string Name { get; set; } = "";
        public int Staff { get; set; }
        public int Shifts { get; set; }
        public decimal Hours { get; set; }
        public int Absences { get; set; }
        public decimal Cost { get; set; }
        public int OpenTasks { get; set; }
        public decimal HoursPerPerson => Staff == 0 ? 0 : Math.Round(Hours / Staff, 1);
        public decimal AbsenceRate => Pct(Absences, Shifts + Absences);
        public decimal CostPerHour => Hours == 0 ? 0 : Math.Round(Cost / Hours, 2);
    }

    public class PersonRow
    {
        public int EmployeeId { get; set; }
        public string Name { get; set; } = "";
        public string? Department { get; set; }
        public string? Job { get; set; }
        public int Shifts { get; set; }
        public decimal Hours { get; set; }
        public int Absences { get; set; }
        public int OvertimeWeeks { get; set; }
        public int OpenTasks { get; set; }
        public decimal Cost { get; set; }
    }

    public class WeekRow
    {
        public DateTime WeekStart { get; set; }
        public int Shifts { get; set; }
        public decimal Hours { get; set; }
        public int Absences { get; set; }
        public decimal Cost { get; set; }
        public decimal AbsenceRate => Pct(Absences, Shifts + Absences);
    }

    public record OvertimeRow(string Name, DateTime WeekStart, decimal Hours, int EmployeeId);
    public record ReasonRow(string Reason, int Count);

    public class CoverageRow
    {
        public string Department { get; set; } = "";
        public decimal UsualDaily { get; set; }
        public List<int> Scheduled { get; set; } = new();
        // Below 75% of the usual headcount is flagged as short-staffed
        public bool IsShort(int n) => UsualDaily > 0 && n < UsualDaily * 0.75m;
    }
}
