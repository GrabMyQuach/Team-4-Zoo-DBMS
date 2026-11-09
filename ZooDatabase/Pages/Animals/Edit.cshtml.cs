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
    public List<BreedingOption> BreedingOptions { get; set; } = new();
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

        // A newborn from a breeding event must be the same species as its mother
        if (ErrorMessage == null && IsNew && Input.BreedingId > 0)
        {
            await LoadDropdownsAsync(conn);
            var b = BreedingOptions.FirstOrDefault(o => o.Id == Input.BreedingId);
            if (b == null)
                ErrorMessage = "Please choose a breeding event from the list.";
            else if (b.SpeciesId != Input.SpeciesId)
                ErrorMessage = $"This breeding event is for {b.Species}, so the newborn's species must be {b.Species}.";
        }

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
                // Breeding_ID links a newborn to its breeding event. When it is set, the triggers
                //   trg_offspring_lineage  fill in mother, father and "Born at Zoo"
                //   trg_offspring_born     mark the event "Offspring Born", enroll the baby in the
                //                          conservation program, and create a neonatal exam + studbook task
                // (trg_auto_feeding_schedule skips newborns: they nurse first.)
                await using var cmd = new MySqlCommand(@"
                    INSERT INTO Animal (Animal_Name, Species_ID, Enclosure_ID, Gender, Date_of_Birth,
                                        Health_Status, Date_Acquired, Acquisition_Type, Breeding_ID)
                    VALUES (@name, @species, @enclosure, @gender, @birth, @health, @acquired, @acqType, @breeding)", conn);
                AddParameters(cmd);
                cmd.Parameters.AddWithValue("@breeding", Input.BreedingId > 0 ? Input.BreedingId : (object)DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
                long newId = cmd.LastInsertedId;

                TempData["Success"] = Input.BreedingId > 0
                    ? await DescribeNewbornAsync(conn, newId)
                    : $"{Input.Name} was added. A feeding schedule was created from the species' diet plan.";
                return Redirect($"/Animals/Details/{newId}");
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
        catch (MySqlException ex)
        {
            // 45000 = a custom error thrown by a trigger with SIGNAL
            ErrorMessage = ex.Message;
            await LoadDropdownsAsync(conn);
            return Page();
        }
    }

    // Read back what the offspring triggers did
    private async Task<string> DescribeNewbornAsync(MySqlConnection conn, long animalId)
    {
        var parts = new List<string>();
        await using (var cmd = new MySqlCommand(@"
            SELECT mom.Animal_Name, dad.Animal_Name, p.Program_Name
            FROM Animal a
            LEFT JOIN Animal mom ON mom.Animal_ID = a.Mother_ID
            LEFT JOIN Animal dad ON dad.Animal_ID = a.Father_ID
            LEFT JOIN Conservation_Record cr ON cr.Animal_ID = a.Animal_ID
            LEFT JOIN Conservation_Program p ON p.Program_ID = cr.Program_ID
            WHERE a.Animal_ID = @id LIMIT 1", conn))
        {
            cmd.Parameters.AddWithValue("@id", animalId);
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            string parents = (r.IsDBNull(0), r.IsDBNull(1)) switch
            {
                (false, false) => $" to {r.GetString(0)} and {r.GetString(1)}",
                (false, true) => $" to {r.GetString(0)}",
                _ => ""
            };
            parts.Add($"{Input.Name} was added as a newborn{parents}.");
            if (!r.IsDBNull(2)) parts.Add($"Enrolled in the {r.GetString(2)} program.");
        }

        await using (var cmd = new MySqlCommand(
            "SELECT Title FROM Care_Task WHERE Animal_ID = @id AND Status = 'Open' ORDER BY Task_ID", conn))
        {
            cmd.Parameters.AddWithValue("@id", animalId);
            await using var r = await cmd.ExecuteReaderAsync();
            var tasks = new List<string>();
            while (await r.ReadAsync()) tasks.Add($"\"{r.GetString(0)}\"");
            if (tasks.Count > 0) parts.Add("Tasks created: " + string.Join(", ", tasks) + ".");
        }
        return string.Join(" ", parts);
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
        BreedingOptions.Clear();

        // Breeding events a newborn can come from (not ones marked unsuccessful)
        await using (var cmd = new MySqlCommand(@"
            SELECT b.Breeding_ID, dam.Species_ID, s.Common_Name, dam.Animal_Name, sire.Animal_Name,
                   b.Expected_Birth_Date, b.Outcome
            FROM Breeding_Event b
            JOIN Animal dam ON dam.Animal_ID = b.Dam_ID
            JOIN Species s ON s.Species_ID = dam.Species_ID
            LEFT JOIN Animal sire ON sire.Animal_ID = b.Sire_ID
            WHERE b.Outcome <> 'Unsuccessful'
            ORDER BY (b.Outcome = 'Pending') DESC, b.Expected_Birth_Date DESC", conn))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string pair = reader.IsDBNull(4) ? reader.GetString(3) : $"{reader.GetString(3)} × {reader.GetString(4)}";
                string due = reader.IsDBNull(5) ? "" : $", due {reader.GetDateTime(5):MMM d, yyyy}";
                string label = $"{reader.GetString(2)}: {pair} ({reader.GetString(6).ToLower()}{due})";
                BreedingOptions.Add(new BreedingOption(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), label));
            }
        }

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
    public record BreedingOption(int Id, int SpeciesId, string Species, string Label);

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
        public int BreedingId { get; set; }
    }
}
