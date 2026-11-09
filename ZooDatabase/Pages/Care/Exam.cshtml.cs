using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Care;

// Exam & History: find any animal, then record an exam (with weight) or open its health history.
[Authorize(Roles = CareOptions.VetRoles)]
public class ExamModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    public ExamModel(IConfiguration config, IWebHostEnvironment env) { _config = config; _env = env; }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public record Row(int Id, string Name, string Species, string Enclosure, string? Health, decimal? LastWeight, DateTime? LastExam, string? Photo);
    public List<Row> Animals { get; set; } = new();

    public async Task OnGetAsync()
    {
        bool searching = !string.IsNullOrWhiteSpace(Q);
        await using var conn = await CareData.OpenAsync(_config);
        await using var cmd = new MySqlCommand($@"
            SELECT a.Animal_ID, a.Animal_Name, s.Common_Name, e.Enclosure_Name, a.Health_Status,
                   (SELECT h.Weight_kg FROM Health_Record h WHERE h.Animal_ID = a.Animal_ID AND h.Weight_kg IS NOT NULL
                     ORDER BY h.Exam_Date DESC LIMIT 1),
                   (SELECT MAX(h.Exam_Date) FROM Health_Record h WHERE h.Animal_ID = a.Animal_ID)
            FROM Animal a
            JOIN Species s ON s.Species_ID = a.Species_ID
            JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
            WHERE a.Is_Active = 1
                  {(searching ? "AND (a.Animal_Name LIKE @q OR s.Common_Name LIKE @q OR e.Enclosure_Name LIKE @q)" : "")}
            ORDER BY a.Animal_Name", conn);
        if (searching) cmd.Parameters.AddWithValue("@q", "%" + Q!.Trim() + "%");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = r.GetInt32(0);
            var species = r.GetString(2);
            Animals.Add(new Row(id, r.GetString(1), species, r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetDecimal(5), r.IsDBNull(6) ? null : r.GetDateTime(6), PublicSite.AnimalPhoto(_env, id, species)));
        }
    }
}
