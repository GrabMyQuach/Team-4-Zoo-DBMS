using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Staff;

// View-only staff list. Everyone logged in can see it, so it never shows
// private info like SSN, pay rate, or home address.
public class EmployeesModel : PageModel
{
    private readonly IConfiguration _config;

    public EmployeesModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public List<EmployeeRow> Rows { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // JOIN Department for the name; LEFT JOIN Keeper/Veterinarian to tag specialists
        var sql = @"
            SELECT e.Employee_ID, e.First_Name, e.Last_Name, e.Job_Position,
                   d.Department_Name, e.Employment_Type, e.Hire_Date, e.Phone_Number,
                   CASE
                       WHEN k.Employee_ID IS NOT NULL THEN 'Keeper'
                       WHEN v.Employee_ID IS NOT NULL THEN 'Veterinarian'
                       ELSE NULL
                   END AS Specialty
            FROM Employee e
            LEFT JOIN Department d ON e.Department_ID = d.Department_ID
            LEFT JOIN Keeper k ON k.Employee_ID = e.Employee_ID
            LEFT JOIN Veterinarian v ON v.Employee_ID = e.Employee_ID";

        if (!string.IsNullOrWhiteSpace(Search))
        {
            sql += @" WHERE CONCAT(e.First_Name, ' ', e.Last_Name) LIKE @search
                         OR e.Job_Position LIKE @search
                         OR d.Department_Name LIKE @search";
        }
        sql += " ORDER BY e.Last_Name, e.First_Name";

        await using var cmd = new MySqlCommand(sql, conn);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            cmd.Parameters.AddWithValue("@search", "%" + Search + "%");
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Rows.Add(new EmployeeRow
            {
                Name = reader.GetString(1) + " " + reader.GetString(2),
                JobPosition = reader.IsDBNull(3) ? null : reader.GetString(3),
                Department = reader.IsDBNull(4) ? null : reader.GetString(4),
                EmploymentType = reader.IsDBNull(5) ? null : reader.GetString(5),
                HireDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                Phone = reader.IsDBNull(7) ? null : reader.GetString(7),
                Specialty = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
    }

    public class EmployeeRow
    {
        public string Name { get; set; } = "";
        public string? JobPosition { get; set; }
        public string? Department { get; set; }
        public string? EmploymentType { get; set; }
        public DateTime? HireDate { get; set; }
        public string? Phone { get; set; }
        public string? Specialty { get; set; }
    }
}