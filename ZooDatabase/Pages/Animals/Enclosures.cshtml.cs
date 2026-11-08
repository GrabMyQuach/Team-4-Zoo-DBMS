using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Animals;

// Enclosure list with a quick "health check" of each habitat. Each row opens its profile.
public class EnclosuresModel : PageModel
{
    private readonly IConfiguration _config;

    public EnclosuresModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Type { get; set; }

    public List<EnclosureRow> Rows { get; set; } = new();
    public List<string> Types { get; set; } = new();

    public static readonly string[] Statuses = { "Open", "Closed", "Under Maintenance" };

    // Only the DBA can add or edit enclosures
    public bool CanEdit => User.IsInRole("DBA");

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(Search)) where.Add("(e.Enclosure_Name LIKE @search OR e.Location LIKE @search)");
        if (!string.IsNullOrWhiteSpace(Status)) where.Add("e.Status = @status");
        if (!string.IsNullOrWhiteSpace(Type)) where.Add("e.Enclosure_Type = @type");

        // Each little SELECT in parentheses (a subquery) counts one thing for that enclosure
        var sql = @"
            SELECT e.Enclosure_ID, e.Enclosure_Name, e.Enclosure_Type, e.Location, e.Capacity, e.Status,
                   (SELECT COUNT(*) FROM Animal a
                     WHERE a.Enclosure_ID = e.Enclosure_ID AND a.Is_Active = 1) AS Animals,
                   (SELECT COUNT(*) FROM Animal a
                     WHERE a.Enclosure_ID = e.Enclosure_ID AND a.Is_Active = 1
                       AND a.Health_Status <> 'Healthy') AS Not_Healthy,
                   (SELECT COUNT(*) FROM Keeper_Enclosure ke
                     WHERE ke.Enclosure_ID = e.Enclosure_ID
                       AND (ke.Assignment_Start_Date IS NULL OR ke.Assignment_Start_Date <= CURDATE())
                       AND (ke.Assignment_End_Date IS NULL OR ke.Assignment_End_Date >= CURDATE())) AS Keepers,
                   (SELECT COUNT(*) FROM Maintenance m
                     WHERE m.Enclosure_ID = e.Enclosure_ID
                       AND (m.Status IS NULL OR m.Status <> 'Completed')) AS Open_Maintenance,
                   (SELECT hi.Result FROM Habitat_Inspection hi
                     WHERE hi.Enclosure_ID = e.Enclosure_ID
                     ORDER BY hi.Inspection_Date DESC LIMIT 1) AS Last_Inspection
            FROM Enclosure e";
        if (where.Count > 0) sql += " WHERE " + string.Join(" AND ", where);
        sql += " ORDER BY e.Enclosure_Name";

        await using (var cmd = new MySqlCommand(sql, conn))
        {
            if (!string.IsNullOrWhiteSpace(Search)) cmd.Parameters.AddWithValue("@search", "%" + Search + "%");
            if (!string.IsNullOrWhiteSpace(Status)) cmd.Parameters.AddWithValue("@status", Status);
            if (!string.IsNullOrWhiteSpace(Type)) cmd.Parameters.AddWithValue("@type", Type);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                Rows.Add(new EnclosureRow
                {
                    Id = r.GetInt32(0),
                    Name = r.GetString(1),
                    Type = r.IsDBNull(2) ? null : r.GetString(2),
                    Location = r.IsDBNull(3) ? null : r.GetString(3),
                    Capacity = r.GetInt32(4),
                    Status = r.GetString(5),
                    Animals = Convert.ToInt32(r.GetValue(6)),
                    NotHealthy = Convert.ToInt32(r.GetValue(7)),
                    Keepers = Convert.ToInt32(r.GetValue(8)),
                    OpenMaintenance = Convert.ToInt32(r.GetValue(9)),
                    LastInspection = r.IsDBNull(10) ? null : r.GetString(10)
                });
            }
        }

        // Enclosure types for the filter dropdown
        await using (var cmd = new MySqlCommand(
            "SELECT DISTINCT Enclosure_Type FROM Enclosure WHERE Enclosure_Type IS NOT NULL ORDER BY Enclosure_Type", conn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) Types.Add(r.GetString(0));
        }
    }

    public static string StatusBadge(string status) => status switch
    {
        "Open" => "bg-success",
        "Closed" => "bg-danger",
        _ => "bg-warning text-dark"
    };

    public class EnclosureRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Type { get; set; }
        public string? Location { get; set; }
        public int Capacity { get; set; }
        public string Status { get; set; } = "";
        public int Animals { get; set; }
        public int NotHealthy { get; set; }
        public int Keepers { get; set; }
        public int OpenMaintenance { get; set; }
        public string? LastInspection { get; set; }
    }
}
