using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Animals;

public class SpeciesModel : PageModel
{
    private readonly IConfiguration _config;

    public SpeciesModel(IConfiguration config)
    {
        _config = config;
    }

    public List<SpeciesRow> Rows { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // LEFT JOIN + COUNT = how many (non-deleted) animals each species has
        const string sql = @"
            SELECT s.Species_ID, s.Common_Name, s.Scientific_Name, s.Diet_Type,
                   s.Conservation_Status, s.Habitat_Type,
                   COUNT(a.Animal_ID) AS Animal_Count
            FROM Species s
            LEFT JOIN Animal a ON a.Species_ID = s.Species_ID AND a.Is_Active = 1
            GROUP BY s.Species_ID, s.Common_Name, s.Scientific_Name, s.Diet_Type,
                     s.Conservation_Status, s.Habitat_Type
            ORDER BY s.Common_Name";

        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Rows.Add(new SpeciesRow
            {
                CommonName = reader.GetString(1),
                ScientificName = reader.IsDBNull(2) ? null : reader.GetString(2),
                DietType = reader.IsDBNull(3) ? null : reader.GetString(3),
                ConservationStatus = reader.IsDBNull(4) ? null : reader.GetString(4),
                HabitatType = reader.IsDBNull(5) ? null : reader.GetString(5),
                AnimalCount = Convert.ToInt32(reader.GetValue(6))
            });
        }
    }

    public class SpeciesRow
    {
        public string CommonName { get; set; } = "";
        public string? ScientificName { get; set; }
        public string? DietType { get; set; }
        public string? ConservationStatus { get; set; }
        public string? HabitatType { get; set; }
        public int AnimalCount { get; set; }
    }
}