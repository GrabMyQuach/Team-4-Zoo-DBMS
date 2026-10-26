using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Staff;

public class DepartmentsModel : PageModel
{
    private readonly IConfiguration _config;

    public DepartmentsModel(IConfiguration config)
    {
        _config = config;
    }

    public List<DepartmentRow> Rows { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // Two LEFT JOINs can repeat rows, so COUNT(DISTINCT ...) counts each person once
        const string sql = @"
            SELECT d.Department_ID, d.Department_Name, d.Location, d.Phone_Number,
                   COUNT(DISTINCT e.Employee_ID) AS Staff_Count,
                   COUNT(DISTINCT vol.Volunteer_ID) AS Volunteer_Count
            FROM Department d
            LEFT JOIN Employee e ON e.Department_ID = d.Department_ID
            LEFT JOIN Volunteer vol ON vol.Department_ID = d.Department_ID
            GROUP BY d.Department_ID, d.Department_Name, d.Location, d.Phone_Number
            ORDER BY d.Department_Name";

        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Rows.Add(new DepartmentRow
            {
                Name = reader.GetString(1),
                Location = reader.IsDBNull(2) ? null : reader.GetString(2),
                Phone = reader.IsDBNull(3) ? null : reader.GetString(3),
                StaffCount = Convert.ToInt32(reader.GetValue(4)),
                VolunteerCount = Convert.ToInt32(reader.GetValue(5))
            });
        }
    }

    public class DepartmentRow
    {
        public string Name { get; set; } = "";
        public string? Location { get; set; }
        public string? Phone { get; set; }
        public int StaffCount { get; set; }
        public int VolunteerCount { get; set; }
    }
}