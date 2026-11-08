using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Staff;

// Department list with headcount, manager, and last-30-day coverage. Click a row for details.
public class DepartmentsModel : PageModel
{
    private readonly IConfiguration _config;

    public DepartmentsModel(IConfiguration config)
    {
        _config = config;
    }

    public List<DepartmentRow> Rows { get; set; } = new();
    public bool CanEdit => StaffOptions.CanEdit(User);

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // Correlated subqueries keep each count separate (joining them all would multiply rows)
        const string sql = @"
            SELECT d.Department_ID, d.Department_Name, d.Location,
                   CONCAT(m.First_Name, ' ', m.Last_Name) AS Manager,
                   (SELECT COUNT(*) FROM Employee e WHERE e.Department_ID = d.Department_ID) AS Staff,
                   (SELECT COUNT(*) FROM Volunteer v WHERE v.Department_ID = d.Department_ID
                      AND (v.End_Date IS NULL OR v.End_Date >= CURDATE())) AS Volunteers,
                   (SELECT COALESCE(SUM(s.Hours), 0) FROM Employee_Shift s
                     WHERE s.Department_ID = d.Department_ID AND s.Status = 'Worked'
                       AND s.Shift_Date >= CURDATE() - INTERVAL 30 DAY) AS Hours30,
                   (SELECT COUNT(*) FROM Employee_Shift s
                     WHERE s.Department_ID = d.Department_ID AND s.Status = 'Absent'
                       AND s.Shift_Date >= CURDATE() - INTERVAL 30 DAY) AS Absences30
            FROM Department d
            LEFT JOIN Employee m ON m.Employee_ID = d.Manager_Employee_ID
            ORDER BY d.Department_Name";

        await using var cmd = new MySqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            Rows.Add(new DepartmentRow
            {
                DepartmentId = r.GetInt32(0),
                Name = r.GetString(1),
                Location = r.IsDBNull(2) ? null : r.GetString(2),
                Manager = r.IsDBNull(3) ? null : r.GetString(3),
                Staff = Convert.ToInt32(r.GetValue(4)),
                Volunteers = Convert.ToInt32(r.GetValue(5)),
                Hours30 = Convert.ToDecimal(r.GetValue(6)),
                Absences30 = Convert.ToInt32(r.GetValue(7))
            });
        }
    }

    public class DepartmentRow
    {
        public int DepartmentId { get; set; }
        public string Name { get; set; } = "";
        public string? Location { get; set; }
        public string? Manager { get; set; }
        public int Staff { get; set; }
        public int Volunteers { get; set; }
        public decimal Hours30 { get; set; }
        public int Absences30 { get; set; }
    }
}
