using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Tickets;

// /Tickets/CustomerDetails/7 -> one customer: memberships, visits, and what they spend where
public class CustomerDetailsModel : PageModel
{
    private readonly IConfiguration _config;

    public CustomerDetailsModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool ShowContact => SalesOptions.CanSeeContact(User);
    public bool CanSell => SalesOptions.CanSell(User);

    public List<MembershipRow> Memberships { get; set; } = new();
    public List<VisitRow> Visits { get; set; } = new();
    public List<ShopRow> ShopPurchases { get; set; } = new();

    public decimal TicketSpend => Visits.Sum(v => v.Total);
    public decimal FoodSpend => ShopPurchases.Where(s => s.Type == "Food").Sum(s => s.Total);
    public decimal GiftSpend => ShopPurchases.Where(s => s.Type != "Food").Sum(s => s.Total);
    public int PastVisits => Visits.Count(v => v.VisitDate <= DateTime.Today);
    public MembershipRow? Current => Memberships.FirstOrDefault(m => m.Status is "Active" or "Expiring Soon");

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using (var cmd = new MySqlCommand("SELECT First_Name, Last_Name, Email, Phone_Number FROM Customer WHERE Customer_ID = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            Name = r.GetString(0) + " " + r.GetString(1);
            Email = r.IsDBNull(2) ? null : r.GetString(2);
            Phone = r.IsDBNull(3) ? null : r.GetString(3);
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT mt.Membership_Name, mt.Price, m.Start_Date, m.Expiration_Date
            FROM Membership m JOIN Membership_Type mt ON mt.MembershipType_ID = m.MembershipType_ID
            WHERE m.Customer_ID = @id
            ORDER BY m.Expiration_Date DESC", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var start = r.GetDateTime(2);
                var end = r.GetDateTime(3);
                Memberships.Add(new MembershipRow(r.GetString(0), r.GetDecimal(1), start, end, SalesOptions.MembershipStatus(start, end)));
            }
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT ts.TicketPurchase_ID, ts.Visit_Date, ts.Total_Amount, COALESCE(SUM(i.Quantity), 0)
            FROM Ticket_Sale ts
            LEFT JOIN Ticket_Purchase_Item i ON i.TicketPurchase_ID = ts.TicketPurchase_ID
            WHERE ts.Customer_ID = @id
            GROUP BY ts.TicketPurchase_ID, ts.Visit_Date, ts.Total_Amount
            ORDER BY ts.Visit_Date DESC", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Visits.Add(new VisitRow(r.GetInt32(0), r.GetDateTime(1), r.GetDecimal(2), Convert.ToInt32(r.GetValue(3))));
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT s.Sale_ID, s.Sale_Date, s.Sale_Type, COALESCE(fs.Stand_Name, gs.GiftShop_Name), s.Total_Amount
            FROM Sale s
            LEFT JOIN Food_Stand fs ON fs.Stand_ID = s.Stand_ID
            LEFT JOIN Gift_Shop gs ON gs.GiftShop_ID = s.GiftShop_ID
            WHERE s.Customer_ID = @id
            ORDER BY s.Sale_Date DESC", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                ShopPurchases.Add(new ShopRow(r.GetInt32(0), r.GetDateTime(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetDecimal(4)));
        }

        return Page();
    }

    public record MembershipRow(string Type, decimal Price, DateTime Start, DateTime End, string Status);
    public record VisitRow(int Id, DateTime VisitDate, decimal Total, int Tickets);
    public record ShopRow(int Id, DateTime Date, string Type, string? Place, decimal Total);
}
