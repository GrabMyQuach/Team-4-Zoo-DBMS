using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Shop;

// /Shop/ProductDetails/4 -> one product: stock, suppliers, sales by week, and restock orders
public class ProductDetailsModel : PageModel
{
    private readonly IConfiguration _config;
    public ProductDetailsModel(IConfiguration config) { _config = config; }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int ShopId { get; set; }
    public string Shop { get; set; } = "";
    public int? Stock { get; set; }
    public int? Reorder { get; set; }
    public DateTime? StockUpdated { get; set; }
    public decimal DailySales { get; set; }

    public List<(string Supplier, decimal? Cost, string? Availability, string? Delivery)> Suppliers { get; set; } = new();
    public List<(DateTime Week, int Sold, decimal Revenue)> Weeks { get; set; } = new();
    public List<(int Id, DateTime Ordered, decimal Qty, string Status, DateTime? Received, decimal? Total)> Orders { get; set; } = new();
    public bool CanManage => SalesOptions.CanManage(User);

    public decimal? DaysLeft => Stock == null || DailySales == 0 ? null : Math.Round(Stock.Value / DailySales, 1);
    public decimal? LowestCost => Suppliers.Where(s => s.Cost != null).Select(s => s.Cost).Min();

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using (var cmd = new MySqlCommand(@"
            SELECT p.Product_Name, p.Category, p.Description, p.Price, p.GiftShop_ID, g.GiftShop_Name,
                   inv.Quantity_In_Stock, inv.Reorder_Level, inv.Last_Updated,
                   (SELECT COALESCE(SUM(si.Quantity), 0) / 30 FROM Sale_Item si JOIN Sale s ON s.Sale_ID = si.Sale_ID
                     WHERE si.Product_ID = p.Product_ID AND s.Sale_Date >= CURDATE() - INTERVAL 30 DAY)
            FROM Product p
            JOIN Gift_Shop g ON g.GiftShop_ID = p.GiftShop_ID
            LEFT JOIN Inventory inv ON inv.Product_ID = p.Product_ID
            WHERE p.Product_ID = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            Name = r.GetString(0);
            Category = r.IsDBNull(1) ? null : r.GetString(1);
            Description = r.IsDBNull(2) ? null : r.GetString(2);
            Price = r.GetDecimal(3);
            ShopId = r.GetInt32(4);
            Shop = r.GetString(5);
            Stock = r.IsDBNull(6) ? null : r.GetInt32(6);
            Reorder = r.IsDBNull(7) ? null : r.GetInt32(7);
            StockUpdated = r.IsDBNull(8) ? null : r.GetDateTime(8);
            DailySales = Math.Round(Convert.ToDecimal(r.GetValue(9)), 2);
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT s.Supplier_Name, sp.Unit_Cost, sp.Availability, COALESCE(sp.Delivery_Schedule, s.Delivery_Schedule)
            FROM Supplier_Product sp JOIN Supplier s ON s.Supplier_ID = sp.Supplier_ID
            WHERE sp.Product_ID = @id ORDER BY sp.Unit_Cost", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Suppliers.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetDecimal(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT MIN(DATE(s.Sale_Date)), SUM(si.Quantity), SUM(si.Quantity * si.Unit_Price)
            FROM Sale_Item si JOIN Sale s ON s.Sale_ID = si.Sale_ID
            WHERE si.Product_ID = @id AND s.Sale_Date >= CURDATE() - INTERVAL 12 WEEK
            GROUP BY YEARWEEK(s.Sale_Date, 1) ORDER BY 1", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Weeks.Add((r.GetDateTime(0), Convert.ToInt32(r.GetValue(1)), Convert.ToDecimal(r.GetValue(2))));
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT PO_ID, Order_Date, Quantity, Status, Received_Date, Total_Cost
            FROM Purchase_Order WHERE Product_ID = @id ORDER BY Order_Date DESC LIMIT 10", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Orders.Add((r.GetInt32(0), r.GetDateTime(1), r.GetDecimal(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetDateTime(4), r.IsDBNull(5) ? null : r.GetDecimal(5)));
        }

        return Page();
    }
}
