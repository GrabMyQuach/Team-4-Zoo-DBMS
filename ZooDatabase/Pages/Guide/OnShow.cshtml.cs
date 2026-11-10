using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;
using ZooDatabase.Pages.Care;

namespace ZooDatabase.Pages.Guide;

// Who's On Show: which animals guests can see today, by enclosure, and which ones are off exhibit and why,
// so a guide never promises an animal that isn't out.
[Authorize(Roles = GuideOptions.GuideRoles)]
public class OnShowModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    public OnShowModel(IConfiguration config, IWebHostEnvironment env) { _config = config; _env = env; }

    public record AnimalRow(int Id, string Name, string Species, string Enclosure, string? Location, string? OffReason, string? Photo);
    public record Area(string Enclosure, string? Location, List<AnimalRow> Animals);

    public List<AnimalRow> Off { get; set; } = new();
    public List<Area> Areas { get; set; } = new();
    public int OnShowCount => Areas.Sum(a => a.Animals.Count);

    public async Task OnGetAsync()
    {
        await using var conn = await CareData.OpenAsync(_config);
        await using var cmd = new MySqlCommand(@"
            SELECT a.Animal_ID, a.Animal_Name, s.Common_Name, e.Enclosure_Name, e.Location,
                   e.Status, e.Closed_Reason, a.Health_Status, a.Needs_Relocation
            FROM Animal a
            JOIN Species s ON s.Species_ID = a.Species_ID
            JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
            WHERE a.Is_Active = 1
            ORDER BY e.Enclosure_Name, s.Common_Name, a.Animal_Name", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var on = new List<AnimalRow>();
        while (await r.ReadAsync())
        {
            int id = r.GetInt32(0);
            string species = r.GetString(2);
            var reason = GuideData.OffExhibitReason(r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                                                   r.IsDBNull(7) ? null : r.GetString(7), r.GetBoolean(8));
            var row = new AnimalRow(id, r.GetString(1), species, r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                                    reason, PublicSite.AnimalPhoto(_env, id, species));
            if (reason == null) on.Add(row); else Off.Add(row);
        }
        Areas = on.GroupBy(a => (a.Enclosure, a.Location))
                  .Select(g => new Area(g.Key.Enclosure, g.Key.Location, g.ToList()))
                  .ToList();
    }
}
