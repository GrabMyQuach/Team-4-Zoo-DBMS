using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Register;

// "Check a Member": search by name, email or phone and see if their membership is active.
// Shows each customer's most recent membership only, and no dollar amounts.
[Authorize(Roles = SalesOptions.SalesRoles)]
public class MembersModel : PageModel
{
    private readonly IConfiguration _config;
    public MembersModel(IConfiguration config) { _config = config; }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public List<Row> Members { get; set; } = new();

    public record Row(int CustomerId, string Name, string? Email, string? Phone, string Tier, DateTime Start, DateTime End, string Status);

    public async Task OnGetAsync()
    {
        var digits = new string((Q ?? "").Where(char.IsDigit).ToArray());
        bool searching = !string.IsNullOrWhiteSpace(Q);
        var where = "l.rn = 1";
        if (searching)
        {
            where += " AND (CONCAT(c.First_Name, ' ', c.Last_Name) LIKE @q OR c.Email LIKE @q"
                   + (digits.Length >= 3 ? " OR REGEXP_REPLACE(COALESCE(c.Phone_Number, ''), '[^0-9]', '') LIKE @digits" : "")
                   + ")";
        }

        await using var conn = await RegisterData.OpenAsync(_config);
        // ROW_NUMBER picks each customer's newest membership
        await using var cmd = new MySqlCommand($@"
            WITH latest AS (
                SELECT m.*, ROW_NUMBER() OVER (PARTITION BY m.Customer_ID ORDER BY m.Expiration_Date DESC) AS rn
                FROM Membership m
            )
            SELECT c.Customer_ID, CONCAT(c.First_Name, ' ', c.Last_Name), c.Email, c.Phone_Number,
                   mt.Membership_Name, l.Start_Date, l.Expiration_Date
            FROM latest l
            JOIN Customer c ON c.Customer_ID = l.Customer_ID
            JOIN Membership_Type mt ON mt.MembershipType_ID = l.MembershipType_ID
            WHERE {where}
            ORDER BY c.Last_Name, c.First_Name", conn);
        if (searching)
        {
            cmd.Parameters.AddWithValue("@q", "%" + Q!.Trim() + "%");
            cmd.Parameters.AddWithValue("@digits", "%" + digits + "%");
        }
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var start = r.GetDateTime(5);
            var end = r.GetDateTime(6);
            Members.Add(new Row(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), start, end, SalesOptions.MembershipStatus(start, end)));
        }
    }
}
