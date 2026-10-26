using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Animals;

// No role lock: everyone who's logged in can VIEW animals (even ReadOnly)
public class AnimalsModel : PageModel
{
    private readonly IConfiguration _config;

    public AnimalsModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public List<AnimalRow> Animals { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // JOIN swaps the ID numbers for real species and enclosure names
        var sql = @"
            SELECT a.Animal_ID, a.Animal_Name, s.Common_Name, s.Scientific_Name,
                   e.Enclosure_Name, a.Gender, a.Date_of_Birth, a.Health_Status
            FROM Animal a
            LEFT JOIN Species s ON a.Species_ID = s.Species_ID
            LEFT JOIN Enclosure e ON a.Enclosure_ID = e.Enclosure_ID
            WHERE a.Is_Active = 1";

        if (!string.IsNullOrWhiteSpace(Search))
        {
            sql += " AND (a.Animal_Name LIKE @search OR s.Common_Name LIKE @search OR e.Enclosure_Name LIKE @search)";
        }
        sql += " ORDER BY a.Animal_Name";

        await using var cmd = new MySqlCommand(sql, conn);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            cmd.Parameters.AddWithValue("@search", "%" + Search + "%");
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Animals.Add(new AnimalRow
            {
                AnimalId = reader.GetInt32(0),
                Name = reader.GetString(1),
                Species = reader.IsDBNull(2) ? "Unknown" : reader.GetString(2),
                ScientificName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Enclosure = reader.IsDBNull(4) ? "Unknown" : reader.GetString(4),
                Gender = reader.IsDBNull(5) ? null : reader.GetString(5),
                DateOfBirth = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                HealthStatus = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }
    }

    public class AnimalRow
    {
        public int AnimalId { get; set; }
        public string Name { get; set; } = "";
        public string Species { get; set; } = "";
        public string? ScientificName { get; set; }
        public string Enclosure { get; set; } = "";
        public string? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? HealthStatus { get; set; }
    }
}