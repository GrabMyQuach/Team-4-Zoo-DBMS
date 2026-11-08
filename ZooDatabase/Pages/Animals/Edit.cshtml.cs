using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Animals;

// One page for BOTH adding and editing:
//   /Animals/Edit      -> add a new animal
//   /Animals/Edit/5    -> edit animal #5
[Authorize(Roles = AnimalOptions.EditorRoles)]
public class EditModel : PageModel
{
    private readonly IConfiguration _config;

    public EditModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public AnimalInput Input { get; set; } = new();

    public List<DropdownOption> SpeciesOptions { get; set; } = new();
    public List<DropdownOption> EnclosureOptions { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public bool IsNew => Id == null;

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        if (!IsNew)
        {
            // Fill the form with the animal's current info
            await using var cmd = new MySqlCommand(@"
                SELECT Animal_Name, Species_ID, Enclosure_ID, Gender, Date_of_Birth,
                       Health_Status, Date_Acquired, Acquisition_Type
                FROM Animal WHERE Animal_ID = @id AND Is_Active = 1", conn);
            cmd.Parameters.AddWithValue("@id", Id);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return NotFound();

            Input = new AnimalInput
            {
                Name = reader.GetString(0),
                SpeciesId = reader.GetInt32(1),
                EnclosureId = reader.GetInt32(2),
                Gender = reader.IsDBNull(3) ? null : reader.GetString(3),
                DateOfBirth = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                HealthStatus = reader.IsDBNull(5) ? null : reader.GetString(5),
                DateAcquired = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                AcquisitionType = reader.IsDBNull(7) ? null : reader.GetString(7)
            };
        }
        else
        {
            Input.HealthStatus = "Healthy";
        }

        await LoadDropdownsAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // 1. Check the form makes sense before touching the database
        Input.Name = (Input.Name ?? "").Trim();
        var today = DateTime.Today;

        if (Input.Name.Length == 0)
            ErrorMessage = "Please enter the animal's name.";
        else if (Input.SpeciesId <= 0)
            ErrorMessage = "Please choose a species.";
        else if (Input.EnclosureId <= 0)
            ErrorMessage = "Please choose an enclosure.";
        else if (Input.DateOfBirth > today)
            ErrorMessage = "Date of birth can't be in the future.";
        else if (Input.DateAcquired > today)
            ErrorMessage = "Arrival date can't be in the future.";
        else if (Input.DateOfBirth != null && Input.DateAcquired != null && Input.DateAcquired < Input.DateOfBirth)
            ErrorMessage = "The animal can't arrive before it was born.";

        if (ErrorMessage != null)
        {
            await LoadDropdownsAsync(conn);
            return Page();
        }

        // 2. Save it. Tien's triggers run automatically here and may refuse
        //    (for example, if the enclosure is full), so we catch that and show the message.
        try
        {
            if (IsNew)
            {
                await using var cmd = new MySqlCommand(@"
                    INSERT INTO Animal (Animal_Name, Species_ID, Enclosure_ID, Gender, Date_of_Birth,
                                        Health_Status, Date_Acquired, Acquisition_Type)
                    VALUES (@name, @species, @enclosure, @gender, @birth, @health, @acquired, @acqType)", conn);
                AddParameters(cmd);
                await cmd.ExecuteNonQueryAsync();

                TempData["Success"] = $"{Input.Name} was added.";
                return Redirect($"/Animals/Details/{cmd.LastInsertedId}");
            }
            else
            {
                await using var cmd = new MySqlCommand(@"
                    UPDATE Animal
                    SET Animal_Name = @name, Species_ID = @species, Enclosure_ID = @enclosure,
                        Gender = @gender, Date_of_Birth = @birth, Health_Status = @health,
                        Date_Acquired = @acquired, Acquisition_Type = @acqType
                    WHERE Animal_ID = @id AND Is_Active = 1", conn);
                AddParameters(cmd);
                cmd.Parameters.AddWithValue("@id", Id);
                int changed = await cmd.ExecuteNonQueryAsync();
                if (changed == 0) return NotFound();

                TempData["Success"] = $"{Input.Name} was updated.";
                return Redirect($"/Animals/Details/{Id}");
            }
        }
        catch (MySqlException ex) when (ex.SqlState == "45000")
        {
            // 45000 = a custom error thrown by a trigger with SIGNAL
            ErrorMessage = ex.Message;
            await LoadDropdownsAsync(conn);
            return Page();
        }
    }

    private void AddParameters(MySqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("@name", Input.Name);
        cmd.Parameters.AddWithValue("@species", Input.SpeciesId);
        cmd.Parameters.AddWithValue("@enclosure", Input.EnclosureId);
        cmd.Parameters.AddWithValue("@gender", (object?)Input.Gender ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@birth", (object?)Input.DateOfBirth ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@health", (object?)Input.HealthStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@acquired", (object?)Input.DateAcquired ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@acqType", (object?)Input.AcquisitionType ?? DBNull.Value);
    }

    // Names for the dropdowns. The user sees names; the form secretly sends the ID.
    private async Task LoadDropdownsAsync(MySqlConnection conn)
    {
        SpeciesOptions.Clear();
        EnclosureOptions.Clear();

        await using (var cmd = new MySqlCommand(
            "SELECT Species_ID, Common_Name FROM Species ORDER BY Common_Name", conn))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                SpeciesOptions.Add(new DropdownOption(reader.GetInt32(0), reader.GetString(1)));
        }

        // Show how full each enclosure is, like "Lion Savanna (3 of 5 spots used)"
        await using (var cmd = new MySqlCommand(@"
            SELECT e.Enclosure_ID, e.Enclosure_Name, e.Capacity, COUNT(a.Animal_ID), e.Status
            FROM Enclosure e
            LEFT JOIN Animal a ON a.Enclosure_ID = e.Enclosure_ID AND a.Is_Active = 1
            GROUP BY e.Enclosure_ID, e.Enclosure_Name, e.Capacity, e.Status
            ORDER BY e.Enclosure_Name", conn))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int capacity = reader.GetInt32(2);
                int used = Convert.ToInt32(reader.GetValue(3));
                string status = reader.GetString(4);
                string note = used >= capacity ? ", full" : "";
                if (status != "Open") note += $", {status.ToLower()}";
                string label = $"{reader.GetString(1)} ({used} of {capacity} spots used{note})";
                EnclosureOptions.Add(new DropdownOption(reader.GetInt32(0), label));
            }
        }
    }

    public record DropdownOption(int Id, string Label);

    public class AnimalInput
    {
        public string Name { get; set; } = "";
        public int SpeciesId { get; set; }
        public int EnclosureId { get; set; }
        public string? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? HealthStatus { get; set; }
        public DateTime? DateAcquired { get; set; }
        public string? AcquisitionType { get; set; }
    }
}
