using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Tickets;

// Add or edit an attraction (DBA only)
[Authorize(Roles = SalesOptions.ManagerRoles)]
public class AttractionEditModel : PageModel
{
    private readonly IConfiguration _config;

    public AttractionEditModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Type { get; set; }
    [BindProperty] public string? Location { get; set; }
    [BindProperty] public decimal? Price { get; set; }
    [BindProperty] public string? Status { get; set; } = "Open";

    public string? ErrorMessage { get; set; }
    public bool IsNew => Id == null;

    public async Task<IActionResult> OnGetAsync()
    {
        if (IsNew) return Page();
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT Attraction_Name, Attraction_Type, Location, Price, Status FROM Attraction WHERE Attraction_ID = @id", conn);
        cmd.Parameters.AddWithValue("@id", Id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return NotFound();
        Name = r.GetString(0);
        Type = r.IsDBNull(1) ? null : r.GetString(1);
        Location = r.IsDBNull(2) ? null : r.GetString(2);
        Price = r.IsDBNull(3) ? null : r.GetDecimal(3);
        Status = r.IsDBNull(4) ? null : r.GetString(4);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Name = (Name ?? "").Trim();
        if (Name.Length == 0) ErrorMessage = "Please enter a name.";
        else if (Price < 0) ErrorMessage = "Price can't be negative.";
        if (ErrorMessage != null) return Page();

        object Val(string? s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : s.Trim();

        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand(IsNew
            ? "INSERT INTO Attraction (Attraction_Name, Attraction_Type, Location, Price, Status) VALUES (@name, @type, @loc, @price, @status)"
            : "UPDATE Attraction SET Attraction_Name = @name, Attraction_Type = @type, Location = @loc, Price = @price, Status = @status WHERE Attraction_ID = @id", conn);
        cmd.Parameters.AddWithValue("@name", Name);
        cmd.Parameters.AddWithValue("@type", Val(Type));
        cmd.Parameters.AddWithValue("@loc", Val(Location));
        cmd.Parameters.AddWithValue("@price", (object?)Price ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", Val(Status));
        cmd.Parameters.AddWithValue("@id", Id ?? 0);
        await cmd.ExecuteNonQueryAsync();

        TempData["Success"] = $"{Name} was {(IsNew ? "added" : "updated")}.";
        return Redirect("/Tickets/Attractions");
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }
}
