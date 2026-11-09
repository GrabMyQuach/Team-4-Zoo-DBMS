using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Animals;

// One enclosure's profile: who lives there, who looks after it, repairs, and inspections.
public class EnclosureDetailsModel : PageModel
{
    private readonly IConfiguration _config;

    public EnclosureDetailsModel(IConfiguration config)
    {
        _config = config;
    }

    public EnclosureInfo Enclosure { get; set; } = new();
    public List<ResidentRow> Residents { get; set; } = new();
    public List<KeeperRow> Keepers { get; set; } = new();
    public List<RepairRow> OpenRepairs { get; set; } = new();
    public List<InspectionRow> Inspections { get; set; } = new();

    public bool CanEdit => User.IsInRole("DBA");

    public bool CanInspect => AnimalOptions.CanInspect(User);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // 1. The enclosure itself
        await using (var cmd = new MySqlCommand(@"
            SELECT Enclosure_ID, Enclosure_Name, Enclosure_Type, Location, Capacity,
                   Status, Closed_Reason, Status_Changed_At
            FROM Enclosure WHERE Enclosure_ID = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();

            Enclosure = new EnclosureInfo
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                Type = Str(r, 2),
                Location = Str(r, 3),
                Capacity = r.GetInt32(4),
                Status = r.GetString(5),
                ClosedReason = Str(r, 6),
                StatusChangedAt = r.IsDBNull(7) ? null : r.GetDateTime(7)
            };
        }

        // 2. Animals living here
        await using (var cmd = new MySqlCommand(@"
            SELECT a.Animal_ID, a.Animal_Name, s.Common_Name, a.Gender, a.Health_Status
            FROM Animal a
            JOIN Species s ON s.Species_ID = a.Species_ID
            WHERE a.Enclosure_ID = @id AND a.Is_Active = 1
            ORDER BY a.Animal_Name", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                Residents.Add(new ResidentRow
                {
                    AnimalId = r.GetInt32(0),
                    Name = r.GetString(1),
                    Species = r.GetString(2),
                    Gender = Str(r, 3),
                    HealthStatus = Str(r, 4)
                });
            }
        }

        // 3. Keepers currently assigned
        await using (var cmd = new MySqlCommand(@"
            SELECT e.First_Name, e.Last_Name, k.Experience_Level, ke.Assignment_Start_Date, e.Phone_Number
            FROM Keeper_Enclosure ke
            JOIN Keeper k ON k.Employee_ID = ke.Employee_ID
            JOIN Employee e ON e.Employee_ID = ke.Employee_ID
            WHERE ke.Enclosure_ID = @id
              AND (ke.Assignment_Start_Date IS NULL OR ke.Assignment_Start_Date <= CURDATE())
              AND (ke.Assignment_End_Date IS NULL OR ke.Assignment_End_Date >= CURDATE())
            ORDER BY e.Last_Name", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                Keepers.Add(new KeeperRow
                {
                    Name = r.GetString(0) + " " + r.GetString(1),
                    Experience = Str(r, 2),
                    Since = r.IsDBNull(3) ? null : r.GetDateTime(3),
                    Phone = Str(r, 4)
                });
            }
        }

        // 4. Unfinished maintenance, most urgent first
        await using (var cmd = new MySqlCommand(@"
            SELECT m.Description, m.Priority, m.Status, m.Request_Date, CONCAT(e.First_Name, ' ', e.Last_Name)
            FROM Maintenance m
            LEFT JOIN Employee e ON e.Employee_ID = m.Employee_ID
            WHERE m.Enclosure_ID = @id AND (m.Status IS NULL OR m.Status <> 'Completed')
            ORDER BY FIELD(m.Priority, 'Emergency', 'High', 'Normal', 'Low'), m.Request_Date", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                OpenRepairs.Add(new RepairRow
                {
                    Description = Str(r, 0),
                    Priority = r.GetString(1),
                    Status = Str(r, 2),
                    Requested = r.IsDBNull(3) ? null : r.GetDateTime(3),
                    AssignedTo = Str(r, 4)
                });
            }
        }

        // 5. The 5 most recent habitat inspections
        await using (var cmd = new MySqlCommand(@"
            SELECT hi.Inspection_Date, hi.Result, hi.Issue_Category, hi.Findings, CONCAT(e.First_Name, ' ', e.Last_Name)
            FROM Habitat_Inspection hi
            LEFT JOIN Employee e ON e.Employee_ID = hi.Inspector_Employee_ID
            WHERE hi.Enclosure_ID = @id
            ORDER BY hi.Inspection_Date DESC
            LIMIT 5", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                Inspections.Add(new InspectionRow
                {
                    Date = r.GetDateTime(0),
                    Result = r.GetString(1),
                    Category = r.GetString(2),
                    Findings = Str(r, 3),
                    Inspector = Str(r, 4)
                });
            }
        }

        return Page();
    }

    private static string? Str(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    public static string ResultBadge(string result) => result switch
    {
        "Pass" => "bg-success",
        "Critical" => "bg-danger",
        _ => "bg-warning text-dark"
    };

    public static string PriorityBadge(string priority) => priority switch
    {
        "Emergency" => "bg-danger",
        "High" => "bg-warning text-dark",
        _ => "bg-light text-dark border"
    };

    public class EnclosureInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Type { get; set; }
        public string? Location { get; set; }
        public int Capacity { get; set; }
        public string Status { get; set; } = "";
        public string? ClosedReason { get; set; }
        public DateTime? StatusChangedAt { get; set; }
    }

    public class ResidentRow
    {
        public int AnimalId { get; set; }
        public string Name { get; set; } = "";
        public string Species { get; set; } = "";
        public string? Gender { get; set; }
        public string? HealthStatus { get; set; }
    }

    public class KeeperRow
    {
        public string Name { get; set; } = "";
        public string? Experience { get; set; }
        public DateTime? Since { get; set; }
        public string? Phone { get; set; }
    }

    public class RepairRow
    {
        public string? Description { get; set; }
        public string Priority { get; set; } = "";
        public string? Status { get; set; }
        public DateTime? Requested { get; set; }
        public string? AssignedTo { get; set; }
    }

    public class InspectionRow
    {
        public DateTime Date { get; set; }
        public string Result { get; set; } = "";
        public string Category { get; set; } = "";
        public string? Findings { get; set; }
        public string? Inspector { get; set; }
    }
}
