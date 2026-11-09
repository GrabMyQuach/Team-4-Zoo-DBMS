using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Register;

// Cashier home: three big buttons, one for each part of the job
[Authorize(Roles = SalesOptions.SalesRoles)]
public class IndexModel : PageModel
{
    private readonly IConfiguration _config;
    public IndexModel(IConfiguration config) { _config = config; }

    public string FirstName { get; set; } = "";

    public async Task OnGetAsync()
    {
        FirstName = User.Identity?.Name ?? "";
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return;

        await using var conn = await RegisterData.OpenAsync(_config);
        await using var cmd = new MySqlCommand(@"
            SELECT e.First_Name FROM User_Account u JOIN Employee e ON e.Employee_ID = u.Employee_ID
            WHERE u.User_ID = @id", conn);
        cmd.Parameters.AddWithValue("@id", userId);
        if (await cmd.ExecuteScalarAsync() is string name) FirstName = name;
    }
}
