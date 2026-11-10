using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Tickets;

// Tickets > Encounters: the schedule of public animal encounters (Animal_Encounter).
//
// How this fits with the triggers:
//   trg_encounter_guard       (BEFORE INSERT) refuses a booking for an animal that isn't cleared.
//                             Booking happens on EncounterNew, so the guard always runs.
//   trg_contagious_diagnosis  cancels an animal's upcoming encounters when it is quarantined.
//   trg_inspection_result     flags animals in an unsafe enclosure (Needs_Relocation) but leaves
//                             their bookings alone, so this page highlights those for staff to cancel.
// This page only changes Status / Guests_Attended / Cancel_Reason, which no trigger watches.
public class EncountersModel : PageModel
{
    private readonly IConfiguration _config;
    public EncountersModel(IConfiguration config) { _config = config; }

    [BindProperty(SupportsGet = true)] public string Show { get; set; } = "upcoming";
    [BindProperty(SupportsGet = true)] public int? Animal { get; set; }

    public bool CanEdit => SalesOptions.CanBookEncounter(User);
    public List<Row> Rows { get; set; } = new();
    public string? AnimalName { get; set; }
    public int Next7Days { get; set; }
    public int SeatsNext7Days { get; set; }
    public int NeedAttention { get; set; }
    public int ToClose { get; set; }

    public static readonly (string Key, string Label)[] Views =
    {
        ("upcoming", "Upcoming"), ("attention", "Needs attention"), ("toclose", "Waiting to be closed"),
        ("past", "Past"), ("cancelled", "Cancelled")
    };

    public record Row(int Id, DateTime Start, int Minutes, int AnimalId, string Animal, string Species,
                      int? AttractionId, string? Attraction, string? Handler, int? MaxGuests, int? Attended,
                      string Status, string? CancelReason, string? Problem)
    {
        public bool IsPast => Start.AddMinutes(Minutes) <= DateTime.Now;
    }

    // Why the animal can't do an encounter right now (same rules as trg_encounter_guard)
    private const string ProblemSql = @"
        CASE WHEN a.Is_Active = 0 THEN 'no longer at the zoo'
             WHEN EXISTS (SELECT 1 FROM Quarantine q WHERE q.Animal_ID = a.Animal_ID AND q.Status IN ('Active', 'Extended'))
                  THEN 'in quarantine'
             WHEN a.Needs_Relocation = 1 THEN 'waiting to be moved out of an unsafe enclosure'
             WHEN a.Public_Encounter_Eligible = 0 THEN 'suspended from encounters'
        END";

    public async Task OnGetAsync()
    {
        if (Views.All(v => v.Key != Show)) Show = "upcoming";
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        if (Animal != null)
        {
            await using var n = new MySqlCommand("SELECT Animal_Name FROM Animal WHERE Animal_ID = @a", conn);
            n.Parameters.AddWithValue("@a", Animal);
            AnimalName = await n.ExecuteScalarAsync() as string;
            if (AnimalName == null) Animal = null;
        }

        // Summary numbers (whole zoo)
        await using (var cmd = new MySqlCommand($@"
            SELECT
              SUM(ae.Status = 'Scheduled' AND ae.Scheduled_Start >= NOW() AND ae.Scheduled_Start < NOW() + INTERVAL 7 DAY),
              COALESCE(SUM(CASE WHEN ae.Status = 'Scheduled' AND ae.Scheduled_Start >= NOW()
                                 AND ae.Scheduled_Start < NOW() + INTERVAL 7 DAY THEN ae.Max_Guests END), 0),
              SUM(ae.Status = 'Scheduled' AND ae.Scheduled_Start >= NOW() AND ({ProblemSql}) IS NOT NULL),
              SUM(ae.Status = 'Scheduled' AND ae.Scheduled_Start + INTERVAL ae.Duration_Minutes MINUTE <= NOW())
            FROM Animal_Encounter ae JOIN Animal a ON a.Animal_ID = ae.Animal_ID", conn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            Next7Days = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0));
            SeatsNext7Days = Convert.ToInt32(r.GetValue(1));
            NeedAttention = r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2));
            ToClose = r.IsDBNull(3) ? 0 : Convert.ToInt32(r.GetValue(3));
        }

        string where = Show switch
        {
            "attention" => $"ae.Status = 'Scheduled' AND ae.Scheduled_Start >= NOW() AND ({ProblemSql}) IS NOT NULL",
            "toclose" => "ae.Status = 'Scheduled' AND ae.Scheduled_Start + INTERVAL ae.Duration_Minutes MINUTE <= NOW()",
            "past" => "ae.Scheduled_Start < NOW() AND ae.Status <> 'Cancelled'",
            "cancelled" => "ae.Status = 'Cancelled'",
            _ => "ae.Status = 'Scheduled' AND ae.Scheduled_Start + INTERVAL ae.Duration_Minutes MINUTE > NOW()"
        };
        string order = Show is "past" or "cancelled" or "toclose" ? "DESC" : "ASC";

