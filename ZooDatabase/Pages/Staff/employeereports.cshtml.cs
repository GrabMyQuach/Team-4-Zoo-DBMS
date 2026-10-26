
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Staff;

[Authorize(Roles = "DBA,SuperUser,Admin")]
public class EmployeeReportModel : PageModel
{
    private readonly IConfiguration _config;

    public EmployeeReportModel(IConfiguration config)
    {
        _config = config;
    }

    public class EmployeeReportRecord
    {
        public int EmployeeId { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string? JobPosition { get; set; }
        public string? DepartmentName { get; set; }
        public string? EmploymentType { get; set; }
        public decimal? PayRate { get; set; }
    }

    public List<EmployeeReportRecord> Employees { get; set; } = new();

    public async Task OnGetAsync()
    {
        var connectionString = _config.GetConnectionString("ZooDb");

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                e.Employee_ID,
                e.First_Name,
                e.Last_Name,
                e.Job_Position,
                d.Department_Name,
                e.Employment_Type,
                e.Pay_Rate
            FROM Employee AS e
            LEFT JOIN Department AS d
                ON e.Department_ID = d.Department_ID
            ORDER BY e.Employee_ID;";

        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            Employees.Add(new EmployeeReportRecord
            {
                EmployeeId = reader.GetInt32(0),
                FirstName = reader.GetString(1),
                LastName = reader.GetString(2),
                JobPosition = reader.IsDBNull(3) ? null : reader.GetString(3),
                DepartmentName = reader.IsDBNull(4) ? null : reader.GetString(4),
                EmploymentType = reader.IsDBNull(5) ? null : reader.GetString(5),
                PayRate = reader.IsDBNull(6) ? null : reader.GetDecimal(6)
            });
        }
    }
}
