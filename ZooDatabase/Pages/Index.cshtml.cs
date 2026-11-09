using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages;

// Home page: a quick look at what needs attention today, then the sections you can open.
// Each user only sees numbers for the sections their role can open.
public class IndexModel : PageModel
{
    private readonly IConfiguration _config;

    public IndexModel(IConfiguration config)
    {
        _config = config;
    }

    public string DisplayName { get; set; } = "";
    public List<ZooModule> Modules { get; set; } = new();
    public List<StatTile> Tiles { get; set; } = new();
    public List<TaskRow> Tasks { get; set; } = new();
    public int OpenTaskCount { get; set; }
    public bool ShowTasks { get; set; }

    // Same rules the sidebar and module pages use
    public bool CanSee(string moduleKey)
    {
        if (User.IsInRole("ReadOnly") && (moduleKey == "Staff" || moduleKey == "Finance")) return false;
        if ((User.IsInRole("Zookeeper") || User.IsInRole("Veterinarian")) && moduleKey != "Animals") return false;
        if (User.IsInRole("Cashier") && moduleKey != "Tickets") return false;
        return true;
    }

    // Cashiers skip the dashboard and land on their register
    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        if (SalesOptions.IsCashierOnly(User)) context.Result = Redirect("/Register");
        else if (CareOptions.LandsOnCare(User)) context.Result = Redirect("/Care");
    }

    public async Task OnGetAsync()
    {
        DisplayName = User.Identity?.Name ?? "";
        Modules = ZooModules.All.Where(m => CanSee(m.Key)).ToList();

        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // Use the employee's first name when the account is linked to one
        int? myEmployeeId = null;
        if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
        {
            await using var cmd = new MySqlCommand(@"
                SELECT e.Employee_ID, e.First_Name FROM User_Account u
                JOIN Employee e ON e.Employee_ID = u.Employee_ID
                WHERE u.User_ID = @id", conn);
            cmd.Parameters.AddWithValue("@id", userId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                myEmployeeId = r.GetInt32(0);
                DisplayName = r.GetString(1);
            }
        }

        if (CanSee("Animals"))
        {
            var a = await RowAsync(conn, @"
                SELECT COUNT(*),
                       COALESCE(SUM(Health_Status NOT IN ('Healthy', 'Recovered')), 0),
                       COALESCE(SUM(Health_Status = 'Quarantined'), 0)
                FROM Animal WHERE Is_Active = 1");
            Tiles.Add(new StatTile("Animals", a[0], $"{a[1]} under vet care · {a[2]} quarantined",
                "/Animals/Animals", a[2] > 0 ? "warn" : "ok"));

            var f = await RowAsync(conn, @"
                SELECT COUNT(*),
                       COALESCE(SUM(EXISTS (SELECT 1 FROM Feeding_Log fl
                                    WHERE fl.Schedule_ID = fs.Schedule_ID AND fl.Feeding_Date = CURDATE())), 0)
                FROM Feeding_Schedule fs
                JOIN Animal a ON a.Animal_ID = fs.Animal_ID AND a.Is_Active = 1
                WHERE fs.Is_Active = 1");
            Tiles.Add(new StatTile("Feedings logged today", f[1], $"of {f[0]} scheduled",
                ReportAccess.CanView(User, ReportAccess.Feeding) ? "/Reports/Feeding" : "/Animals/Animals", f[1] < f[0] ? "warn" : "ok", $"/ {f[0]}"));

            // ReadOnly can't open the Feed page, so they don't get its tile
            if (!User.IsInRole("ReadOnly"))
            {
                var low = await RowAsync(conn,
                    "SELECT COUNT(*) FROM Feed WHERE Reorder_Level IS NOT NULL AND Quantity_In_Stock <= Reorder_Level");
                var drafts = await RowAsync(conn, "SELECT COUNT(*) FROM Purchase_Order WHERE Status = 'Draft'");
                Tiles.Add(new StatTile("Feeds low on stock", low[0], $"{drafts[0]} restock order(s) awaiting approval",
                    "/Animals/Feed", low[0] > 0 ? "bad" : "ok"));
            }

            await LoadTasksAsync(conn, myEmployeeId);
        }

        if (CanSee("Staff"))
        {
            var m = await RowAsync(conn, @"
                SELECT COUNT(*), COALESCE(SUM(Priority = 'Emergency'), 0)
                FROM Maintenance WHERE Status IN ('Open', 'Pending', 'In Progress')");
            Tiles.Add(new StatTile("Open repairs", m[0], m[1] > 0 ? $"{m[1]} emergency" : "No emergencies",
                "/Staff/Maintenance", m[1] > 0 ? "bad" : "ok"));
        }

        // Sales numbers are hidden from ReadOnly, like the Ticket Sales page in their sidebar
        if (CanSee("Tickets") && !User.IsInRole("ReadOnly"))
        {
            await using var cmd = new MySqlCommand(@"
                SELECT COUNT(*), COALESCE(SUM(Total_Amount), 0)
                FROM Ticket_Sale WHERE Purchase_Date >= CURDATE() - INTERVAL 6 DAY", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            Tiles.Add(new StatTile("Ticket orders, last 7 days", r.GetInt32(0), $"{r.GetDecimal(1):C0} in sales",
                "/Tickets/TicketSales", "ok"));
        }
    }

    private async Task LoadTasksAsync(MySqlConnection conn, int? myEmployeeId)
    {
        ShowTasks = true;
        OpenTaskCount = (await RowAsync(conn,
            "SELECT COUNT(*) FROM Care_Task WHERE Status IN ('Open', 'In Progress')"))[0];

        // Most urgent first: overdue, then by priority, then by due date
        await using var cmd = new MySqlCommand(@"
            SELECT t.Title, t.Priority, t.Due_At, t.Animal_ID, a.Animal_Name,
                   CONCAT(e.First_Name, ' ', e.Last_Name), t.Assigned_Employee_ID
            FROM Care_Task t
            LEFT JOIN Animal a ON a.Animal_ID = t.Animal_ID
            LEFT JOIN Employee e ON e.Employee_ID = t.Assigned_Employee_ID
            WHERE t.Status IN ('Open', 'In Progress')
            ORDER BY (t.Due_At < NOW()) DESC, FIELD(t.Priority, 'Urgent', 'High', 'Normal', 'Low'),
                     t.Due_At IS NULL, t.Due_At
            LIMIT 6", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            Tasks.Add(new TaskRow(
                r.GetString(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetDateTime(2),
                r.IsDBNull(3) ? null : r.GetInt32(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? "Unassigned" : r.GetString(5),
                !r.IsDBNull(6) && myEmployeeId == r.GetInt32(6)));
        }
    }

    // Run a query that returns one row of whole numbers
    private static async Task<int[]> RowAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        var values = new int[r.FieldCount];
        for (int i = 0; i < r.FieldCount; i++) values[i] = Convert.ToInt32(r.GetValue(i));
        return values;
    }

    // Tone: "ok" (green), "warn" (amber), "bad" (red)
    public record StatTile(string Label, int Value, string Detail, string Url, string Tone, string? Suffix = null);

    public record TaskRow(string Title, string Priority, DateTime? DueAt, int? AnimalId, string? AnimalName,
                          string AssignedTo, bool IsMine)
    {
        public bool IsOverdue => DueAt < DateTime.Now;
    }
}
