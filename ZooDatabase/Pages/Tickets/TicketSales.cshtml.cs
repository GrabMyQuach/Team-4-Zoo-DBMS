using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Tickets;

// Ticket sales by visit date, with filters and totals. Click a row for the full order.
public class TicketSalesModel : PageModel
{
    private readonly IConfiguration _config;

    public TicketSalesModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Payment { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<string> PaymentOptions { get; set; } = new();
    public List<SaleRow> Rows { get; set; } = new();
    public bool CanSell => SalesOptions.CanSell(User);

    public int TotalTickets => Rows.Sum(r => r.Tickets);
    public decimal TotalRevenue => Rows.Sum(r => r.Total);

    public async Task OnGetAsync()
    {
        EndDate = (To ?? DateTime.Today.AddDays(7)).Date;
        StartDate = (From ?? DateTime.Today.AddDays(-30)).Date;

        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using (var cmd = new MySqlCommand("SELECT DISTINCT Payment_Method FROM Ticket_Sale WHERE Payment_Method IS NOT NULL", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            var found = new List<string>();
            while (await r.ReadAsync()) found.Add(r.GetString(0));
            PaymentOptions = SalesOptions.WithExisting(found).ToList();
        }

        var sql = @"
            SELECT ts.TicketPurchase_ID, ts.Visit_Date, ts.Purchase_Date, CONCAT(c.First_Name, ' ', c.Last_Name),
                   ts.Payment_Method, ts.Total_Amount,
                   COALESCE(SUM(i.Quantity), 0),
                   GROUP_CONCAT(CONCAT(i.Quantity, ' ', tt.TicketType_Name) ORDER BY tt.TicketType_Name SEPARATOR ', ')
            FROM Ticket_Sale ts
            JOIN Customer c ON c.Customer_ID = ts.Customer_ID
            LEFT JOIN Ticket_Purchase_Item i ON i.TicketPurchase_ID = ts.TicketPurchase_ID
            LEFT JOIN Ticket_Type tt ON tt.TicketType_ID = i.TicketType_ID
            WHERE ts.Visit_Date BETWEEN @from AND @to";
        if (!string.IsNullOrEmpty(Payment)) sql += " AND ts.Payment_Method = @payment";
        if (!string.IsNullOrWhiteSpace(Search)) sql += " AND CONCAT(c.First_Name, ' ', c.Last_Name) LIKE @search";
        sql += @"
            GROUP BY ts.TicketPurchase_ID, ts.Visit_Date, ts.Purchase_Date, c.First_Name, c.Last_Name, ts.Payment_Method, ts.Total_Amount
            ORDER BY ts.Visit_Date DESC, ts.Purchase_Date DESC
            LIMIT 500";

        await using var cmd2 = new MySqlCommand(sql, conn);
        cmd2.Parameters.AddWithValue("@from", StartDate);
        cmd2.Parameters.AddWithValue("@to", EndDate);
        cmd2.Parameters.AddWithValue("@payment", Payment);
        cmd2.Parameters.AddWithValue("@search", "%" + Search + "%");

        await using var reader = await cmd2.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Rows.Add(new SaleRow
            {
                Id = reader.GetInt32(0),
                VisitDate = reader.GetDateTime(1),
                PurchaseDate = reader.GetDateTime(2),
                Customer = reader.GetString(3),
                Payment = reader.IsDBNull(4) ? null : reader.GetString(4),
                Total = reader.GetDecimal(5),
                Tickets = Convert.ToInt32(reader.GetValue(6)),
                Items = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }
    }

    public class SaleRow
    {
        public int Id { get; set; }
        public DateTime VisitDate { get; set; }
        public DateTime PurchaseDate { get; set; }
        public string Customer { get; set; } = "";
        public string? Payment { get; set; }
        public decimal Total { get; set; }
        public int Tickets { get; set; }
        public string? Items { get; set; }
    }
}
