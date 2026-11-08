using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Shop;

// Gift shop products with price, cost, profit margin, stock and 30-day sales
public class ProductsModel : PageModel
{
    private readonly IConfiguration _config;
    public ProductsModel(IConfiguration config) { _config = config; }

    [BindProperty(SupportsGet = true)] public int? ShopId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Category { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }

    public List<(int Id, string Name)> Shops { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public List<ProductRow> Rows { get; set; } = new();
    public bool CanManage => SalesOptions.CanManage(User);

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using (var cmd = new MySqlCommand("SELECT GiftShop_ID, GiftShop_Name FROM Gift_Shop ORDER BY GiftShop_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) Shops.Add((r.GetInt32(0), r.GetString(1)));

        await using (var cmd = new MySqlCommand("SELECT DISTINCT Category FROM Product WHERE Category IS NOT NULL ORDER BY Category", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) Categories.Add(r.GetString(0));

        // Cost = cheapest supplier price for the product
        var sql = @"
            SELECT p.Product_ID, p.Product_Name, p.Category, p.Price, g.GiftShop_Name,
                   inv.Quantity_In_Stock, inv.Reorder_Level,
                   (SELECT MIN(sp.Unit_Cost) FROM Supplier_Product sp WHERE sp.Product_ID = p.Product_ID),
                   (SELECT COALESCE(SUM(si.Quantity), 0) FROM Sale_Item si JOIN Sale s ON s.Sale_ID = si.Sale_ID
                     WHERE si.Product_ID = p.Product_ID AND s.Sale_Date >= CURDATE() - INTERVAL 30 DAY),
                   (SELECT COALESCE(SUM(si.Quantity * si.Unit_Price), 0) FROM Sale_Item si JOIN Sale s ON s.Sale_ID = si.Sale_ID
                     WHERE si.Product_ID = p.Product_ID AND s.Sale_Date >= CURDATE() - INTERVAL 30 DAY)
            FROM Product p
            JOIN Gift_Shop g ON g.GiftShop_ID = p.GiftShop_ID
            LEFT JOIN Inventory inv ON inv.Product_ID = p.Product_ID
            WHERE 1 = 1";
        if (ShopId != null) sql += " AND p.GiftShop_ID = @shop";
        if (!string.IsNullOrEmpty(Category)) sql += " AND p.Category = @cat";
        if (!string.IsNullOrWhiteSpace(Search)) sql += " AND p.Product_Name LIKE @search";
        sql += " ORDER BY 10 DESC, p.Product_Name";

        await using var cmd2 = new MySqlCommand(sql, conn);
        cmd2.Parameters.AddWithValue("@shop", ShopId);
        cmd2.Parameters.AddWithValue("@cat", Category);
        cmd2.Parameters.AddWithValue("@search", "%" + Search + "%");
        await using var rd = await cmd2.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            Rows.Add(new ProductRow
            {
                Id = rd.GetInt32(0),
                Name = rd.GetString(1),
                Category = rd.IsDBNull(2) ? null : rd.GetString(2),
                Price = rd.GetDecimal(3),
                Shop = rd.GetString(4),
                Stock = rd.IsDBNull(5) ? null : rd.GetInt32(5),
                Reorder = rd.IsDBNull(6) ? null : rd.GetInt32(6),
                Cost = rd.IsDBNull(7) ? null : rd.GetDecimal(7),
                Sold30 = Convert.ToInt32(rd.GetValue(8)),
                Revenue30 = Convert.ToDecimal(rd.GetValue(9))
            });
        }
    }

    public class ProductRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Category { get; set; }
        public decimal Price { get; set; }
        public string Shop { get; set; } = "";
        public int? Stock { get; set; }
        public int? Reorder { get; set; }
        public decimal? Cost { get; set; }
        public int Sold30 { get; set; }
        public decimal Revenue30 { get; set; }
        public decimal? MarginPct => Cost == null || Price == 0 ? null : Math.Round(100 * (Price - Cost.Value) / Price, 1);
        public decimal? Profit30 => Cost == null ? null : Math.Round(Revenue30 - Sold30 * Cost.Value, 2);
        public bool LowStock => Stock != null && Reorder != null && Stock <= Reorder;
    }
}
