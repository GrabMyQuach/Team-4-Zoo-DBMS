using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Staff;

public class KeepersModel : PageModel
{
    private readonly IConfiguration _config;

    public KeepersModel(IConfiguration config)
    {
        _config = config;
    }

    public List<KeeperRow> Rows { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // Only show assignments that haven't ended (no end date, or end date in the future)
        const string sql = @"
            SELECT k.Employee_ID, e.First_Name, e.Last_Name, k.Experience_Level, e.Phone_Number,
                   GROUP_CONCAT(enc.Enclosure_Name ORDER BY enc.Enclosure_Name SEPARATOR ', ') AS Enclosures
            FROM Keeper k
            JOIN Employee e ON e.Employee_ID = k.Employee_ID
            LEFT JOIN Keeper_Enclosure ke ON ke.Employee_ID = k.Employee_ID
                 AND (ke.Assignment_End_Date IS NULL OR ke.Assignment_End_Date >= CURDATE())
            LEFT JOIN Enclosure enc ON enc.Enclosure_ID = ke.Enclosure_ID
            GROUP BY k.Employee_ID, e.First_Name, e.Last_Name, k.Experience_Level, e.Phone_Number
            ORDER BY e.Last_Name, e.First_Name";

        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Rows.Add(new KeeperRow
            {
                Name = reader.GetString(1) + " " + reader.GetString(2),
                ExperienceLevel = reader.IsDBNull(3) ? null : reader.GetString(3),
                Phone = reader.IsDBNull(4) ? null : reader.GetString(4),
                Enclosures = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }
    }

    public class KeeperRow
    {
        public string Name { get; set; } = "";
        public string? ExperienceLevel { get; set; }
        public string? Phone { get; set; }
        public string? Enclosures { get; set; }
    }
}