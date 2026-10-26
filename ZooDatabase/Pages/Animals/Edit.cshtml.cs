
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Animals;

[Authorize(Roles = "DBA,SuperUser,Admin")]
public class EditModel : PageModel
{
    private readonly IConfiguration _config;

    public EditModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty]
    public AnimalInput Animal { get; set; } = new();

    public class AnimalInput
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

    // Load the selected animal
    public async Task<IActionResult> OnGetAsync(int id)
    {
        await using var connection = new MySqlConnection(
            _config.GetConnectionString("ZooDb"));

        await connection.OpenAsync();

        string sql = @"
            SELECT Animal_ID, Animal_Name, Species_ID,
                   Enclosure_ID, Gender, Date_of_Birth,
                   Health_Status, Date_Acquired, Acquisition_Type
            FROM Animal
            WHERE Animal_ID = @id";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return NotFound();
        }

        Animal = new AnimalInput
        {
            AnimalId = reader.GetInt32(0),
            AnimalName = reader.GetString(1),
            SpeciesId = reader.GetInt32(2),
            EnclosureId = reader.GetInt32(3),
            Gender = reader.IsDBNull(4) ? null : reader.GetString(4),
            DateOfBirth = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
            HealthStatus = reader.IsDBNull(6) ? null : reader.GetString(6),
            DateAcquired = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            AcquisitionType = reader.IsDBNull(8) ? null : reader.GetString(8)
        };

        return Page();
    }

    // Save changes to MySQL
    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Animal.AnimalName) ||
            Animal.SpeciesId <= 0 ||
            Animal.EnclosureId <= 0)
        {
            ModelState.AddModelError(
                "", "Please complete all required fields.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await using var connection = new MySqlConnection(
            _config.GetConnectionString("ZooDb"));

        await connection.OpenAsync();

        string sql = @"
            UPDATE Animal
            SET Animal_Name = @name,
                Species_ID = @species,
                Enclosure_ID = @enclosure,
                Gender = @gender,
                Date_of_Birth = @birth,
                Health_Status = @health,
                Date_Acquired = @acquired,
                Acquisition_Type = @acquisition
            WHERE Animal_ID = @id";

        await using var command = new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue("@id", Animal.AnimalId);
        command.Parameters.AddWithValue("@name", Animal.AnimalName);
        command.Parameters.AddWithValue("@species", Animal.SpeciesId);
        command.Parameters.AddWithValue("@enclosure", Animal.EnclosureId);
        command.Parameters.AddWithValue(
            "@gender", (object?)Animal.Gender ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@birth", (object?)Animal.DateOfBirth ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@health", (object?)Animal.HealthStatus ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@acquired", (object?)Animal.DateAcquired ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@acquisition", (object?)Animal.AcquisitionType ?? DBNull.Value);

        int rowsUpdated = await command.ExecuteNonQueryAsync();

        if (rowsUpdated == 0)
        {
            return NotFound();
        }

        TempData["SuccessMessage"] = "Animal updated successfully!";

        return RedirectToPage("./Manage");
    }
}
