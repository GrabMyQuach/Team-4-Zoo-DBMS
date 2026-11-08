using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Shop;

// Food and gift shop sales with filters and totals. Click a row for the receipt.
public class SalesModel : PageModel
{
    private readonly IConfiguration _config;
    public SalesModel(IConfiguration config) { _config = config; }

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Type { get; set; }
    [BindProperty(SupportsGet = true)] public string? Payment { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<string> PaymentOptions { get; set; } = new();
    public List<SaleRow> Rows { get; set; } = new();
    public bool CanSell => SalesOptions.CanSell(User);

    public async Task OnGetAsync()
    {
        EndDate = (To ?? DateTime.Today).Date;
        StartDate = (From ?? DateTime.Today.AddDays(-7)).Date;

        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using (var cmd = new MySqlCommand("SELECT DISTINCT Payment_Method FROM Sale WHERE Payment_Method IS NOT NULL", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            var found = new List<string>();
            while (await r.ReadAsync()) found.Add(r.GetString(0));
            PaymentOptions = SalesOptions.WithExisting(found).ToList();
        }

        var sql = @"
            SELECT s.Sale_ID, s.Sale_Date, s.Sale_Type, COALESCE(fs.Stand_Name, gs.GiftShop_Name),
                   CONCAT(c.First_Name, ' ', c.Last_Name), CONCAT(e.First_Name, ' ', e.Last_Name),
                   s.Payment_Method, s.Total_Amount,
                   (SELECT COALESCE(SUM(si.Quantity), 0) FROM Sale_Item si WHERE si.Sale_ID = s.Sale_ID)
            FROM Sale s
            LEFT JOIN Food_Stand fs ON fs.Stand_ID = s.Stand_ID
            LEFT JOIN Gift_Shop gs ON gs.GiftShop_ID = s.GiftShop_ID
            LEFT JOIN Customer c ON c.Customer_ID = s.Customer_ID
            LEFT JOIN Employee e ON e.Employee_ID = s.Employee_ID
            WHERE s.Sale_Date >= @from AND s.Sale_Date < @to";
        if (Type == "Food" || Type == "GiftShop") sql += " AND s.Sale_Type = @type";
        if (!string.IsNullOrEmpty(Payment)) sql += " AND s.Payment_Method = @payment";
        sql += " ORDER BY s.Sale_Date DESC LIMIT 1000";

        await using var cmd2 = new MySqlCommand(sql, conn);
        cmd2.Parameters.AddWithValue("@from", StartDate);
        cmd2.Parameters.AddWithValue("@to", EndDate.AddDays(1));
        cmd2.Parameters.AddWithValue("@type", Type);
        cmd2.Parameters.AddWithValue("@payment", Payment);
        await using var rd = await cmd2.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            Rows.Add(new SaleRow
            {
                Id = rd.GetInt32(0),
                Date = rd.GetDateTime(1),
                Type = rd.GetString(2),
                Place = rd.IsDBNull(3) ? null : rd.GetString(3),
                Customer = rd.IsDBNull(4) ? null : rd.GetString(4),
                Cashier = rd.IsDBNull(5) ? null : rd.GetString(5),
                Payment = rd.IsDBNull(6) ? null : rd.GetString(6),
                Total = rd.GetDecimal(7),
                Items = Convert.ToInt32(rd.GetValue(8))
            });
        }
    }

    public class SaleRow
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Type { get; set; } = "";
        public string? Place { get; set; }
        public string? Customer { get; set; }
        public string? Cashier { get; set; }
        public string? Payment { get; set; }
        public decimal Total { get; set; }
        public int Items { get; set; }
    }
}
