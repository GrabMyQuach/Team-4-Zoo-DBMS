using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Care;

// Today's Feedings: every feeding due today for the keeper's animals, logged with one tap.
// Saves to Feeding_Log the same way as the Log Feeding page, so Tien's triggers still run
// (feed stock goes down; missed or refused meals create tasks and alert the vet).
[Authorize(Roles = CareOptions.KeeperRoles)]
public class FeedingsModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    public FeedingsModel(IConfiguration config, IWebHostEnvironment env) { _config = config; _env = env; }

    public record Feeding(int ScheduleId, TimeSpan Time, int AnimalId, string Animal, string Species, string Enclosure,
                          string Feed, decimal Quantity, string Unit, string? Logged, string? Photo);

    public List<Feeding> Feedings { get; set; } = new();
    public int Logged => Feedings.Count(f => f.Logged != null);
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await using var conn = await CareData.OpenAsync(_config);
        await LoadAsync(conn, await CareData.MeAsync(conn, User));
    }

    public async Task<IActionResult> OnPostLogAsync(int scheduleId, string status, decimal? amount)
    {
        await using var conn = await CareData.OpenAsync(_config);
        var me = await CareData.MeAsync(conn, User);
        await LoadAsync(conn, me);

        var f = Feedings.FirstOrDefault(x => x.ScheduleId == scheduleId);
        if (f == null) ErrorMessage = "That feeding isn't on your list.";
        else if (f.Logged != null) ErrorMessage = $"{f.Animal}'s {Clock(f.Time)} feeding was already logged as {f.Logged}.";
        else if (!AnimalOptions.FeedingStatuses.Contains(status)) ErrorMessage = "Choose what happened.";
        else if (status == "Partial" && (amount == null || amount <= 0 || amount >= f.Quantity))
            ErrorMessage = $"For a partial feeding, enter how much {f.Animal} ate (less than {f.Quantity:0.##} {f.Unit}).";
        if (ErrorMessage != null) return Page();

        decimal? given = status switch
        {
            "Completed" => f!.Quantity,
            "Partial" => amount,
            _ => null
        };
        try
        {
            await using var cmd = new MySqlCommand(@"
                INSERT INTO Feeding_Log (Schedule_ID, Feeding_Date, Status, Quantity_Given, Recorded_By_Employee_ID, Notes)
                VALUES (@schedule, @date, @status, @qty, @by, NULL)", conn);
            cmd.Parameters.AddWithValue("@schedule", scheduleId);
            cmd.Parameters.AddWithValue("@date", DateTime.Today);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@qty", (object?)given ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@by", (object?)me ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex)
        {
            ErrorMessage = ex.SqlState == "45000" ? ex.Message : "That feeding couldn't be saved. Please try again.";
            return Page();
        }

        var word = status switch { "Completed" => "ate it all", "Partial" => $"ate {given:0.##} {f!.Unit}", "Refused" => "refused it", _ => "missed it" };
        TempData["Done"] = $"Logged {f!.Animal}'s {Clock(f.Time)} feeding: {word}."
            + (status is "Refused" or "Missed" ? " The vet team is alerted if this keeps happening." : "");
        return Redirect("/Care/Feedings");
    }

    // Undo today's log for one of my feedings (a mis-tap), so it can be logged again
    public async Task<IActionResult> OnPostUndoAsync(int scheduleId)
    {
        await using var conn = await CareData.OpenAsync(_config);
        await LoadAsync(conn, await CareData.MeAsync(conn, User));
        if (Feedings.All(x => x.ScheduleId != scheduleId)) return Redirect("/Care/Feedings");

        await using var cmd = new MySqlCommand(
            "SELECT Feeding_Log_ID FROM Feeding_Log WHERE Schedule_ID = @s AND Feeding_Date = @today LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@s", scheduleId);
        cmd.Parameters.AddWithValue("@today", DateTime.Today);
        var logId = await cmd.ExecuteScalarAsync();
        if (logId != null)
            TempData["Done"] = await FeedingUndo.UndoAsync(conn, Convert.ToInt32(logId));
        return Redirect($"/Care/Feedings#feed-{scheduleId}");
    }

    public static string Clock(TimeSpan t) => DateTime.Today.Add(t).ToString("h:mm tt");

    private async Task LoadAsync(MySqlConnection conn, int? me)
    {
        Feedings.Clear();
        await using var cmd = new MySqlCommand($@"
            SELECT fs.Schedule_ID, fs.Feeding_Time, a.Animal_ID, a.Animal_Name, s.Common_Name, e.Enclosure_Name,
                   f.Feed_Name, fs.Quantity, fs.Unit,
                   (SELECT fl.Status FROM Feeding_Log fl WHERE fl.Schedule_ID = fs.Schedule_ID AND fl.Feeding_Date = @today LIMIT 1)
            FROM Feeding_Schedule fs
            JOIN Animal a ON a.Animal_ID = fs.Animal_ID
            JOIN Species s ON s.Species_ID = a.Species_ID
            JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
            JOIN Feed f ON f.Feed_ID = fs.Feed_ID
            WHERE fs.Is_Active = 1 AND a.Is_Active = 1 AND {CareData.MyAnimalsSql(User)}
            ORDER BY fs.Feeding_Time, e.Enclosure_Name, a.Animal_Name", conn);
        cmd.Parameters.AddWithValue("@me", me ?? 0);
        cmd.Parameters.AddWithValue("@today", DateTime.Today);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var animalId = r.GetInt32(2);
            var species = r.GetString(4);
            Feedings.Add(new Feeding(r.GetInt32(0), r.GetTimeSpan(1), animalId, r.GetString(3), species, r.GetString(5),
                r.GetString(6), r.GetDecimal(7), r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9),
                PublicSite.AnimalPhoto(_env, animalId, species)));
        }
    }
}
