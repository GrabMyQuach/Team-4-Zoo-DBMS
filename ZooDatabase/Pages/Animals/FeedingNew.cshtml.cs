using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Animals;

// Log one scheduled feeding for an animal:
//   /Animals/FeedingNew/5  -> log a feeding for animal #5
//
// Saving inserts a row into Feeding_Log, and two triggers run automatically:
//   trg_feeding_consumption  Completed/Partial: takes the amount out of feed stock,
//                            and drafts a restock order if stock drops to the reorder level
//   trg_feeding_problem      Missed: urgent make-up feeding task + keeper alert
//                            2+ missed/refused in 3 days: vet welfare check
// This page reports what they did.
[Authorize(Roles = AnimalOptions.FeedingEditorRoles)]
public class FeedingNewModel : PageModel
{
    private readonly IConfiguration _config;

    public FeedingNewModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty] public FeedingInput Input { get; set; } = new();

    public string AnimalName { get; set; } = "";
    public List<ScheduleOption> Schedules { get; set; } = new();
    public List<EmployeeOption> Keepers { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int? schedule)
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        if (!await LoadAnimalAsync(conn)) return NotFound();
        await LoadDropdownsAsync(conn);

        // Start with today, "Completed", and the first feeding not yet logged today
        Input.FeedingDate = DateTime.Today;
        Input.Status = "Completed";
        var pick = Schedules.FirstOrDefault(s => s.Id == schedule)
                   ?? Schedules.FirstOrDefault(s => !s.LoggedToday)
                   ?? Schedules.FirstOrDefault();
        if (pick != null)
        {
            Input.ScheduleId = pick.Id;
            Input.QuantityGiven = pick.Quantity;
            Input.RecordedBy = pick.KeeperId ?? 0;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        if (!await LoadAnimalAsync(conn)) return NotFound();
        await LoadDropdownsAsync(conn);

        // 1. Check the form makes sense before touching the database
        Input.Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim();
        var schedule = Schedules.FirstOrDefault(s => s.Id == Input.ScheduleId);

        // Nothing was eaten for a missed or refused feeding
        if (Input.Status is "Missed" or "Refused") Input.QuantityGiven = null;

        if (schedule == null)
            ErrorMessage = "Please choose which feeding this is.";
        else if (!AnimalOptions.FeedingStatuses.Contains(Input.Status))
            ErrorMessage = "Please choose what happened.";
        else if (Input.FeedingDate > DateTime.Today)
            ErrorMessage = "You can't log a feeding for a future date.";
        else if (Input.QuantityGiven < 0)
            ErrorMessage = "Amount given can't be negative.";
        else if (Input.Status == "Partial" && Input.QuantityGiven == null)
            ErrorMessage = "Please enter how much was eaten for a partial feeding.";

        if (ErrorMessage != null) return Page();

        // 2. Save it. The triggers do the rest.
        try
        {
            int draftsBefore = await CountDraftOrdersAsync(conn, schedule!.FeedId);

            long logId;
            await using (var cmd = new MySqlCommand(@"
                INSERT INTO Feeding_Log (Schedule_ID, Feeding_Date, Status, Quantity_Given, Recorded_By_Employee_ID, Notes)
                VALUES (@schedule, @date, @status, @qty, @by, @notes)", conn))
            {
                cmd.Parameters.AddWithValue("@schedule", Input.ScheduleId);
                cmd.Parameters.AddWithValue("@date", Input.FeedingDate.Date);
                cmd.Parameters.AddWithValue("@status", Input.Status);
                cmd.Parameters.AddWithValue("@qty", (object?)Input.QuantityGiven ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@by", Input.RecordedBy > 0 ? Input.RecordedBy : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@notes", (object?)Input.Notes ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
                logId = cmd.LastInsertedId;
            }

            TempData["Success"] = await DescribeResultAsync(conn, logId, schedule, draftsBefore);
            return Redirect($"/Animals/Details/{Id}");
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            // The database allows one log per scheduled feeding per day (uq_feeding_once_per_day)
            ErrorMessage = $"The {schedule!.Label} feeding is already logged for {Input.FeedingDate:MMM d}. " +
                           "Each scheduled feeding can only be logged once per day.";
            return Page();
        }
        catch (MySqlException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    // Read back what the triggers did so staff can see it
    private async Task<string> DescribeResultAsync(MySqlConnection conn, long logId, ScheduleOption schedule, int draftsBefore)
    {
        var parts = new List<string> { $"{schedule.Label} feeding logged as {Input.Status} for {AnimalName}." };

        // Tasks created from this log (make-up feeding, vet welfare check)
        await using (var cmd = new MySqlCommand(@"
            SELECT t.Title, CONCAT(e.First_Name, ' ', e.Last_Name)
            FROM Care_Task t
            LEFT JOIN Employee e ON e.Employee_ID = t.Assigned_Employee_ID
            WHERE t.Source_Type = 'Feeding_Log' AND t.Source_ID = @log", conn))
        {
            cmd.Parameters.AddWithValue("@log", logId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                parts.Add($"Task created: \"{r.GetString(0)}\"" + (r.IsDBNull(1) ? "." : $" for {r.GetString(1)}."));
        }

        // Stock left, if this feeding used some
        if (Input.Status is "Completed" or "Partial")
        {
            await using var cmd = new MySqlCommand(
                "SELECT Quantity_In_Stock, Unit FROM Feed WHERE Feed_ID = @feed", conn);
            cmd.Parameters.AddWithValue("@feed", schedule.FeedId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync() && !r.IsDBNull(0))
                parts.Add($"{schedule.FeedName} left in stock: {r.GetDecimal(0):0.##} {r.GetString(1)}.");
        }

        if (await CountDraftOrdersAsync(conn, schedule.FeedId) > draftsBefore)
            parts.Add($"{schedule.FeedName} is running low, so a restock order was drafted for approval.");

        return string.Join(" ", parts);
    }

    private static async Task<int> CountDraftOrdersAsync(MySqlConnection conn, int feedId)
    {
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM Purchase_Order WHERE Feed_ID = @feed AND Status = 'Draft'", conn);
        cmd.Parameters.AddWithValue("@feed", feedId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<bool> LoadAnimalAsync(MySqlConnection conn)
    {
        await using var cmd = new MySqlCommand(
            "SELECT Animal_Name FROM Animal WHERE Animal_ID = @id AND Is_Active = 1", conn);
        cmd.Parameters.AddWithValue("@id", Id);
        var name = await cmd.ExecuteScalarAsync();
        if (name is not string s) return false;
        AnimalName = s;
        return true;
    }

    private async Task LoadDropdownsAsync(MySqlConnection conn)
    {
        Schedules.Clear();
        Keepers.Clear();

        // This animal's active feedings, and whether each is already logged today
        await using (var cmd = new MySqlCommand(@"
            SELECT fs.Schedule_ID, fs.Feeding_Time, fs.Feed_ID, f.Feed_Name, fs.Quantity, fs.Unit,
                   fs.Keeper_Employee_ID,
                   EXISTS (SELECT 1 FROM Feeding_Log fl
                           WHERE fl.Schedule_ID = fs.Schedule_ID AND fl.Feeding_Date = CURDATE())
            FROM Feeding_Schedule fs
            JOIN Feed f ON f.Feed_ID = fs.Feed_ID
            WHERE fs.Animal_ID = @id AND fs.Is_Active = 1
            ORDER BY fs.Feeding_Time", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var time = DateTime.Today.Add(r.GetTimeSpan(1)).ToString("h:mm tt");
                Schedules.Add(new ScheduleOption
                {
                    Id = r.GetInt32(0),
                    FeedId = r.GetInt32(2),
                    FeedName = r.GetString(3),
                    Quantity = r.GetDecimal(4),
                    Unit = r.GetString(5),
                    KeeperId = r.IsDBNull(6) ? null : r.GetInt32(6),
                    LoggedToday = Convert.ToInt32(r.GetValue(7)) == 1,
                    Label = $"{time} {r.GetString(3)}"
                });
            }
        }

        // Who recorded it: keepers first, then vets
        await using (var cmd = new MySqlCommand(@"
            SELECT e.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name), e.Job_Position
            FROM Employee e
            WHERE e.Employee_ID IN (SELECT Employee_ID FROM Keeper)
               OR e.Employee_ID IN (SELECT Employee_ID FROM Veterinarian)
            ORDER BY (e.Employee_ID IN (SELECT Employee_ID FROM Keeper)) DESC, e.Last_Name, e.First_Name", conn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                string label = r.IsDBNull(2) ? r.GetString(1) : $"{r.GetString(1)} ({r.GetString(2)})";
                Keepers.Add(new EmployeeOption(r.GetInt32(0), label));
            }
        }
    }

    public record EmployeeOption(int Id, string Label);

    public class ScheduleOption
    {
        public int Id { get; set; }
        public int FeedId { get; set; }
        public string FeedName { get; set; } = "";
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = "";
        public int? KeeperId { get; set; }
        public bool LoggedToday { get; set; }
        public string Label { get; set; } = "";
    }

    public class FeedingInput
    {
        public int ScheduleId { get; set; }
        public DateTime FeedingDate { get; set; }
        public string Status { get; set; } = "Completed";
        public decimal? QuantityGiven { get; set; }
        public int RecordedBy { get; set; }
        public string? Notes { get; set; }
    }
}
