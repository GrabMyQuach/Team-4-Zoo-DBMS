using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Tickets;

// Ring up a ticket order: pick the customer, the visit date, and how many of each ticket.
// Prices come from Ticket_Type (never from the form), and Tien's trigger adds up the total.
[Authorize(Roles = SalesOptions.SalesRoles)]
public class TicketSaleNewModel : PageModel
{
    private readonly IConfiguration _config;

    public TicketSaleNewModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int CustomerId { get; set; }
    [BindProperty] public DateTime VisitDate { get; set; } = DateTime.Today;
    [BindProperty] public string? PaymentMethod { get; set; }
    [BindProperty] public Dictionary<int, int> Quantities { get; set; } = new();

    public List<(int Id, string Name)> Customers { get; set; } = new();
    public List<TicketTypeOption> TicketTypes { get; set; } = new();
    public string? ErrorMessage { get; set; }

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

        var lines = TicketTypes
            .Select(t => (Type: t, Qty: Quantities.TryGetValue(t.Id, out var q) ? q : 0))
            .Where(x => x.Qty != 0)
            .ToList();

        if (CustomerId <= 0) ErrorMessage = "Please choose a customer.";
        else if (VisitDate.Date < DateTime.Today) ErrorMessage = "Visit date can't be in the past.";
        else if (VisitDate.Date > DateTime.Today.AddYears(1)) ErrorMessage = "Visit date must be within the next year.";
        else if (lines.Any(x => x.Qty < 0 || x.Qty > 50)) ErrorMessage = "Ticket quantities must be between 0 and 50.";
        else if (lines.Count == 0) ErrorMessage = "Add at least one ticket.";
        else if (string.IsNullOrEmpty(PaymentMethod)) ErrorMessage = "Please choose a payment method.";

        if (ErrorMessage != null) return Page();

        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            int saleId;
            await using (var cmd = new MySqlCommand(@"
                INSERT INTO Ticket_Sale (Customer_ID, Purchase_Date, Total_Amount, Payment_Method, Visit_Date)
                VALUES (@customer, NOW(), 0, @payment, @visit)", conn, tx))
            {
                cmd.Parameters.AddWithValue("@customer", CustomerId);
                cmd.Parameters.AddWithValue("@payment", PaymentMethod);
                cmd.Parameters.AddWithValue("@visit", VisitDate.Date);
                await cmd.ExecuteNonQueryAsync();
                saleId = (int)cmd.LastInsertedId;
            }

            foreach (var (type, qty) in lines)
            {
                await using var cmd = new MySqlCommand(@"
                    INSERT INTO Ticket_Purchase_Item (TicketPurchase_ID, TicketType_ID, Quantity, Unit_Price)
                    VALUES (@sale, @type, @qty, @price)", conn, tx);
                cmd.Parameters.AddWithValue("@sale", saleId);
                cmd.Parameters.AddWithValue("@type", type.Id);
                cmd.Parameters.AddWithValue("@qty", qty);
                cmd.Parameters.AddWithValue("@price", type.Price);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            TempData["Success"] = $"Ticket order #{saleId} saved.";
            return Redirect($"/Tickets/TicketSaleDetails/{saleId}");
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
        Customers.Clear();
        TicketTypes.Clear();

        await using (var cmd = new MySqlCommand(
            "SELECT Customer_ID, CONCAT(Last_Name, ', ', First_Name, COALESCE(CONCAT(' (', Email, ')'), '')) FROM Customer ORDER BY Last_Name, First_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) Customers.Add((r.GetInt32(0), r.GetString(1)));
        }

        await using (var cmd = new MySqlCommand(
            "SELECT TicketType_ID, TicketType_Name, Price, Eligibility_Requirements FROM Ticket_Type ORDER BY Price DESC, TicketType_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                TicketTypes.Add(new TicketTypeOption(r.GetInt32(0), r.GetString(1), r.GetDecimal(2), r.IsDBNull(3) ? null : r.GetString(3)));
        }
    }

    public record TicketTypeOption(int Id, string Name, decimal Price, string? Eligibility);
}
