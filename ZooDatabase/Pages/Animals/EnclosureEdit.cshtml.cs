using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Animals;

// Add a new enclosure (/Animals/EnclosureEdit) or edit one (/Animals/EnclosureEdit/3). DBA only.
[Authorize(Roles = "DBA")]
public class EnclosureEditModel : PageModel
{
    private readonly IConfiguration _config;

    public EnclosureEditModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public EnclosureInput Input { get; set; } = new();

    public List<string> ExistingTypes { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public bool IsNew => Id == null;

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        if (!IsNew)
        {
            await using var cmd = new MySqlCommand(@"
                SELECT Enclosure_Name, Enclosure_Type, Location, Capacity, Status, Closed_Reason
                FROM Enclosure WHERE Enclosure_ID = @id", conn);
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();

            Input = new EnclosureInput
            {
                Name = r.GetString(0),
                Type = r.IsDBNull(1) ? null : r.GetString(1),
                Location = r.IsDBNull(2) ? null : r.GetString(2),
                Capacity = r.GetInt32(3),
                Status = r.GetString(4),
                ClosedReason = r.IsDBNull(5) ? null : r.GetString(5)
            };
        }

        await LoadTypesAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        Input.Name = (Input.Name ?? "").Trim();
        Input.Type = string.IsNullOrWhiteSpace(Input.Type) ? null : Input.Type.Trim();
        Input.Location = string.IsNullOrWhiteSpace(Input.Location) ? null : Input.Location.Trim();
        Input.ClosedReason = string.IsNullOrWhiteSpace(Input.ClosedReason) ? null : Input.ClosedReason.Trim();
        if (Input.Status == "Open") Input.ClosedReason = null;

        // How many animals live here now? Capacity can't go below that.
        int residents = 0;
        if (!IsNew)
        {
            await using var countCmd = new MySqlCommand(
                "SELECT COUNT(*) FROM Animal WHERE Enclosure_ID = @id AND Is_Active = 1", conn);
            countCmd.Parameters.AddWithValue("@id", Id);
            residents = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        if (Input.Name.Length == 0)
            ErrorMessage = "Please enter the enclosure name.";
        else if (Input.Capacity < 1)
            ErrorMessage = "Capacity must be at least 1.";
        else if (Input.Capacity < residents)
            ErrorMessage = $"{residents} animals live here, so capacity can't be lower than {residents}. Move some animals first.";
        else if (!EnclosuresModel.Statuses.Contains(Input.Status))
            ErrorMessage = "Please choose a valid status.";
        else if (Input.Status != "Open" && Input.ClosedReason == null)
            ErrorMessage = "Please explain why the enclosure isn't open.";

        if (ErrorMessage != null)
        {
            await LoadTypesAsync(conn);
            return Page();
        }

        try
        {
            if (IsNew)
            {
                await using var cmd = new MySqlCommand(@"
                    INSERT INTO Enclosure (Enclosure_Name, Enclosure_Type, Location, Capacity, Status,
                                           Closed_Reason, Status_Changed_At)
                    VALUES (@name, @type, @location, @capacity, @status, @reason,
                            IF(@status = 'Open', NULL, NOW()))", conn);
                AddParameters(cmd);
                await cmd.ExecuteNonQueryAsync();

                TempData["Success"] = $"{Input.Name} was added.";
                return Redirect($"/Animals/EnclosureDetails/{cmd.LastInsertedId}");
            }
            else
            {
                // Only stamp Status_Changed_At when the status actually changes
                await using var cmd = new MySqlCommand(@"
                    UPDATE Enclosure
                    SET Status_Changed_At = IF(Status <> @status, NOW(), Status_Changed_At),
                        Enclosure_Name = @name, Enclosure_Type = @type, Location = @location,
                        Capacity = @capacity, Status = @status, Closed_Reason = @reason
                    WHERE Enclosure_ID = @id", conn);
                AddParameters(cmd);
                cmd.Parameters.AddWithValue("@id", Id);
                int changed = await cmd.ExecuteNonQueryAsync();

                TempData["Success"] = $"{Input.Name} was updated.";
                return Redirect($"/Animals/EnclosureDetails/{Id}");
            }
        }
        catch (MySqlException ex) when (ex.SqlState == "45000")
        {
            // A trigger refused the change (for example, an invalid capacity)
            ErrorMessage = ex.Message;
            await LoadTypesAsync(conn);
            return Page();
        }
    }

    private void AddParameters(MySqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("@name", Input.Name);
        cmd.Parameters.AddWithValue("@type", (object?)Input.Type ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@location", (object?)Input.Location ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@capacity", Input.Capacity);
        cmd.Parameters.AddWithValue("@status", Input.Status);
        cmd.Parameters.AddWithValue("@reason", (object?)Input.ClosedReason ?? DBNull.Value);
    }

    // Existing types become suggestions while typing, so names stay consistent
    private async Task LoadTypesAsync(MySqlConnection conn)
    {
        ExistingTypes.Clear();
        await using var cmd = new MySqlCommand(
            "SELECT DISTINCT Enclosure_Type FROM Enclosure WHERE Enclosure_Type IS NOT NULL ORDER BY Enclosure_Type", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) ExistingTypes.Add(r.GetString(0));
    }

    public class EnclosureInput
    {
        public string Name { get; set; } = "";
        public string? Type { get; set; }
        public string? Location { get; set; }
        public int Capacity { get; set; } = 1;
        public string Status { get; set; } = "Open";
        public string? ClosedReason { get; set; }
    }
}
