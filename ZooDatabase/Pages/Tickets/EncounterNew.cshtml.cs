using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Tickets;

// Book a public animal encounter (Tickets > Attractions > Book Encounter).
//
// Saving inserts a row into Animal_Encounter. trg_encounter_guard checks it first and
// refuses the booking if the animal is quarantined, suspended from encounters, waiting
// to be moved out of an unsafe enclosure, or no longer at the zoo.
[Authorize(Roles = SalesOptions.EncounterRoles)]
public class EncounterNewModel : PageModel
{
    private readonly IConfiguration _config;

    public EncounterNewModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty] public EncounterInput Input { get; set; } = new();

    public List<AnimalOption> Animals { get; set; } = new();
    public List<Option> Attractions { get; set; } = new();
    public List<Option> Handlers { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(int? attraction, int? animal)
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        await LoadAsync(conn);

        var start = DateTime.Today.AddDays(1).AddHours(10);
        Input.ScheduledStart = start;
        Input.DurationMinutes = 30;
        Input.MaxGuests = 10;
        Input.AttractionId = attraction ?? 0;
        Input.AnimalId = animal ?? 0;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        await LoadAsync(conn);

        if (Animals.All(a => a.Id != Input.AnimalId))
            ErrorMessage = "Please choose an animal.";
        else if (Input.ScheduledStart < DateTime.Now)
            ErrorMessage = "The encounter has to be in the future.";
        else if (Input.DurationMinutes is < 5 or > 240)
            ErrorMessage = "Length must be between 5 and 240 minutes.";
        else if (Input.MaxGuests is < 1)
            ErrorMessage = "Max guests must be at least 1.";
        if (ErrorMessage != null) return Page();

        // The same animal or handler can't be in two encounters at once
        await using (var clash = new MySqlCommand(@"
            SELECT CONCAT(a.Animal_Name, ' is'), ae.Scheduled_Start FROM Animal_Encounter ae JOIN Animal a ON a.Animal_ID = ae.Animal_ID
            WHERE ae.Animal_ID = @animal AND ae.Status = 'Scheduled'
              AND ae.Scheduled_Start < @end AND ae.Scheduled_Start + INTERVAL ae.Duration_Minutes MINUTE > @start
            UNION ALL
            SELECT CONCAT(e.First_Name, ' ', e.Last_Name, ' is'), ae.Scheduled_Start FROM Animal_Encounter ae JOIN Employee e ON e.Employee_ID = ae.Handler_Employee_ID
            WHERE ae.Handler_Employee_ID = @handler AND ae.Status = 'Scheduled'
              AND ae.Scheduled_Start < @end AND ae.Scheduled_Start + INTERVAL ae.Duration_Minutes MINUTE > @start
            LIMIT 1", conn))
        {
            clash.Parameters.AddWithValue("@animal", Input.AnimalId);
            clash.Parameters.AddWithValue("@handler", Input.HandlerId);
            clash.Parameters.AddWithValue("@start", Input.ScheduledStart);
            clash.Parameters.AddWithValue("@end", Input.ScheduledStart.AddMinutes(Input.DurationMinutes));
            await using var r = await clash.ExecuteReaderAsync();
            if (await r.ReadAsync())
                ErrorMessage = $"{r.GetString(0)} already booked for an encounter at {r.GetDateTime(1):h:mm tt} on {r.GetDateTime(1):MMM d}. Pick another time.";
        }
        if (ErrorMessage != null) return Page();

        try
        {
            await using var cmd = new MySqlCommand(@"
                INSERT INTO Animal_Encounter (Animal_ID, Attraction_ID, Handler_Employee_ID, Scheduled_Start,
                                              Duration_Minutes, Max_Guests, Status)
                VALUES (@animal, @attraction, @handler, @start, @minutes, @guests, 'Scheduled')", conn);
            cmd.Parameters.AddWithValue("@animal", Input.AnimalId);
            cmd.Parameters.AddWithValue("@attraction", Input.AttractionId > 0 ? Input.AttractionId : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@handler", Input.HandlerId > 0 ? Input.HandlerId : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@start", Input.ScheduledStart);
            cmd.Parameters.AddWithValue("@minutes", Input.DurationMinutes);
            cmd.Parameters.AddWithValue("@guests", (object?)Input.MaxGuests ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();

            var animal = Animals.First(a => a.Id == Input.AnimalId);
            TempData["Success"] = $"Encounter with {animal.Name} booked for {Input.ScheduledStart:ddd, MMM d 'at' h:mm tt}.";
            return Redirect("/Tickets/Encounters");
        }
        catch (MySqlException ex) when (ex.SqlState == "45000")
        {
            // trg_encounter_guard said no
            var animal = Animals.First(a => a.Id == Input.AnimalId);
            ErrorMessage = $"Can't book {animal.Name}: {ex.Message}" +
                           (animal.Reason != null ? $" ({animal.Reason})." : ".");
            return Page();
        }
        catch (MySqlException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    private async Task LoadAsync(MySqlConnection conn)
    {
        Animals.Clear(); Attractions.Clear(); Handlers.Clear();

        // Every active animal, with the reason it can't do encounters (if any).
        // Unavailable animals stay in the list on purpose, so the database rule can be seen working.
        await using (var cmd = new MySqlCommand(@"
            SELECT a.Animal_ID, a.Animal_Name, s.Common_Name,
                   CASE
                     WHEN EXISTS (SELECT 1 FROM Quarantine q WHERE q.Animal_ID = a.Animal_ID AND q.Status IN ('Active', 'Extended'))
                          THEN 'in quarantine'
                     WHEN a.Needs_Relocation = 1 THEN 'waiting to be moved out of an unsafe enclosure'
                     WHEN a.Public_Encounter_Eligible = 0 THEN 'suspended from encounters'
                   END
            FROM Animal a JOIN Species s ON s.Species_ID = a.Species_ID
            WHERE a.Is_Active = 1
            ORDER BY a.Animal_Name", conn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Animals.Add(new AnimalOption(r.GetInt32(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT Attraction_ID, Attraction_Name FROM Attraction
            WHERE Status = 'Open'
            ORDER BY (Attraction_Type = 'Animal Experience') DESC, Attraction_Name", conn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) Attractions.Add(new Option(r.GetInt32(0), r.GetString(1)));
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT e.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name)
            FROM Employee e WHERE e.Employee_ID IN (SELECT Employee_ID FROM Keeper)
            ORDER BY e.Last_Name, e.First_Name", conn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) Handlers.Add(new Option(r.GetInt32(0), r.GetString(1)));
        }
    }

    public record Option(int Id, string Label);
    public record AnimalOption(int Id, string Name, string Species, string? Reason);

    public class EncounterInput
    {
        public int AnimalId { get; set; }
        public int AttractionId { get; set; }
        public int HandlerId { get; set; }
        public DateTime ScheduledStart { get; set; }
        public int DurationMinutes { get; set; } = 30;
        public int? MaxGuests { get; set; }
    }
}
