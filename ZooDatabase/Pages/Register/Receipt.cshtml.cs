using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Register;

// A printable receipt for one sale. Kind "t" = ticket sale, "s" = food or gift shop sale.
[Authorize(Roles = SalesOptions.SalesRoles)]
public class ReceiptModel : PageModel
{
    private readonly IConfiguration _config;
    public ReceiptModel(IConfiguration config) { _config = config; }

    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = "s";
    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public record Line(int Qty, string Name, decimal Price)
    {
        public decimal Amount => Qty * Price;
    }

    public string Title { get; set; } = "";
    public string Number { get; set; } = "";
    public DateTime When { get; set; }
    public string Location { get; set; } = "";
    public string? Cashier { get; set; }
    public string? Customer { get; set; }
    public DateTime? VisitDate { get; set; }
    public string? Payment { get; set; }
    public decimal Total { get; set; }
    public List<Line> Lines { get; set; } = new();
    public decimal Subtotal => Lines.Sum(l => l.Amount);
    public int ItemCount => Lines.Sum(l => l.Qty);

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = await RegisterData.OpenAsync(_config);
        return Kind == "t" ? await TicketAsync(conn) : await ShopAsync(conn);
    }

    private async Task<IActionResult> TicketAsync(MySqlConnection conn)
    {
        await using (var cmd = new MySqlCommand(@"
            SELECT ts.Purchase_Date, ts.Visit_Date, ts.Payment_Method, ts.Total_Amount, CONCAT(c.First_Name, ' ', c.Last_Name)
            FROM Ticket_Sale ts LEFT JOIN Customer c ON c.Customer_ID = ts.Customer_ID
            WHERE ts.TicketPurchase_ID = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            When = r.GetDateTime(0);
            VisitDate = r.IsDBNull(1) ? null : r.GetDateTime(1);
            Payment = r.IsDBNull(2) ? null : r.GetString(2);
            Total = r.IsDBNull(3) ? 0 : r.GetDecimal(3);
            Customer = r.IsDBNull(4) ? null : r.GetString(4);
        }
        await using (var cmd = new MySqlCommand(@"
            SELECT i.Quantity, tt.TicketType_Name, i.Unit_Price
            FROM Ticket_Purchase_Item i JOIN Ticket_Type tt ON tt.TicketType_ID = i.TicketType_ID
            WHERE i.TicketPurchase_ID = @id ORDER BY i.Unit_Price DESC", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) Lines.Add(new Line(r.GetInt32(0), r.GetString(1), r.GetDecimal(2)));
        }
        Title = "Admission";
        Number = "T-" + Id.ToString("D6");
        Location = "Front Gate";
        return Page();
    }

    private async Task<IActionResult> ShopAsync(MySqlConnection conn)
    {
        await using (var cmd = new MySqlCommand(@"
            SELECT s.Sale_Date, s.Sale_Type, COALESCE(fs.Stand_Name, gs.GiftShop_Name), s.Payment_Method, s.Total_Amount,
                   CONCAT(e.First_Name, ' ', LEFT(e.Last_Name, 1), '.'), CONCAT(c.First_Name, ' ', c.Last_Name)
            FROM Sale s
            LEFT JOIN Food_Stand fs ON fs.Stand_ID = s.Stand_ID
            LEFT JOIN Gift_Shop gs ON gs.GiftShop_ID = s.GiftShop_ID
            LEFT JOIN Employee e ON e.Employee_ID = s.Employee_ID
            LEFT JOIN Customer c ON c.Customer_ID = s.Customer_ID
            WHERE s.Sale_ID = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            When = r.GetDateTime(0);
            Title = r.GetString(1) == "Food" ? "Food & Drink" : "Gift Shop";
            Location = r.IsDBNull(2) ? "" : r.GetString(2);
            Payment = r.IsDBNull(3) ? null : r.GetString(3);
            Total = r.IsDBNull(4) ? 0 : r.GetDecimal(4);
            Cashier = r.IsDBNull(5) ? "Online order" : r.GetString(5);
            Customer = r.IsDBNull(6) ? null : r.GetString(6);
        }
        await using (var cmd = new MySqlCommand(@"
            SELECT si.Quantity, COALESCE(p.Product_Name, f.FoodItem_Name), si.Unit_Price
            FROM Sale_Item si
            LEFT JOIN Product p ON p.Product_ID = si.Product_ID
            LEFT JOIN Food_Item f ON f.FoodItem_ID = si.FoodItem_ID
            WHERE si.Sale_ID = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) Lines.Add(new Line(r.GetInt32(0), r.IsDBNull(1) ? "Item" : r.GetString(1), r.GetDecimal(2)));
        }
        Number = "S-" + Id.ToString("D6");
        return Page();
    }
}
