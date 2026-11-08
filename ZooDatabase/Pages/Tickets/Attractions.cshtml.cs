using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Tickets;

// Attractions with staffing, animal encounters, and open repairs
public class AttractionsModel : PageModel
{
    private readonly IConfiguration _config;

    public AttractionsModel(IConfiguration config)
    {
        _config = config;
    }

    public List<AttractionRow> Rows { get; set; } = new();
    public bool CanManage => SalesOptions.CanManage(User);

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            SELECT a.Attraction_ID, a.Attraction_Name, a.Attraction_Type, a.Location, a.Price, a.Status,
                   (SELECT COUNT(*) FROM Employee_Attraction ea WHERE ea.Attraction_ID = a.Attraction_ID) AS Staff,
                   (SELECT COUNT(*) FROM Ticket_Attraction ta WHERE ta.Attraction_ID = a.Attraction_ID) AS In_Tickets,
                   (SELECT COUNT(*) FROM Animal_Encounter e WHERE e.Attraction_ID = a.Attraction_ID
                      AND e.Status = 'Scheduled' AND e.Scheduled_Start BETWEEN NOW() AND NOW() + INTERVAL 7 DAY) AS Upcoming,
                   (SELECT COALESCE(SUM(e.Guests_Attended), 0) FROM Animal_Encounter e WHERE e.Attraction_ID = a.Attraction_ID
                      AND e.Status = 'Completed' AND e.Scheduled_Start >= NOW() - INTERVAL 30 DAY) AS Guests30,
                   (SELECT COUNT(*) FROM Maintenance m WHERE m.Attraction_ID = a.Attraction_ID
                      AND COALESCE(m.Status, 'Open') NOT IN ('Completed', 'Cancelled')) AS Open_Repairs
            FROM Attraction a
            ORDER BY a.Attraction_Name", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            Rows.Add(new AttractionRow
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                Type = r.IsDBNull(2) ? null : r.GetString(2),
                Location = r.IsDBNull(3) ? null : r.GetString(3),
                Price = r.IsDBNull(4) ? null : r.GetDecimal(4),
                Status = r.IsDBNull(5) ? null : r.GetString(5),
                Staff = Convert.ToInt32(r.GetValue(6)),
                InTickets = Convert.ToInt32(r.GetValue(7)),
                Upcoming = Convert.ToInt32(r.GetValue(8)),
                Guests30 = Convert.ToInt32(r.GetValue(9)),
                OpenRepairs = Convert.ToInt32(r.GetValue(10))
            });
        }
    }

    public class AttractionRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Type { get; set; }
        public string? Location { get; set; }
        public decimal? Price { get; set; }
        public string? Status { get; set; }
        public int Staff { get; set; }
        public int InTickets { get; set; }
        public int Upcoming { get; set; }
        public int Guests30 { get; set; }
        public int OpenRepairs { get; set; }
        // An open attraction with nobody assigned to run it needs attention
        public bool NeedsAttention => (Status == "Open" && Staff == 0) || OpenRepairs > 0;
    }
}
