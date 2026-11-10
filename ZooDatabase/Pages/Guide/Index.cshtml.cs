using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;
using ZooDatabase.Pages.Care;

namespace ZooDatabase.Pages.Guide;

// Home for tour guides: big cards for each part of the job, each with a live count.
[Authorize(Roles = GuideOptions.GuideRoles)]
public class IndexModel : PageModel
{
    private readonly IConfiguration _config;
    public IndexModel(IConfiguration config) { _config = config; }

    public int ToursToday { get; set; }
    public int ToursLeft { get; set; }
    public int OnShow { get; set; }
    public int AllAnimals { get; set; }
    public int SpeciesCount { get; set; }

    public async Task OnGetAsync()
    {
        await using var conn = await CareData.OpenAsync(_config);

        async Task<int> Count(string sql)
        {
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@today", DateTime.Today);
            cmd.Parameters.AddWithValue("@tomorrow", DateTime.Today.AddDays(1));
            cmd.Parameters.AddWithValue("@now", DateTime.Now);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        ToursToday = await Count(@"
            SELECT COUNT(*) FROM Animal_Encounter
            WHERE Scheduled_Start >= @today AND Scheduled_Start < @tomorrow AND Status <> 'Cancelled'");
        ToursLeft = await Count(@"
            SELECT COUNT(*) FROM Animal_Encounter
            WHERE Scheduled_Start >= @now AND Scheduled_Start < @tomorrow AND Status = 'Scheduled'");
        AllAnimals = await Count("SELECT COUNT(*) FROM Animal a WHERE a.Is_Active = 1");
        OnShow = await Count($@"
            SELECT COUNT(*) FROM Animal a JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
            WHERE a.Is_Active = 1 AND {GuideData.OnShowSql}");
        SpeciesCount = await Count("SELECT COUNT(DISTINCT a.Species_ID) FROM Animal a WHERE a.Is_Active = 1");
    }
}
