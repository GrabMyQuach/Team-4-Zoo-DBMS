using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Care;

// Home for zookeepers and vets: big cards for each part of the job, each with a live count.
[Authorize(Roles = CareOptions.KeeperRoles)]
public class IndexModel : PageModel
{
    private readonly IConfiguration _config;
    public IndexModel(IConfiguration config) { _config = config; }

    public string FirstName { get; set; } = "";
    public bool Keeper { get; set; }
    public bool Vet { get; set; }
    public int FeedingsLeft { get; set; }
    public int FeedingsToday { get; set; }
    public int MyTasks { get; set; }
    public int MyAnimals { get; set; }
    public int NeedAttention { get; set; }
    public int Quarantined { get; set; }

    public async Task OnGetAsync()
    {
        Keeper = CareOptions.ShowKeeperTools(User);
        Vet = CareOptions.ShowVetTools(User);
        FirstName = User.Identity?.Name ?? "";

        await using var conn = await CareData.OpenAsync(_config);
        var me = await CareData.MeAsync(conn, User);
        if (me != null)
        {
            await using var cmd = new MySqlCommand("SELECT First_Name FROM Employee WHERE Employee_ID = @me", conn);
            cmd.Parameters.AddWithValue("@me", me);
            if (await cmd.ExecuteScalarAsync() is string name) FirstName = name;
        }

        async Task<int> Count(string sql)
        {
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@me", me ?? 0);
            cmd.Parameters.AddWithValue("@today", DateTime.Today);
            cmd.Parameters.AddWithValue("@recent", DateTime.Today.AddDays(-3));
            cmd.Parameters.AddWithValue("@yearAgo", DateTime.Today.AddYears(-1));
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        var mine = CareData.MyAnimalsSql(User);
        var taskFilter = CareOptions.IsOffice(User) ? "1=1" : "t.Assigned_Employee_ID = @me";
        MyTasks = await Count($"SELECT COUNT(*) FROM Care_Task t WHERE t.Status IN ('Open', 'In Progress') AND {taskFilter}");

        if (Keeper)
        {
            FeedingsToday = await Count($@"
                SELECT COUNT(*) FROM Feeding_Schedule fs JOIN Animal a ON a.Animal_ID = fs.Animal_ID
                WHERE fs.Is_Active = 1 AND a.Is_Active = 1 AND {mine}");
            FeedingsLeft = FeedingsToday - await Count($@"
                SELECT COUNT(DISTINCT fs.Schedule_ID) FROM Feeding_Schedule fs JOIN Animal a ON a.Animal_ID = fs.Animal_ID
                JOIN Feeding_Log fl ON fl.Schedule_ID = fs.Schedule_ID AND fl.Feeding_Date = @today
                WHERE fs.Is_Active = 1 AND a.Is_Active = 1 AND {mine}");
            MyAnimals = await Count($"SELECT COUNT(*) FROM Animal a WHERE a.Is_Active = 1 AND {mine}");
        }
        if (Vet)
        {
            NeedAttention = await Count(AttentionModel.CountSql);
            Quarantined = await Count("SELECT COUNT(*) FROM Quarantine WHERE Status IN ('Active', 'Extended')");
        }
    }
}
