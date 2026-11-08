using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Staff;

// /Staff/DepartmentDetails/3 -> one department: manager, staff with their
// last-30-day hours and absences, and volunteers.
public class DepartmentDetailsModel : PageModel
{
    private readonly IConfiguration _config;

    public DepartmentDetailsModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public string Name { get; set; } = "";
    public string? Location { get; set; }
    public string? Phone { get; set; }
    public int? ManagerId { get; set; }
    public string? Manager { get; set; }
    public List<StaffRow> Staff { get; set; } = new();
    public List<VolunteerRow> Volunteers { get; set; } = new();
    public bool CanEdit => StaffOptions.CanEdit(User);

    public decimal TotalHours => Staff.Sum(s => s.Hours30);
    public int TotalAbsences => Staff.Sum(s => s.Absences30);
    public int FullTime => Staff.Count(s => s.EmploymentType == "Full-Time");

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using (var cmd = new MySqlCommand(@"
            SELECT d.Department_Name, d.Location, d.Phone_Number, d.Manager_Employee_ID,
                   CONCAT(m.First_Name, ' ', m.Last_Name)
            FROM Department d
            LEFT JOIN Employee m ON m.Employee_ID = d.Manager_Employee_ID
            WHERE d.Department_ID = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            Name = r.GetString(0);
            Location = r.IsDBNull(1) ? null : r.GetString(1);
            Phone = r.IsDBNull(2) ? null : r.GetString(2);
            ManagerId = r.IsDBNull(3) ? null : r.GetInt32(3);
            Manager = r.IsDBNull(4) ? null : r.GetString(4);
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT e.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name), e.Job_Position, e.Employment_Type,
                   COALESCE(SUM(CASE WHEN s.Status = 'Worked' THEN s.Hours END), 0),
                   COALESCE(SUM(s.Status = 'Absent'), 0)
            FROM Employee e
            LEFT JOIN Employee_Shift s ON s.Employee_ID = e.Employee_ID
                                      AND s.Shift_Date BETWEEN CURDATE() - INTERVAL 30 DAY AND CURDATE()
            WHERE e.Department_ID = @id
            GROUP BY e.Employee_ID, e.First_Name, e.Last_Name, e.Job_Position, e.Employment_Type
            ORDER BY e.Last_Name, e.First_Name", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                Staff.Add(new StaffRow
                {
                    EmployeeId = r.GetInt32(0),
                    Name = r.GetString(1),
                    JobPosition = r.IsDBNull(2) ? null : r.GetString(2),
                    EmploymentType = r.IsDBNull(3) ? null : r.GetString(3),
                    Hours30 = Convert.ToDecimal(r.GetValue(4)),
                    Absences30 = Convert.ToInt32(r.GetValue(5))
                });
            }
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT CONCAT(First_Name, ' ', Last_Name), Volunteer_Status, Start_Date
            FROM Volunteer
            WHERE Department_ID = @id AND (End_Date IS NULL OR End_Date >= CURDATE())
            ORDER BY Last_Name, First_Name", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                Volunteers.Add(new VolunteerRow(r.GetString(0),
                    r.IsDBNull(1) ? null : r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetDateTime(2)));
            }
        }

        return Page();
    }

    public class StaffRow
    {
        public int EmployeeId { get; set; }
        public string Name { get; set; } = "";
        public string? JobPosition { get; set; }
        public string? EmploymentType { get; set; }
        public decimal Hours30 { get; set; }
        public int Absences30 { get; set; }
    }

    public record VolunteerRow(string Name, string? Status, DateTime? Since);
}
