using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Shop;

// Ring up a food or gift shop sale.
//   Step 1: pick where (a food stand or gift shop)
//   Step 2: enter quantities for that location's items
// Prices come from the database. Tien's trigger checks each item belongs to that location,
// subtracts gift shop stock (refusing if there isn't enough), and adds up the total.
[Authorize(Roles = SalesOptions.SalesRoles)]
public class SaleNewModel : PageModel
{
    private readonly IConfiguration _config;
    public SaleNewModel(IConfiguration config) { _config = config; }

    [BindProperty(SupportsGet = true)] public string? Type { get; set; }
    [BindProperty(SupportsGet = true)] public int OutletId { get; set; }
    [BindProperty] public int CustomerId { get; set; }
    [BindProperty] public string? PaymentMethod { get; set; }
    [BindProperty] public Dictionary<int, int> Quantities { get; set; } = new();

    public List<(int Id, string Name)> Stands { get; set; } = new();
    public List<(int Id, string Name)> Shops { get; set; } = new();
    public List<(int Id, string Name)> Customers { get; set; } = new();
    public List<ItemOption> Items { get; set; } = new();
    public string? OutletName { get; set; }
    public string? ErrorMessage { get; set; }

    public bool IsFood => Type == "Food";
    public bool OutletChosen => (Type == "Food" || Type == "GiftShop") && OutletId > 0 && OutletName != null;
    public OutletKind Kind => IsFood ? OutletKind.Food : OutletKind.Gift;

    public async Task OnGetAsync()
    {
        await using var conn = await OpenAsync();
        PaymentMethod = SalesOptions.PaymentMethods[0];
        await LoadAsync(conn);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = await OpenAsync();
        await LoadAsync(conn);
        if (!OutletChosen) return Page();

        var lines = Items
            .Select(i => (Item: i, Qty: Quantities.TryGetValue(i.Id, out var q) ? q : 0))
            .Where(x => x.Qty != 0)
            .ToList();

        if (lines.Count == 0) ErrorMessage = "Add at least one item.";
        else if (lines.Any(x => x.Qty < 0 || x.Qty > 100)) ErrorMessage = "Quantities must be between 0 and 100.";
        else if (lines.FirstOrDefault(x => x.Item.Stock != null && x.Qty > x.Item.Stock) is { Item: not null } tooMany)
            ErrorMessage = $"Only {tooMany.Item.Stock} of {tooMany.Item.Name} left in stock.";
        else if (string.IsNullOrEmpty(PaymentMethod)) ErrorMessage = "Please choose a payment method.";
        if (ErrorMessage != null) return Page();

        var k = Kind;
        var cashier = await ShopText.CurrentEmployeeIdAsync(conn, User.Identity?.Name);

        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            int saleId;
            await using (var cmd = new MySqlCommand($@"
                INSERT INTO Sale (Sale_Date, Employee_ID, Customer_ID, {k.IdCol}, Sale_Type, Total_Amount, Payment_Method)
                VALUES (NOW(), @emp, @cust, @outlet, @type, 0, @pay)", conn, tx))
            {
                cmd.Parameters.AddWithValue("@emp", (object?)cashier ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cust", CustomerId > 0 ? (object)CustomerId : DBNull.Value);
                cmd.Parameters.AddWithValue("@outlet", OutletId);
                cmd.Parameters.AddWithValue("@type", k.SaleType);
                cmd.Parameters.AddWithValue("@pay", PaymentMethod);
                await cmd.ExecuteNonQueryAsync();
                saleId = (int)cmd.LastInsertedId;
            }

            foreach (var (item, qty) in lines)
            {
                await using var cmd = new MySqlCommand($@"
                    INSERT INTO Sale_Item (Sale_ID, {k.ItemIdCol}, Quantity, Unit_Price) VALUES (@sale, @item, @qty, @price)", conn, tx);
                cmd.Parameters.AddWithValue("@sale", saleId);
                cmd.Parameters.AddWithValue("@item", item.Id);
                cmd.Parameters.AddWithValue("@qty", qty);
                cmd.Parameters.AddWithValue("@price", item.Price);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            TempData["Success"] = $"Sale #{saleId} saved.";
            return Redirect($"/Shop/SaleDetails/{saleId}");
        }
        catch (MySqlException ex)
        {
            await tx.RollbackAsync();
            ErrorMessage = ex.SqlState == "45000" ? ex.Message : "Could not save the sale: " + ex.Message;
            return Page();
        }
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }

    private async Task LoadAsync(MySqlConnection conn)
    {
        await using (var cmd = new MySqlCommand("SELECT Stand_ID, Stand_Name FROM Food_Stand ORDER BY Stand_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) Stands.Add((r.GetInt32(0), r.GetString(1)));

        await using (var cmd = new MySqlCommand("SELECT GiftShop_ID, GiftShop_Name FROM Gift_Shop ORDER BY GiftShop_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) Shops.Add((r.GetInt32(0), r.GetString(1)));

        if (Type != "Food" && Type != "GiftShop") return;
        OutletName = (IsFood ? Stands : Shops).FirstOrDefault(o => o.Id == OutletId).Name;
        if (OutletName == null) return;

        await using (var cmd = new MySqlCommand(
            "SELECT Customer_ID, CONCAT(Last_Name, ', ', First_Name) FROM Customer ORDER BY Last_Name, First_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) Customers.Add((r.GetInt32(0), r.GetString(1)));

        var sql = IsFood
            ? "SELECT FoodItem_ID, FoodItem_Name, Category, Price, NULL FROM Food_Item WHERE Stand_ID = @o ORDER BY Category, FoodItem_Name"
            : @"SELECT p.Product_ID, p.Product_Name, p.Category, p.Price, COALESCE(i.Quantity_In_Stock, 0)
                FROM Product p LEFT JOIN Inventory i ON i.Product_ID = p.Product_ID
                WHERE p.GiftShop_ID = @o ORDER BY p.Category, p.Product_Name";
        await using (var cmd = new MySqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@o", OutletId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                Items.Add(new ItemOption(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetDecimal(3),
                    r.IsDBNull(4) ? null : Convert.ToInt32(r.GetValue(4))));
            }
        }
    }

    public record ItemOption(int Id, string Name, string? Category, decimal Price, int? Stock);
}
