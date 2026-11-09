using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Care;

// Quarantines: who is in quarantine now (with a Release button) and who was released recently.
// Quarantines start automatically when a vet records a contagious exam (Tien's trigger).
// Releasing sets Status = 'Released', which fires trg_quarantine_released: it sets the end date,
// marks the animal Recovered, returns it to public encounters and tells the keepers.
[Authorize(Roles = CareOptions.VetRoles)]
public class QuarantinesModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    public QuarantinesModel(IConfiguration config, IWebHostEnvironment env) { _config = config; _env = env; }

    public record Row(int QuarantineId, int AnimalId, string Animal, string Species, string Enclosure, string? Reason,
                      DateTime? Start, DateTime? ExpectedEnd, DateTime? Ended, string? Status, string? Photo);

    public List<Row> Active { get; set; } = new();
    public List<Row> Released { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = await CareData.OpenAsync(_config);
        await using var cmd = new MySqlCommand(@"
            SELECT q.Quarantine_ID, a.Animal_ID, a.Animal_Name, s.Common_Name, e.Enclosure_Name, q.Reason,
                   q.Start_Date, q.Expected_End_Date, q.Actual_End_Date, q.Status
            FROM Quarantine q
            JOIN Animal a ON a.Animal_ID = q.Animal_ID
            JOIN Species s ON s.Species_ID = a.Species_ID
            JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
            WHERE q.Status IN ('Active', 'Extended') OR q.Actual_End_Date >= @since
            ORDER BY q.Start_Date DESC", conn);
        cmd.Parameters.AddWithValue("@since", DateTime.Today.AddDays(-60));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var animalId = r.GetInt32(1);
            var species = r.GetString(3);
            var row = new Row(r.GetInt32(0), animalId, r.GetString(2), species, r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetDateTime(6),
                r.IsDBNull(7) ? null : r.GetDateTime(7), r.IsDBNull(8) ? null : r.GetDateTime(8),
                r.IsDBNull(9) ? null : r.GetString(9), PublicSite.AnimalPhoto(_env, animalId, species));
            if (row.Status is "Active" or "Extended") Active.Add(row); else Released.Add(row);
        }
    }

    public async Task<IActionResult> OnPostReleaseAsync(int quarantineId)
    {
        await using var conn = await CareData.OpenAsync(_config);
        string? animal = null;
        await using (var who = new MySqlCommand(
            "SELECT a.Animal_Name FROM Quarantine q JOIN Animal a ON a.Animal_ID = q.Animal_ID WHERE q.Quarantine_ID = @q", conn))
        {
            who.Parameters.AddWithValue("@q", quarantineId);
            animal = await who.ExecuteScalarAsync() as string;
        }
        try
        {
            await using var cmd = new MySqlCommand(@"
                UPDATE Quarantine SET Status = 'Released'
                WHERE Quarantine_ID = @q AND Status IN ('Active', 'Extended')", conn);
            cmd.Parameters.AddWithValue("@q", quarantineId);
            var changed = await cmd.ExecuteNonQueryAsync();
            TempData["Done"] = changed == 0
                ? "That quarantine was already released."
                : $"{animal ?? "The animal"} was released from quarantine and marked Recovered. The keepers have been notified.";
        }
        catch (MySqlException ex)
        {
            TempData["Done"] = ex.SqlState == "45000" ? ex.Message : "The quarantine couldn't be released. Please try again.";
        }
        return Redirect("/Care/Quarantines");
    }
}
