using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Shop;

// Food stands and gift shops work the same way (a place that sells things),
// so their list, details, and edit pages share the code in this file.
// "Food" outlets use Food_Stand / Food_Item; "GiftShop" outlets use Gift_Shop / Product.
public record OutletKind(string SaleType, string Table, string IdCol, string NameCol, string ItemTable, string ItemIdCol,
                         string ItemNameCol, string ItemFkCol, string Label, string Plural, string UrlBase)
{
    public static readonly OutletKind Food = new("Food", "Food_Stand", "Stand_ID", "Stand_Name", "Food_Item", "FoodItem_ID",
        "FoodItem_Name", "Stand_ID", "Food Stand", "Food Stands", "/Shop/FoodStand");

    public static readonly OutletKind Gift = new("GiftShop", "Gift_Shop", "GiftShop_ID", "GiftShop_Name", "Product", "Product_ID",
        "Product_Name", "GiftShop_ID", "Gift Shop", "Gift Shops", "/Shop/GiftShop");
}

// ---------- List ----------
public abstract class OutletListModel : PageModel
{
    private readonly IConfiguration _config;
    protected OutletListModel(IConfiguration config) { _config = config; }

    public abstract OutletKind Kind { get; }
    public List<OutletRow> Rows { get; set; } = new();
    public bool CanManage => SalesOptions.CanManage(User);

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        var k = Kind;

        // Sales in the last 30 days, per outlet, compared with the 30 days before that
        await using var cmd = new MySqlCommand($@"
            SELECT o.{k.IdCol}, o.{k.NameCol}, o.Category, o.Location, o.Opening_Time, o.Closing_Time,
                   (SELECT COUNT(*) FROM {k.ItemTable} i WHERE i.{k.ItemFkCol} = o.{k.IdCol}),
                   (SELECT COUNT(*) FROM Sale s WHERE s.{k.IdCol} = o.{k.IdCol} AND s.Sale_Date >= CURDATE() - INTERVAL 30 DAY),
                   (SELECT COALESCE(SUM(s.Total_Amount), 0) FROM Sale s WHERE s.{k.IdCol} = o.{k.IdCol} AND s.Sale_Date >= CURDATE() - INTERVAL 30 DAY),
                   (SELECT COALESCE(SUM(s.Total_Amount), 0) FROM Sale s WHERE s.{k.IdCol} = o.{k.IdCol}
                      AND s.Sale_Date >= CURDATE() - INTERVAL 60 DAY AND s.Sale_Date < CURDATE() - INTERVAL 30 DAY)
            FROM {k.Table} o
            ORDER BY o.{k.NameCol}", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            Rows.Add(new OutletRow
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                Category = r.IsDBNull(2) ? null : r.GetString(2),
                Location = r.IsDBNull(3) ? null : r.GetString(3),
                Opens = r.IsDBNull(4) ? null : r.GetTimeSpan(4),
                Closes = r.IsDBNull(5) ? null : r.GetTimeSpan(5),
                Items = Convert.ToInt32(r.GetValue(6)),
                Orders30 = Convert.ToInt32(r.GetValue(7)),
                Revenue30 = Convert.ToDecimal(r.GetValue(8)),
                RevenuePrev30 = Convert.ToDecimal(r.GetValue(9))
            });
        }
        Rows = Rows.OrderByDescending(x => x.Revenue30).ToList();
    }
}

public class OutletRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string? Location { get; set; }
    public TimeSpan? Opens { get; set; }
    public TimeSpan? Closes { get; set; }
    public int Items { get; set; }
    public int Orders30 { get; set; }
    public decimal Revenue30 { get; set; }
    public decimal RevenuePrev30 { get; set; }
    public decimal AvgOrder => Orders30 == 0 ? 0 : Math.Round(Revenue30 / Orders30, 2);
    public decimal? ChangePct => RevenuePrev30 == 0 ? null : Math.Round(100 * (Revenue30 - RevenuePrev30) / RevenuePrev30, 1);
    public string Hours => ShopText.Hours(Opens, Closes);
}

// ---------- Details ----------
public abstract class OutletDetailsModel : PageModel
{
    private readonly IConfiguration _config;
    protected OutletDetailsModel(IConfiguration config) { _config = config; }

