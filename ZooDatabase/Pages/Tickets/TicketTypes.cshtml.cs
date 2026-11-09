using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Tickets;

// Ticket types with what they include and how well they are selling (last 30 days of visits)
public class TicketTypesModel : PageModel
{
    private readonly IConfiguration _config;

    public TicketTypesModel(IConfiguration config)
    {
        _config = config;
    }

    public List<TypeRow> Rows { get; set; } = new();
    public bool CanManage => SalesOptions.CanManage(User);
    public int TotalSold => Rows.Sum(r => r.Sold30);

    // Cashiers don't see ticket revenue; send them to their register
    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        if (SalesOptions.IsCashierOnly(User)) context.Result = Redirect("/Register/Tickets");
    }

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            SELECT tt.TicketType_ID, tt.TicketType_Name, tt.Price, tt.Eligibility_Requirements,
                   (SELECT GROUP_CONCAT(a.Attraction_Name ORDER BY a.Attraction_Name SEPARATOR ', ')
                      FROM Ticket_Attraction ta JOIN Attraction a ON a.Attraction_ID = ta.Attraction_ID
                     WHERE ta.TicketType_ID = tt.TicketType_ID),
                   (SELECT COALESCE(SUM(i.Quantity), 0) FROM Ticket_Purchase_Item i
                      JOIN Ticket_Sale ts ON ts.TicketPurchase_ID = i.TicketPurchase_ID
                     WHERE i.TicketType_ID = tt.TicketType_ID
                       AND ts.Visit_Date BETWEEN CURDATE() - INTERVAL 30 DAY AND CURDATE()),
                   (SELECT COALESCE(SUM(i.Quantity * i.Unit_Price), 0) FROM Ticket_Purchase_Item i
                      JOIN Ticket_Sale ts ON ts.TicketPurchase_ID = i.TicketPurchase_ID
                     WHERE i.TicketType_ID = tt.TicketType_ID
                       AND ts.Visit_Date BETWEEN CURDATE() - INTERVAL 30 DAY AND CURDATE())
            FROM Ticket_Type tt
            ORDER BY tt.Price DESC, tt.TicketType_Name", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            Rows.Add(new TypeRow
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                Price = r.GetDecimal(2),
                Eligibility = r.IsDBNull(3) ? null : r.GetString(3),
                Attractions = r.IsDBNull(4) ? null : r.GetString(4),
                Sold30 = Convert.ToInt32(r.GetValue(5)),
                Revenue30 = Convert.ToDecimal(r.GetValue(6))
            });
        }
    }

    public class TypeRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public string? Eligibility { get; set; }
        public string? Attractions { get; set; }
        public int Sold30 { get; set; }
        public decimal Revenue30 { get; set; }
    }
}
