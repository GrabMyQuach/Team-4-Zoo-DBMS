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
    // The person who uses this login (only the one that fits the role is kept)
    [BindProperty] public int? EmployeeId { get; set; }
    [BindProperty] public int? VolunteerId { get; set; }
    [BindProperty] public int? CustomerId { get; set; }

    // People this account can be linked to
    public List<ZooDatabase.Data.UserEmployees.Choice> Employees { get; set; } = new();
    public List<ZooDatabase.Data.UserEmployees.Choice> Volunteers { get; set; } = new();
    public List<ZooDatabase.Data.UserEmployees.Choice> Customers { get; set; } = new();

    public ZooDatabase.Data.UserEmployees.LinkFields LinkFields =>
        new(Employees, Volunteers, Customers, EmployeeId, VolunteerId, CustomerId);
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
        var links = ZooDatabase.Data.UserEmployees.ForRole(Role, EmployeeId, VolunteerId, CustomerId);
        (EmployeeId, VolunteerId, CustomerId) = (links.EmployeeId, links.VolunteerId, links.CustomerId);
        ErrorMessage = ZooDatabase.Data.UserEmployees.Check(Role, links, Employees, Volunteers, Customers);
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
            "UPDATE User_Account SET Role = @role, Is_Active = @active, " +
            "Employee_ID = @emp, Volunteer_ID = @vol, Customer_ID = @cust WHERE User_ID = @id", conn);
        update.Parameters.AddWithValue("@role", Role);
        update.Parameters.AddWithValue("@emp", (object?)EmployeeId ?? DBNull.Value);
        update.Parameters.AddWithValue("@vol", (object?)VolunteerId ?? DBNull.Value);
        update.Parameters.AddWithValue("@cust", (object?)CustomerId ?? DBNull.Value);
        update.Parameters.AddWithValue("@active", IsActive);
        update.Parameters.AddWithValue("@id", Id);
        try
        {
            await update.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.SqlState == "45000")
        {
            // The database refused it (unified_users.sql triggers), show its reason
            ErrorMessage = ex.Message;
            return Page();
        }

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
            "SELECT Username, Role, Is_Active, Employee_ID, Volunteer_ID, Customer_ID FROM User_Account WHERE User_ID = @id", conn);
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
                VolunteerId = reader.IsDBNull(4) ? null : reader.GetInt32(4);
                CustomerId = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            }
        }
        Employees = await ZooDatabase.Data.UserEmployees.LoadAsync(conn, Id);
        Volunteers = await ZooDatabase.Data.UserEmployees.LoadVolunteersAsync(conn, Id);
        Customers = await ZooDatabase.Data.UserEmployees.LoadCustomersAsync(conn, Id);
        return true;
    }
}