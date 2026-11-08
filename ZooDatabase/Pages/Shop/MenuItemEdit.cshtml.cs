using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Shop;

// Add or edit a food item (DBA only)
[Authorize(Roles = SalesOptions.ManagerRoles)]
public class MenuItemEditModel : PageModel
{
    private readonly IConfiguration _config;
    public MenuItemEditModel(IConfiguration config) { _config = config; }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty(SupportsGet = true)] public int StandId { get; set; }
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Category { get; set; }
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public decimal? Price { get; set; }

    public List<(int Id, string Name)> Stands { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public bool IsNew => Id == null;

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = await OpenAsync();
        if (!IsNew)
        {
            await using var cmd = new MySqlCommand("SELECT Stand_ID, FoodItem_Name, Category, Description, Price FROM Food_Item WHERE FoodItem_ID = @id", conn);
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            StandId = r.GetInt32(0);
            Name = r.GetString(1);
            Category = r.IsDBNull(2) ? null : r.GetString(2);
            Description = r.IsDBNull(3) ? null : r.GetString(3);
            Price = r.GetDecimal(4);
        }
        await LoadStandsAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = await OpenAsync();
        Name = (Name ?? "").Trim();
        if (StandId <= 0) ErrorMessage = "Please choose a food stand.";
        else if (Name.Length == 0) ErrorMessage = "Please enter a name.";
        else if (Price == null || Price < 0) ErrorMessage = "Please enter a price of $0 or more.";
        if (ErrorMessage != null)
        {
            await LoadStandsAsync(conn);
            return Page();
        }

        object Val(string? s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
        await using var cmd = new MySqlCommand(IsNew
            ? "INSERT INTO Food_Item (Stand_ID, FoodItem_Name, Category, Description, Price) VALUES (@stand, @name, @cat, @desc, @price)"
            : "UPDATE Food_Item SET Stand_ID = @stand, FoodItem_Name = @name, Category = @cat, Description = @desc, Price = @price WHERE FoodItem_ID = @id", conn);
        cmd.Parameters.AddWithValue("@stand", StandId);
        cmd.Parameters.AddWithValue("@name", Name);
        cmd.Parameters.AddWithValue("@cat", Val(Category));
        cmd.Parameters.AddWithValue("@desc", Val(Description));
        cmd.Parameters.AddWithValue("@price", Price);
        cmd.Parameters.AddWithValue("@id", Id ?? 0);
        await cmd.ExecuteNonQueryAsync();

        TempData["Success"] = $"{Name} was {(IsNew ? "added" : "updated")}.";
        return Redirect("/Shop/MenuItems");
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }

    private async Task LoadStandsAsync(MySqlConnection conn)
    {
        Stands.Clear();
        await using var cmd = new MySqlCommand("SELECT Stand_ID, Stand_Name FROM Food_Stand ORDER BY Stand_Name", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) Stands.Add((r.GetInt32(0), r.GetString(1)));
    }
}
