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
    // The person who uses this login (only the one that fits the role is kept)
    [BindProperty] public int? EmployeeId { get; set; }
    [BindProperty] public int? VolunteerId { get; set; }
    [BindProperty] public int? CustomerId { get; set; }

    public string? ErrorMessage { get; set; }

    // People this account can be linked to
    public List<ZooDatabase.Data.UserEmployees.Choice> Employees { get; set; } = new();
    public List<ZooDatabase.Data.UserEmployees.Choice> Volunteers { get; set; } = new();
    public List<ZooDatabase.Data.UserEmployees.Choice> Customers { get; set; } = new();

    public ZooDatabase.Data.UserEmployees.LinkFields LinkFields =>
        new(Employees, Volunteers, Customers, EmployeeId, VolunteerId, CustomerId);

    public async Task OnGetAsync()
    {
        using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        await LoadPeopleAsync(conn);
    }

    private async Task LoadPeopleAsync(MySqlConnection conn)
    {
        Employees = await ZooDatabase.Data.UserEmployees.LoadAsync(conn);
        Volunteers = await ZooDatabase.Data.UserEmployees.LoadVolunteersAsync(conn);
        Customers = await ZooDatabase.Data.UserEmployees.LoadCustomersAsync(conn);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Username = Username.Trim();

        using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        await LoadPeopleAsync(conn);

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
        var links = ZooDatabase.Data.UserEmployees.ForRole(Role, EmployeeId, VolunteerId, CustomerId);
        (EmployeeId, VolunteerId, CustomerId) = (links.EmployeeId, links.VolunteerId, links.CustomerId);
        ErrorMessage = ZooDatabase.Data.UserEmployees.Check(Role, links, Employees, Volunteers, Customers);
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
            "INSERT INTO User_Account (Username, Password_Hash, Role, Employee_ID, Volunteer_ID, Customer_ID) " +
            "VALUES (@username, @hash, @role, @emp, @vol, @cust)", conn);
        insert.Parameters.AddWithValue("@username", Username);
        insert.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(Password));
        insert.Parameters.AddWithValue("@role", Role);
        insert.Parameters.AddWithValue("@emp", (object?)EmployeeId ?? DBNull.Value);
        insert.Parameters.AddWithValue("@vol", (object?)VolunteerId ?? DBNull.Value);
        insert.Parameters.AddWithValue("@cust", (object?)CustomerId ?? DBNull.Value);
        try
        {
            await insert.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.SqlState == "45000")
        {
            // The database refused it (unified_users.sql triggers), show its reason
            ErrorMessage = ex.Message;
            return Page();
        }

        // 4. Go back to the users list with a success message
        TempData["Success"] = $"User '{Username}' was created.";
        return RedirectToPage("/ManageUsers");
    }
}