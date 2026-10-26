using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Staff;

public class MaintenanceModel : PageModel
{
    private readonly IConfiguration _config;

    public MaintenanceModel(IConfiguration config)
    {
        _config = config;
    }

    // "open" = only unfinished requests, "all" = every request
    [BindProperty(SupportsGet = true)]
    public string Show { get; set; } = "open";

    public List<MaintenanceRow> Rows { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // A request can be for an enclosure, attraction, food stand, OR gift shop.
        // COALESCE picks the first one that isn't empty, so we get one "Where" column.
        var sql = @"
            SELECT m.Maintenance_ID, m.Description,
                   COALESCE(enc.Enclosure_Name, att.Attraction_Name, fs.Stand_Name,
                            gs.GiftShop_Name, m.Location) AS Place,
                   m.Status, m.Request_Date, m.Completion_Date, m.Cost,
                   CONCAT(e.First_Name, ' ', e.Last_Name) AS Assigned_To
            FROM Maintenance m
            LEFT JOIN Enclosure enc ON enc.Enclosure_ID = m.Enclosure_ID
            LEFT JOIN Attraction att ON att.Attraction_ID = m.Attraction_ID
            LEFT JOIN Food_Stand fs ON fs.Stand_ID = m.Stand_ID
            LEFT JOIN Gift_Shop gs ON gs.GiftShop_ID = m.GiftShop_ID
            LEFT JOIN Employee e ON e.Employee_ID = m.Employee_ID";

        if (Show != "all")
        {
            sql += " WHERE m.Status IS NULL OR m.Status <> 'Completed'";
        }
        sql += " ORDER BY m.Request_Date DESC";

        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Rows.Add(new MaintenanceRow
            {
                Id = reader.GetInt32(0),
                Description = reader.IsDBNull(1) ? null : reader.GetString(1),
                Place = reader.IsDBNull(2) ? null : reader.GetString(2),
                Status = reader.IsDBNull(3) ? null : reader.GetString(3),
                RequestDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                CompletionDate = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                Cost = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                AssignedTo = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }
    }

    public class MaintenanceRow
    {
        public int Id { get; set; }
        public string? Description { get; set; }
        public string? Place { get; set; }
        public string? Status { get; set; }
        public DateTime? RequestDate { get; set; }
        public DateTime? CompletionDate { get; set; }
        public decimal? Cost { get; set; }
        public string? AssignedTo { get; set; }
    }
}