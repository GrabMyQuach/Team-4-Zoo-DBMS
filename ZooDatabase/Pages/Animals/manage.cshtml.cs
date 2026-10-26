
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Animals;

[Authorize(Roles = "DBA,SuperUser,Admin")]
public class ManageModel : PageModel
{
    private readonly IConfiguration _config;

    public ManageModel(IConfiguration config)
    {
        _config = config;
    }

    // Represents one animal record from MySQL
    public class AnimalRecord
    {
        public int AnimalId { get; set; }
        public string AnimalName { get; set; } = "";
        public int SpeciesId { get; set; }
        public int EnclosureId { get; set; }
        public string? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? HealthStatus { get; set; }
        public DateTime? DateAcquired { get; set; }
        public string? AcquisitionType { get; set; }
    }

    // Stores the animals retrieved from MySQL
    public List<AnimalRecord> Animals { get; set; } = new();

    // GET: Load all animals when the page opens
    public async Task OnGetAsync()
    {
        await LoadAnimalsAsync();
    }

    // Retrieve animal records from MySQL
    private async Task LoadAnimalsAsync()
    {
        Animals.Clear();

        await using var connection = new MySqlConnection(
            _config.GetConnectionString("ZooDb"));

        await connection.OpenAsync();

        string sql = @"
            SELECT Animal_ID, Animal_Name, Species_ID,
                   Enclosure_ID, Gender, Date_of_Birth,
                   Health_Status, Date_Acquired, Acquisition_Type
            FROM Animal
            WHERE Is_Active = 1
            ORDER BY Animal_ID";

        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            Animals.Add(new AnimalRecord
            {
                AnimalId = reader.GetInt32(0),
                AnimalName = reader.GetString(1),
                SpeciesId = reader.GetInt32(2),
                EnclosureId = reader.GetInt32(3),
                Gender = reader.IsDBNull(4)
                    ? null : reader.GetString(4),
                DateOfBirth = reader.IsDBNull(5)
                    ? null : reader.GetDateTime(5),
                HealthStatus = reader.IsDBNull(6)
                    ? null : reader.GetString(6),
                DateAcquired = reader.IsDBNull(7)
                    ? null : reader.GetDateTime(7),
                AcquisitionType = reader.IsDBNull(8)
                    ? null : reader.GetString(8)
            });
        }
    }

    // POST: Add a new animal to MySQL
    public async Task<IActionResult> OnPostAsync(
        string AnimalName,
        int SpeciesId,
        int EnclosureId,
        string? Gender,
        DateTime? DateOfBirth,
        string? HealthStatus,
        DateTime? DateAcquired,
        string? AcquisitionType)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(AnimalName) ||
            SpeciesId <= 0 || EnclosureId <= 0)
        {
            ModelState.AddModelError(
                "", "Please enter the required fields.");

            await LoadAnimalsAsync();
            return Page();
        }

        string sql = @"
            INSERT INTO Animal
            (Animal_Name, Species_ID, Enclosure_ID,
             Gender, Date_of_Birth, Health_Status,
             Date_Acquired, Acquisition_Type)
            VALUES
            (@name, @species, @enclosure,
             @gender, @birth, @health,
             @acquired, @acquisition)";

        await using var connection = new MySqlConnection(
            _config.GetConnectionString("ZooDb"));

        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@name", AnimalName);
        command.Parameters.AddWithValue("@species", SpeciesId);
        command.Parameters.AddWithValue("@enclosure", EnclosureId);
        command.Parameters.AddWithValue(
            "@gender", (object?)Gender ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@birth", (object?)DateOfBirth ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@health", (object?)HealthStatus ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@acquired", (object?)DateAcquired ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@acquisition", (object?)AcquisitionType ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();

        TempData["SuccessMessage"] = "Animal added successfully!";

        return RedirectToPage();
    }
    
    // POST: Soft delete an animal
    public async Task<IActionResult> OnPostDeleteAsync(int AnimalId)
    {
        if (AnimalId <= 0)
        {
            return BadRequest();
        }

        await using var connection = new MySqlConnection(
            _config.GetConnectionString("ZooDb"));

        await connection.OpenAsync();

        string sql = @"
            UPDATE Animal
            SET Is_Active = 0
            WHERE Animal_ID = @animalId
              AND Is_Active = 1";

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@animalId", AnimalId);

        int affectedRows = await command.ExecuteNonQueryAsync();

        TempData["SuccessMessage"] = affectedRows > 0
            ? "Animal deleted successfully!"
            : "Animal not found or already inactive.";

        return RedirectToPage();
    }


}


