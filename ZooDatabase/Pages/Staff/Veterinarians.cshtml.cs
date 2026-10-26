using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Staff;

public class VeterinariansModel : PageModel
{
    private readonly IConfiguration _config;

    public VeterinariansModel(IConfiguration config)
    {
        _config = config;
    }

    public List<VetRow> Rows { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // Count the (non-deleted) animals each vet is assigned to through Animal_Employee
        const string sql = @"
            SELECT v.Employee_ID, e.First_Name, e.Last_Name, v.Specialization,
                   e.Phone_Number, e.Hire_Date,
                   COUNT(a.Animal_ID) AS Animal_Count
            FROM Veterinarian v
            JOIN Employee e ON e.Employee_ID = v.Employee_ID
            LEFT JOIN Animal_Employee ae ON ae.Employee_ID = v.Employee_ID
            LEFT JOIN Animal a ON a.Animal_ID = ae.Animal_ID AND a.Is_Active = 1
            GROUP BY v.Employee_ID, e.First_Name, e.Last_Name, v.Specialization,
                     e.Phone_Number, e.Hire_Date
            ORDER BY e.Last_Name, e.First_Name";

        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Rows.Add(new VetRow
            {
                Name = "Dr. " + reader.GetString(1) + " " + reader.GetString(2),
                Specialization = reader.IsDBNull(3) ? null : reader.GetString(3),
                Phone = reader.IsDBNull(4) ? null : reader.GetString(4),
                HireDate = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                AnimalCount = Convert.ToInt32(reader.GetValue(6))
            });
        }
    }

    public class VetRow
    {
        public string Name { get; set; } = "";
        public string? Specialization { get; set; }
        public string? Phone { get; set; }
        public DateTime? HireDate { get; set; }
        public int AnimalCount { get; set; }
    }
}