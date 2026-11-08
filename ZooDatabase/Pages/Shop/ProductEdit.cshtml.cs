using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Shop;

// Add or edit a gift shop product and its inventory settings (DBA only).
// New products also get an Inventory row, because the sales trigger needs one to subtract stock.
[Authorize(Roles = SalesOptions.ManagerRoles)]
public class ProductEditModel : PageModel
{
    private readonly IConfiguration _config;
    public ProductEditModel(IConfiguration config) { _config = config; }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public int ShopId { get; set; }
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Category { get; set; }
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public decimal? Price { get; set; }
    [BindProperty] public int ReorderLevel { get; set; } = 10;
    [BindProperty] public int StartingStock { get; set; }
    [BindProperty] public int SupplierId { get; set; }
    [BindProperty] public decimal? SupplierCost { get; set; }

    public List<(int Id, string Name)> Shops { get; set; } = new();
    public List<(int Id, string Name)> Suppliers { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public bool IsNew => Id == null;

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = await OpenAsync();
        if (!IsNew)
        {
            await using var cmd = new MySqlCommand(@"
                SELECT p.GiftShop_ID, p.Product_Name, p.Category, p.Description, p.Price, COALESCE(i.Reorder_Level, 0)
                FROM Product p LEFT JOIN Inventory i ON i.Product_ID = p.Product_ID
                WHERE p.Product_ID = @id", conn);
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            ShopId = r.GetInt32(0);
            Name = r.GetString(1);
            Category = r.IsDBNull(2) ? null : r.GetString(2);
            Description = r.IsDBNull(3) ? null : r.GetString(3);
            Price = r.GetDecimal(4);
            ReorderLevel = Convert.ToInt32(r.GetValue(5));
        }
        await LoadAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = await OpenAsync();
        Name = (Name ?? "").Trim();
        if (ShopId <= 0) ErrorMessage = "Please choose a gift shop.";
        else if (Name.Length == 0) ErrorMessage = "Please enter a name.";
        else if (Price == null || Price < 0) ErrorMessage = "Please enter a price of $0 or more.";
        else if (ReorderLevel < 0 || StartingStock < 0) ErrorMessage = "Stock numbers can't be negative.";
        else if (SupplierCost < 0) ErrorMessage = "Supplier cost can't be negative.";
        else if (SupplierCost > Price) ErrorMessage = "Supplier cost is higher than the price, so every sale would lose money.";
        if (ErrorMessage != null)
        {
            await LoadAsync(conn);
            return Page();
        }

        object Val(string? s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
        await using var tx = await conn.BeginTransactionAsync();
        int productId;

        await using (var cmd = new MySqlCommand(IsNew
            ? "INSERT INTO Product (GiftShop_ID, Product_Name, Description, Category, Price) VALUES (@shop, @name, @desc, @cat, @price)"
            : "UPDATE Product SET GiftShop_ID = @shop, Product_Name = @name, Description = @desc, Category = @cat, Price = @price WHERE Product_ID = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("@shop", ShopId);
            cmd.Parameters.AddWithValue("@name", Name);
            cmd.Parameters.AddWithValue("@desc", Val(Description));
            cmd.Parameters.AddWithValue("@cat", Val(Category));
            cmd.Parameters.AddWithValue("@price", Price);
            cmd.Parameters.AddWithValue("@id", Id ?? 0);
            await cmd.ExecuteNonQueryAsync();
            productId = IsNew ? (int)cmd.LastInsertedId : Id!.Value;
        }

        // Inventory row: create with starting stock, or just update the reorder level
        await using (var cmd = new MySqlCommand(@"
            INSERT INTO Inventory (Product_ID, Quantity_In_Stock, Reorder_Level) VALUES (@id, @stock, @reorder)
            ON DUPLICATE KEY UPDATE Reorder_Level = @reorder", conn, tx))
        {
            cmd.Parameters.AddWithValue("@id", productId);
            cmd.Parameters.AddWithValue("@stock", StartingStock);
            cmd.Parameters.AddWithValue("@reorder", ReorderLevel);
            await cmd.ExecuteNonQueryAsync();
        }

        if (SupplierId > 0)
        {
            await using var cmd = new MySqlCommand(@"
                INSERT INTO Supplier_Product (Supplier_ID, Product_ID, Unit_Cost, Availability) VALUES (@s, @id, @cost, 'In Stock')
                ON DUPLICATE KEY UPDATE Unit_Cost = COALESCE(@cost, Unit_Cost)", conn, tx);
            cmd.Parameters.AddWithValue("@s", SupplierId);
            cmd.Parameters.AddWithValue("@id", productId);
            cmd.Parameters.AddWithValue("@cost", (object?)SupplierCost ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        TempData["Success"] = $"{Name} was {(IsNew ? "added" : "updated")}.";
        return Redirect($"/Shop/ProductDetails/{productId}");
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }

    private async Task LoadAsync(MySqlConnection conn)
    {
        Shops.Clear();
        Suppliers.Clear();
        await using (var cmd = new MySqlCommand("SELECT GiftShop_ID, GiftShop_Name FROM Gift_Shop ORDER BY GiftShop_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) Shops.Add((r.GetInt32(0), r.GetString(1)));
        await using (var cmd = new MySqlCommand("SELECT Supplier_ID, Supplier_Name FROM Supplier ORDER BY Supplier_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) Suppliers.Add((r.GetInt32(0), r.GetString(1)));
    }
}
