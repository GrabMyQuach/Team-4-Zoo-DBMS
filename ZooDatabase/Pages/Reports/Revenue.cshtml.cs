using Microsoft.AspNetCore.Authorization;
using MySqlConnector;
using ZooDatabase.Data;
using static ZooDatabase.Pages.Reports.LaborMath;

namespace ZooDatabase.Pages.Reports;

// REVENUE & VISITORS REPORT
// Question: "Where does the money come from, where does it go, and when are we busiest?"
// Uses Ticket_Sale, Ticket_Purchase_Item, Membership, Sale, Sale_Item, Employee_Shift, Purchase_Order, Maintenance.
// Every dollar amount links to the sales, shifts, orders or repairs that add up to it.
[Authorize(Roles = ReportAccess.Revenue)]
public class RevenueModel : RunnableReport
{
    public RevenueModel(IConfiguration config) : base(config) { }

    public override string ReportTitle => "Revenue & Visitors";

    public override IReadOnlyList<ReportView> Views { get; } = new[]
    {
        new ReportView("revenue", "Revenue by month", "visitors and income from tickets, memberships, food and gifts."),
        new ReportView("profit", "Costs & profit by month", "revenue vs. labor, supplies and repairs, and what's left."),
        new ReportView("weekday", "Busiest days of the week", "average visitors and revenue on each weekday."),
        new ReportView("tickettypes", "Ticket mix", "how many of each ticket type sold and what they brought in."),
        new ReportView("items", "Top sellers", "best-selling food and gift items."),
        new ReportView("outlets", "Food stands & gift shops", "sales at each stand and shop."),
        new ReportView("payments", "Payment methods", "how visitors pay, across tickets and shops."),
        new ReportView("members", "Members vs. non-members", "whether members spend differently per visit."),
        new ReportView("upcoming", "Upcoming bookings", "tickets already sold for the next 14 days.", UsesDates: false),
        new ReportView("ticketitems", "Ticket sales", "", Hidden: true),
        new ReportView("ticketorders", "Ticket orders", "", Hidden: true),
        new ReportView("memberships", "Memberships sold", "", Hidden: true),
        new ReportView("shopsales", "Shop sales", "", Hidden: true),
        new ReportView("shopitems", "Items sold", "", Hidden: true),
        new ReportView("labor", "Labor", "", Hidden: true),
        new ReportView("supplies", "Supplies received", "", Hidden: true),
        new ReportView("repairs", "Repairs completed", "", Hidden: true),
        new ReportView("paylist", "Payments", "", Hidden: true)
    };

    // Tickets count on the visit date, and only past visits (future dates are bookings, not visitors yet)
    private const string TicketWhere = "ts.Visit_Date >= @from AND ts.Visit_Date < @to AND ts.Visit_Date <= @today";
    private const string SaleWhere = "s.Sale_Date >= @from AND s.Sale_Date < @to";
    private const string TicketFrom = @"
        FROM Ticket_Purchase_Item i
        JOIN Ticket_Sale ts ON ts.TicketPurchase_ID = i.TicketPurchase_ID
        JOIN Ticket_Type tt ON tt.TicketType_ID = i.TicketType_ID";

