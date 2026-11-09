using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Animals;

// Record a habitat inspection for one enclosure:
//   /Animals/InspectionNew/4  -> inspect enclosure #4
//
// Saving inserts a row into Habitat_Inspection, and trg_inspection_result runs automatically:
//   Critical -> emergency repair request, enclosure closed, every animal flagged to move,
//               a relocation task per animal for its keeper, keepers alerted
//   Pass     -> if the enclosure was closed and its emergency repairs are done:
//               enclosure reopened, relocation tasks cancelled, re-inspection task completed
// This page reports what it did.
[Authorize(Roles = AnimalOptions.InspectionRoles)]
public class InspectionNewModel : PageModel
{
    private readonly IConfiguration _config;

    public InspectionNewModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty] public InspectionInput Input { get; set; } = new();

    public string EnclosureName { get; set; } = "";
    public string EnclosureStatus { get; set; } = "";
    public int AnimalCount { get; set; }
    public List<EmployeeOption> Inspectors { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        if (!await LoadAsync(conn)) return NotFound();

        Input.InspectionDate = DateTime.Now.AddSeconds(-DateTime.Now.Second);
        Input.Result = "Pass";
        Input.IssueCategory = "None";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        if (!await LoadAsync(conn)) return NotFound();

        // 1. Check the form makes sense before touching the database
        Input.Findings = string.IsNullOrWhiteSpace(Input.Findings) ? null : Input.Findings.Trim();
        if (Input.Result == "Pass" && Input.IssueCategory != "None") Input.IssueCategory = "None";

        if (!AnimalOptions.InspectionResults.Contains(Input.Result))
            ErrorMessage = "Please choose a result.";
        else if (!AnimalOptions.IssueCategories.Contains(Input.IssueCategory))
            ErrorMessage = "Please choose an issue type.";
        else if (Input.Result != "Pass" && Input.IssueCategory == "None")
            ErrorMessage = "Please choose what kind of issue was found.";
        else if (Input.Result == "Critical" && Input.Findings == null)
            ErrorMessage = "Please describe the critical issue so the repair crew knows what to fix.";
        else if (Input.InspectionDate > DateTime.Now.AddMinutes(5))
            ErrorMessage = "The inspection date can't be in the future.";
        if (ErrorMessage != null) return Page();

        // 2. Save it. The trigger does the rest.
        string statusBefore = EnclosureStatus;
        try
        {
            long inspectionId;
            await using (var cmd = new MySqlCommand(@"
                INSERT INTO Habitat_Inspection (Enclosure_ID, Inspector_Employee_ID, Inspection_Date, Result, Issue_Category, Findings)
                VALUES (@enc, @inspector, @date, @result, @issue, @findings)", conn))
            {
                cmd.Parameters.AddWithValue("@enc", Id);
                cmd.Parameters.AddWithValue("@inspector", Input.InspectorId > 0 ? Input.InspectorId : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@date", Input.InspectionDate);
                cmd.Parameters.AddWithValue("@result", Input.Result);
                cmd.Parameters.AddWithValue("@issue", Input.IssueCategory);
                cmd.Parameters.AddWithValue("@findings", (object?)Input.Findings ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
                inspectionId = cmd.LastInsertedId;
            }

            TempData["Success"] = await DescribeResultAsync(conn, inspectionId, statusBefore);
            return Redirect($"/Animals/EnclosureDetails/{Id}");
        }
        catch (MySqlException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    // Read back what the trigger did so staff can see it
    private async Task<string> DescribeResultAsync(MySqlConnection conn, long inspectionId, string statusBefore)
    {
        var parts = new List<string> { $"{Input.Result} inspection recorded for {EnclosureName}." };

        string statusAfter = "";
        await using (var cmd = new MySqlCommand("SELECT Status FROM Enclosure WHERE Enclosure_ID = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            statusAfter = (await cmd.ExecuteScalarAsync()) as string ?? "";
        }

        if (Input.Result == "Critical")
        {
            await using var cmd = new MySqlCommand(@"
                SELECT (SELECT Maintenance_ID FROM Maintenance WHERE Inspection_ID = @insp ORDER BY Maintenance_ID LIMIT 1),
                       (SELECT COUNT(*) FROM Care_Task WHERE Source_Type = 'Habitat_Inspection' AND Source_ID = @insp)", conn);
            cmd.Parameters.AddWithValue("@insp", inspectionId);
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            if (statusAfter == "Closed") parts.Add("The enclosure is now CLOSED.");
            if (!r.IsDBNull(0)) parts.Add($"Emergency repair request #{r.GetInt64(0)} was created.");
            long tasks = Convert.ToInt64(r.GetValue(1));
            if (tasks > 0) parts.Add($"{tasks} animal(s) need to be moved; a relocation task was created for each one's keeper.");
        }
        else if (Input.Result == "Pass" && statusBefore == "Closed")
        {
            parts.Add(statusAfter == "Open"
                ? "The enclosure passed re-inspection and is OPEN again. Leftover relocation tasks were cancelled."
                : "The enclosure stays closed because it still has an emergency repair that isn't completed.");
        }
        return string.Join(" ", parts);
    }

    private async Task<bool> LoadAsync(MySqlConnection conn)
    {
        await using (var cmd = new MySqlCommand(@"
            SELECT e.Enclosure_Name, e.Status,
                   (SELECT COUNT(*) FROM Animal a WHERE a.Enclosure_ID = e.Enclosure_ID AND a.Is_Active = 1)
            FROM Enclosure e WHERE e.Enclosure_ID = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return false;
            EnclosureName = r.GetString(0);
            EnclosureStatus = r.GetString(1);
            AnimalCount = Convert.ToInt32(r.GetValue(2));
        }

        // Inspectors: Facilities staff first, then everyone else
        Inspectors.Clear();
        await using (var cmd = new MySqlCommand(@"
            SELECT e.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name), e.Job_Position
            FROM Employee e
            LEFT JOIN Department d ON d.Department_ID = e.Department_ID
            ORDER BY (d.Department_Name LIKE 'Facilities%') DESC, e.Last_Name, e.First_Name", conn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Inspectors.Add(new EmployeeOption(r.GetInt32(0),
                    r.IsDBNull(2) ? r.GetString(1) : $"{r.GetString(1)} ({r.GetString(2)})"));
        }
        return true;
    }

    public record EmployeeOption(int Id, string Label);

    public class InspectionInput
    {
        public DateTime InspectionDate { get; set; }
        public string Result { get; set; } = "Pass";
        public string IssueCategory { get; set; } = "None";
        public int InspectorId { get; set; }
        public string? Findings { get; set; }
    }
}
