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

        var sql = "SELECT User_ID, Username, Role, Is_Active, Created_At, Last_Login FROM User_Account";
        if (!string.IsNullOrWhiteSpace(Search))
        {
            sql += " WHERE Username LIKE @search OR Role LIKE @search";
        }
        sql += " ORDER BY User_ID";

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
                    : (DateTime?)reader.GetDateTime("Last_Login")
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
    }
}