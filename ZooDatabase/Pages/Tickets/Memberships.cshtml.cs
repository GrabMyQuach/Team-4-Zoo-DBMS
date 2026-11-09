using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Tickets;

// Memberships: who is active, who is about to lapse (renewal follow-ups), and how each tier is doing
public class MembershipsModel : PageModel
{
    private readonly IConfiguration _config;

    public MembershipsModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int? TypeId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }

    public List<(int Id, string Name)> Types { get; set; } = new();
    public List<MemberRow> All { get; set; } = new();
    public List<MemberRow> Rows { get; set; } = new();
    public List<TierRow> Tiers { get; set; } = new();
    public bool CanSell => SalesOptions.CanSell(User);
    public static readonly string[] Statuses = { "Active", "Expiring Soon", "Expired", "Upcoming" };

    public int ActiveCount => All.Count(m => m.Status is "Active" or "Expiring Soon");
    public int ExpiringCount => All.Count(m => m.Status == "Expiring Soon");
    public int NewThisMonth => All.Count(m => m.Start >= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1) && m.Start <= DateTime.Today);
    public decimal ActiveValue => All.Where(m => m.Status is "Active" or "Expiring Soon").Sum(m => m.Price);

    // Cashiers only need to check who is a member, not membership values
    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        if (SalesOptions.IsCashierOnly(User)) context.Result = Redirect("/Register/Members");
    }

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using (var cmd = new MySqlCommand("SELECT MembershipType_ID, Membership_Name FROM Membership_Type ORDER BY Price", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) Types.Add((r.GetInt32(0), r.GetString(1)));
        }

        // Each customer's most recent membership (so a renewed member appears once)
        await using (var cmd = new MySqlCommand(@"
            WITH latest AS (
                SELECT m.*, ROW_NUMBER() OVER (PARTITION BY m.Customer_ID ORDER BY m.Expiration_Date DESC) AS rn
                FROM Membership m
            )
            SELECT l.Customer_ID, CONCAT(c.First_Name, ' ', c.Last_Name), l.MembershipType_ID, mt.Membership_Name, mt.Price,
                   l.Start_Date, l.Expiration_Date,
                   (SELECT COUNT(*) FROM Ticket_Sale ts WHERE ts.Customer_ID = l.Customer_ID
                      AND ts.Visit_Date BETWEEN l.Start_Date AND LEAST(l.Expiration_Date, CURDATE())) AS Visits
            FROM latest l
            JOIN Customer c ON c.Customer_ID = l.Customer_ID
            JOIN Membership_Type mt ON mt.MembershipType_ID = l.MembershipType_ID
            WHERE l.rn = 1
            ORDER BY l.Expiration_Date", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var start = r.GetDateTime(5);
                var end = r.GetDateTime(6);
                All.Add(new MemberRow
                {
                    CustomerId = r.GetInt32(0),
                    Name = r.GetString(1),
                    TypeId = r.GetInt32(2),
                    Type = r.GetString(3),
                    Price = r.GetDecimal(4),
                    Start = start,
                    End = end,
                    Visits = Convert.ToInt32(r.GetValue(7)),
                    Status = SalesOptions.MembershipStatus(start, end)
                });
            }
        }

        Rows = All
            .Where(m => string.IsNullOrEmpty(Status) || m.Status == Status)
            .Where(m => TypeId == null || m.TypeId == TypeId)
            .Where(m => string.IsNullOrWhiteSpace(Search) || m.Name.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => Array.IndexOf(Statuses, m.Status)).ThenBy(m => m.End)
            .ToList();

        Tiers = Types.Select(t =>
        {
            var members = All.Where(m => m.TypeId == t.Id && (m.Status == "Active" || m.Status == "Expiring Soon")).ToList();
            return new TierRow(t.Name, members.Count, members.Sum(m => m.Price),
                members.Count == 0 ? 0 : Math.Round((decimal)members.Average(m => m.Visits), 1));
        }).ToList();
    }

    public class MemberRow
    {
        public int CustomerId { get; set; }
        public string Name { get; set; } = "";
        public int TypeId { get; set; }
        public string Type { get; set; } = "";
        public decimal Price { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public int Visits { get; set; }
        public string Status { get; set; } = "";
        public int DaysLeft => (End - DateTime.Today).Days;
    }

    public record TierRow(string Name, int Active, decimal AnnualValue, decimal AvgVisits);
}
