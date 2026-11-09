
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Tickets;

[Authorize(Roles = "Customer")]
public class BuyTicketsModel : PageModel
{
    private readonly IConfiguration _config;

    public BuyTicketsModel(IConfiguration config)
    {
        _config = config;
    }

    public int CustomerId { get; private set; }

    public List<TicketOption> TicketTypes { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public record TicketOption(
        int Id,
        string Name,
        decimal Price,
        string? Eligibility
    );

    public async Task<IActionResult> OnGetAsync()
    {
        // Identify the logged-in user.
        var userIdText = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdText, out int userId))
        {
            return Forbid();
        }

        await using var conn = new MySqlConnection(
            _config.GetConnectionString("ZooDb"));

        await conn.OpenAsync();

        // Find the customer profile linked to this login.
        await using (var cmd = new MySqlCommand(
            @"SELECT Customer_ID
              FROM Customer
              WHERE User_ID = @userId", conn))
        {
            cmd.Parameters.AddWithValue("@userId", userId);

            var result = await cmd.ExecuteScalarAsync();

            if (result == null)
            {
                ErrorMessage = "Customer profile not found.";
                return Page();
            }

            CustomerId = Convert.ToInt32(result);
        }

        // Load available ticket types and prices.
        await using (var cmd = new MySqlCommand(
            @"SELECT TicketType_ID,
                     TicketType_Name,
                     Price,
                     Eligibility_Requirements
              FROM Ticket_Type
              ORDER BY Price DESC, TicketType_Name", conn))
        {
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                TicketTypes.Add(new TicketOption(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetDecimal(2),
                    reader.IsDBNull(3)
                        ? null
                        : reader.GetString(3)
                ));
            }
        }

        return Page();
    }
    
    [BindProperty]
    public DateTime VisitDate { get; set; }

    [BindProperty]
    public Dictionary<int, int> Quantities { get; set; } = new();

    public async Task<IActionResult> OnPostAsync()
    {
        var userIdText = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdText, out int userId))
            return Forbid();

        if (VisitDate.Date < DateTime.Today)
        {
            ErrorMessage = "Please select a valid visit date.";
            await LoadTicketTypesAsync();
            return Page();
        }

        var selected = Quantities
            .Where(q => q.Value > 0)
            .ToList();

        if (selected.Count == 0 ||
            Quantities.Any(q => q.Value < 0 || q.Value > 50))
        {
            ErrorMessage = "Please select a valid ticket quantity.";
            await LoadTicketTypesAsync();
            return Page();
        }

        await using var conn = new MySqlConnection(
            _config.GetConnectionString("ZooDb"));

        await conn.OpenAsync();

        // Identify the customer using their login, not form data.
        await using (var cmd = new MySqlCommand(
            "SELECT Customer_ID FROM Customer WHERE User_ID = @userId",
            conn))
        {
            cmd.Parameters.AddWithValue("@userId", userId);

            var result = await cmd.ExecuteScalarAsync();

            if (result == null)
                return Forbid();

            CustomerId = Convert.ToInt32(result);
        }

        // Read trusted ticket prices from the database.
        var items = new List<(int Id, int Quantity, decimal Price)>();

        foreach (var selection in selected)
        {
            await using var cmd = new MySqlCommand(
                "SELECT Price FROM Ticket_Type WHERE TicketType_ID = @id",
                conn);

            cmd.Parameters.AddWithValue("@id", selection.Key);

            var result = await cmd.ExecuteScalarAsync();

            if (result == null)
            {
                ErrorMessage = "One of the selected tickets is invalid.";
                await LoadTicketTypesAsync();
                return Page();
            }

            items.Add((
                selection.Key,
                selection.Value,
                Convert.ToDecimal(result)
            ));
        }

        decimal total = items.Sum(
            i => i.Quantity * i.Price);

        await using var transaction =
            await conn.BeginTransactionAsync();

        try
        {
            // Create the ticket order.
            await using var saleCmd = new MySqlCommand(
                @"INSERT INTO Ticket_Sale
                    (Customer_ID, Purchase_Date, Total_Amount,
                     Payment_Method, Visit_Date)
                  VALUES
                    (@customerId, NOW(), @total,
                     @payment, @visitDate)",
                conn, transaction);

            saleCmd.Parameters.AddWithValue(
                "@customerId", CustomerId);
            saleCmd.Parameters.AddWithValue("@total", 0m);
            saleCmd.Parameters.AddWithValue(
                "@payment", DBNull.Value);
            saleCmd.Parameters.AddWithValue(
                "@visitDate", VisitDate.Date);

            await saleCmd.ExecuteNonQueryAsync();

            int saleId = Convert.ToInt32(
                saleCmd.LastInsertedId);

            // Save each selected ticket.
            foreach (var item in items)
            {
                await using var itemCmd = new MySqlCommand(
                    @"INSERT INTO Ticket_Purchase_Item
                        (TicketPurchase_ID, TicketType_ID,
                         Quantity, Unit_Price)
                      VALUES
                        (@saleId, @ticketId, @quantity, @price)",
                    conn, transaction);

                itemCmd.Parameters.AddWithValue(
                    "@saleId", saleId);
                itemCmd.Parameters.AddWithValue(
                    "@ticketId", item.Id);
                itemCmd.Parameters.AddWithValue(
                    "@quantity", item.Quantity);
                itemCmd.Parameters.AddWithValue(
                    "@price", item.Price);

                await itemCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

            TempData["OrderConfirmation"] =
                $"Your ticket order #{saleId} was created successfully.";

            return RedirectToPage("/Tickets/BuyTickets");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task LoadTicketTypesAsync()
    {
        TicketTypes.Clear();

        await using var conn = new MySqlConnection(
            _config.GetConnectionString("ZooDb"));

        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            @"SELECT TicketType_ID, TicketType_Name,
                     Price, Eligibility_Requirements
              FROM Ticket_Type
              ORDER BY Price DESC, TicketType_Name",
            conn);

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            TicketTypes.Add(new TicketOption(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetDecimal(2),
                reader.IsDBNull(3)
                    ? null
                    : reader.GetString(3)
            ));
        }
    }

}
