using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Shop;

// Every food item across all stands, with 30-day sales, so slow sellers stand out
public class MenuItemsModel : PageModel
{
    private readonly IConfiguration _config;
    public MenuItemsModel(IConfiguration config) { _config = config; }

    [BindProperty(SupportsGet = true)] public int? StandId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Category { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }

    public List<(int Id, string Name)> Stands { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public List<Row> Rows { get; set; } = new();
    public bool CanManage => SalesOptions.CanManage(User);

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using (var cmd = new MySqlCommand("SELECT Stand_ID, Stand_Name FROM Food_Stand ORDER BY Stand_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) Stands.Add((r.GetInt32(0), r.GetString(1)));

        await using (var cmd = new MySqlCommand("SELECT DISTINCT Category FROM Food_Item WHERE Category IS NOT NULL ORDER BY Category", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) Categories.Add(r.GetString(0));

        var sql = @"
            SELECT f.FoodItem_ID, f.FoodItem_Name, f.Category, f.Price, fs.Stand_Name,
                   COALESCE(SUM(CASE WHEN s.Sale_ID IS NOT NULL THEN si.Quantity END), 0),
                   COALESCE(SUM(CASE WHEN s.Sale_ID IS NOT NULL THEN si.Quantity * si.Unit_Price END), 0)
            FROM Food_Item f
            JOIN Food_Stand fs ON fs.Stand_ID = f.Stand_ID
            LEFT JOIN Sale_Item si ON si.FoodItem_ID = f.FoodItem_ID
            LEFT JOIN Sale s ON s.Sale_ID = si.Sale_ID AND s.Sale_Date >= CURDATE() - INTERVAL 30 DAY
            WHERE 1 = 1";
        if (StandId != null) sql += " AND f.Stand_ID = @stand";
        if (!string.IsNullOrEmpty(Category)) sql += " AND f.Category = @cat";
        if (!string.IsNullOrWhiteSpace(Search)) sql += " AND f.FoodItem_Name LIKE @search";
        sql += " GROUP BY f.FoodItem_ID, f.FoodItem_Name, f.Category, f.Price, fs.Stand_Name ORDER BY 7 DESC";

        await using var cmd2 = new MySqlCommand(sql, conn);
        cmd2.Parameters.AddWithValue("@stand", StandId);
        cmd2.Parameters.AddWithValue("@cat", Category);
        cmd2.Parameters.AddWithValue("@search", "%" + Search + "%");
        await using var rd = await cmd2.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            Rows.Add(new Row(rd.GetInt32(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.GetDecimal(3),
                rd.GetString(4), Convert.ToInt32(rd.GetValue(5)), Convert.ToDecimal(rd.GetValue(6))));
        }
    }

    public record Row(int Id, string Name, string? Category, decimal Price, string Stand, int Sold30, decimal Revenue30);
}
