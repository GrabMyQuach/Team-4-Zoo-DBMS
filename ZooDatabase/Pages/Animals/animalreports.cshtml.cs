
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Animals;

[Authorize(Roles = "DBA,SuperUser,Admin")]
public class AnimalReportModel : PageModel
{
    private readonly IConfiguration _config;

    public AnimalReportModel(IConfiguration config)
    {
        _config = config;
    }

    public class AnimalReportRecord
    {
        public int AnimalId { get; set; }
        public string AnimalName { get; set; } = "";
        public string Species { get; set; } = "";
        public string? Gender { get; set; }
        public string? HealthStatus { get; set; }
    }

    public List<AnimalReportRecord> Animals { get; set; } = new();

    public async Task OnGetAsync()
    {
        var connectionString =
            _config.GetConnectionString("ZooDb");

        await using var connection =
            new MySqlConnection(connectionString);

        await connection.OpenAsync();

        const string sql = @"
            SELECT
                a.Animal_ID,
                a.Animal_Name,
                s.Common_Name AS Species,
                a.Gender,
                a.Health_Status
            FROM Animal AS a
            INNER JOIN Species AS s
                ON a.Species_ID = s.Species_ID
            ORDER BY a.Animal_ID;";

        await using var command =
            new MySqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            Animals.Add(new AnimalReportRecord
            {
                AnimalId = reader.GetInt32(0),
                AnimalName = reader.GetString(1),
                Species = reader.GetString(2),
                Gender = reader.IsDBNull(3)
                    ? null : reader.GetString(3),
                HealthStatus = reader.IsDBNull(4)
                    ? null : reader.GetString(4)
            });
        }
    }
}
