using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Tickets;

// Customer list with visits, spending (tickets + food + gift shop), and membership. Click for details.
public class CustomersModel : PageModel
{
    private readonly IConfiguration _config;

    public CustomersModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Member { get; set; }

    public List<CustomerRow> Rows { get; set; } = new();
    public bool CanSell => SalesOptions.CanSell(User);
    public bool ShowContact => SalesOptions.CanSeeContact(User);

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // Subqueries keep each total separate, so joining tickets, sales and memberships can't double-count
        var sql = @"
            SELECT * FROM (
                SELECT c.Customer_ID, c.First_Name, c.Last_Name, c.Email, c.Phone_Number,
                       (SELECT COUNT(*) FROM Ticket_Sale ts WHERE ts.Customer_ID = c.Customer_ID) AS Visits,
                       (SELECT MAX(ts.Visit_Date) FROM Ticket_Sale ts WHERE ts.Customer_ID = c.Customer_ID AND ts.Visit_Date <= CURDATE()) AS Last_Visit,
                       (SELECT COALESCE(SUM(ts.Total_Amount), 0) FROM Ticket_Sale ts WHERE ts.Customer_ID = c.Customer_ID)
                     + (SELECT COALESCE(SUM(s.Total_Amount), 0) FROM Sale s WHERE s.Customer_ID = c.Customer_ID) AS Total_Spent,
                       (SELECT mt.Membership_Name FROM Membership m
                          JOIN Membership_Type mt ON mt.MembershipType_ID = m.MembershipType_ID
                         WHERE m.Customer_ID = c.Customer_ID
                           AND m.Start_Date <= CURDATE() AND m.Expiration_Date >= CURDATE()
                         ORDER BY m.Expiration_Date DESC LIMIT 1) AS Active_Membership
                FROM Customer c
            ) x
            WHERE 1 = 1";
        if (!string.IsNullOrWhiteSpace(Search))
            sql += ShowContact
                ? " AND (CONCAT(x.First_Name, ' ', x.Last_Name) LIKE @search OR x.Email LIKE @search)"
                : " AND CONCAT(x.First_Name, ' ', x.Last_Name) LIKE @search";
        if (Member == "yes") sql += " AND x.Active_Membership IS NOT NULL";
        if (Member == "no") sql += " AND x.Active_Membership IS NULL";
        sql += " ORDER BY x.Last_Name, x.First_Name";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@search", "%" + Search + "%");

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Rows.Add(new CustomerRow
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1) + " " + reader.GetString(2),
                Email = reader.IsDBNull(3) ? null : reader.GetString(3),
                Phone = reader.IsDBNull(4) ? null : reader.GetString(4),
                Visits = Convert.ToInt32(reader.GetValue(5)),
                LastVisit = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                TotalSpent = Convert.ToDecimal(reader.GetValue(7)),
                Membership = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
    }

    public class CustomerRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public int Visits { get; set; }
        public DateTime? LastVisit { get; set; }
        public decimal TotalSpent { get; set; }
        public string? Membership { get; set; }
    }
}
