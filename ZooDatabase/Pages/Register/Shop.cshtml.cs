using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;
using ZooDatabase.Pages.Shop;

namespace ZooDatabase.Pages.Register;

// Food stand / gift shop register. The cashier picks their location once (remembered in a cookie),
// then taps items. Saves to Sale + Sale_Item like the staff New Sale page; the stock trigger
// subtracts gift shop inventory and refuses the sale if there isn't enough.
[Authorize(Roles = SalesOptions.SalesRoles)]
public class ShopRegisterModel : PageModel
{
    private readonly IConfiguration _config;
    public ShopRegisterModel(IConfiguration config) { _config = config; }

    private const string LocationCookie = "RegisterLocation";

    [BindProperty(SupportsGet = true)] public string? Location { get; set; }   // "f:3" = food stand 3, "g:2" = gift shop 2
    [BindProperty] public Dictionary<int, int> Qty { get; set; } = new();
    [BindProperty] public string PaymentMethod { get; set; } = "Cash";
    [TempData] public string? Done { get; set; }
    [TempData] public string? ReceiptUrl { get; set; }
    public string? ErrorMessage { get; set; }

    public List<(string Key, string Name, string Kind)> Locations { get; set; } = new();
    public string? LocationName { get; set; }
    public PosView Pos { get; set; } = new();

    private bool IsFood => Location?.StartsWith("f:") == true;
    private int LocationId => Location is { Length: > 2 } && int.TryParse(Location[2..], out var id) ? id : 0;

    public async Task<IActionResult> OnGetAsync(bool change = false)
    {
        if (change)
        {
            Response.Cookies.Delete(LocationCookie);
            return RedirectToPage();
        }
        Location ??= Request.Cookies[LocationCookie];

        await using var conn = await RegisterData.OpenAsync(_config);
        await LoadAsync(conn);
        if (LocationName != null)
            Response.Cookies.Append(LocationCookie, Location!, new CookieOptions { Expires = DateTimeOffset.Now.AddHours(12), HttpOnly = true, IsEssential = true });
        else
            Location = null;   // unknown or missing location: show the picker
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = await RegisterData.OpenAsync(_config);
        await LoadAsync(conn);
        if (LocationName == null) return RedirectToPage();

        var lines = Pos.Items
            .Select(i => (Item: i, Count: Math.Max(0, Qty.GetValueOrDefault(i.Id))))
            .Where(x => x.Count > 0).ToList();
        var tooMany = lines.FirstOrDefault(x => x.Count > x.Item.Max);
        if (lines.Count == 0) ErrorMessage = "Tap at least one item first.";
        else if (tooMany.Item != null) ErrorMessage = $"Only {tooMany.Item.Max} {tooMany.Item.Name} left in stock.";
        else if (!SalesOptions.CounterPayments.Contains(PaymentMethod)) ErrorMessage = "Choose a payment method.";
        if (ErrorMessage != null) return Page();

        var cashier = await ShopText.CurrentEmployeeIdAsync(conn, User.Identity?.Name);
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            int saleId;
            await using (var cmd = new MySqlCommand($@"
                INSERT INTO Sale (Sale_Date, Employee_ID, Customer_ID, {(IsFood ? "Stand_ID" : "GiftShop_ID")}, Sale_Type, Total_Amount, Payment_Method)
                VALUES (@now, @emp, NULL, @loc, @type, 0, @pay)", conn, tx))
            {
                cmd.Parameters.AddWithValue("@now", DateTime.Now);
                cmd.Parameters.AddWithValue("@emp", (object?)cashier ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@loc", LocationId);
                cmd.Parameters.AddWithValue("@type", IsFood ? "Food" : "GiftShop");
                cmd.Parameters.AddWithValue("@pay", PaymentMethod);
                await cmd.ExecuteNonQueryAsync();
                saleId = (int)cmd.LastInsertedId;
            }
            foreach (var (item, count) in lines)
            {
                await using var cmd = new MySqlCommand($@"
                    INSERT INTO Sale_Item (Sale_ID, {(IsFood ? "FoodItem_ID" : "Product_ID")}, Quantity, Unit_Price)
                    VALUES (@sale, @item, @qty, @price)", conn, tx);
                cmd.Parameters.AddWithValue("@sale", saleId);
                cmd.Parameters.AddWithValue("@item", item.Id);
                cmd.Parameters.AddWithValue("@qty", count);
                cmd.Parameters.AddWithValue("@price", item.Price);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();

            var total = lines.Sum(x => x.Count * x.Item.Price);
            ReceiptUrl = $"/Register/Receipt?Kind=s&Id={saleId}";
            Done = $"Sale #{saleId} complete at {LocationName}: {lines.Sum(x => x.Count)} item(s), {total:C}, paid by {PaymentMethod}.";
            return RedirectToPage(new { Location });
        }
        catch (MySqlException ex)
        {
            await tx.RollbackAsync();
            ErrorMessage = ex.SqlState == "45000" ? ex.Message : "The sale couldn't be saved. Please try again.";
            return Page();
        }
    }

    private async Task LoadAsync(MySqlConnection conn)
    {
        await using (var cmd = new MySqlCommand(@"
            SELECT CONCAT('f:', Stand_ID), Stand_Name, 'Food stand' FROM Food_Stand
            UNION ALL
            SELECT CONCAT('g:', GiftShop_ID), GiftShop_Name, 'Gift shop' FROM Gift_Shop
            ORDER BY 3, 2", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) Locations.Add((r.GetString(0), r.GetString(1), r.GetString(2)));

        LocationName = Locations.FirstOrDefault(l => l.Key == Location).Name;
        if (LocationName == null) return;

        Pos.Hidden["Location"] = Location!;
        var sql = IsFood
            ? "SELECT FoodItem_ID, FoodItem_Name, Category, Price, 999 FROM Food_Item WHERE Stand_ID = @o ORDER BY Category, FoodItem_Name"
            : @"SELECT p.Product_ID, p.Product_Name, p.Category, p.Price, COALESCE(i.Quantity_In_Stock, 0)
                FROM Product p LEFT JOIN Inventory i ON i.Product_ID = p.Product_ID
                WHERE p.GiftShop_ID = @o ORDER BY p.Category, p.Product_Name";
        await using var items = new MySqlCommand(sql, conn);
        items.Parameters.AddWithValue("@o", LocationId);
        await using var ir = await items.ExecuteReaderAsync();
        while (await ir.ReadAsync())
            Pos.Items.Add(new PosItem(ir.GetInt32(0), ir.GetString(1), ir.IsDBNull(2) ? null : ir.GetString(2),
                                      ir.GetDecimal(3), Convert.ToInt32(ir.GetValue(4))));
    }
}