    // A ticket order counts as a member visit if the customer had a membership that day
    private const string IsMember = @"EXISTS (SELECT 1 FROM Membership m WHERE m.Customer_ID = ts.Customer_ID
                                        AND ts.Visit_Date BETWEEN m.Start_Date AND m.Expiration_Date)";

    protected override Task LoadFiltersAsync(MySqlConnection conn) => Task.CompletedTask;

    private MySqlCommand Cmd(MySqlConnection conn, string sql)
    {
        var cmd = new MySqlCommand(sql, conn);
        AddDateParams(cmd);
        return cmd;
    }

    protected override Task<ReportTable> BuildAsync(MySqlConnection conn) => CurrentView.Key switch
    {
        "profit" => ProfitAsync(conn),
        "weekday" => WeekdayAsync(conn),
        "tickettypes" => TicketMixAsync(conn),
        "items" => TopItemsAsync(conn),
        "outlets" => OutletsAsync(conn),
        "payments" => PaymentsAsync(conn),
        "members" => MembersAsync(conn),
        "upcoming" => UpcomingAsync(conn),
        "ticketitems" => TicketItemsAsync(conn),
        "ticketorders" => TicketOrdersAsync(conn),
        "memberships" => MembershipsAsync(conn),
        "shopsales" => ShopSalesAsync(conn),
        "shopitems" => ShopItemsAsync(conn),
        "labor" => LaborAsync(conn),
        "supplies" => SuppliesAsync(conn),
        "repairs" => RepairsAsync(conn),
        "paylist" => PayListAsync(conn),
        _ => RevenueAsync(conn)
    };

    // ---- Monthly money, shared by the two monthly views ----
    private class Month
    {
        public int Visitors;
        public decimal Tickets, Memberships, Food, Gifts, Labor, Supplies, Repairs;
        public decimal Revenue => Tickets + Memberships + Food + Gifts;
        public decimal Costs => Labor + Supplies + Repairs;
    }

    private async Task<SortedDictionary<string, Month>> MonthsAsync(MySqlConnection conn, bool withCosts)
    {
        var months = new SortedDictionary<string, Month>();
        Month M(string ym) => months.TryGetValue(ym, out var x) ? x : months[ym] = new Month();

        async Task Read(string sql, Action<Month, System.Data.Common.DbDataReader> apply)
        {
            await using var cmd = Cmd(conn, sql);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) apply(M(r.GetString(0)), r);
        }

        await Read($"SELECT DATE_FORMAT(ts.Visit_Date, '%Y-%m'), SUM(i.Quantity), SUM(i.Quantity * i.Unit_Price) {TicketFrom} WHERE {TicketWhere} GROUP BY 1",
            (m, r) => { m.Visitors = Int(r, 1); m.Tickets = Dec(r, 2); });
        await Read($@"SELECT DATE_FORMAT(s.Sale_Date, '%Y-%m'), SUM(CASE WHEN s.Sale_Type = 'Food' THEN s.Total_Amount END),
                             SUM(CASE WHEN s.Sale_Type <> 'Food' THEN s.Total_Amount END)
                      FROM Sale s WHERE {SaleWhere} GROUP BY 1",
            (m, r) => { m.Food = Dec(r, 1); m.Gifts = Dec(r, 2); });
        await Read(@"SELECT DATE_FORMAT(m.Start_Date, '%Y-%m'), SUM(mt.Price)
                     FROM Membership m JOIN Membership_Type mt ON mt.MembershipType_ID = m.MembershipType_ID
                     WHERE m.Start_Date >= @from AND m.Start_Date < @to GROUP BY 1",
            (m, r) => m.Memberships = Dec(r, 1));

        if (withCosts)
        {
            await Read($@"SELECT DATE_FORMAT(s.Shift_Date, '%Y-%m'), SUM({PaidHours} * {HourlyRate})
                          FROM Employee_Shift s JOIN Employee e ON e.Employee_ID = s.Employee_ID
                          WHERE s.Status = 'Worked' AND s.Shift_Date >= @from AND s.Shift_Date < @to AND s.Shift_Date <= @today
                          GROUP BY 1",
                (m, r) => m.Labor = Math.Round(Dec(r, 1), 2));
            await Read(@"SELECT DATE_FORMAT(Received_Date, '%Y-%m'), SUM(Total_Cost) FROM Purchase_Order
                         WHERE Status = 'Received' AND Received_Date >= @from AND Received_Date < @to GROUP BY 1",
                (m, r) => m.Supplies = Dec(r, 1));
            await Read(@"SELECT DATE_FORMAT(Completion_Date, '%Y-%m'), SUM(Cost) FROM Maintenance
                         WHERE Status = 'Completed' AND Completion_Date >= @from AND Completion_Date < @to GROUP BY 1",
                (m, r) => m.Repairs = Dec(r, 1));
        }
        return months;
    }

    // ===== Revenue by month =====
    private async Task<ReportTable> RevenueAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Month"), new Col("Visitors", true), new Col("Tickets", true), new Col("Memberships", true),
                      new Col("Food", true), new Col("Gift shop", true), new Col("Total revenue", true));
        var months = await MonthsAsync(conn, withCosts: false);
        foreach (var (ym, m) in months)
        {
            var key = "m:" + ym;
            t.Rows.Add(R(new[]
            {
                T(MonthName(ym)), N(m.Visitors, Drill("ticketitems", key)), Money(m.Tickets, Drill("ticketitems", key)),
                Money(m.Memberships, Drill("memberships", key)), Money(m.Food, Drill("shopsales", key, "food")),
                Money(m.Gifts, Drill("shopsales", key, "gift")), Money(m.Revenue)
            }));
        }
        var all = months.Values;
        t.Total = R(new[]
        {
            T("Total"), N(all.Sum(m => m.Visitors), Drill("ticketitems", "all")), Money(all.Sum(m => m.Tickets), Drill("ticketitems", "all")),
            Money(all.Sum(m => m.Memberships), Drill("memberships", "all")), Money(all.Sum(m => m.Food), Drill("shopsales", "all", "food")),
            Money(all.Sum(m => m.Gifts), Drill("shopsales", "all", "gift")), Money(all.Sum(m => m.Revenue))
        });
        t.Note = "Tickets count in the month of the visit (past visits only). Memberships count in the month they start. Click any amount to see the sales behind it.";
        return t;
    }

    // ===== Costs & profit by month =====
    private async Task<ReportTable> ProfitAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Month"), new Col("Revenue", true), new Col("Labor", true), new Col("Supplies", true),
                      new Col("Repairs", true), new Col("Total costs", true), new Col("Profit", true), new Col("Margin", true));
        var months = await MonthsAsync(conn, withCosts: true);
        foreach (var (ym, m) in months)
        {
            var key = "m:" + ym;
            var profit = m.Revenue - m.Costs;
            t.Rows.Add(R(new[]
            {
                T(MonthName(ym)), Money(m.Revenue), Money(m.Labor, Drill("labor", key)), Money(m.Supplies, Drill("supplies", key)),
                Money(m.Repairs, Drill("repairs", key)), Money(m.Costs), Money(profit),
                m.Revenue == 0 ? T("—") : PctCell(Math.Round(100 * profit / m.Revenue, 1))
            }, profit < 0 ? "bad" : null));
        }
        var all = months.Values;
        decimal rev = all.Sum(m => m.Revenue), costs = all.Sum(m => m.Costs);
        t.Total = R(new[]
        {
            T("Total"), Money(rev), Money(all.Sum(m => m.Labor), Drill("labor", "all")),
            Money(all.Sum(m => m.Supplies), Drill("supplies", "all")), Money(all.Sum(m => m.Repairs), Drill("repairs", "all")),
            Money(costs), Money(rev - costs), rev == 0 ? T("—") : PctCell(Math.Round(100 * (rev - costs) / rev, 1))
        });
        t.Note = "Red: a month that lost money. Labor = paid hours x pay rate. Supplies = orders received. Repairs = work completed. "
               + "Our sample data has a full staff but only a small number of test visitors, so costs are higher than revenue.";
        return t;
    }

    // ===== Busiest days of the week =====
    private async Task<ReportTable> WeekdayAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Day"), new Col("Days counted", true), new Col("Avg visitors", true), new Col("Avg revenue", true));
        await using var cmd = Cmd(conn, $@"
            SELECT DAYOFWEEK(d.Day) AS Dow, DAYNAME(d.Day) AS Day_Name, COUNT(*), AVG(d.Visitors), AVG(d.Revenue + COALESCE(sh.Shop, 0))
            FROM (SELECT ts.Visit_Date AS Day, SUM(i.Quantity) AS Visitors, SUM(i.Quantity * i.Unit_Price) AS Revenue
                  {TicketFrom} WHERE {TicketWhere} GROUP BY ts.Visit_Date) d
            LEFT JOIN (SELECT DATE(s.Sale_Date) AS Day, SUM(s.Total_Amount) AS Shop FROM Sale s WHERE {SaleWhere}
                       GROUP BY DATE(s.Sale_Date)) sh ON sh.Day = d.Day
            GROUP BY Dow, Day_Name
            ORDER BY FIELD(Dow, 2, 3, 4, 5, 6, 7, 1)");
        var rows = new List<(int Dow, string Name, int Days, decimal Visitors, decimal Revenue)>();
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) rows.Add((Int(r, 0), r.GetString(1), Int(r, 2), Dec(r, 3), Dec(r, 4)));

        var best = rows.Count == 0 ? 0 : rows.Max(x => x.Visitors);
        foreach (var x in rows)
        {
            t.Rows.Add(R(new[]
            {
                T(x.Name), N(x.Days, Drill("ticketorders", "dow:" + x.Dow)),
                Dec1(Math.Round(x.Visitors, 0)), Money(Math.Round(x.Revenue, 2))
            }, x.Visitors == best && best > 0 ? "warn" : null));
        }
        t.Note = "Yellow: the busiest day. Revenue includes food and gift sales on visit days. Click Days counted to see those ticket orders.";
        return t;
    }

    // ===== Ticket mix =====
    private async Task<ReportTable> TicketMixAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Ticket type"), new Col("Tickets sold", true), new Col("Revenue", true), new Col("Share of ticket revenue", true));
        var rows = new List<(int Id, string Name, int Qty, decimal Amount)>();
        await using (var cmd = Cmd(conn, $@"
            SELECT tt.TicketType_ID, tt.TicketType_Name, SUM(i.Quantity), SUM(i.Quantity * i.Unit_Price)
            {TicketFrom} WHERE {TicketWhere}
            GROUP BY tt.TicketType_ID, tt.TicketType_Name ORDER BY 4 DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) rows.Add((r.GetInt32(0), r.GetString(1), Int(r, 2), Dec(r, 3)));

        var total = rows.Sum(x => x.Amount);
        foreach (var x in rows)
            t.Rows.Add(R(new[]
            {
                T(x.Name), N(x.Qty, Drill("ticketitems", "t:" + x.Id)), Money(x.Amount, Drill("ticketitems", "t:" + x.Id)),
                PctCell(total == 0 ? 0 : Math.Round(100 * x.Amount / total, 1))
            }));
        if (rows.Count > 0)
            t.Total = R(new[] { T("Total"), N(rows.Sum(x => x.Qty), Drill("ticketitems", "all")), Money(total, Drill("ticketitems", "all")), PctCell(100) });
        return t;
    }

    // ===== Top sellers =====
    private async Task<ReportTable> TopItemsAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Item"), new Col("Type"), new Col("Sold at"), new Col("Quantity", true), new Col("Revenue", true));
        await using var cmd = Cmd(conn, $@"
            SELECT si.Product_ID, si.FoodItem_ID, COALESCE(p.Product_Name, f.FoodItem_Name),
                   IF(si.Product_ID IS NULL, 'Food', 'Gift'),
                   GROUP_CONCAT(DISTINCT COALESCE(gs.GiftShop_Name, fs.Stand_Name) SEPARATOR ', '),
                   SUM(si.Quantity), SUM(si.Quantity * si.Unit_Price)
            FROM Sale_Item si
            JOIN Sale s ON s.Sale_ID = si.Sale_ID
            LEFT JOIN Product p ON p.Product_ID = si.Product_ID
            LEFT JOIN Food_Item f ON f.FoodItem_ID = si.FoodItem_ID
            LEFT JOIN Gift_Shop gs ON gs.GiftShop_ID = s.GiftShop_ID
            LEFT JOIN Food_Stand fs ON fs.Stand_ID = s.Stand_ID
            WHERE {SaleWhere}
            GROUP BY si.Product_ID, si.FoodItem_ID, p.Product_Name, f.FoodItem_Name
            ORDER BY 7 DESC
            LIMIT 25");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var key = r.IsDBNull(0) ? "f:" + Int(r, 1) : "p:" + Int(r, 0);
            t.Rows.Add(R(new[]
            {
                T(Str(r, 2) ?? "Item"), T(Str(r, 3)), T(Str(r, 4)),
                N(Int(r, 5), Drill("shopitems", key)), Money(Dec(r, 6), Drill("shopitems", key))
            }, link: r.IsDBNull(0) ? null : $"/Shop/ProductDetails/{Int(r, 0)}"));
        }
        t.Note = "The 25 best sellers by revenue. Click a number to see every sale of that item.";
        return t;
    }

    // ===== Food stands & gift shops =====
    private async Task<ReportTable> OutletsAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Outlet"), new Col("Type"), new Col("Sales", true), new Col("Revenue", true), new Col("Average sale", true));
        await using var cmd = Cmd(conn, $@"
            SELECT s.Stand_ID, s.GiftShop_ID, COALESCE(fs.Stand_Name, gs.GiftShop_Name), IF(s.Sale_Type = 'Food', 'Food', 'Gift shop'),
                   COUNT(*), SUM(s.Total_Amount)
            FROM Sale s
            LEFT JOIN Food_Stand fs ON fs.Stand_ID = s.Stand_ID
            LEFT JOIN Gift_Shop gs ON gs.GiftShop_ID = s.GiftShop_ID
            WHERE {SaleWhere}
            GROUP BY s.Stand_ID, s.GiftShop_ID, fs.Stand_Name, gs.GiftShop_Name, s.Sale_Type
            ORDER BY 6 DESC");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            bool food = !r.IsDBNull(0);
            var id = food ? Int(r, 0) : Int(r, 1);
            var key = (food ? "st:" : "gs:") + id;
            int count = Int(r, 4);
            decimal amount = Dec(r, 5);
            t.Rows.Add(R(new[]
            {
                T(Str(r, 2) ?? "Unknown"), T(Str(r, 3)), N(count, Drill("shopsales", key)), Money(amount, Drill("shopsales", key)),
                Money(count == 0 ? 0 : Math.Round(amount / count, 2))
            }, link: food ? $"/Shop/FoodStandDetails/{id}" : $"/Shop/GiftShopDetails/{id}"));
        }
        return t;
    }

    // ===== Payment methods =====
    private async Task<ReportTable> PaymentsAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Payment method"), new Col("Transactions", true), new Col("Amount", true), new Col("Share", true));
        var rows = new List<(string Method, int Count, decimal Amount)>();
        await using (var cmd = Cmd(conn, $@"
            SELECT COALESCE(x.Method, 'Not recorded'), COUNT(*), SUM(x.Amount) FROM (
                SELECT ts.Payment_Method AS Method, ts.Total_Amount AS Amount FROM Ticket_Sale ts WHERE {TicketWhere}
                UNION ALL
                SELECT s.Payment_Method, s.Total_Amount FROM Sale s WHERE {SaleWhere}
            ) x GROUP BY 1 ORDER BY 3 DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) rows.Add((r.GetString(0), Int(r, 1), Dec(r, 2)));

        var total = rows.Sum(x => x.Amount);
        foreach (var x in rows)
            t.Rows.Add(R(new[]
            {
                T(x.Method), N(x.Count, Drill("paylist", "pm:" + x.Method)), Money(x.Amount, Drill("paylist", "pm:" + x.Method)),
                PctCell(total == 0 ? 0 : Math.Round(100 * x.Amount / total, 1))
            }));
        if (rows.Count > 0) t.Total = R(new[] { T("Total"), N(rows.Sum(x => x.Count)), Money(total), PctCell(100) });
        t.Note = "Includes ticket orders and food and gift shop sales.";
        return t;
    }

    // ===== Members vs. non-members =====
    private async Task<ReportTable> MembersAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Visitors"), new Col("Ticket orders", true), new Col("Avg ticket spend", true), new Col("Avg shop spend same day", true));
        await using var cmd = Cmd(conn, $@"
            SELECT IF({IsMember}, 'Members', 'Non-members') AS Seg, COUNT(*), AVG(ts.Total_Amount),
                   AVG((SELECT COALESCE(SUM(s.Total_Amount), 0) FROM Sale s
                         WHERE s.Customer_ID = ts.Customer_ID AND DATE(s.Sale_Date) = ts.Visit_Date))
            FROM Ticket_Sale ts
            WHERE {TicketWhere}
            GROUP BY Seg ORDER BY Seg");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var seg = r.GetString(0);
            t.Rows.Add(R(new[]
            {
                T(seg), N(Int(r, 1), Drill("ticketorders", "seg:" + (seg == "Members" ? "m" : "n"))),
                Money(Math.Round(Dec(r, 2), 2)), Money(Math.Round(Dec(r, 3), 2))
            }));
        }
        t.Note = "A visit counts as a member visit if the customer had an active membership on the visit date.";
        return t;
    }

    // ===== Upcoming bookings (next 14 days) =====
    private async Task<ReportTable> UpcomingAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Visit date"), new Col("Orders", true), new Col("Visitors", true), new Col("Revenue", true));
        await using var cmd = Cmd(conn, $@"
            SELECT ts.Visit_Date, COUNT(DISTINCT ts.TicketPurchase_ID), SUM(i.Quantity), SUM(i.Quantity * i.Unit_Price)
            {TicketFrom}
            WHERE ts.Visit_Date > @today AND ts.Visit_Date <= @today + INTERVAL 14 DAY
            GROUP BY ts.Visit_Date ORDER BY ts.Visit_Date");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var key = "v:" + r.GetDateTime(0).ToString("yyyy-MM-dd");
            t.Rows.Add(R(new[]
            {
                T(r.GetDateTime(0).ToString("ddd, MMM d")), N(Int(r, 1), Drill("ticketitems", key)),
                N(Int(r, 2), Drill("ticketitems", key)), Money(Dec(r, 3), Drill("ticketitems", key))
            }));
        }
        t.EmptyText = "No tickets are booked for the next 14 days yet.";
        return t;
    }

    // ===== Source records: ticket lines =====
    private async Task<ReportTable> TicketItemsAsync(MySqlConnection conn)
    {
        var (type, text) = SplitKey(Key);
        var where = TicketWhere;
        var title = "Ticket sales";
        object? k = null;
        switch (type)
        {
            case "m": where += " AND DATE_FORMAT(ts.Visit_Date, '%Y-%m') = @k"; k = text; title += ": " + MonthName(text); break;
            case "t": where += " AND i.TicketType_ID = @k"; k = text; break;
            case "v": where = "ts.Visit_Date = @k"; k = text; title = "Booked tickets: " + (DateTime.TryParse(text, out var d) ? d.ToString("dddd, MMM d") : text); break;
        }

        var t = Table(new Col("Order"), new Col("Visit date"), new Col("Customer"), new Col("Ticket type"),
                      new Col("Qty", true), new Col("Price", true), new Col("Amount", true), new Col("Payment"));
        await using var cmd = Cmd(conn, $@"
            SELECT ts.TicketPurchase_ID, ts.Visit_Date, c.Customer_ID, CONCAT(c.First_Name, ' ', c.Last_Name), tt.TicketType_Name,
                   i.Quantity, i.Unit_Price, ts.Payment_Method
            {TicketFrom}
            LEFT JOIN Customer c ON c.Customer_ID = ts.Customer_ID
            WHERE {where}
            ORDER BY ts.Visit_Date DESC, ts.TicketPurchase_ID DESC
            LIMIT 500");
        if (k != null) cmd.Parameters.AddWithValue("@k", k);
        await using var r = await cmd.ExecuteReaderAsync();
        int qty = 0; decimal amount = 0;
        string? typeName = null;
        while (await r.ReadAsync())
        {
            typeName ??= r.GetString(4);
            int q = Int(r, 5);
            decimal price = Dec(r, 6);
            qty += q; amount += q * price;
            t.Rows.Add(R(new[]
            {
                T("#" + r.GetInt32(0), $"/Tickets/TicketSaleDetails/{r.GetInt32(0)}"), D(r.GetDateTime(1)),
                r.IsDBNull(2) ? T("Walk-in") : T(Str(r, 3), $"/Tickets/CustomerDetails/{r.GetInt32(2)}"),
                T(r.GetString(4)), N(q), Money(price), Money(q * price), T(Str(r, 7))
            }));
        }
        if (type == "t" && typeName != null) title += ": " + typeName;
        if (t.Rows.Count > 0) t.Total = R(new[] { T("Total"), T(""), T(""), T(""), N(qty), T(""), Money(amount), T("") });
        ResultTitle = title;
        t.Note = "One row per ticket type on each order. Click an order to open it. Shows up to 500 lines.";
        return t;
    }

    // ===== Source records: ticket orders =====
    private async Task<ReportTable> TicketOrdersAsync(MySqlConnection conn)
    {
        var (type, text) = SplitKey(Key);
        var where = TicketWhere;
        var title = "Ticket orders";
        object? k = null;
        switch (type)
        {
            case "dow": where += " AND DAYOFWEEK(ts.Visit_Date) = @k"; k = text; title += " on " + DayName(IntOrNull(text)) + "s"; break;
            case "seg": where += text == "m" ? $" AND {IsMember}" : $" AND NOT {IsMember}"; title += text == "m" ? ": members" : ": non-members"; break;
        }

        var t = Table(new Col("Order"), new Col("Visit date"), new Col("Customer"), new Col("Tickets", true), new Col("Total", true), new Col("Payment"));
        await using var cmd = Cmd(conn, $@"
            SELECT ts.TicketPurchase_ID, ts.Visit_Date, c.Customer_ID, CONCAT(c.First_Name, ' ', c.Last_Name),
                   (SELECT COALESCE(SUM(i.Quantity), 0) FROM Ticket_Purchase_Item i WHERE i.TicketPurchase_ID = ts.TicketPurchase_ID),
                   ts.Total_Amount, ts.Payment_Method
            FROM Ticket_Sale ts
            LEFT JOIN Customer c ON c.Customer_ID = ts.Customer_ID
            WHERE {where}
            ORDER BY ts.Visit_Date DESC
            LIMIT 500");
        if (k != null) cmd.Parameters.AddWithValue("@k", k);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            t.Rows.Add(R(new[]
            {
                T("#" + r.GetInt32(0), $"/Tickets/TicketSaleDetails/{r.GetInt32(0)}"), D(r.GetDateTime(1)),
                r.IsDBNull(2) ? T("Walk-in") : T(Str(r, 3), $"/Tickets/CustomerDetails/{r.GetInt32(2)}"),
                N(Int(r, 4)), Money(Dec(r, 5)), T(Str(r, 6))
            }));
        }
        ResultTitle = title;
        t.Note = "Click an order to open it. Shows up to 500 orders.";
        return t;
    }

    private static string DayName(int? dow) => dow is >= 1 and <= 7
        ? ((DayOfWeek)(dow.Value - 1)).ToString()
        : "that day";

    // ===== Source records: memberships sold =====
    private async Task<ReportTable> MembershipsAsync(MySqlConnection conn)
    {
        var (type, text) = SplitKey(Key);
        var where = "m.Start_Date >= @from AND m.Start_Date < @to";
        object? k = null;
        if (type == "m") { where += " AND DATE_FORMAT(m.Start_Date, '%Y-%m') = @k"; k = text; }

        var t = Table(new Col("Customer"), new Col("Membership"), new Col("Starts"), new Col("Expires"), new Col("Price", true), new Col("Status"));
        await using var cmd = Cmd(conn, $@"
            SELECT c.Customer_ID, CONCAT(c.First_Name, ' ', c.Last_Name), mt.Membership_Name, m.Start_Date, m.Expiration_Date, mt.Price, m.Status
            FROM Membership m
            JOIN Membership_Type mt ON mt.MembershipType_ID = m.MembershipType_ID
            JOIN Customer c ON c.Customer_ID = m.Customer_ID
            WHERE {where}
            ORDER BY m.Start_Date DESC");
        if (k != null) cmd.Parameters.AddWithValue("@k", k);
        await using var r = await cmd.ExecuteReaderAsync();
        decimal total = 0;
        while (await r.ReadAsync())
        {
            total += Dec(r, 5);
            t.Rows.Add(R(new[]
            {
                T(r.GetString(1), $"/Tickets/CustomerDetails/{r.GetInt32(0)}"), T(r.GetString(2)), D(r.GetDateTime(3)),
                D(r.GetDateTime(4)), Money(Dec(r, 5)), T(Str(r, 6))
            }));
        }
        if (t.Rows.Count > 0) t.Total = R(new[] { T("Total"), T(""), T(""), T(""), Money(total), T("") });
        ResultTitle = "Memberships sold" + (type == "m" ? ": " + MonthName(text) : "");
        t.Note = "Counted in the month the membership starts. Click a name to open the customer.";
        return t;
    }

    // ===== Source records: food and gift shop sales =====
    private async Task<ReportTable> ShopSalesAsync(MySqlConnection conn)
    {
        var (type, text) = SplitKey(Key);
        var where = SaleWhere;
        var parts = new List<string>();
        object? k = null;
        switch (Key2)
        {
            case "food": where += " AND s.Sale_Type = 'Food'"; parts.Add("Food"); break;
            case "gift": where += " AND s.Sale_Type <> 'Food'"; parts.Add("Gift shop"); break;
        }
        switch (type)
        {
            case "m": where += " AND DATE_FORMAT(s.Sale_Date, '%Y-%m') = @k"; k = text; parts.Add(MonthName(text)); break;
            case "st": where += " AND s.Stand_ID = @k"; k = text; break;
            case "gs": where += " AND s.GiftShop_ID = @k"; k = text; break;
        }

        var t = Table(new Col("Sale"), new Col("Date"), new Col("Outlet"), new Col("Customer"), new Col("Payment"), new Col("Total", true));
        await using var cmd = Cmd(conn, $@"
            SELECT s.Sale_ID, s.Sale_Date, COALESCE(fs.Stand_Name, gs.GiftShop_Name), c.Customer_ID,
                   CONCAT(c.First_Name, ' ', c.Last_Name), s.Payment_Method, s.Total_Amount
            FROM Sale s
            LEFT JOIN Food_Stand fs ON fs.Stand_ID = s.Stand_ID
            LEFT JOIN Gift_Shop gs ON gs.GiftShop_ID = s.GiftShop_ID
            LEFT JOIN Customer c ON c.Customer_ID = s.Customer_ID
            WHERE {where}
            ORDER BY s.Sale_Date DESC
            LIMIT 500");
        if (k != null) cmd.Parameters.AddWithValue("@k", k);
        await using var r = await cmd.ExecuteReaderAsync();
        decimal total = 0;
        string? outlet = null;
        while (await r.ReadAsync())
        {
            outlet ??= Str(r, 2);
            total += Dec(r, 6);
            t.Rows.Add(R(new[]
            {
                T("#" + r.GetInt32(0), $"/Shop/SaleDetails/{r.GetInt32(0)}"), T(r.GetDateTime(1).ToString("MMM d, yyyy h:mm tt")),
                T(Str(r, 2)), r.IsDBNull(3) ? T("—") : T(Str(r, 4), $"/Tickets/CustomerDetails/{r.GetInt32(3)}"),
                T(Str(r, 5)), Money(Dec(r, 6))
            }));
        }
        if (t.Rows.Count > 0) t.Total = R(new[] { T("Total"), T(""), T(""), T(""), T(""), Money(total) });
        if ((type == "st" || type == "gs") && outlet != null) parts.Add(outlet);
        ResultTitle = "Shop sales" + (parts.Count > 0 ? ": " + string.Join(" · ", parts) : "");
        t.Note = "Click a sale to open it. Shows up to 500 sales.";
        return t;
    }

    // ===== Source records: every sale of one item =====
    private async Task<ReportTable> ShopItemsAsync(MySqlConnection conn)
    {
        var (type, text) = SplitKey(Key);
        var where = SaleWhere + (type == "f" ? " AND si.FoodItem_ID = @k" : " AND si.Product_ID = @k");
        var t = Table(new Col("Sale"), new Col("Date"), new Col("Outlet"), new Col("Item"), new Col("Qty", true), new Col("Price", true), new Col("Amount", true));
        await using var cmd = Cmd(conn, $@"
            SELECT s.Sale_ID, s.Sale_Date, COALESCE(fs.Stand_Name, gs.GiftShop_Name), COALESCE(p.Product_Name, f.FoodItem_Name),
                   si.Quantity, si.Unit_Price
            FROM Sale_Item si
            JOIN Sale s ON s.Sale_ID = si.Sale_ID
            LEFT JOIN Product p ON p.Product_ID = si.Product_ID
            LEFT JOIN Food_Item f ON f.FoodItem_ID = si.FoodItem_ID
            LEFT JOIN Food_Stand fs ON fs.Stand_ID = s.Stand_ID
            LEFT JOIN Gift_Shop gs ON gs.GiftShop_ID = s.GiftShop_ID
            WHERE {where}
            ORDER BY s.Sale_Date DESC
            LIMIT 500");
        cmd.Parameters.AddWithValue("@k", IntOrNull(text) ?? 0);
        await using var r = await cmd.ExecuteReaderAsync();
        int qty = 0; decimal amount = 0;
        string? item = null;
        while (await r.ReadAsync())
        {
            item ??= Str(r, 3);
            int q = Int(r, 4);
            decimal price = Dec(r, 5);
            qty += q; amount += q * price;
            t.Rows.Add(R(new[]
            {
                T("#" + r.GetInt32(0), $"/Shop/SaleDetails/{r.GetInt32(0)}"), T(r.GetDateTime(1).ToString("MMM d, yyyy")),
                T(Str(r, 2)), T(Str(r, 3)), N(q), Money(price), Money(q * price)
            }));
        }
        if (t.Rows.Count > 0) t.Total = R(new[] { T("Total"), T(""), T(""), T(""), N(qty), T(""), Money(amount) });
        ResultTitle = "Items sold" + (item == null ? "" : ": " + item);
        t.Note = "Click a sale to open it. Shows up to 500 sales.";
        return t;
    }

    // ===== Source records: labor (worked shifts and their cost) =====
    private async Task<ReportTable> LaborAsync(MySqlConnection conn)
    {
        var (type, text) = SplitKey(Key);
        var where = "s.Status = 'Worked' AND s.Shift_Date >= @from AND s.Shift_Date < @to AND s.Shift_Date <= @today";
        object? k = null;
        if (type == "m") { where += " AND DATE_FORMAT(s.Shift_Date, '%Y-%m') = @k"; k = text; }

        var t = Table(new Col("Employee"), new Col("Department"), new Col("Shifts", true), new Col("Paid hours", true),
                      new Col("Hourly rate", true), new Col("Cost", true));
        await using var cmd = Cmd(conn, $@"
            SELECT e.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name), d.Department_Name, COUNT(*),
                   SUM({PaidHours}), MAX({HourlyRate}), SUM({PaidHours} * {HourlyRate})
            FROM Employee_Shift s
            JOIN Employee e ON e.Employee_ID = s.Employee_ID
            LEFT JOIN Department d ON d.Department_ID = e.Department_ID
            WHERE {where}
            GROUP BY e.Employee_ID, e.First_Name, e.Last_Name, d.Department_Name
            ORDER BY 7 DESC");
        if (k != null) cmd.Parameters.AddWithValue("@k", k);
        await using var r = await cmd.ExecuteReaderAsync();
        decimal hours = 0, cost = 0;
        int shifts = 0;
        while (await r.ReadAsync())
        {
            var c = Math.Round(Dec(r, 6), 2);
            shifts += Int(r, 3); hours += Dec(r, 4); cost += c;
            t.Rows.Add(R(new[]
            {
                T(r.GetString(1)), T(Str(r, 2)), N(Int(r, 3)), Dec1(Math.Round(Dec(r, 4), 1)), Money(Math.Round(Dec(r, 5), 2)), Money(c)
            }, link: $"/Staff/EmployeeDetails/{r.GetInt32(0)}"));
        }
        if (t.Rows.Count > 0) t.Total = R(new[] { T("Total"), T(""), N(shifts), Dec1(Math.Round(hours, 1)), T(""), Money(cost) });
        ResultTitle = "Labor" + (type == "m" ? ": " + MonthName(text) : "");
        t.Note = "Paid hours subtract a 30-minute unpaid break on shifts of 6+ hours. Salaried pay is divided by 2,080 hours a year. "
               + "The Staffing report shows each shift.";
        return t;
    }

    // ===== Source records: supplies received =====
    private async Task<ReportTable> SuppliesAsync(MySqlConnection conn)
    {
        var (type, text) = SplitKey(Key);
        var where = "po.Status = 'Received' AND po.Received_Date >= @from AND po.Received_Date < @to";
        object? k = null;
        if (type == "m") { where += " AND DATE_FORMAT(po.Received_Date, '%Y-%m') = @k"; k = text; }

        var t = Table(new Col("Order"), new Col("Item"), new Col("Supplier"), new Col("Ordered"), new Col("Received"),
                      new Col("Quantity", true), new Col("Cost", true));
        await using var cmd = Cmd(conn, $@"
            SELECT po.PO_ID, COALESCE(f.Feed_Name, p.Product_Name), sup.Supplier_Name, po.Order_Date, po.Received_Date,
                   po.Quantity, po.Total_Cost
            FROM Purchase_Order po
            LEFT JOIN Feed f ON f.Feed_ID = po.Feed_ID
            LEFT JOIN Product p ON p.Product_ID = po.Product_ID
            LEFT JOIN Supplier sup ON sup.Supplier_ID = po.Supplier_ID
            WHERE {where}
            ORDER BY po.Received_Date DESC");
        if (k != null) cmd.Parameters.AddWithValue("@k", k);
        await using var r = await cmd.ExecuteReaderAsync();
        decimal total = 0;
        while (await r.ReadAsync())
        {
            total += Dec(r, 6);
            t.Rows.Add(R(new[]
            {
                T("#" + r.GetInt32(0), "/Shop/Inventory"), T(Str(r, 1)), T(Str(r, 2)), D(DateOrNull(r, 3)), D(DateOrNull(r, 4)),
                Dec1(DecOrNull(r, 5)), Money(Dec(r, 6))
            }));
        }
        if (t.Rows.Count > 0) t.Total = R(new[] { T("Total"), T(""), T(""), T(""), T(""), T(""), Money(total) });
        ResultTitle = "Supplies received" + (type == "m" ? ": " + MonthName(text) : "");
        t.Note = "Feed and gift shop stock orders, counted when they were received.";
        return t;
    }

    // ===== Source records: repairs completed =====
    private async Task<ReportTable> RepairsAsync(MySqlConnection conn)
    {
        var (type, text) = SplitKey(Key);
        var where = "m.Status = 'Completed' AND m.Completion_Date >= @from AND m.Completion_Date < @to";
        object? k = null;
        if (type == "m") { where += " AND DATE_FORMAT(m.Completion_Date, '%Y-%m') = @k"; k = text; }

        var t = Table(new Col("Work order"), new Col("Enclosure"), new Col("Description"), new Col("Requested"), new Col("Completed"), new Col("Cost", true));
        await using var cmd = Cmd(conn, $@"
            SELECT m.Maintenance_ID, e.Enclosure_Name, m.Description, m.Request_Date, m.Completion_Date, m.Cost
            FROM Maintenance m
            LEFT JOIN Enclosure e ON e.Enclosure_ID = m.Enclosure_ID
            WHERE {where}
            ORDER BY m.Completion_Date DESC");
        if (k != null) cmd.Parameters.AddWithValue("@k", k);
        await using var r = await cmd.ExecuteReaderAsync();
        decimal total = 0;
        while (await r.ReadAsync())
        {
            total += Dec(r, 5);
            t.Rows.Add(R(new[]
            {
                T("#" + r.GetInt32(0), $"/Staff/MaintenanceEdit/{r.GetInt32(0)}"), T(Str(r, 1) ?? "General"), T(Str(r, 2)),
                D(DateOrNull(r, 3)), D(DateOrNull(r, 4)), Money(Dec(r, 5))
            }));
        }
        if (t.Rows.Count > 0) t.Total = R(new[] { T("Total"), T(""), T(""), T(""), T(""), Money(total) });
        ResultTitle = "Repairs completed" + (type == "m" ? ": " + MonthName(text) : "");
        return t;
    }

    // ===== Source records: every payment made one way =====
    private async Task<ReportTable> PayListAsync(MySqlConnection conn)
    {
        var (_, method) = SplitKey(Key);
        bool none = method == "Not recorded";
        var t = Table(new Col("Kind"), new Col("Number"), new Col("Date"), new Col("Amount", true));
        await using var cmd = Cmd(conn, $@"
            SELECT 'Tickets', ts.TicketPurchase_ID, ts.Visit_Date, ts.Total_Amount FROM Ticket_Sale ts
            WHERE {TicketWhere} AND {(none ? "ts.Payment_Method IS NULL" : "ts.Payment_Method = @k")}
            UNION ALL
            SELECT IF(s.Sale_Type = 'Food', 'Food', 'Gift shop'), s.Sale_ID, s.Sale_Date, s.Total_Amount FROM Sale s
            WHERE {SaleWhere} AND {(none ? "s.Payment_Method IS NULL" : "s.Payment_Method = @k")}
            ORDER BY 3 DESC
            LIMIT 500");
        cmd.Parameters.AddWithValue("@k", method);
        await using var r = await cmd.ExecuteReaderAsync();
        decimal total = 0;
        while (await r.ReadAsync())
        {
            var kind = r.GetString(0);
            var id = Convert.ToInt32(r.GetValue(1));
            total += Dec(r, 3);
            var link = kind == "Tickets" ? $"/Tickets/TicketSaleDetails/{id}" : $"/Shop/SaleDetails/{id}";
            t.Rows.Add(R(new[] { T(kind), T("#" + id, link), D(r.GetDateTime(2)), Money(Dec(r, 3)) }));
        }
        if (t.Rows.Count > 0) t.Total = R(new[] { T("Total"), T(""), T(""), Money(total) });
        ResultTitle = "Payments: " + method;
        t.Note = "Ticket orders use the visit date; shop sales use the sale date. Shows up to 500.";
        return t;
    }
}
