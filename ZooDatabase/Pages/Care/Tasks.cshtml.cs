using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Care;

// My Tasks: open care tasks assigned to me (office roles see everyone's), most urgent first, with a Done button.
[Authorize(Roles = CareOptions.KeeperRoles)]
public class TasksModel : PageModel
{
    private readonly IConfiguration _config;
    public TasksModel(IConfiguration config) { _config = config; }

    public record TaskRow(int Id, string Title, int? AnimalId, string? Animal, string? Priority, DateTime? Due, string Status, string? AssignedTo);

    public List<TaskRow> Tasks { get; set; } = new();
    public bool ShowAssigned => CareOptions.IsOffice(User);

    public async Task OnGetAsync()
    {
        await using var conn = await CareData.OpenAsync(_config);
        var me = await CareData.MeAsync(conn, User);
        await using var cmd = new MySqlCommand($@"
            SELECT t.Task_ID, t.Title, t.Animal_ID, a.Animal_Name, t.Priority, t.Due_At, t.Status,
                   CONCAT(e.First_Name, ' ', e.Last_Name)
            FROM Care_Task t
            LEFT JOIN Animal a ON a.Animal_ID = t.Animal_ID
            LEFT JOIN Employee e ON e.Employee_ID = t.Assigned_Employee_ID
            WHERE t.Status IN ('Open', 'In Progress') {(ShowAssigned ? "" : "AND t.Assigned_Employee_ID = @me")}
            ORDER BY (t.Due_At < @now) DESC, FIELD(t.Priority, 'Urgent', 'High', 'Normal', 'Low'), t.Due_At", conn);
        cmd.Parameters.AddWithValue("@me", me ?? 0);
        cmd.Parameters.AddWithValue("@now", DateTime.Now);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            Tasks.Add(new TaskRow(r.GetInt32(0), r.IsDBNull(1) ? "Task" : r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetDateTime(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7)));
    }

    public async Task<IActionResult> OnPostDoneAsync(int taskId)
    {
        await using var conn = await CareData.OpenAsync(_config);
        var me = await CareData.MeAsync(conn, User);
        await using var cmd = new MySqlCommand($@"
            UPDATE Care_Task SET Status = 'Completed', Completed_At = @now
            WHERE Task_ID = @id AND Status IN ('Open', 'In Progress')
                  {(CareOptions.IsOffice(User) ? "" : "AND Assigned_Employee_ID = @me")}", conn);
        cmd.Parameters.AddWithValue("@id", taskId);
        cmd.Parameters.AddWithValue("@me", me ?? 0);
        cmd.Parameters.AddWithValue("@now", DateTime.Now);
        int changed;
        try
        {
            changed = await cmd.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.SqlState == "45000")
        {
            TempData["Done"] = ex.Message;
            return Redirect("/Care/Tasks");
        }
        TempData["Done"] = changed == 1 ? "Task marked done." : "That task was already closed.";
        return Redirect("/Care/Tasks");
    }
}
