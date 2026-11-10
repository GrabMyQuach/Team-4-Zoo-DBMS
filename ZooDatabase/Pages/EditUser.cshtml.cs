using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages;

[Authorize(Roles = "DBA")]
public class EditUserModel : PageModel
{
    private readonly IConfiguration _config;

    public EditUserModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty] public string Role { get; set; } = "";
    [BindProperty] public bool IsActive { get; set; }
    [BindProperty] public int? EmployeeId { get; set; }

    // Employees this account can be linked to
    public List<ZooDatabase.Data.UserEmployees.Choice> Employees { get; set; } = new();
    [BindProperty] public string? NewPassword { get; set; }
    [BindProperty] public string? ConfirmPassword { get; set; }

    public string Username { get; set; } = "";
    public string? ErrorMessage { get; set; }

    // Is the DBA editing their own account?
    public bool IsSelf => User.FindFirst(ClaimTypes.NameIdentifier)?.Value == Id.ToString();

    public async Task<IActionResult> OnGetAsync()
    {
        using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        if (!await LoadUserAsync(conn, loadEditableFields: true))
            return NotFound();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        if (!await LoadUserAsync(conn, loadEditableFields: false))
            return NotFound();

        // 1. Validation
        if (!AddUserModel.Roles.Contains(Role))
        {
            ErrorMessage = "Please choose a valid role.";
            return Page();
        }
        if (IsSelf && (Role != "DBA" || !IsActive))
        {
            ErrorMessage = "You can't remove your own DBA role or deactivate your own account.";
            return Page();
        }
        ErrorMessage = ZooDatabase.Data.UserEmployees.Check(Employees, EmployeeId, Role);
        if (ErrorMessage != null) return Page();
        bool changingPassword = !string.IsNullOrWhiteSpace(NewPassword);
        if (changingPassword)
        {
            if (NewPassword!.Length < 8)
            {
                ErrorMessage = "New password must be at least 8 characters.";
                return Page();
            }
            if (NewPassword != ConfirmPassword)
            {
                ErrorMessage = "Passwords don't match.";
                return Page();
            }
        }

        // 2. Save role and active status
        using var update = new MySqlCommand(
            "UPDATE User_Account SET Role = @role, Is_Active = @active, Employee_ID = @emp WHERE User_ID = @id", conn);
        update.Parameters.AddWithValue("@role", Role);
        update.Parameters.AddWithValue("@emp", (object?)EmployeeId ?? DBNull.Value);
        update.Parameters.AddWithValue("@active", IsActive);
        update.Parameters.AddWithValue("@id", Id);
        await update.ExecuteNonQueryAsync();

        // 3. Reset the password, only if a new one was typed
        if (changingPassword)
        {
            using var pw = new MySqlCommand(
                "UPDATE User_Account SET Password_Hash = @hash WHERE User_ID = @id", conn);
            pw.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(NewPassword));
            pw.Parameters.AddWithValue("@id", Id);
            await pw.ExecuteNonQueryAsync();
        }

        TempData["Success"] = $"User '{Username}' was updated.";
        return RedirectToPage("/ManageUsers");
    }

    private async Task<bool> LoadUserAsync(MySqlConnection conn, bool loadEditableFields)
    {
        using var cmd = new MySqlCommand(
            "SELECT Username, Role, Is_Active, Employee_ID FROM User_Account WHERE User_ID = @id", conn);
        cmd.Parameters.AddWithValue("@id", Id);

        using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) return false;

            Username = reader.GetString("Username");
            if (loadEditableFields)
            {
                Role = reader.GetString("Role");
                IsActive = reader.GetBoolean("Is_Active");
                EmployeeId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            }
        }
        Employees = await ZooDatabase.Data.UserEmployees.LoadAsync(conn, Id);
        return true;
    }
}