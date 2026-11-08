using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Animals;

// The main animal list. Everyone logged in can see it; each row opens the animal's profile.
public class AnimalsModel : PageModel
{
    private readonly IConfiguration _config;

    public AnimalsModel(IConfiguration config)
    {
        _config = config;
    }

    // Filters (they come from the web address, like ?Health=Recovering)
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Health { get; set; }
    [BindProperty(SupportsGet = true)] public int? EnclosureId { get; set; }

    public List<AnimalRow> Rows { get; set; } = new();
    public List<EnclosureOption> Enclosures { get; set; } = new();

    public bool CanEdit => AnimalOptions.CanEdit(User);

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // Build the WHERE part from whichever filters were used
        var where = new List<string> { "a.Is_Active = 1" };
        if (!string.IsNullOrWhiteSpace(Search))
            where.Add("(a.Animal_Name LIKE @search OR s.Common_Name LIKE @search OR e.Enclosure_Name LIKE @search)");
        if (!string.IsNullOrWhiteSpace(Health))
            where.Add("a.Health_Status = @health");
        if (EnclosureId != null)
            where.Add("a.Enclosure_ID = @enclosure");

        var sql = @"
            SELECT a.Animal_ID, a.Animal_Name, s.Common_Name, s.Scientific_Name,
                   e.Enclosure_Name, a.Gender, a.Date_of_Birth, a.Health_Status
            FROM Animal a
            JOIN Species s ON a.Species_ID = s.Species_ID
            JOIN Enclosure e ON a.Enclosure_ID = e.Enclosure_ID
            WHERE " + string.Join(" AND ", where) + @"
            ORDER BY a.Animal_Name";

        await using (var cmd = new MySqlCommand(sql, conn))
        {
            if (!string.IsNullOrWhiteSpace(Search)) cmd.Parameters.AddWithValue("@search", "%" + Search + "%");
            if (!string.IsNullOrWhiteSpace(Health)) cmd.Parameters.AddWithValue("@health", Health);
            if (EnclosureId != null) cmd.Parameters.AddWithValue("@enclosure", EnclosureId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                Rows.Add(new AnimalRow
                {
                    AnimalId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Species = reader.GetString(2),
                    ScientificName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Enclosure = reader.GetString(4),
                    Gender = reader.IsDBNull(5) ? null : reader.GetString(5),
                    DateOfBirth = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    HealthStatus = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }
        }

        // Enclosure names for the filter dropdown
        await using (var cmd = new MySqlCommand(
            "SELECT Enclosure_ID, Enclosure_Name FROM Enclosure ORDER BY Enclosure_Name", conn))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                Enclosures.Add(new EnclosureOption(reader.GetInt32(0), reader.GetString(1)));
            }
        }
    }

    public record EnclosureOption(int Id, string Name);

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
