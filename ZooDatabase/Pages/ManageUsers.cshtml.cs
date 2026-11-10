using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages;

[Authorize(Roles = "DBA")]
public class ManageUsersModel : PageModel
{
    private readonly IConfiguration _config;

    public ManageUsersModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public List<UserRow> Users { get; set; } = new();

    public async Task OnGetAsync()
    {
        using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        var sql = @"SELECT u.User_ID, u.Username, u.Role, u.Is_Active, u.Created_At, u.Last_Login,
                           CONCAT(e.First_Name, ' ', e.Last_Name) AS Employee_Name
                    FROM User_Account u LEFT JOIN Employee e ON e.Employee_ID = u.Employee_ID";
        if (!string.IsNullOrWhiteSpace(Search))
        {
            // Also match the names people see, so "manager" finds SuperUser accounts
            var roles = ZooDatabase.Data.RoleNames.Matching(Search.Trim());
            var roleList = string.Join(",", roles.Select(r => "'" + r + "'"));
            sql += " WHERE u.Username LIKE @search OR u.Role LIKE @search"
                   + " OR CONCAT(e.First_Name, ' ', e.Last_Name) LIKE @search"
                   + (roles.Length > 0 ? " OR u.Role IN (" + roleList + ")" : "");
        }
        sql += " ORDER BY u.User_ID";

        using var cmd = new MySqlCommand(sql, conn);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            cmd.Parameters.AddWithValue("@search", "%" + Search + "%");
        }

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Users.Add(new UserRow
            {
                UserId = reader.GetInt32("User_ID"),
                Username = reader.GetString("Username"),
                Role = reader.GetString("Role"),
                IsActive = reader.GetBoolean("Is_Active"),
                CreatedAt = reader.GetDateTime("Created_At"),
                LastLogin = reader.IsDBNull(reader.GetOrdinal("Last_Login"))
                    ? null
                    : (DateTime?)reader.GetDateTime("Last_Login"),
                EmployeeName = reader.IsDBNull(reader.GetOrdinal("Employee_Name"))
                    ? null
                    : reader.GetString("Employee_Name")
            });
        }
    }

    public class UserRow
    {
        public int UserId { get; set; }
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLogin { get; set; }
        public string? EmployeeName { get; set; }
    }
}