        await using (var cmd = new MySqlCommand($@"
            SELECT ae.Encounter_ID, ae.Scheduled_Start, ae.Duration_Minutes, a.Animal_ID, a.Animal_Name, s.Common_Name,
                   ae.Attraction_ID, at.Attraction_Name, CONCAT(h.First_Name, ' ', h.Last_Name),
                   ae.Max_Guests, ae.Guests_Attended, ae.Status, ae.Cancel_Reason, {ProblemSql}
            FROM Animal_Encounter ae
            JOIN Animal a ON a.Animal_ID = ae.Animal_ID
            JOIN Species s ON s.Species_ID = a.Species_ID
            LEFT JOIN Attraction at ON at.Attraction_ID = ae.Attraction_ID
            LEFT JOIN Employee h ON h.Employee_ID = ae.Handler_Employee_ID
            WHERE {where} AND (@animal IS NULL OR ae.Animal_ID = @animal)
            ORDER BY ae.Scheduled_Start {order}, ae.Encounter_ID
            LIMIT 200", conn))
        {
            cmd.Parameters.AddWithValue("@animal", (object?)Animal ?? DBNull.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Rows.Add(new Row(r.GetInt32(0), r.GetDateTime(1), r.GetInt32(2), r.GetInt32(3), r.GetString(4), r.GetString(5),
                    r.IsDBNull(6) ? null : r.GetInt32(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
                    r.IsDBNull(9) ? null : r.GetInt32(9), r.IsDBNull(10) ? null : r.GetInt32(10), r.GetString(11),
                    r.IsDBNull(12) ? null : r.GetString(12), r.IsDBNull(13) ? null : r.GetString(13)));
        }
    }

    // Cancel a scheduled encounter (weather, handler sick, animal not cleared...)
    public async Task<IActionResult> OnPostCancelAsync(int encounterId, string? reason)
    {
        if (!CanEdit) return Forbid();
        reason = reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            TempData["Error"] = "Please give a reason for cancelling.";
            return Back();
        }
        if (reason.Length > 255) reason = reason[..255];

        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        string? animal = null;
        int? handler = null, animalId = null;
        DateTime start = default;
        await using (var look = new MySqlCommand(@"
            SELECT a.Animal_Name, ae.Handler_Employee_ID, ae.Scheduled_Start, a.Animal_ID
            FROM Animal_Encounter ae JOIN Animal a ON a.Animal_ID = ae.Animal_ID
            WHERE ae.Encounter_ID = @id AND ae.Status = 'Scheduled' FOR UPDATE", conn, tx))
        {
            look.Parameters.AddWithValue("@id", encounterId);
            await using var r = await look.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                animal = r.GetString(0);
                handler = r.IsDBNull(1) ? null : r.GetInt32(1);
                start = r.GetDateTime(2);
                animalId = r.GetInt32(3);
            }
        }
        if (animal == null)
        {
            TempData["Error"] = "That encounter isn't scheduled any more.";
            return Back();
        }

        await using (var cmd = new MySqlCommand(
            "UPDATE Animal_Encounter SET Status = 'Cancelled', Cancel_Reason = @reason WHERE Encounter_ID = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("@reason", reason);
            cmd.Parameters.AddWithValue("@id", encounterId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Let the handler know, the same way the quarantine trigger does
        if (handler != null)
        {
            await using var note = new MySqlCommand(@"
                INSERT INTO Staff_Notification (Employee_ID, Animal_ID, Priority, Message)
                VALUES (@emp, @animal, 'Important', @msg)", conn, tx);
            note.Parameters.AddWithValue("@emp", handler);
            note.Parameters.AddWithValue("@animal", animalId);
            note.Parameters.AddWithValue("@msg", $"Encounter with {animal} on {start:MMM d 'at' h:mm tt} was cancelled: {reason}");
            await note.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();

        TempData["Success"] = $"Encounter #{encounterId} with {animal} on {start:ddd, MMM d 'at' h:mm tt} was cancelled."
            + (handler != null ? " The handler was notified." : "");
        return Back();
    }

    // Close an encounter that has happened, with how many guests came
    public async Task<IActionResult> OnPostCompleteAsync(int encounterId, int? guests)
    {
        if (!CanEdit) return Forbid();
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        int? max = null;
        DateTime start;
        await using (var look = new MySqlCommand(
            "SELECT Max_Guests, Scheduled_Start FROM Animal_Encounter WHERE Encounter_ID = @id AND Status = 'Scheduled'", conn))
        {
            look.Parameters.AddWithValue("@id", encounterId);
            await using var r = await look.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                TempData["Error"] = "That encounter isn't scheduled any more.";
                return Back();
            }
            max = r.IsDBNull(0) ? null : r.GetInt32(0);
            start = r.GetDateTime(1);
        }

        if (start > DateTime.Now)
            TempData["Error"] = "That encounter hasn't started yet.";
        else if (guests == null || guests < 0 || (max != null && guests > max))
            TempData["Error"] = max == null ? "Enter how many guests came." : $"Enter how many guests came (0 to {max}).";
        if (TempData.ContainsKey("Error")) return Back();

        await using var cmd = new MySqlCommand(@"
            UPDATE Animal_Encounter SET Status = 'Completed', Guests_Attended = @guests
            WHERE Encounter_ID = @id AND Status = 'Scheduled'", conn);
        cmd.Parameters.AddWithValue("@guests", guests);
        cmd.Parameters.AddWithValue("@id", encounterId);
        await cmd.ExecuteNonQueryAsync();

        TempData["Success"] = $"Encounter #{encounterId} closed with {guests} {(guests == 1 ? "guest" : "guests")}.";
        return Back();
    }

    private IActionResult Back() =>
        Redirect($"/Tickets/Encounters?Show={Uri.EscapeDataString(Show)}" + (Animal != null ? $"&Animal={Animal}" : ""));
}
