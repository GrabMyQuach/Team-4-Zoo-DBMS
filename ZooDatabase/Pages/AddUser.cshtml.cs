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

    public static readonly string[] Roles = { "ReadOnly", "Admin", "SuperUser", "DBA" };

    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";
    [BindProperty] public string Role { get; set; } = "ReadOnly";

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Username = Username.Trim();

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

        using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

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
            "INSERT INTO User_Account (Username, Password_Hash, Role) " +
            "VALUES (@username, @hash, @role)", conn);
        insert.Parameters.AddWithValue("@username", Username);
        insert.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(Password));
        insert.Parameters.AddWithValue("@role", Role);
        await insert.ExecuteNonQueryAsync();

        // 4. Go back to the users list with a success message
        TempData["Success"] = $"User '{Username}' was created.";
        return RedirectToPage("/ManageUsers");
    }
}