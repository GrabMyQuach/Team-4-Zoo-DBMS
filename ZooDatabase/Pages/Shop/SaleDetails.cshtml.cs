using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Shop;

// /Shop/SaleDetails/55 -> a receipt for one food or gift shop sale
public class SaleDetailsModel : PageModel
{
    private readonly IConfiguration _config;
    public SaleDetailsModel(IConfiguration config) { _config = config; }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public DateTime Date { get; set; }
    public string Type { get; set; } = "";
    public int? OutletId { get; set; }
    public string? Place { get; set; }
    public int? CustomerId { get; set; }
    public string? Customer { get; set; }
    public int? CashierId { get; set; }
    public string? Cashier { get; set; }
    public string? Payment { get; set; }
    public decimal Total { get; set; }
    public List<(int ItemId, string Name, int Qty, decimal Price)> Items { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using (var cmd = new MySqlCommand(@"
            SELECT s.Sale_Date, s.Sale_Type, COALESCE(s.Stand_ID, s.GiftShop_ID), COALESCE(fs.Stand_Name, gs.GiftShop_Name),
                   s.Customer_ID, CONCAT(c.First_Name, ' ', c.Last_Name), s.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name),
                   s.Payment_Method, s.Total_Amount
            FROM Sale s
            LEFT JOIN Food_Stand fs ON fs.Stand_ID = s.Stand_ID
            LEFT JOIN Gift_Shop gs ON gs.GiftShop_ID = s.GiftShop_ID
            LEFT JOIN Customer c ON c.Customer_ID = s.Customer_ID
            LEFT JOIN Employee e ON e.Employee_ID = s.Employee_ID
            WHERE s.Sale_ID = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            Date = r.GetDateTime(0);
            Type = r.GetString(1);
            OutletId = r.IsDBNull(2) ? null : Convert.ToInt32(r.GetValue(2));
            Place = r.IsDBNull(3) ? null : r.GetString(3);
            CustomerId = r.IsDBNull(4) ? null : r.GetInt32(4);
            Customer = r.IsDBNull(5) ? null : r.GetString(5);
            CashierId = r.IsDBNull(6) ? null : r.GetInt32(6);
            Cashier = r.IsDBNull(7) ? null : r.GetString(7);
            Payment = r.IsDBNull(8) ? null : r.GetString(8);
            Total = r.GetDecimal(9);
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT COALESCE(si.Product_ID, si.FoodItem_ID), COALESCE(p.Product_Name, f.FoodItem_Name, 'Item'), si.Quantity, si.Unit_Price
            FROM Sale_Item si
            LEFT JOIN Product p ON p.Product_ID = si.Product_ID
            LEFT JOIN Food_Item f ON f.FoodItem_ID = si.FoodItem_ID
            WHERE si.Sale_ID = @id ORDER BY si.SaleItem_ID", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Items.Add((Convert.ToInt32(r.GetValue(0)), r.GetString(1), r.GetInt32(2), r.GetDecimal(3)));
        }
        return Page();
    }
}
