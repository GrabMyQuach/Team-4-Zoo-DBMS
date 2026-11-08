using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Staff;

// Staff list. Everyone logged in can see it, so it never shows SSN, pay, or address.
// Click a row to see the employee's details.
public class EmployeesModel : PageModel
{
    private readonly IConfiguration _config;

    public EmployeesModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int? DepartmentId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Specialty { get; set; }

    public List<EmployeeRow> Rows { get; set; } = new();
    public List<(int Id, string Name)> Departments { get; set; } = new();
    public bool CanEdit => StaffOptions.CanEdit(User);

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using (var cmd = new MySqlCommand("SELECT Department_ID, Department_Name FROM Department ORDER BY Department_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) Departments.Add((r.GetInt32(0), r.GetString(1)));
        }

        // LEFT JOIN Keeper/Veterinarian to tag specialists
        var sql = @"
            SELECT e.Employee_ID, e.First_Name, e.Last_Name, e.Job_Position,
                   d.Department_Name, e.Employment_Type, e.Hire_Date,
                   CASE WHEN k.Employee_ID IS NOT NULL THEN 'Keeper'
                        WHEN v.Employee_ID IS NOT NULL THEN 'Veterinarian' END AS Specialty
            FROM Employee e
            LEFT JOIN Department d ON e.Department_ID = d.Department_ID
            LEFT JOIN Keeper k ON k.Employee_ID = e.Employee_ID
            LEFT JOIN Veterinarian v ON v.Employee_ID = e.Employee_ID
            WHERE 1 = 1";

        if (!string.IsNullOrWhiteSpace(Search))
            sql += " AND (CONCAT(e.First_Name, ' ', e.Last_Name) LIKE @search OR e.Job_Position LIKE @search)";
        if (DepartmentId != null)
            sql += " AND e.Department_ID = @dept";
        if (Specialty == "Keeper")
            sql += " AND k.Employee_ID IS NOT NULL";
        else if (Specialty == "Veterinarian")
            sql += " AND v.Employee_ID IS NOT NULL";
        sql += " ORDER BY e.Last_Name, e.First_Name";

        await using var cmd2 = new MySqlCommand(sql, conn);
        cmd2.Parameters.AddWithValue("@search", "%" + Search + "%");
        cmd2.Parameters.AddWithValue("@dept", DepartmentId);

        await using var reader = await cmd2.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Rows.Add(new EmployeeRow
            {
                EmployeeId = reader.GetInt32(0),
                Name = reader.GetString(1) + " " + reader.GetString(2),
                JobPosition = reader.IsDBNull(3) ? null : reader.GetString(3),
                Department = reader.IsDBNull(4) ? null : reader.GetString(4),
                EmploymentType = reader.IsDBNull(5) ? null : reader.GetString(5),
                HireDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                Specialty = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }
    }

    public class EmployeeRow
    {
        public int EmployeeId { get; set; }
        public string Name { get; set; } = "";
        public string? JobPosition { get; set; }
        public string? Department { get; set; }
        public string? EmploymentType { get; set; }
        public DateTime? HireDate { get; set; }
        public string? Specialty { get; set; }
    }
}
