using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Reports;

// REVENUE, EXPENSES & VISITORS REPORT
// Question: "Is the zoo making money, where does it come from, and how do visitors behave?"
// Uses Ticket_Sale, Ticket_Purchase_Item, Ticket_Type, Sale, Sale_Item, Membership, Membership_Type,
// Employee_Shift + Employee (labor), Purchase_Order (supplies), Maintenance (repairs).
[Authorize(Roles = ReportAccess.Revenue)]
public class RevenueModel : ReportPageModel
{
    public RevenueModel(IConfiguration config) : base(config) { }

    // Same hourly-rate rule as the staffing report
    private const string HourlyRate =
        "CASE WHEN e.Pay_Type = 'Salary' AND e.Pay_Rate > 500 THEN e.Pay_Rate / 2080 ELSE COALESCE(e.Pay_Rate, 0) END";

    // ---- Results ----
    public decimal Tickets { get; set; }
    public decimal Food { get; set; }
    public decimal Gifts { get; set; }
    public decimal Memberships { get; set; }
    public decimal Revenue => Tickets + Food + Gifts + Memberships;

    public decimal Labor { get; set; }
    public decimal Supplies { get; set; }
    public decimal Repairs { get; set; }
    public decimal Expenses => Labor + Supplies + Repairs;
    public decimal Net => Revenue - Expenses;
    public decimal MarginPct => Revenue == 0 ? 0 : Math.Round(100 * Net / Revenue, 1);

    public int Visitors { get; set; }
    public int VisitDays { get; set; }
    public decimal SpendPerVisitor => Visitors == 0 ? 0 : Math.Round((Tickets + Food + Gifts) / Visitors, 2);
    public decimal InParkPerVisitor => Visitors == 0 ? 0 : Math.Round((Food + Gifts) / Visitors, 2);

