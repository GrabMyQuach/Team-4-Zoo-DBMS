using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Tickets;

public class CustomersModel : PageModel
{
    private readonly IConfiguration _config;

    public CustomersModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public List<CustomerRow> Rows { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // Subqueries (SELECT inside SELECT) work out each customer's totals separately,
        // so joining tickets and memberships at the same time can't double-count anything.
        var sql = @"
            SELECT c.Customer_ID, c.First_Name, c.Last_Name, c.Email, c.Phone_Number,
                   (SELECT COUNT(*) FROM Ticket_Sale ts
                     WHERE ts.Customer_ID = c.Customer_ID) AS Purchases,
                   (SELECT COALESCE(SUM(ts.Total_Amount), 0) FROM Ticket_Sale ts
                     WHERE ts.Customer_ID = c.Customer_ID) AS Total_Spent,
                   (SELECT mt.Membership_Name
                      FROM Membership m
                      JOIN Membership_Type mt ON mt.MembershipType_ID = m.MembershipType_ID
                     WHERE m.Customer_ID = c.Customer_ID
                       AND m.Start_Date <= CURDATE() AND m.Expiration_Date >= CURDATE()
                     ORDER BY m.Expiration_Date DESC
                     LIMIT 1) AS Active_Membership
            FROM Customer c";

        if (!string.IsNullOrWhiteSpace(Search))
        {
            sql += " WHERE CONCAT(c.First_Name, ' ', c.Last_Name) LIKE @search OR c.Email LIKE @search";
        }
        sql += " ORDER BY c.Last_Name, c.First_Name";

        await using var cmd = new MySqlCommand(sql, conn);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            cmd.Parameters.AddWithValue("@search", "%" + Search + "%");
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Rows.Add(new CustomerRow
            {
                Name = reader.GetString(1) + " " + reader.GetString(2),
                Email = reader.IsDBNull(3) ? null : reader.GetString(3),
                Phone = reader.IsDBNull(4) ? null : reader.GetString(4),
                Purchases = Convert.ToInt32(reader.GetValue(5)),
                TotalSpent = Convert.ToDecimal(reader.GetValue(6)),
                Membership = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }
    }

    public class CustomerRow
    {
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public int Purchases { get; set; }
        public decimal TotalSpent { get; set; }
        public string? Membership { get; set; }
    }
}