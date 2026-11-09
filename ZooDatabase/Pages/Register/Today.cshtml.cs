using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Register;

// "Today's Sales": every ticket, food and gift sale rung up or bought today, newest first,
// with totals by payment method for closing out the drawer.
[Authorize(Roles = SalesOptions.SalesRoles)]
public class TodayModel : PageModel
{
    private readonly IConfiguration _config;
    public TodayModel(IConfiguration config) { _config = config; }

    public record SaleRow(string Kind, int Id, DateTime Time, string Where, string Items, string? Payment, decimal Total, string Link);

    public List<SaleRow> Sales { get; set; } = new();
    public decimal Total => Sales.Sum(s => s.Total);
    public List<(string Method, int Count, decimal Amount)> ByPayment =>
        Sales.GroupBy(s => s.Payment ?? "Not recorded")
             .Select(g => (g.Key, g.Count(), g.Sum(s => s.Total)))
             .OrderByDescending(x => x.Item3).ToList();

    public async Task OnGetAsync()
    {
        await using var conn = await RegisterData.OpenAsync(_config);
        await using var cmd = new MySqlCommand(@"
            SELECT 'Tickets', ts.TicketPurchase_ID, ts.Purchase_Date, 'Front gate',
                   (SELECT GROUP_CONCAT(CONCAT(i.Quantity, ' ', tt.TicketType_Name) SEPARATOR ', ')
                      FROM Ticket_Purchase_Item i JOIN Ticket_Type tt ON tt.TicketType_ID = i.TicketType_ID
                     WHERE i.TicketPurchase_ID = ts.TicketPurchase_ID),
                   ts.Payment_Method, ts.Total_Amount
            FROM Ticket_Sale ts
            WHERE ts.Purchase_Date >= @day AND ts.Purchase_Date < @next
            UNION ALL
            SELECT IF(s.Sale_Type = 'Food', 'Food', 'Gift shop'), s.Sale_ID, s.Sale_Date, COALESCE(fs.Stand_Name, gs.GiftShop_Name),
                   (SELECT GROUP_CONCAT(CONCAT(si.Quantity, ' ', COALESCE(p.Product_Name, f.FoodItem_Name)) SEPARATOR ', ')
                      FROM Sale_Item si
                      LEFT JOIN Product p ON p.Product_ID = si.Product_ID
                      LEFT JOIN Food_Item f ON f.FoodItem_ID = si.FoodItem_ID
                     WHERE si.Sale_ID = s.Sale_ID),
                   s.Payment_Method, s.Total_Amount
            FROM Sale s
            LEFT JOIN Food_Stand fs ON fs.Stand_ID = s.Stand_ID
            LEFT JOIN Gift_Shop gs ON gs.GiftShop_ID = s.GiftShop_ID
            WHERE s.Sale_Date >= @day AND s.Sale_Date < @next
            ORDER BY 3 DESC", conn);
        // Houston's today, from the app (the database server's clock is UTC)
        cmd.Parameters.AddWithValue("@day", DateTime.Today);
        cmd.Parameters.AddWithValue("@next", DateTime.Today.AddDays(1));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var kind = r.GetString(0);
            var id = Convert.ToInt32(r.GetValue(1));
            Sales.Add(new SaleRow(kind, id, r.GetDateTime(2), r.IsDBNull(3) ? "—" : r.GetString(3),
                r.IsDBNull(4) ? "—" : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? 0 : r.GetDecimal(6),
                kind == "Tickets" ? $"/Tickets/TicketSaleDetails/{id}" : $"/Shop/SaleDetails/{id}"));
        }
    }
}
