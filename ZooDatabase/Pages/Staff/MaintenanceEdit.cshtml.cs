using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Staff;

// One page for BOTH creating and updating a maintenance request:
//   /Staff/MaintenanceEdit      -> new request
//   /Staff/MaintenanceEdit/12   -> update request #12
//
// Marking an EMERGENCY repair on an enclosure as Completed fires the
// trg_emergency_repair_done trigger, which creates a re-inspection task.
// The enclosure reopens once that re-inspection is recorded as a Pass.
[Authorize(Roles = StaffOptions.MaintenanceEditorRoles)]
public class MaintenanceEditModel : PageModel
{
    private readonly IConfiguration _config;

    public MaintenanceEditModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public MaintenanceInput Input { get; set; } = new();

    public List<PlaceGroup> PlaceGroups { get; set; } = new();
    public List<Option> EmployeeOptions { get; set; } = new();
    public int? InspectionId { get; set; }
    public string? ErrorMessage { get; set; }

    public bool IsNew => Id == null;

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        if (!IsNew)
        {
            await using var cmd = new MySqlCommand(@"
                SELECT Description, Location, Request_Date, Completion_Date, Status, Cost, Employee_ID,
                       Enclosure_ID, Attraction_ID, Stand_ID, GiftShop_ID, Priority, Inspection_ID
                FROM Maintenance WHERE Maintenance_ID = @id", conn);
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();

            Input = new MaintenanceInput
            {
                Description = r.IsDBNull(0) ? "" : r.GetString(0),
                Location = r.IsDBNull(1) ? null : r.GetString(1),
                RequestDate = r.IsDBNull(2) ? null : r.GetDateTime(2),
                CompletionDate = r.IsDBNull(3) ? null : r.GetDateTime(3),
                Status = r.IsDBNull(4) ? "Open" : r.GetString(4),
                Cost = r.IsDBNull(5) ? null : r.GetDecimal(5),
                EmployeeId = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                Place = !r.IsDBNull(7) ? $"E:{r.GetInt32(7)}"
                      : !r.IsDBNull(8) ? $"A:{r.GetInt32(8)}"
                      : !r.IsDBNull(9) ? $"S:{r.GetInt32(9)}"
                      : !r.IsDBNull(10) ? $"G:{r.GetInt32(10)}"
                      : "",
                Priority = r.GetString(11)
            };
            InspectionId = r.IsDBNull(12) ? null : r.GetInt32(12);
        }
        else
        {
            Input.RequestDate = DateTime.Today;
            Input.Status = "Open";
            Input.Priority = "Normal";
        }

        await LoadDropdownsAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // 1. Check the form makes sense before touching the database
        Input.Description = (Input.Description ?? "").Trim();
        Input.Location = string.IsNullOrWhiteSpace(Input.Location) ? null : Input.Location.Trim();

        // A finished request gets today's date if none was entered
        if (Input.Status == "Completed" && Input.CompletionDate == null)
            Input.CompletionDate = DateTime.Today;

        var (enclosureId, attractionId, standId, giftShopId) = ParsePlace(Input.Place);

        if (Input.Description.Length == 0)
            ErrorMessage = "Please describe what needs to be done.";
        else if (!StaffOptions.MaintenanceStatuses.Contains(Input.Status))
            ErrorMessage = "Please choose a status.";
        else if (!StaffOptions.MaintenancePriorities.Contains(Input.Priority))
            ErrorMessage = "Please choose a priority.";
        else if (enclosureId == null && attractionId == null && standId == null && giftShopId == null && Input.Location == null)
            ErrorMessage = "Please choose where the work is, or type a location.";
        else if (Input.RequestDate > DateTime.Today)
            ErrorMessage = "Request date can't be in the future.";
        else if (Input.CompletionDate != null && Input.RequestDate != null && Input.CompletionDate < Input.RequestDate)
            ErrorMessage = "Completion date can't be before the request date.";
        else if (Input.Cost < 0)
            ErrorMessage = "Cost can't be negative.";

        if (ErrorMessage != null)
        {
            await LoadDropdownsAsync(conn);
            return Page();
        }

        // If no location was typed, use the chosen place's location (e.g. "Africa Trail")
        Input.Location ??= await PlaceLocationAsync(conn, enclosureId, attractionId, standId, giftShopId);

        // 2. Save it. Triggers run automatically and may refuse, so catch that and show the message.
        try
        {
            long maintenanceId;
            string? oldStatus = null;

            if (IsNew)
            {
                await using var cmd = new MySqlCommand(@"
                    INSERT INTO Maintenance (Description, Location, Request_Date, Completion_Date, Status, Cost,
                                             Employee_ID, Enclosure_ID, Attraction_ID, Stand_ID, GiftShop_ID, Priority)
                    VALUES (@desc, @loc, @req, @done, @status, @cost,
                            @emp, @enc, @att, @stand, @shop, @priority)", conn);
                AddParameters(cmd, enclosureId, attractionId, standId, giftShopId);
                await cmd.ExecuteNonQueryAsync();
                maintenanceId = cmd.LastInsertedId;
            }
            else
            {
                await using (var check = new MySqlCommand(
                    "SELECT Status FROM Maintenance WHERE Maintenance_ID = @id", conn))
                {
                    check.Parameters.AddWithValue("@id", Id);
                    var current = await check.ExecuteScalarAsync();
                    if (current == null) return NotFound();
                    oldStatus = current as string;
                }

                await using var cmd = new MySqlCommand(@"
                    UPDATE Maintenance
                    SET Description = @desc, Location = @loc, Request_Date = @req, Completion_Date = @done,
                        Status = @status, Cost = @cost, Employee_ID = @emp, Enclosure_ID = @enc,
                        Attraction_ID = @att, Stand_ID = @stand, GiftShop_ID = @shop, Priority = @priority
                    WHERE Maintenance_ID = @id", conn);
                AddParameters(cmd, enclosureId, attractionId, standId, giftShopId);
                cmd.Parameters.AddWithValue("@id", Id);
                await cmd.ExecuteNonQueryAsync();
                maintenanceId = Id!.Value;
            }

            TempData["Success"] = await DescribeResultAsync(conn, maintenanceId, oldStatus, enclosureId != null);
            return Redirect("/Staff/Maintenance");
        }
        catch (MySqlException ex)
        {
            // 45000 = a trigger refused; anything else (like a rule on Status) is shown too
            ErrorMessage = ex.Message;
            await LoadDropdownsAsync(conn);
            return Page();
        }
    }

    // Say what was saved, and what the trigger did if an emergency repair was just finished
    private async Task<string> DescribeResultAsync(MySqlConnection conn, long maintenanceId, string? oldStatus, bool isEnclosure)
    {
        string saved = IsNew ? $"Request #{maintenanceId} created." : $"Request #{maintenanceId} updated.";

        // The re-inspection trigger only applies to emergency repairs on an enclosure
        bool justCompleted = Input.Status == "Completed" && oldStatus != "Completed";
        if (!justCompleted || Input.Priority != "Emergency" || !isEnclosure) return saved;

        await using var cmd = new MySqlCommand(@"
            SELECT t.Title, CONCAT(e.First_Name, ' ', e.Last_Name)
            FROM Care_Task t
            LEFT JOIN Employee e ON e.Employee_ID = t.Assigned_Employee_ID
            WHERE t.Source_Type = 'Maintenance' AND t.Source_ID = @id
            ORDER BY t.Task_ID DESC LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@id", maintenanceId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return saved + " Other emergency repairs on this enclosure are still open, so no re-inspection yet.";

        string who = r.IsDBNull(1) ? "unassigned" : $"assigned to {r.GetString(1)}";
        return saved + $" Re-inspection task created: \"{r.GetString(0)}\" ({who}). " +
               "The enclosure reopens when that inspection is recorded as a Pass.";
    }

    private void AddParameters(MySqlCommand cmd, int? enc, int? att, int? stand, int? shop)
    {
        cmd.Parameters.AddWithValue("@desc", Input.Description);
        cmd.Parameters.AddWithValue("@loc", (object?)Input.Location ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@req", (object?)Input.RequestDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@done", (object?)Input.CompletionDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", Input.Status);
        cmd.Parameters.AddWithValue("@cost", (object?)Input.Cost ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@emp", Input.EmployeeId > 0 ? Input.EmployeeId : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@enc", (object?)enc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@att", (object?)att ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@stand", (object?)stand ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@shop", (object?)shop ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@priority", Input.Priority);
    }

    // The "Where" dropdown sends values like "E:3" (enclosure 3) or "A:2" (attraction 2)
    private static (int? enc, int? att, int? stand, int? shop) ParsePlace(string? place)
    {
        if (string.IsNullOrEmpty(place) || place.Length < 3 || place[1] != ':' ||
            !int.TryParse(place[2..], out int id))
            return (null, null, null, null);

        return place[0] switch
        {
            'E' => (id, null, null, null),
            'A' => (null, id, null, null),
            'S' => (null, null, id, null),
            'G' => (null, null, null, id),
            _ => (null, null, null, null)
        };
    }

    private static async Task<string?> PlaceLocationAsync(MySqlConnection conn, int? enc, int? att, int? stand, int? shop)
    {
        string? sql = enc != null ? "SELECT Location FROM Enclosure WHERE Enclosure_ID = @id"
                    : att != null ? "SELECT Location FROM Attraction WHERE Attraction_ID = @id"
                    : stand != null ? "SELECT Location FROM Food_Stand WHERE Stand_ID = @id"
                    : shop != null ? "SELECT Location FROM Gift_Shop WHERE GiftShop_ID = @id"
                    : null;
        if (sql == null) return null;

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", enc ?? att ?? stand ?? shop);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private async Task LoadDropdownsAsync(MySqlConnection conn)
    {
        PlaceGroups.Clear();
        EmployeeOptions.Clear();

        var sources = new (string Label, string Prefix, string Sql)[]
        {
            ("Enclosures", "E", "SELECT Enclosure_ID, Enclosure_Name FROM Enclosure ORDER BY Enclosure_Name"),
            ("Attractions", "A", "SELECT Attraction_ID, Attraction_Name FROM Attraction ORDER BY Attraction_Name"),
            ("Food stands", "S", "SELECT Stand_ID, Stand_Name FROM Food_Stand ORDER BY Stand_Name"),
            ("Gift shops", "G", "SELECT GiftShop_ID, GiftShop_Name FROM Gift_Shop ORDER BY GiftShop_Name")
        };
        foreach (var (label, prefix, sql) in sources)
        {
            var group = new PlaceGroup(label, new List<PlaceOption>());
            await using var cmd = new MySqlCommand(sql, conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                group.Places.Add(new PlaceOption($"{prefix}:{r.GetInt32(0)}", r.GetString(1)));
            PlaceGroups.Add(group);
        }

        // Facilities staff first, then everyone else
        await using (var cmd = new MySqlCommand(@"
            SELECT e.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name), e.Job_Position
            FROM Employee e
            LEFT JOIN Department d ON d.Department_ID = e.Department_ID
            ORDER BY (d.Department_Name LIKE 'Facilities%') DESC, e.Last_Name, e.First_Name", conn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                string label = r.IsDBNull(2) ? r.GetString(1) : $"{r.GetString(1)} ({r.GetString(2)})";
                EmployeeOptions.Add(new Option(r.GetInt32(0), label));
            }
        }
    }

    public record Option(int Id, string Label);
    public record PlaceOption(string Value, string Label);
    public record PlaceGroup(string Label, List<PlaceOption> Places);

    public class MaintenanceInput
    {
        public string Description { get; set; } = "";
        public string? Place { get; set; }
        public string? Location { get; set; }
        public string Priority { get; set; } = "Normal";
        public string Status { get; set; } = "Open";
        public int EmployeeId { get; set; }
        public DateTime? RequestDate { get; set; }
        public DateTime? CompletionDate { get; set; }
        public decimal? Cost { get; set; }
    }
}
