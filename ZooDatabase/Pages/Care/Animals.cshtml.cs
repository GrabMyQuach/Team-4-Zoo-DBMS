using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Care;

// My Animals: the keeper's animals as photo cards, sick or quarantined ones first.
[Authorize(Roles = CareOptions.KeeperRoles)]
public class MyAnimalsModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    public MyAnimalsModel(IConfiguration config, IWebHostEnvironment env) { _config = config; _env = env; }

    public record AnimalRow(int Id, string Name, string Species, string Enclosure, string? Health, int FedToday, int FeedingsToday, string? Photo);

    public List<AnimalRow> Animals { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = await CareData.OpenAsync(_config);
        var me = await CareData.MeAsync(conn, User);
        await using var cmd = new MySqlCommand($@"
            SELECT a.Animal_ID, a.Animal_Name, s.Common_Name, e.Enclosure_Name, a.Health_Status,
                   (SELECT COUNT(DISTINCT fl.Schedule_ID) FROM Feeding_Log fl JOIN Feeding_Schedule fs ON fs.Schedule_ID = fl.Schedule_ID
                     WHERE fs.Animal_ID = a.Animal_ID AND fs.Is_Active = 1 AND fl.Feeding_Date = @today),
                   (SELECT COUNT(*) FROM Feeding_Schedule fs WHERE fs.Animal_ID = a.Animal_ID AND fs.Is_Active = 1)
            FROM Animal a
            JOIN Species s ON s.Species_ID = a.Species_ID
            JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
            WHERE a.Is_Active = 1 AND {CareData.MyAnimalsSql(User)}
            ORDER BY FIELD(a.Health_Status, 'Quarantined', 'Under Observation', 'Recovering') DESC, e.Enclosure_Name, a.Animal_Name", conn);
        cmd.Parameters.AddWithValue("@me", me ?? 0);
        cmd.Parameters.AddWithValue("@today", DateTime.Today);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = r.GetInt32(0);
            var species = r.GetString(2);
            Animals.Add(new AnimalRow(id, r.GetString(1), species, r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                Convert.ToInt32(r.GetValue(5)), Convert.ToInt32(r.GetValue(6)), PublicSite.AnimalPhoto(_env, id, species)));
        }
    }
}
