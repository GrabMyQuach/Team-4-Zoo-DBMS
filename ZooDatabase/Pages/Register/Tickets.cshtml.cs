using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Register;

// Front-gate ticket register. Saves to Ticket_Sale + Ticket_Purchase_Item like the staff ticket page,
// for today's visit. Prices come from the database, never from the browser.
[Authorize(Roles = SalesOptions.SalesRoles)]
public class TicketsModel : PageModel
{
    private readonly IConfiguration _config;
    public TicketsModel(IConfiguration config) { _config = config; }

    public PosView Pos { get; set; } = new() { ShowCustomer = true };
    [BindProperty] public Dictionary<int, int> Qty { get; set; } = new();
    [BindProperty] public string PaymentMethod { get; set; } = "Cash";
    [BindProperty] public string? CustomerLookup { get; set; }
    [BindProperty] public string? FirstName { get; set; }
    [BindProperty] public string? LastName { get; set; }
    [TempData] public string? Done { get; set; }
    [TempData] public string? ReceiptUrl { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await using var conn = await RegisterData.OpenAsync(_config);
        await LoadAsync(conn);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = await RegisterData.OpenAsync(_config);
        await LoadAsync(conn);

        var lines = Pos.Items
            .Select(i => (Item: i, Count: Math.Clamp(Qty.GetValueOrDefault(i.Id), 0, i.Max)))
            .Where(x => x.Count > 0).ToList();
        if (lines.Count == 0) ErrorMessage = "Tap at least one ticket first.";
        else if (string.IsNullOrWhiteSpace(CustomerLookup)) ErrorMessage = "Enter the customer's email or phone number.";
        else if (!SalesOptions.CounterPayments.Contains(PaymentMethod)) ErrorMessage = "Choose a payment method.";
        if (ErrorMessage != null) return Page();

        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            var (customerId, error) = await RegisterData.CustomerForSaleAsync(conn, tx, CustomerLookup, FirstName, LastName);
            if (customerId == null)
            {
                await tx.RollbackAsync();
                ErrorMessage = error;
                return Page();
            }

            int saleId;
            await using (var cmd = new MySqlCommand(@"
                INSERT INTO Ticket_Sale (Customer_ID, Purchase_Date, Total_Amount, Payment_Method, Visit_Date)
                VALUES (@c, @now, 0, @pay, @today)", conn, tx))
            {
                // Houston time from the app (the database server's clock is UTC)
                cmd.Parameters.AddWithValue("@now", DateTime.Now);
                cmd.Parameters.AddWithValue("@today", DateTime.Today);
                cmd.Parameters.AddWithValue("@c", customerId.Value);
                cmd.Parameters.AddWithValue("@pay", PaymentMethod);
                await cmd.ExecuteNonQueryAsync();
                saleId = (int)cmd.LastInsertedId;
            }
            foreach (var (item, count) in lines)
            {
                await using var cmd = new MySqlCommand(@"
                    INSERT INTO Ticket_Purchase_Item (TicketPurchase_ID, TicketType_ID, Quantity, Unit_Price)
                    VALUES (@sale, @type, @qty, @price)", conn, tx);
                cmd.Parameters.AddWithValue("@sale", saleId);
                cmd.Parameters.AddWithValue("@type", item.Id);
                cmd.Parameters.AddWithValue("@qty", count);
                cmd.Parameters.AddWithValue("@price", item.Price);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();

            var total = lines.Sum(x => x.Count * x.Item.Price);
            ReceiptUrl = $"/Register/Receipt?Kind=t&Id={saleId}";
            Done = $"Ticket sale #{saleId} complete: {lines.Sum(x => x.Count)} ticket(s), {total:C}, paid by {PaymentMethod}.";
            return RedirectToPage();
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
        await using var cmd = new MySqlCommand(
            "SELECT TicketType_ID, TicketType_Name, Eligibility_Requirements, Price FROM Ticket_Type ORDER BY Price DESC, TicketType_Name", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            Pos.Items.Add(new PosItem(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetDecimal(3), Cart.MaxPerLine));
    }
}
