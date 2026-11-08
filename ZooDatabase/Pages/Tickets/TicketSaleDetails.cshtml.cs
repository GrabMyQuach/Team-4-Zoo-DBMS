using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Tickets;

// /Tickets/TicketSaleDetails/12 -> one ticket order and what was in it
public class TicketSaleDetailsModel : PageModel
{
    private readonly IConfiguration _config;

    public TicketSaleDetailsModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public int CustomerId { get; set; }
    public string Customer { get; set; } = "";
    public DateTime PurchaseDate { get; set; }
    public DateTime VisitDate { get; set; }
    public string? Payment { get; set; }
    public decimal Total { get; set; }
    public string? Membership { get; set; }
    public List<ItemRow> Items { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using (var cmd = new MySqlCommand(@"
            SELECT ts.Customer_ID, CONCAT(c.First_Name, ' ', c.Last_Name), ts.Purchase_Date, ts.Visit_Date,
                   ts.Payment_Method, ts.Total_Amount,
                   (SELECT mt.Membership_Name FROM Membership m
                     JOIN Membership_Type mt ON mt.MembershipType_ID = m.MembershipType_ID
                    WHERE m.Customer_ID = ts.Customer_ID
                      AND ts.Visit_Date BETWEEN m.Start_Date AND m.Expiration_Date
                    ORDER BY m.Expiration_Date DESC LIMIT 1)
            FROM Ticket_Sale ts
            JOIN Customer c ON c.Customer_ID = ts.Customer_ID
            WHERE ts.TicketPurchase_ID = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            CustomerId = r.GetInt32(0);
            Customer = r.GetString(1);
            PurchaseDate = r.GetDateTime(2);
            VisitDate = r.GetDateTime(3);
            Payment = r.IsDBNull(4) ? null : r.GetString(4);
            Total = r.GetDecimal(5);
            Membership = r.IsDBNull(6) ? null : r.GetString(6);
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT tt.TicketType_Name, i.Quantity, i.Unit_Price, tt.Price
            FROM Ticket_Purchase_Item i
            JOIN Ticket_Type tt ON tt.TicketType_ID = i.TicketType_ID
            WHERE i.TicketPurchase_ID = @id
            ORDER BY tt.TicketType_Name", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Items.Add(new ItemRow(r.GetString(0), r.GetInt32(1), r.GetDecimal(2), r.GetDecimal(3)));
        }

        return Page();
    }

    public record ItemRow(string TicketType, int Quantity, decimal UnitPrice, decimal ListPrice)
    {
        public decimal LineTotal => Quantity * UnitPrice;
    }
}