    public abstract OutletKind Kind { get; }
    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public OutletRow Outlet { get; set; } = new();
    public string? Description { get; set; }
    public List<ItemSales> Items { get; set; } = new();
    public List<HourRow> ByHour { get; set; } = new();
    public List<SaleSummary> RecentSales { get; set; } = new();
    public bool CanManage => SalesOptions.CanManage(User);
    public bool CanSell => SalesOptions.CanSell(User);

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        var k = Kind;
        string descCol = k == OutletKind.Food ? "o.Description" : "NULL";

        await using (var cmd = new MySqlCommand($@"
            SELECT o.{k.NameCol}, o.Category, o.Location, o.Opening_Time, o.Closing_Time, {descCol},
                   (SELECT COUNT(*) FROM Sale s WHERE s.{k.IdCol} = o.{k.IdCol} AND s.Sale_Date >= CURDATE() - INTERVAL 30 DAY),
                   (SELECT COALESCE(SUM(s.Total_Amount), 0) FROM Sale s WHERE s.{k.IdCol} = o.{k.IdCol} AND s.Sale_Date >= CURDATE() - INTERVAL 30 DAY),
                   (SELECT COALESCE(SUM(s.Total_Amount), 0) FROM Sale s WHERE s.{k.IdCol} = o.{k.IdCol}
                      AND s.Sale_Date >= CURDATE() - INTERVAL 60 DAY AND s.Sale_Date < CURDATE() - INTERVAL 30 DAY)
            FROM {k.Table} o WHERE o.{k.IdCol} = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            Outlet = new OutletRow
            {
                Id = Id,
                Name = r.GetString(0),
                Category = r.IsDBNull(1) ? null : r.GetString(1),
                Location = r.IsDBNull(2) ? null : r.GetString(2),
                Opens = r.IsDBNull(3) ? null : r.GetTimeSpan(3),
                Closes = r.IsDBNull(4) ? null : r.GetTimeSpan(4),
                Orders30 = Convert.ToInt32(r.GetValue(6)),
                Revenue30 = Convert.ToDecimal(r.GetValue(7)),
                RevenuePrev30 = Convert.ToDecimal(r.GetValue(8))
            };
            Description = r.IsDBNull(5) ? null : r.GetString(5);
        }

        // Every item it sells, best sellers first (items that never sold still show, at the bottom)
        await using (var cmd = new MySqlCommand($@"
            SELECT i.{k.ItemIdCol}, i.{k.ItemNameCol}, i.Category, i.Price,
                   COALESCE(SUM(CASE WHEN s.Sale_ID IS NOT NULL THEN si.Quantity END), 0),
                   COALESCE(SUM(CASE WHEN s.Sale_ID IS NOT NULL THEN si.Quantity * si.Unit_Price END), 0)
            FROM {k.ItemTable} i
            LEFT JOIN Sale_Item si ON si.{k.ItemIdCol} = i.{k.ItemIdCol}
            LEFT JOIN Sale s ON s.Sale_ID = si.Sale_ID AND s.Sale_Date >= CURDATE() - INTERVAL 30 DAY
            WHERE i.{k.ItemFkCol} = @id
            GROUP BY i.{k.ItemIdCol}, i.{k.ItemNameCol}, i.Category, i.Price
            ORDER BY 6 DESC, i.{k.ItemNameCol}", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                Items.Add(new ItemSales(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                    r.GetDecimal(3), Convert.ToInt32(r.GetValue(4)), Convert.ToDecimal(r.GetValue(5))));
            }
        }

        // Busiest hours (last 30 days) to plan staffing
        await using (var cmd = new MySqlCommand($@"
            SELECT HOUR(s.Sale_Date), COUNT(*), SUM(s.Total_Amount)
            FROM Sale s WHERE s.{k.IdCol} = @id AND s.Sale_Date >= CURDATE() - INTERVAL 30 DAY
            GROUP BY HOUR(s.Sale_Date) ORDER BY 1", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                ByHour.Add(new HourRow(Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)), Convert.ToDecimal(r.GetValue(2))));
        }

        await using (var cmd = new MySqlCommand($@"
            SELECT s.Sale_ID, s.Sale_Date, s.Total_Amount, s.Payment_Method,
                   (SELECT COALESCE(SUM(si.Quantity), 0) FROM Sale_Item si WHERE si.Sale_ID = s.Sale_ID)
            FROM Sale s WHERE s.{k.IdCol} = @id
            ORDER BY s.Sale_Date DESC LIMIT 10", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                RecentSales.Add(new SaleSummary(r.GetInt32(0), r.GetDateTime(1), r.GetDecimal(2), r.IsDBNull(3) ? null : r.GetString(3), Convert.ToInt32(r.GetValue(4))));
        }

        return Page();
    }
}

public record ItemSales(int Id, string Name, string? Category, decimal Price, int Sold30, decimal Revenue30);
public record HourRow(int Hour, int Orders, decimal Revenue);
public record SaleSummary(int Id, DateTime Date, decimal Total, string? Payment, int Items);

// ---------- Add / Edit (DBA only) ----------
[Authorize(Roles = SalesOptions.ManagerRoles)]
public abstract class OutletEditModel : PageModel
{
    private readonly IConfiguration _config;
    protected OutletEditModel(IConfiguration config) { _config = config; }

    public abstract OutletKind Kind { get; }
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Category { get; set; }
    [BindProperty] public string? Location { get; set; }
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public TimeSpan? Opens { get; set; }
    [BindProperty] public TimeSpan? Closes { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsNew => Id == null;
    public bool HasDescription => Kind == OutletKind.Food;

    public async Task<IActionResult> OnGetAsync()
    {
        if (IsNew)
        {
            Opens = new TimeSpan(9, 0, 0);
            Closes = new TimeSpan(17, 0, 0);
            return Page();
        }
        var k = Kind;
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand($@"
            SELECT {k.NameCol}, Category, Location, Opening_Time, Closing_Time, {(HasDescription ? "Description" : "NULL")}
            FROM {k.Table} WHERE {k.IdCol} = @id", conn);
        cmd.Parameters.AddWithValue("@id", Id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return NotFound();
        Name = r.GetString(0);
        Category = r.IsDBNull(1) ? null : r.GetString(1);
        Location = r.IsDBNull(2) ? null : r.GetString(2);
        Opens = r.IsDBNull(3) ? null : r.GetTimeSpan(3);
        Closes = r.IsDBNull(4) ? null : r.GetTimeSpan(4);
        Description = r.IsDBNull(5) ? null : r.GetString(5);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var k = Kind;
        Name = (Name ?? "").Trim();
        if (Name.Length == 0) ErrorMessage = "Please enter a name.";
        else if (Opens != null && Closes != null && Closes <= Opens) ErrorMessage = "Closing time must be after opening time.";
        if (ErrorMessage != null) return Page();

        object Val(string? s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
        string descSet = HasDescription ? ", Description = @desc" : "";
        string descCol = HasDescription ? ", Description" : "";
        string descVal = HasDescription ? ", @desc" : "";

        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(IsNew
            ? $"INSERT INTO {k.Table} ({k.NameCol}, Category, Location, Opening_Time, Closing_Time{descCol}) VALUES (@name, @cat, @loc, @open, @close{descVal})"
            : $"UPDATE {k.Table} SET {k.NameCol} = @name, Category = @cat, Location = @loc, Opening_Time = @open, Closing_Time = @close{descSet} WHERE {k.IdCol} = @id", conn);
        cmd.Parameters.AddWithValue("@name", Name);
        cmd.Parameters.AddWithValue("@cat", Val(Category));
        cmd.Parameters.AddWithValue("@loc", Val(Location));
        cmd.Parameters.AddWithValue("@open", (object?)Opens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@close", (object?)Closes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@desc", Val(Description));
        cmd.Parameters.AddWithValue("@id", Id ?? 0);
        await cmd.ExecuteNonQueryAsync();

        var newId = IsNew ? (int)cmd.LastInsertedId : Id!.Value;
        TempData["Success"] = $"{Name} was {(IsNew ? "added" : "updated")}.";
        return Redirect($"{k.UrlBase}Details/{newId}");
    }
}

public static class ShopText
{
    public static string Time(TimeSpan t) => DateTime.Today.Add(t).ToString("h:mm tt");

    public static string Hours(TimeSpan? open, TimeSpan? close) =>
        open == null || close == null ? "—" : $"{Time(open.Value)} - {Time(close.Value)}";

    public static string HourLabel(int hour) => DateTime.Today.AddHours(hour).ToString("h tt");

    // Looks up the employee linked to the logged-in user, so sales record who rang them up
    public static async Task<int?> CurrentEmployeeIdAsync(MySqlConnection conn, string? username)
    {
        if (string.IsNullOrEmpty(username)) return null;
        await using var cmd = new MySqlCommand("SELECT Employee_ID FROM User_Account WHERE Username = @u", conn);
        cmd.Parameters.AddWithValue("@u", username);
        var v = await cmd.ExecuteScalarAsync();
        return v is null or DBNull ? null : Convert.ToInt32(v);
    }
}
