using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Tickets;

// Add or edit a customer (cashiers and managers)
//   /Tickets/CustomerEdit             -> add
//   /Tickets/CustomerEdit?next=sale   -> add, then go straight to selling them tickets
//   /Tickets/CustomerEdit/7           -> edit customer #7
[Authorize(Roles = SalesOptions.SalesRoles)]
public class CustomerEditModel : PageModel
{
    private readonly IConfiguration _config;

    public CustomerEditModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Next { get; set; }
    [BindProperty] public string FirstName { get; set; } = "";
    [BindProperty] public string LastName { get; set; } = "";
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Phone { get; set; }

    public string? ErrorMessage { get; set; }
    public bool IsNew => Id == null;

    public async Task<IActionResult> OnGetAsync()
    {
        if (IsNew) return Page();

        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT First_Name, Last_Name, Email, Phone_Number FROM Customer WHERE Customer_ID = @id", conn);
        cmd.Parameters.AddWithValue("@id", Id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return NotFound();
        FirstName = r.GetString(0);
        LastName = r.GetString(1);
        Email = r.IsDBNull(2) ? null : r.GetString(2);
        Phone = r.IsDBNull(3) ? null : r.GetString(3);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        FirstName = (FirstName ?? "").Trim();
        LastName = (LastName ?? "").Trim();
        Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim().ToLowerInvariant();
        Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim();

        if (FirstName.Length == 0 || LastName.Length == 0)
            ErrorMessage = "Please enter a first and last name.";
        else if (Email != null && !Regex.IsMatch(Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            ErrorMessage = "That email address doesn't look right.";
        if (ErrorMessage != null) return Page();

        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand(IsNew
            ? "INSERT INTO Customer (First_Name, Last_Name, Email, Phone_Number) VALUES (@first, @last, @email, @phone)"
            : "UPDATE Customer SET First_Name = @first, Last_Name = @last, Email = @email, Phone_Number = @phone WHERE Customer_ID = @id", conn);
        cmd.Parameters.AddWithValue("@first", FirstName);
        cmd.Parameters.AddWithValue("@last", LastName);
        cmd.Parameters.AddWithValue("@email", Email);
        cmd.Parameters.AddWithValue("@phone", Phone);
        cmd.Parameters.AddWithValue("@id", Id ?? 0);

        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            ErrorMessage = "Another customer already uses that email.";
            return Page();
        }

        var customerId = IsNew ? (int)cmd.LastInsertedId : Id!.Value;
        TempData["Success"] = $"{FirstName} {LastName} was {(IsNew ? "added" : "updated")}.";
        return Next == "sale"
            ? Redirect($"/Tickets/TicketSaleNew?customerId={customerId}")
            : Redirect($"/Tickets/CustomerDetails/{customerId}");
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }
}
