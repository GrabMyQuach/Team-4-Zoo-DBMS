using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Care;

// Needs Attention: every animal a vet should look at today, with the reasons:
// not healthy, in quarantine, refusing or missing meals (last 3 days), open vet tasks, or no checkup in a year.
[Authorize(Roles = CareOptions.VetRoles)]
public class AttentionModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    public AttentionModel(IConfiguration config, IWebHostEnvironment env) { _config = config; _env = env; }

    // The checks, as one query. Uses @recent (3 days ago) and @yearAgo.
    private const string Checks = @"
        SELECT a.Animal_ID, a.Animal_Name, s.Common_Name, e.Enclosure_Name, a.Health_Status AS hs,
               (SELECT MAX(h.Exam_Date) FROM Health_Record h WHERE h.Animal_ID = a.Animal_ID AND h.Record_Type = 'Routine Checkup') AS last_check,
               (SELECT COUNT(*) FROM Feeding_Log fl JOIN Feeding_Schedule fs ON fs.Schedule_ID = fl.Schedule_ID
                 WHERE fs.Animal_ID = a.Animal_ID AND fl.Status IN ('Refused', 'Missed') AND fl.Feeding_Date >= @recent) AS food,
               (SELECT COUNT(*) FROM Quarantine q WHERE q.Animal_ID = a.Animal_ID AND q.Status IN ('Active', 'Extended')) AS quarantined,
               (SELECT COUNT(*) FROM Care_Task t JOIN Veterinarian v ON v.Employee_ID = t.Assigned_Employee_ID
                 WHERE t.Animal_ID = a.Animal_ID AND t.Status IN ('Open', 'In Progress')) AS vet_tasks,
               (SELECT MAX(h.Exam_Date) FROM Health_Record h WHERE h.Animal_ID = a.Animal_ID) AS last_exam
        FROM Animal a
        JOIN Species s ON s.Species_ID = a.Species_ID
        JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
        WHERE a.Is_Active = 1
        HAVING hs NOT IN ('Healthy', 'Recovered') OR quarantined > 0 OR food >= 2 OR vet_tasks > 0
            OR last_check IS NULL OR last_check < @yearAgo";

    public const string CountSql = "SELECT COUNT(*) FROM (" + Checks + ") x";

    public record Row(int Id, string Name, string Species, string Enclosure, string? Health, List<(string Text, bool Serious)> Reasons,
                      DateTime? LastExam, string? Photo)
    {
        public int Rank => Reasons.Count(r => r.Serious) * 10 + Reasons.Count;
    }

    public List<Row> Animals { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = await CareData.OpenAsync(_config);
        await using var cmd = new MySqlCommand(Checks, conn);
        cmd.Parameters.AddWithValue("@recent", DateTime.Today.AddDays(-3));
        cmd.Parameters.AddWithValue("@yearAgo", DateTime.Today.AddYears(-1));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var health = r.IsDBNull(4) ? null : r.GetString(4);
            var lastCheck = r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5);
            int food = Convert.ToInt32(r.GetValue(6)), quarantined = Convert.ToInt32(r.GetValue(7)), tasks = Convert.ToInt32(r.GetValue(8));

            var reasons = new List<(string, bool)>();
            if (quarantined > 0) reasons.Add(("In quarantine", true));
            if (health is not null and not "Healthy" and not "Recovered" and not "Quarantined") reasons.Add((health, true));
            if (food >= 2) reasons.Add(($"Refused or missed {food} meals in 3 days", true));
            if (tasks > 0) reasons.Add(($"{tasks} open vet {(tasks == 1 ? "task" : "tasks")}", false));
            if (lastCheck == null) reasons.Add(("Never had a routine checkup", false));
            else if (lastCheck < DateTime.Today.AddYears(-1)) reasons.Add(($"Checkup overdue (last {lastCheck:MMM yyyy})", false));

            var id = r.GetInt32(0);
            var species = r.GetString(2);
            Animals.Add(new Row(id, r.GetString(1), species, r.GetString(3), health, reasons,
                r.IsDBNull(9) ? null : r.GetDateTime(9), PublicSite.AnimalPhoto(_env, id, species)));
        }
        Animals = Animals.OrderByDescending(a => a.Rank).ThenBy(a => a.Name).ToList();
    }
}
