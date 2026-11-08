using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Shop;

// Add or edit a supplier (DBA only)
[Authorize(Roles = SalesOptions.ManagerRoles)]
public class SupplierEditModel : PageModel
{
    private readonly IConfiguration _config;
    public SupplierEditModel(IConfiguration config) { _config = config; }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Address { get; set; }
    [BindProperty] public string? Delivery { get; set; }

    public string? ErrorMessage { get; set; }
    public bool IsNew => Id == null;

    public async Task<IActionResult> OnGetAsync()
    {
        if (IsNew) return Page();
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT Supplier_Name, Phone, Email, Address, Delivery_Schedule FROM Supplier WHERE Supplier_ID = @id", conn);
        cmd.Parameters.AddWithValue("@id", Id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return NotFound();
        Name = r.GetString(0);
        Phone = r.IsDBNull(1) ? null : r.GetString(1);
        Email = r.IsDBNull(2) ? null : r.GetString(2);
        Address = r.IsDBNull(3) ? null : r.GetString(3);
        Delivery = r.IsDBNull(4) ? null : r.GetString(4);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Name = (Name ?? "").Trim();
        if (Name.Length == 0) ErrorMessage = "Please enter a name.";
        else if (!string.IsNullOrWhiteSpace(Email) && !Regex.IsMatch(Email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            ErrorMessage = "That email address doesn't look right.";
        if (ErrorMessage != null) return Page();

        object Val(string? s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand(IsNew
            ? "INSERT INTO Supplier (Supplier_Name, Phone, Email, Address, Delivery_Schedule) VALUES (@name, @phone, @email, @addr, @del)"
            : "UPDATE Supplier SET Supplier_Name = @name, Phone = @phone, Email = @email, Address = @addr, Delivery_Schedule = @del WHERE Supplier_ID = @id", conn);
        cmd.Parameters.AddWithValue("@name", Name);
        cmd.Parameters.AddWithValue("@phone", Val(Phone));
        cmd.Parameters.AddWithValue("@email", Val(Email));
        cmd.Parameters.AddWithValue("@addr", Val(Address));
        cmd.Parameters.AddWithValue("@del", Val(Delivery));
        cmd.Parameters.AddWithValue("@id", Id ?? 0);
        await cmd.ExecuteNonQueryAsync();

        TempData["Success"] = $"{Name} was {(IsNew ? "added" : "updated")}.";
        return Redirect("/Shop/Suppliers");
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }
}
