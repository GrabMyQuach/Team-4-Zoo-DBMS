using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages;

public class LoginModel : PageModel
{
    private readonly IConfiguration _config;

    public LoginModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter your username and password.";
            return Page();
        }

        using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // Look up the user (the @username placeholder blocks SQL injection)
        using var cmd = new MySqlCommand(
            "SELECT User_ID, Password_Hash, Role FROM User_Account " +
            "WHERE Username = @username AND Is_Active = TRUE", conn);
        cmd.Parameters.AddWithValue("@username", Username);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        int userId = reader.GetInt32("User_ID");
        string hash = reader.GetString("Password_Hash");
        string role = reader.GetString("Role");
        await reader.CloseAsync();

        // Check the typed password against the stored hash
        if (!BCrypt.Net.BCrypt.Verify(Password, hash))
        {
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        // Record when they logged in
        using var update = new MySqlCommand(
            "UPDATE User_Account SET Last_Login = NOW() WHERE User_ID = @id", conn);
        update.Parameters.AddWithValue("@id", userId);
        await update.ExecuteNonQueryAsync();

        // Make the "wristband" (cookie) with their name and role
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, Username),
            new Claim(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        // Send them back where they were going, or to the home page
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return RedirectToPage("/Index");
    }
}