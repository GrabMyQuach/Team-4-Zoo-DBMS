using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Tickets;

// Sign up or renew a member. Memberships last one year.
// A renewal starts the day after the current one ends, so there's never an overlap.
[Authorize(Roles = SalesOptions.SalesRoles)]
public class MembershipNewModel : PageModel
{
    private readonly IConfiguration _config;

    public MembershipNewModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int CustomerId { get; set; }
    [BindProperty] public int TypeId { get; set; }
    [BindProperty] public DateTime StartDate { get; set; } = DateTime.Today;

    public List<(int Id, string Name)> Customers { get; set; } = new();
    public List<(int Id, string Name, decimal Price, string? Benefits)> Types { get; set; } = new();
    public DateTime? CurrentEnd { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await using var conn = await OpenAsync();
        await LoadAsync(conn);
        if (CurrentEnd != null && CurrentEnd >= DateTime.Today) StartDate = CurrentEnd.Value.AddDays(1);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = await OpenAsync();
        await LoadAsync(conn);

        if (CustomerId <= 0) ErrorMessage = "Please choose a customer.";
        else if (!Types.Any(t => t.Id == TypeId)) ErrorMessage = "Please choose a membership tier.";
        else if (StartDate.Date < DateTime.Today.AddDays(-7)) ErrorMessage = "Start date can't be more than a week in the past.";
        else
        {
            // Overlap check: no two memberships for the same person can cover the same day
            await using var check = new MySqlCommand(@"
                SELECT MAX(Expiration_Date) FROM Membership
                WHERE Customer_ID = @c AND Expiration_Date >= @start", conn);
            check.Parameters.AddWithValue("@c", CustomerId);
            check.Parameters.AddWithValue("@start", StartDate.Date);
            var overlap = await check.ExecuteScalarAsync();
            if (overlap is DateTime end)
                ErrorMessage = $"This customer is already a member until {end:MMM d, yyyy}. Start the renewal on {end.AddDays(1):MMM d, yyyy} or later.";
        }
        if (ErrorMessage != null) return Page();

        await using var cmd = new MySqlCommand(@"
            INSERT INTO Membership (Customer_ID, MembershipType_ID, Start_Date, Expiration_Date, Status)
            VALUES (@c, @t, @start, @end, 'Active')", conn);
        cmd.Parameters.AddWithValue("@c", CustomerId);
        cmd.Parameters.AddWithValue("@t", TypeId);
        cmd.Parameters.AddWithValue("@start", StartDate.Date);
        cmd.Parameters.AddWithValue("@end", StartDate.Date.AddYears(1).AddDays(-1));
        await cmd.ExecuteNonQueryAsync();

        var tier = Types.First(t => t.Id == TypeId);
        TempData["Success"] = $"{tier.Name} membership added ({tier.Price:C}), valid until {StartDate.AddYears(1).AddDays(-1):MMM d, yyyy}.";
        return Redirect($"/Tickets/CustomerDetails/{CustomerId}");
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }

    private async Task LoadAsync(MySqlConnection conn)
    {
        await using (var cmd = new MySqlCommand(
            "SELECT Customer_ID, CONCAT(Last_Name, ', ', First_Name, COALESCE(CONCAT(' (', Email, ')'), '')) FROM Customer ORDER BY Last_Name, First_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) Customers.Add((r.GetInt32(0), r.GetString(1)));
        }

        await using (var cmd = new MySqlCommand("SELECT MembershipType_ID, Membership_Name, Price, Benefits FROM Membership_Type ORDER BY Price", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) Types.Add((r.GetInt32(0), r.GetString(1), r.GetDecimal(2), r.IsDBNull(3) ? null : r.GetString(3)));
        }

        if (CustomerId > 0)
        {
            await using var cmd = new MySqlCommand("SELECT MAX(Expiration_Date) FROM Membership WHERE Customer_ID = @c", conn);
            cmd.Parameters.AddWithValue("@c", CustomerId);
            if (await cmd.ExecuteScalarAsync() is DateTime end) CurrentEnd = end;
        }
    }
}
