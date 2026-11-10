using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;
using ZooDatabase.Pages.Care;

namespace ZooDatabase.Pages.Guide;

// Today's Tours: today's animal encounters in time order (with a "Done" button to record how many guests came),
// the rest of the week, and the attractions guests can be taken to.
[Authorize(Roles = GuideOptions.GuideRoles)]
public class ToursModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    public ToursModel(IConfiguration config, IWebHostEnvironment env) { _config = config; _env = env; }

    public record Tour(int Id, DateTime Start, int Minutes, int? MaxGuests, int? Guests, string Status, string? CancelReason,
                       int AnimalId, string Animal, string Species, string Enclosure, string? Attraction, string? Where,
                       string? Handler, string? Warning, string? Photo)
    {
        public DateTime End => Start.AddMinutes(Minutes);
        public bool CanFinish => Status == "Scheduled" && Start <= DateTime.Now && Start.Date == DateTime.Today;
    }
    public record Place(string Name, string? Type, string? Location, string? Status)
    {
        public bool IsOpen => Status == null || Status is "Open" or "Active" or "Operating";
    }

    public List<Tour> Today { get; set; } = new();
    public List<Tour> ThisWeek { get; set; } = new();
    public List<Place> Attractions { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = await CareData.OpenAsync(_config);
        await using (var cmd = new MySqlCommand(@"
            SELECT ae.Encounter_ID, ae.Scheduled_Start, ae.Duration_Minutes, ae.Max_Guests, ae.Guests_Attended, ae.Status, ae.Cancel_Reason,
                   a.Animal_ID, a.Animal_Name, s.Common_Name, e.Enclosure_Name, e.Status, e.Closed_Reason,
                   a.Health_Status, a.Needs_Relocation, a.Public_Encounter_Eligible,
                   at.Attraction_Name, COALESCE(at.Location, e.Location), CONCAT(h.First_Name, ' ', h.Last_Name)
            FROM Animal_Encounter ae
            JOIN Animal a ON a.Animal_ID = ae.Animal_ID
            JOIN Species s ON s.Species_ID = a.Species_ID
            JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
            LEFT JOIN Attraction at ON at.Attraction_ID = ae.Attraction_ID
            LEFT JOIN Employee h ON h.Employee_ID = ae.Handler_Employee_ID
            WHERE ae.Scheduled_Start >= @today AND ae.Scheduled_Start < @weekEnd
            ORDER BY ae.Scheduled_Start", conn))
        {
            cmd.Parameters.AddWithValue("@today", DateTime.Today);
            cmd.Parameters.AddWithValue("@weekEnd", DateTime.Today.AddDays(8));
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                string status = r.GetString(5);
                string? warning = null;
                if (status == "Scheduled")
                {
                    var reason = GuideData.OffExhibitReason(r.IsDBNull(11) ? null : r.GetString(11), r.IsDBNull(12) ? null : r.GetString(12),
                                                           r.IsDBNull(13) ? null : r.GetString(13), r.GetBoolean(14));
                    if (reason != null) warning = reason + ". Check with the keeper before this tour.";
                    else if (!r.GetBoolean(15)) warning = "This animal isn't cleared for public encounters right now. Check with the keeper.";
                }
                int animalId = r.GetInt32(7);
                string species = r.GetString(9);
                var tour = new Tour(r.GetInt32(0), r.GetDateTime(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetInt32(3),
                    r.IsDBNull(4) ? null : r.GetInt32(4), status, r.IsDBNull(6) ? null : r.GetString(6),
                    animalId, r.GetString(8), species, r.GetString(10), r.IsDBNull(16) ? null : r.GetString(16),
                    r.IsDBNull(17) ? null : r.GetString(17), r.IsDBNull(18) ? null : r.GetString(18), warning,
                    PublicSite.AnimalPhoto(_env, animalId, species));
                if (tour.Start.Date == DateTime.Today) Today.Add(tour);
                else if (tour.Status != "Cancelled" && ThisWeek.Count < 30) ThisWeek.Add(tour);
            }
        }

        await using var places = new MySqlCommand(
            "SELECT Attraction_Name, Attraction_Type, Location, Status FROM Attraction ORDER BY Attraction_Name", conn);
        await using var p = await places.ExecuteReaderAsync();
        while (await p.ReadAsync())
            Attractions.Add(new Place(p.GetString(0), p.IsDBNull(1) ? null : p.GetString(1),
                p.IsDBNull(2) ? null : p.GetString(2), p.IsDBNull(3) ? null : p.GetString(3)));
        Attractions = Attractions.OrderByDescending(a => a.IsOpen).ToList();
    }

    // "Done": the tour happened, save how many guests came
    public async Task<IActionResult> OnPostDoneAsync(int encounterId, int? guests)
    {
        await using var conn = await CareData.OpenAsync(_config);
        int? max = null;
        await using (var look = new MySqlCommand("SELECT Max_Guests FROM Animal_Encounter WHERE Encounter_ID = @id", conn))
        {
            look.Parameters.AddWithValue("@id", encounterId);
            var v = await look.ExecuteScalarAsync();
            if (v == null) { TempData["Done"] = "That tour wasn't found."; return Redirect("/Guide/Tours"); }
            if (v != DBNull.Value) max = Convert.ToInt32(v);
        }
        if (guests == null || guests < 0 || guests > (max ?? 500))
        {
            TempData["Done"] = max == null ? "Enter how many guests came." : $"Enter how many guests came (0 to {max}).";
            return Redirect("/Guide/Tours");
        }

        await using var cmd = new MySqlCommand(@"
            UPDATE Animal_Encounter SET Status = 'Completed', Guests_Attended = @guests
            WHERE Encounter_ID = @id AND Status = 'Scheduled'", conn);
        cmd.Parameters.AddWithValue("@guests", guests);
        cmd.Parameters.AddWithValue("@id", encounterId);
        try
        {
            var changed = await cmd.ExecuteNonQueryAsync();
            TempData["Done"] = changed == 1
                ? $"Tour marked done with {guests} {(guests == 1 ? "guest" : "guests")}."
                : "That tour was already closed.";
        }
        catch (MySqlException ex)
        {
            TempData["Done"] = ex.SqlState == "45000" ? ex.Message : "The tour couldn't be saved. Please try again.";
        }
        return Redirect("/Guide/Tours");
    }
}
