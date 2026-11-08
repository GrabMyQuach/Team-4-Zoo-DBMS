using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Tickets;

// Add or edit a ticket type and which attractions it includes (DBA only)
[Authorize(Roles = SalesOptions.ManagerRoles)]
public class TicketTypeEditModel : PageModel
{
    private readonly IConfiguration _config;

    public TicketTypeEditModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public decimal? Price { get; set; }
    [BindProperty] public string? Eligibility { get; set; }
    [BindProperty] public List<int> AttractionIds { get; set; } = new();

    public List<(int Id, string Name)> Attractions { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public bool IsNew => Id == null;

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = await OpenAsync();
        if (!IsNew)
        {
            await using (var cmd = new MySqlCommand(
                "SELECT TicketType_Name, Price, Eligibility_Requirements FROM Ticket_Type WHERE TicketType_ID = @id", conn))
            {
                cmd.Parameters.AddWithValue("@id", Id);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return NotFound();
                Name = r.GetString(0);
                Price = r.GetDecimal(1);
                Eligibility = r.IsDBNull(2) ? null : r.GetString(2);
            }
            await using (var cmd = new MySqlCommand("SELECT Attraction_ID FROM Ticket_Attraction WHERE TicketType_ID = @id", conn))
            {
                cmd.Parameters.AddWithValue("@id", Id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) AttractionIds.Add(r.GetInt32(0));
            }
        }
        await LoadAttractionsAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = await OpenAsync();
        Name = (Name ?? "").Trim();

        if (Name.Length == 0) ErrorMessage = "Please enter a name.";
        else if (Price == null || Price < 0) ErrorMessage = "Please enter a price of $0 or more.";

        if (ErrorMessage != null)
        {
            await LoadAttractionsAsync(conn);
            return Page();
        }

        await using var tx = await conn.BeginTransactionAsync();
        int typeId;
        await using (var cmd = new MySqlCommand(IsNew
            ? "INSERT INTO Ticket_Type (TicketType_Name, Price, Eligibility_Requirements) VALUES (@name, @price, @elig)"
            : "UPDATE Ticket_Type SET TicketType_Name = @name, Price = @price, Eligibility_Requirements = @elig WHERE TicketType_ID = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("@name", Name);
            cmd.Parameters.AddWithValue("@price", Price);
            cmd.Parameters.AddWithValue("@elig", string.IsNullOrWhiteSpace(Eligibility) ? DBNull.Value : (object)Eligibility.Trim());
            cmd.Parameters.AddWithValue("@id", Id ?? 0);
            await cmd.ExecuteNonQueryAsync();
            typeId = IsNew ? (int)cmd.LastInsertedId : Id!.Value;
        }

        // Replace the included attractions with what was checked
        await using (var cmd = new MySqlCommand("DELETE FROM Ticket_Attraction WHERE TicketType_ID = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("@id", typeId);
            await cmd.ExecuteNonQueryAsync();
        }
        foreach (var a in AttractionIds.Distinct())
        {
            await using var cmd = new MySqlCommand("INSERT INTO Ticket_Attraction (TicketType_ID, Attraction_ID) VALUES (@id, @a)", conn, tx);
            cmd.Parameters.AddWithValue("@id", typeId);
            cmd.Parameters.AddWithValue("@a", a);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();

        TempData["Success"] = $"{Name} was {(IsNew ? "added" : "updated")}. Price changes apply to new sales only.";
        return Redirect("/Tickets/TicketTypes");
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }

    private async Task LoadAttractionsAsync(MySqlConnection conn)
    {
        Attractions.Clear();
        await using var cmd = new MySqlCommand("SELECT Attraction_ID, Attraction_Name FROM Attraction ORDER BY Attraction_Name", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) Attractions.Add((r.GetInt32(0), r.GetString(1)));
    }
}
