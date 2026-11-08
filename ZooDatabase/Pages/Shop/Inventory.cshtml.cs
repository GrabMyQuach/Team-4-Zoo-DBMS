using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Shop;

// Stock levels for gift shop products, how fast they sell, and the restock order workflow:
//   Draft -> Ordered -> Received (Tien's trigger adds the stock when an order is received)
// Feed orders drafted automatically by the feeding triggers show up here too.
public class InventoryModel : PageModel
{
    private readonly IConfiguration _config;
    public InventoryModel(IConfiguration config) { _config = config; }

    [BindProperty(SupportsGet = true)] public string? Show { get; set; }

    public List<StockRow> Rows { get; set; } = new();
    public List<OrderRow> OpenOrders { get; set; } = new();
    public bool CanManage => SalesOptions.CanManage(User);
    public string? Message { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    // Create a restock order for one product: about a month of sales, from the cheapest supplier
    public async Task<IActionResult> OnPostReorderAsync(int productId)
    {
        if (!CanManage) return Forbid();
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand(@"
            INSERT INTO Purchase_Order (Supplier_ID, Product_ID, Quantity, Unit_Cost, Order_Date, Expected_Delivery, Status, Auto_Generated)
            SELECT (SELECT sp.Supplier_ID FROM Supplier_Product sp WHERE sp.Product_ID = i.Product_ID ORDER BY sp.Unit_Cost LIMIT 1),
                   i.Product_ID,
                   GREATEST(10, i.Reorder_Level * 2 - i.Quantity_In_Stock,
                            CEIL((SELECT COALESCE(SUM(si.Quantity), 0) FROM Sale_Item si JOIN Sale s ON s.Sale_ID = si.Sale_ID
                                   WHERE si.Product_ID = i.Product_ID AND s.Sale_Date >= CURDATE() - INTERVAL 30 DAY))),
                   (SELECT MIN(sp.Unit_Cost) FROM Supplier_Product sp WHERE sp.Product_ID = i.Product_ID),
                   CURDATE(), CURDATE() + INTERVAL 5 DAY, 'Ordered', 0
            FROM Inventory i
            WHERE i.Product_ID = @p
              AND NOT EXISTS (SELECT 1 FROM Purchase_Order po WHERE po.Product_ID = i.Product_ID AND po.Status IN ('Draft', 'Ordered'))", conn);
        cmd.Parameters.AddWithValue("@p", productId);
        var n = await cmd.ExecuteNonQueryAsync();
        TempData["Success"] = n == 0 ? "There is already an open order for that product." : $"Restock order #{cmd.LastInsertedId} placed.";
        return RedirectToPage();
    }

    // Move an order along: place a draft, mark an order received, or cancel it
    public async Task<IActionResult> OnPostOrderStatusAsync(int poId, string status)
    {
        if (!CanManage) return Forbid();
        var allowedFrom = status switch
        {
            "Ordered" => new[] { "Draft" },
            "Received" => new[] { "Ordered", "Draft" },
            "Cancelled" => new[] { "Draft", "Ordered" },
            _ => Array.Empty<string>()
        };
        if (allowedFrom.Length == 0) return BadRequest();

        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand($@"
            UPDATE Purchase_Order SET Status = @status
            WHERE PO_ID = @id AND Status IN ({string.Join(",", allowedFrom.Select(s => $"'{s}'"))})", conn);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@id", poId);
        var n = await cmd.ExecuteNonQueryAsync();
        TempData["Success"] = n == 0 ? "That order can't be changed anymore." : status switch
        {
            "Received" => $"Order #{poId} received. Stock was added automatically.",
            "Ordered" => $"Order #{poId} placed with the supplier.",
            _ => $"Order #{poId} cancelled."
        };
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        await using var conn = await OpenAsync();

        await using (var cmd = new MySqlCommand(@"
            SELECT p.Product_ID, p.Product_Name, g.GiftShop_Name, i.Quantity_In_Stock, i.Reorder_Level, i.Last_Updated,
                   (SELECT COALESCE(SUM(si.Quantity), 0) / 30 FROM Sale_Item si JOIN Sale s ON s.Sale_ID = si.Sale_ID
                     WHERE si.Product_ID = p.Product_ID AND s.Sale_Date >= CURDATE() - INTERVAL 30 DAY),
                   (SELECT COUNT(*) FROM Purchase_Order po WHERE po.Product_ID = p.Product_ID AND po.Status IN ('Draft', 'Ordered')),
                   (SELECT COUNT(*) FROM Supplier_Product sp WHERE sp.Product_ID = p.Product_ID)
            FROM Inventory i
            JOIN Product p ON p.Product_ID = i.Product_ID
            JOIN Gift_Shop g ON g.GiftShop_ID = p.GiftShop_ID", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                Rows.Add(new StockRow
                {
                    ProductId = r.GetInt32(0),
                    Name = r.GetString(1),
                    Shop = r.GetString(2),
                    Stock = r.GetInt32(3),
                    Reorder = r.GetInt32(4),
                    Updated = r.IsDBNull(5) ? null : r.GetDateTime(5),
                    DailySales = Math.Round(Convert.ToDecimal(r.GetValue(6)), 2),
                    OpenOrders = Convert.ToInt32(r.GetValue(7)),
                    HasSupplier = Convert.ToInt32(r.GetValue(8)) > 0
                });
            }
        }

        // Worst first
        Rows = Rows
            .Where(x => Show != "attention" || x.Status != "OK")
            .OrderBy(x => x.Status switch { "Out of stock" => 0, "Below reorder level" => 1, "Under 14 days left" => 2, _ => 3 })
            .ThenBy(x => x.DaysLeft ?? 9999)
            .ToList();

        await using (var cmd = new MySqlCommand(@"
            SELECT po.PO_ID, COALESCE(p.Product_Name, f.Feed_Name), IF(po.Feed_ID IS NULL, 'Gift Shop', 'Animal Feed'),
                   s.Supplier_Name, po.Quantity, COALESCE(f.Unit, 'units'), po.Total_Cost, po.Order_Date, po.Expected_Delivery,
                   po.Status, po.Auto_Generated
            FROM Purchase_Order po
            LEFT JOIN Product p ON p.Product_ID = po.Product_ID
            LEFT JOIN Feed f ON f.Feed_ID = po.Feed_ID
            LEFT JOIN Supplier s ON s.Supplier_ID = po.Supplier_ID
            WHERE po.Status IN ('Draft', 'Ordered')
            ORDER BY FIELD(po.Status, 'Draft', 'Ordered'), po.Expected_Delivery", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                OpenOrders.Add(new OrderRow
                {
                    Id = r.GetInt32(0),
                    Item = r.IsDBNull(1) ? "—" : r.GetString(1),
                    Kind = r.GetString(2),
                    Supplier = r.IsDBNull(3) ? null : r.GetString(3),
                    Quantity = r.GetDecimal(4),
                    Unit = r.GetString(5),
                    Total = r.IsDBNull(6) ? null : r.GetDecimal(6),
                    Ordered = r.GetDateTime(7),
                    Expected = r.IsDBNull(8) ? null : r.GetDateTime(8),
                    Status = r.GetString(9),
                    Auto = r.GetBoolean(10)
                });
            }
        }
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }

    public class StockRow
    {
        public int ProductId { get; set; }
        public string Name { get; set; } = "";
        public string Shop { get; set; } = "";
        public int Stock { get; set; }
        public int Reorder { get; set; }
        public DateTime? Updated { get; set; }
        public decimal DailySales { get; set; }
        public int OpenOrders { get; set; }
        public bool HasSupplier { get; set; }
        public decimal? DaysLeft => DailySales == 0 ? null : Math.Round(Stock / DailySales, 1);
        public string Status =>
            Stock <= 0 ? "Out of stock"
            : Stock <= Reorder ? "Below reorder level"
            : DaysLeft < 14 ? "Under 14 days left"
            : "OK";
    }

    public class OrderRow
    {
        public int Id { get; set; }
        public string Item { get; set; } = "";
        public string Kind { get; set; } = "";
        public string? Supplier { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = "";
        public decimal? Total { get; set; }
        public DateTime Ordered { get; set; }
        public DateTime? Expected { get; set; }
        public string Status { get; set; } = "";
        public bool Auto { get; set; }
        public bool Late => Status == "Ordered" && Expected < DateTime.Today;
    }
}
