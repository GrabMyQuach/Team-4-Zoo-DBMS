using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Animals;

public class EnclosuresModel : PageModel
{
    private readonly IConfiguration _config;

    public EnclosuresModel(IConfiguration config)
    {
        _config = config;
    }

    public List<EnclosureRow> Rows { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // Count the animals living in each enclosure, to compare with its capacity
        const string sql = @"
            SELECT e.Enclosure_ID, e.Enclosure_Name, e.Enclosure_Type, e.Location, e.Capacity,
                   COUNT(a.Animal_ID) AS Animal_Count
            FROM Enclosure e
            LEFT JOIN Animal a ON a.Enclosure_ID = e.Enclosure_ID AND a.Is_Active = 1
            GROUP BY e.Enclosure_ID, e.Enclosure_Name, e.Enclosure_Type, e.Location, e.Capacity
            ORDER BY e.Enclosure_Name";

        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Rows.Add(new EnclosureRow
            {
                Name = reader.GetString(1),
                Type = reader.IsDBNull(2) ? null : reader.GetString(2),
                Location = reader.IsDBNull(3) ? null : reader.GetString(3),
                Capacity = reader.GetInt32(4),
                AnimalCount = Convert.ToInt32(reader.GetValue(5))
            });
        }
    }

    public class EnclosureRow
    {
        public string Name { get; set; } = "";
        public string? Type { get; set; }
        public string? Location { get; set; }
        public int Capacity { get; set; }
        public int AnimalCount { get; set; }
    }
}