using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages;

public class MyAccountModel : PageModel
{
    private readonly IConfiguration _config;

    public MyAccountModel(IConfiguration config)
    {
        _config = config;
    }

    // Read straight from the wristband (cookie)
    public string Username => User.Identity?.Name ?? "";
    public string Role => User.FindFirst(ClaimTypes.Role)?.Value ?? "";

    [BindProperty] public string CurrentPassword { get; set; } = "";
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // 1. Check the form
        if (string.IsNullOrWhiteSpace(CurrentPassword) || string.IsNullOrWhiteSpace(NewPassword))
        {
            ErrorMessage = "Please fill in all fields.";
            return Page();
        }
        if (NewPassword.Length < 8)
        {
            ErrorMessage = "New password must be at least 8 characters.";
            return Page();
        }
        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "New passwords don't match.";
            return Page();
        }
        if (NewPassword == CurrentPassword)
        {
            ErrorMessage = "New password must be different from your current one.";
            return Page();
        }

        // 2. Find out who's logged in
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int userId))
        {
            return RedirectToPage("/Login");
        }

        using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // 3. Check their current password is correct
        using var get = new MySqlCommand(
            "SELECT Password_Hash FROM User_Account WHERE User_ID = @id", conn);
        get.Parameters.AddWithValue("@id", userId);
        var hash = (string?)await get.ExecuteScalarAsync();

        if (hash == null || !BCrypt.Net.BCrypt.Verify(CurrentPassword, hash))
        {
            ErrorMessage = "Your current password is incorrect.";
            return Page();
        }

        // 4. Save the new password (hashed!)
        using var update = new MySqlCommand(
            "UPDATE User_Account SET Password_Hash = @hash WHERE User_ID = @id", conn);
        update.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(NewPassword));
        update.Parameters.AddWithValue("@id", userId);
        await update.ExecuteNonQueryAsync();

        SuccessMessage = "Your password was changed.";
        return Page();
    }
}