    public List<MonthRow> Months { get; set; } = new();
    public List<DayRow> Weekdays { get; set; } = new();
    public List<MixRow> TicketMix { get; set; } = new();
    public List<MixRow> TopItems { get; set; } = new();
    public List<OutletRow> Outlets { get; set; } = new();
    public List<MixRow> Payments { get; set; } = new();
    public List<SegmentRow> Segments { get; set; } = new();
    public List<DayRow> Upcoming { get; set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnGetExcelAsync()
    {
        await LoadAsync();
        var subtitle = $"{DateRangeText} · Generated {DateTime.Now:MMM d, yyyy h:mm tt}";

        var xl = new ExcelReport("Revenue, Expenses & Visitors Report", subtitle)
            .AddSheet("Summary", new[] { "Measure", "Value" }, new[]
            {
                new object?[] { "Ticket revenue ($)", Tickets },
                new object?[] { "Food revenue ($)", Food },
                new object?[] { "Gift shop revenue ($)", Gifts },
                new object?[] { "Membership revenue ($)", Memberships },
                new object?[] { "Total revenue ($)", Revenue },
                new object?[] { "Labor ($, estimated)", Labor },
                new object?[] { "Feed and merchandise purchases ($)", Supplies },
                new object?[] { "Repairs ($)", Repairs },
                new object?[] { "Total expenses ($)", Expenses },
                new object?[] { "Net ($)", Net },
                new object?[] { "Margin (%)", MarginPct },
                new object?[] { "Visitors", Visitors },
                new object?[] { "Spend per visitor ($)", SpendPerVisitor },
                new object?[] { "Food and gift spend per visitor ($)", InParkPerVisitor }
            })
            .AddSheet("Monthly", new[] { "Month", "Visitors", "Tickets ($)", "Food ($)", "Gift Shop ($)", "Memberships ($)", "Revenue ($)", "Labor ($)", "Supplies ($)", "Repairs ($)", "Net ($)" },
                Months.Select(m => new object?[] { MonthName(m.Key), m.Visitors, m.Tickets, m.Food, m.Gifts, m.Memberships, m.Revenue, m.Labor, m.Supplies, m.Repairs, m.Net }),
                row => (decimal)(row[10] ?? 0m) < 0 ? ExcelReport.Red : null)
            .AddSheet("Day of Week", new[] { "Day", "Avg Visitors", "Avg Revenue ($)" },
                Weekdays.Select(d => new object?[] { d.Label, d.Visitors, d.Revenue }))
            .AddSheet("Ticket Mix", new[] { "Ticket Type", "Tickets", "Revenue ($)", "Avg Price ($)" },
                TicketMix.Select(t => new object?[] { t.Name, t.Count, t.Amount, t.Count == 0 ? 0 : Math.Round(t.Amount / t.Count, 2) }))
            .AddSheet("Top Items", new[] { "Item", "Where", "Sold", "Revenue ($)" },
                TopItems.Select(t => new object?[] { t.Name, t.Detail, t.Count, t.Amount }))
            .AddSheet("Locations", new[] { "Location", "Type", "Orders", "Revenue ($)", "Avg Order ($)", "Revenue per Visitor ($)" },
                Outlets.Select(o => new object?[] { o.Name, o.Type, o.Orders, o.Revenue, o.AvgOrder, Visitors == 0 ? 0 : Math.Round(o.Revenue / Visitors, 2) }))
            .AddSheet("Members vs Others", new[] { "Visitor Type", "Visits", "Ticket Spend per Visit ($)", "Food and Gift Spend per Visit ($)" },
                Segments.Select(s => new object?[] { s.Name, s.Visits, s.TicketPerVisit, s.ShopPerVisit }))
            .AddSheet("Payment Methods", new[] { "Method", "Transactions", "Amount ($)" },
                Payments.Select(p => new object?[] { p.Name, p.Count, p.Amount }));

        return File(xl.ToBytes(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"revenue-report-{DateTime.Today:yyyy-MM-dd}.xlsx");
    }

    private async Task LoadAsync()
    {
        ResolveDates();
        await using var conn = await OpenAsync();

        // "Past" visits only: tickets for future dates are bookings, not visitors yet
        const string ticketWhere = "ts.Visit_Date >= @from AND ts.Visit_Date < @to AND ts.Visit_Date <= CURDATE()";
        const string saleWhere = "s.Sale_Date >= @from AND s.Sale_Date < @to";

        MySqlCommand Cmd(string sql)
        {
            var cmd = new MySqlCommand(sql, conn);
            AddDateParams(cmd);
            return cmd;
        }

        var months = new SortedDictionary<string, MonthRow>();
        MonthRow M(string key) => months.TryGetValue(key, out var m) ? m : months[key] = new MonthRow { Key = key };

        // 1. Ticket revenue and visitors by month
        await using (var cmd = Cmd($@"
            SELECT DATE_FORMAT(ts.Visit_Date, '%Y-%m'), SUM(i.Quantity), SUM(i.Quantity * i.Unit_Price)
            FROM Ticket_Sale ts JOIN Ticket_Purchase_Item i ON i.TicketPurchase_ID = ts.TicketPurchase_ID
            WHERE {ticketWhere}
            GROUP BY 1"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) { var m = M(r.GetString(0)); m.Visitors = Int(r, 1); m.Tickets = Dec(r, 2); }

        // 2. Food and gift shop by month
        await using (var cmd = Cmd($@"
            SELECT DATE_FORMAT(s.Sale_Date, '%Y-%m'), SUM(CASE WHEN s.Sale_Type = 'Food' THEN s.Total_Amount END),
                   SUM(CASE WHEN s.Sale_Type <> 'Food' THEN s.Total_Amount END)
            FROM Sale s WHERE {saleWhere} GROUP BY 1"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) { var m = M(r.GetString(0)); m.Food = Dec(r, 1); m.Gifts = Dec(r, 2); }

        // 3. Membership sales (counted in the month they start)
        await using (var cmd = Cmd(@"
            SELECT DATE_FORMAT(m.Start_Date, '%Y-%m'), SUM(mt.Price)
            FROM Membership m JOIN Membership_Type mt ON mt.MembershipType_ID = m.MembershipType_ID
            WHERE m.Start_Date >= @from AND m.Start_Date < @to GROUP BY 1"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) M(r.GetString(0)).Memberships = Dec(r, 1);

        // 4. Expenses: labor (worked shifts), supplies (orders received), repairs (completed work)
        await using (var cmd = Cmd($@"
            SELECT DATE_FORMAT(s.Shift_Date, '%Y-%m'), SUM(s.Hours * {HourlyRate})
            FROM Employee_Shift s JOIN Employee e ON e.Employee_ID = s.Employee_ID
            WHERE s.Status = 'Worked' AND s.Shift_Date >= @from AND s.Shift_Date < @to AND s.Shift_Date <= CURDATE()
            GROUP BY 1"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) M(r.GetString(0)).Labor = Dec(r, 1);

        await using (var cmd = Cmd(@"
            SELECT DATE_FORMAT(Received_Date, '%Y-%m'), SUM(Total_Cost)
            FROM Purchase_Order WHERE Status = 'Received' AND Received_Date >= @from AND Received_Date < @to GROUP BY 1"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) M(r.GetString(0)).Supplies = Dec(r, 1);

        await using (var cmd = Cmd(@"
            SELECT DATE_FORMAT(Completion_Date, '%Y-%m'), SUM(Cost)
            FROM Maintenance WHERE Status = 'Completed' AND Completion_Date >= @from AND Completion_Date < @to GROUP BY 1"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) M(r.GetString(0)).Repairs = Dec(r, 1);

        Months = months.Values.ToList();
        Tickets = Months.Sum(m => m.Tickets);
        Food = Months.Sum(m => m.Food);
        Gifts = Months.Sum(m => m.Gifts);
        Memberships = Months.Sum(m => m.Memberships);
        Labor = Math.Round(Months.Sum(m => m.Labor), 2);
        Supplies = Months.Sum(m => m.Supplies);
        Repairs = Months.Sum(m => m.Repairs);
        Visitors = Months.Sum(m => m.Visitors);

        // 5. Day of week: average visitors and revenue on each weekday (to plan staffing and promotions)
        await using (var cmd = Cmd($@"
            SELECT DAYOFWEEK(d.Day) AS Dow, DAYNAME(d.Day) AS Day_Name, AVG(d.Visitors), AVG(d.Revenue + COALESCE(sh.Shop, 0)), COUNT(*)
            FROM (SELECT ts.Visit_Date AS Day, SUM(i.Quantity) AS Visitors, SUM(i.Quantity * i.Unit_Price) AS Revenue
                  FROM Ticket_Sale ts JOIN Ticket_Purchase_Item i ON i.TicketPurchase_ID = ts.TicketPurchase_ID
                  WHERE {ticketWhere}
                  GROUP BY ts.Visit_Date) d
            LEFT JOIN (SELECT DATE(s.Sale_Date) AS Day, SUM(s.Total_Amount) AS Shop FROM Sale s WHERE {saleWhere} GROUP BY DATE(s.Sale_Date)) sh
                   ON sh.Day = d.Day
            GROUP BY Dow, Day_Name
            ORDER BY FIELD(Dow, 2, 3, 4, 5, 6, 7, 1)"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                Weekdays.Add(new DayRow(r.GetString(1), Math.Round(Dec(r, 2), 0), Math.Round(Dec(r, 3), 0)));
                VisitDays += Int(r, 4);
            }
        }

        // 6. Ticket mix
        await using (var cmd = Cmd($@"
            SELECT tt.TicketType_Name, SUM(i.Quantity), SUM(i.Quantity * i.Unit_Price)
            FROM Ticket_Sale ts
            JOIN Ticket_Purchase_Item i ON i.TicketPurchase_ID = ts.TicketPurchase_ID
            JOIN Ticket_Type tt ON tt.TicketType_ID = i.TicketType_ID
            WHERE {ticketWhere}
            GROUP BY tt.TicketType_ID, tt.TicketType_Name ORDER BY 3 DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) TicketMix.Add(new MixRow(r.GetString(0), null, Int(r, 1), Dec(r, 2)));

        // 7. Top 10 food items and products
        await using (var cmd = Cmd($@"
            SELECT COALESCE(p.Product_Name, f.FoodItem_Name), COALESCE(gs.GiftShop_Name, fs.Stand_Name),
                   SUM(si.Quantity), SUM(si.Quantity * si.Unit_Price)
            FROM Sale_Item si
            JOIN Sale s ON s.Sale_ID = si.Sale_ID
            LEFT JOIN Product p ON p.Product_ID = si.Product_ID
            LEFT JOIN Food_Item f ON f.FoodItem_ID = si.FoodItem_ID
            LEFT JOIN Gift_Shop gs ON gs.GiftShop_ID = s.GiftShop_ID
            LEFT JOIN Food_Stand fs ON fs.Stand_ID = s.Stand_ID
            WHERE {saleWhere}
            GROUP BY si.Product_ID, si.FoodItem_ID, p.Product_Name, f.FoodItem_Name, gs.GiftShop_Name, fs.Stand_Name
            ORDER BY 4 DESC LIMIT 10"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) TopItems.Add(new MixRow(Str(r, 0) ?? "Item", Str(r, 1), Int(r, 2), Dec(r, 3)));

        // 8. Each food stand and gift shop
        await using (var cmd = Cmd($@"
            SELECT COALESCE(fs.Stand_Name, gs.GiftShop_Name), IF(s.Sale_Type = 'Food', 'Food', 'Gift Shop'),
                   COUNT(*), SUM(s.Total_Amount)
            FROM Sale s
            LEFT JOIN Food_Stand fs ON fs.Stand_ID = s.Stand_ID
            LEFT JOIN Gift_Shop gs ON gs.GiftShop_ID = s.GiftShop_ID
            WHERE {saleWhere}
            GROUP BY s.Stand_ID, s.GiftShop_ID, fs.Stand_Name, gs.GiftShop_Name, s.Sale_Type
            ORDER BY 4 DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) Outlets.Add(new OutletRow(Str(r, 0) ?? "Unknown", r.GetString(1), Int(r, 2), Dec(r, 3)));

        // 9. Payment methods across tickets and shop sales
        await using (var cmd = Cmd($@"
            SELECT COALESCE(x.Method, 'Not recorded'), COUNT(*), SUM(x.Amount) FROM (
                SELECT ts.Payment_Method AS Method, ts.Total_Amount AS Amount FROM Ticket_Sale ts WHERE {ticketWhere}
                UNION ALL
                SELECT s.Payment_Method, s.Total_Amount FROM Sale s WHERE {saleWhere}
            ) x GROUP BY 1 ORDER BY 3 DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) Payments.Add(new MixRow(r.GetString(0), null, Int(r, 1), Dec(r, 2)));

        // 10. Members vs non-members: does a membership change how people spend per visit?
        await using (var cmd = Cmd($@"
            SELECT IF(EXISTS (SELECT 1 FROM Membership m WHERE m.Customer_ID = ts.Customer_ID
                                AND ts.Visit_Date BETWEEN m.Start_Date AND m.Expiration_Date), 'Members', 'Non-members') AS Seg,
                   COUNT(*), AVG(ts.Total_Amount),
                   AVG((SELECT COALESCE(SUM(s.Total_Amount), 0) FROM Sale s
                         WHERE s.Customer_ID = ts.Customer_ID AND DATE(s.Sale_Date) = ts.Visit_Date))
            FROM Ticket_Sale ts
            WHERE {ticketWhere}
            GROUP BY Seg ORDER BY Seg"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                Segments.Add(new SegmentRow(r.GetString(0), Int(r, 1), Math.Round(Dec(r, 2), 2), Math.Round(Dec(r, 3), 2)));

        // 11. Already-booked visitors for the next 14 days (always from today)
        await using (var cmd = Cmd(@"
            SELECT ts.Visit_Date, SUM(i.Quantity), SUM(i.Quantity * i.Unit_Price)
            FROM Ticket_Sale ts JOIN Ticket_Purchase_Item i ON i.TicketPurchase_ID = ts.TicketPurchase_ID
            WHERE ts.Visit_Date > CURDATE() AND ts.Visit_Date <= CURDATE() + INTERVAL 14 DAY
            GROUP BY ts.Visit_Date ORDER BY ts.Visit_Date"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) Upcoming.Add(new DayRow(r.GetDateTime(0).ToString("ddd, MMM d"), Dec(r, 1), Dec(r, 2)));
    }

    public class MonthRow
    {
        public string Key { get; set; } = "";
        public int Visitors { get; set; }
        public decimal Tickets { get; set; }
        public decimal Food { get; set; }
        public decimal Gifts { get; set; }
        public decimal Memberships { get; set; }
        public decimal Labor { get; set; }
        public decimal Supplies { get; set; }
        public decimal Repairs { get; set; }
        public decimal Revenue => Tickets + Food + Gifts + Memberships;
        public decimal Expenses => Labor + Supplies + Repairs;
        public decimal Net => Math.Round(Revenue - Expenses, 2);
    }

    public record DayRow(string Label, decimal Visitors, decimal Revenue);
    public record MixRow(string Name, string? Detail, int Count, decimal Amount);
    public record OutletRow(string Name, string Type, int Orders, decimal Revenue)
    {
        public decimal AvgOrder => Orders == 0 ? 0 : Math.Round(Revenue / Orders, 2);
    }
    public record SegmentRow(string Name, int Visits, decimal TicketPerVisit, decimal ShopPerVisit);
}
