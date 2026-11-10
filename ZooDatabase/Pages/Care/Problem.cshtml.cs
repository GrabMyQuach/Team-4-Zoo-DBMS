using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;
using ZooDatabase.Pages.Reports;

namespace ZooDatabase.Pages.Care;

// Report a Problem: a keeper describes an enclosure problem and it becomes an open work order (Maintenance),
// which shows up for the maintenance team and in the Habitat Safety report.
[Authorize(Roles = CareOptions.KeeperRoles + "," + GuideOptions.GuideRole)]
public class ProblemModel : PageModel
{
    private readonly IConfiguration _config;
    public ProblemModel(IConfiguration config) { _config = config; }

    [BindProperty] public int EnclosureId { get; set; }
    [BindProperty] public string Priority { get; set; } = "Normal";
    [BindProperty] public string? Description { get; set; }
    public string? ErrorMessage { get; set; }

    public List<ReportPageModel.Option> MyEnclosures { get; set; } = new();
    public List<ReportPageModel.Option> OtherEnclosures { get; set; } = new();
    public record OpenOrder(int Id, string Enclosure, string? Description, string Priority, string Status, DateTime? Requested);
    public List<OpenOrder> OpenOrders { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = await CareData.OpenAsync(_config);
        await LoadAsync(conn);
        EnclosureId = MyEnclosures.FirstOrDefault()?.Id ?? 0;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = await CareData.OpenAsync(_config);
        await LoadAsync(conn);

        Description = Description?.Trim();
        var enclosure = MyEnclosures.Concat(OtherEnclosures).FirstOrDefault(e => e.Id == EnclosureId);
        if (enclosure == null) ErrorMessage = "Choose the enclosure.";
        else if (string.IsNullOrEmpty(Description) || Description.Length < 5) ErrorMessage = "Describe the problem in a few words.";
        else if (!StaffOptions.MaintenancePriorities.Contains(Priority)) ErrorMessage = "Choose how urgent it is.";
        if (ErrorMessage != null) return Page();

        string reporter = User.Identity?.Name ?? "staff";
        var me = await CareData.MeAsync(conn, User);
        if (me != null)
        {
            await using var who = new MySqlCommand("SELECT CONCAT(First_Name, ' ', Last_Name) FROM Employee WHERE Employee_ID = @me", conn);
            who.Parameters.AddWithValue("@me", me);
            if (await who.ExecuteScalarAsync() is string n) reporter = n;
        }

        long id;
        try
        {
            await using var cmd = new MySqlCommand(@"
                INSERT INTO Maintenance (Description, Location, Request_Date, Completion_Date, Status, Cost,
                                         Employee_ID, Enclosure_ID, Attraction_ID, Stand_ID, GiftShop_ID, Priority)
                VALUES (@desc, @loc, @today, NULL, 'Open', NULL, NULL, @enc, NULL, NULL, NULL, @priority)", conn);
            cmd.Parameters.AddWithValue("@desc", $"{Description} (reported by {reporter})");
            cmd.Parameters.AddWithValue("@loc", enclosure!.Name);
            cmd.Parameters.AddWithValue("@today", DateTime.Today);
            cmd.Parameters.AddWithValue("@enc", enclosure.Id);
            cmd.Parameters.AddWithValue("@priority", Priority);
            await cmd.ExecuteNonQueryAsync();
            id = cmd.LastInsertedId;
        }
        catch (MySqlException ex)
        {
            ErrorMessage = ex.SqlState == "45000" ? ex.Message : "The report couldn't be saved. Please try again.";
            return Page();
        }

        TempData["Done"] = $"Thanks! Work order #{id} was sent to maintenance for {enclosure.Name}"
                           + (Priority == "Emergency" ? " as an emergency." : ".");
        return Redirect("/Care/Problem");
    }

    private async Task LoadAsync(MySqlConnection conn)
    {
        var me = await CareData.MeAsync(conn, User);
        await using (var cmd = new MySqlCommand($@"
            SELECT e.Enclosure_ID, e.Enclosure_Name,
                   EXISTS (SELECT 1 FROM Animal a WHERE a.Enclosure_ID = e.Enclosure_ID AND a.Is_Active = 1 AND {CareData.MyAnimalsSql(User)})
            FROM Enclosure e ORDER BY e.Enclosure_Name", conn))
        {
            cmd.Parameters.AddWithValue("@me", me ?? 0);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var opt = new ReportPageModel.Option(r.GetInt32(0), r.GetString(1));
                if (Convert.ToInt32(r.GetValue(2)) == 1 && !CareOptions.IsOffice(User)) MyEnclosures.Add(opt);
                else OtherEnclosures.Add(opt);
            }
        }

        // Open work orders for the keeper's enclosures, so they can see a problem was already reported
        var ids = (MyEnclosures.Count > 0 ? MyEnclosures : OtherEnclosures).Select(o => o.Id).ToList();
        if (ids.Count == 0) return;
        await using var orders = new MySqlCommand($@"
            SELECT m.Maintenance_ID, e.Enclosure_Name, m.Description, m.Priority, COALESCE(m.Status, 'Open'), m.Request_Date
            FROM Maintenance m JOIN Enclosure e ON e.Enclosure_ID = m.Enclosure_ID
            WHERE COALESCE(m.Status, 'Open') NOT IN ('Completed', 'Cancelled') AND m.Enclosure_ID IN ({string.Join(",", ids)})
            ORDER BY FIELD(m.Priority, 'Emergency', 'High', 'Normal', 'Low'), m.Request_Date DESC
            LIMIT 20", conn);
        await using var or = await orders.ExecuteReaderAsync();
        while (await or.ReadAsync())
            OpenOrders.Add(new OpenOrder(or.GetInt32(0), or.GetString(1), or.IsDBNull(2) ? null : or.GetString(2),
                or.IsDBNull(3) ? "Normal" : or.GetString(3), or.GetString(4), or.IsDBNull(5) ? null : or.GetDateTime(5)));
    }
}
