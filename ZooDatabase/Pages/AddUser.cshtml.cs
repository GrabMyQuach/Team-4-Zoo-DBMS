using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages;

[Authorize(Roles = "DBA")]
public class AddUserModel : PageModel
{
    private readonly IConfiguration _config;

    public AddUserModel(IConfiguration config)
    {
        _config = config;
    }

    // Every role an account can have (Data/RoleNames.cs also has the name shown for each)
    public static readonly string[] Roles = ZooDatabase.Data.RoleNames.Stored;

    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";
    [BindProperty] public string Role { get; set; } = "ReadOnly";
    [BindProperty] public int? EmployeeId { get; set; }

    public string? ErrorMessage { get; set; }

    // Employees this account can be linked to
    public List<ZooDatabase.Data.UserEmployees.Choice> Employees { get; set; } = new();

    public async Task OnGetAsync()
    {
        using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        Employees = await ZooDatabase.Data.UserEmployees.LoadAsync(conn);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Username = Username.Trim();

        using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        Employees = await ZooDatabase.Data.UserEmployees.LoadAsync(conn);

        // 1. Check the form is filled in correctly
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please fill in all fields.";
            return Page();
        }
        if (Password.Length < 8)
        {
            ErrorMessage = "Password must be at least 8 characters.";
            return Page();
        }
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Passwords don't match.";
            return Page();
        }
        if (!Roles.Contains(Role))
        {
            ErrorMessage = "Please choose a valid role.";
            return Page();
        }
        ErrorMessage = ZooDatabase.Data.UserEmployees.Check(Employees, EmployeeId, Role);
        if (ErrorMessage != null) return Page();

        // 2. Make sure the username isn't taken
        using var check = new MySqlCommand(
            "SELECT COUNT(*) FROM User_Account WHERE Username = @username", conn);
        check.Parameters.AddWithValue("@username", Username);
        var count = Convert.ToInt32(await check.ExecuteScalarAsync());
        if (count > 0)
        {
            ErrorMessage = "That username is already taken.";
            return Page();
        }

        // 3. Save the new user with a hashed password
        using var insert = new MySqlCommand(
            "INSERT INTO User_Account (Username, Password_Hash, Role, Employee_ID) " +
            "VALUES (@username, @hash, @role, @emp)", conn);
        insert.Parameters.AddWithValue("@username", Username);
        insert.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(Password));
        insert.Parameters.AddWithValue("@role", Role);
        insert.Parameters.AddWithValue("@emp", (object?)EmployeeId ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync();

        // 4. Go back to the users list with a success message
        TempData["Success"] = $"User '{Username}' was created.";
        return RedirectToPage("/ManageUsers");
    }
}