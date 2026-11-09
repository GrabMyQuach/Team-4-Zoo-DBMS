using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Visit;

// Cart and checkout for the public website.
// Checkout saves into the SAME tables the staff screens use, so online orders show up in
// Ticket Sales, Memberships, Shop Sales and the Revenue report:
//   tickets     -> Ticket_Sale + Ticket_Purchase_Item
//   membership  -> Membership
//   gift items  -> Sale (Sale_Type 'GiftShop', no cashier) + Sale_Item, one sale per gift shop
// Prices always come from the database, never from the browser. Tien's triggers add up totals
// and subtract gift shop stock (and refuse the sale if there isn't enough).
public class CartPageModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public CartPageModel(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    public record Line(string Kind, int Id, string Name, string? Detail, int Qty, decimal UnitPrice)
    {
        public decimal Total => Qty * UnitPrice;
    }

    public List<Line> Lines { get; set; } = new();
    public decimal Total => Lines.Sum(l => l.Total);
    public bool HasTickets => Lines.Any(l => l.Kind == "T");
    public bool HasMembership => Lines.Any(l => l.Kind == "M");
    public string? ErrorMessage { get; set; }

    // "cart" shows the items and totals; "checkout" shows the contact and payment form
    public string Step { get; set; } = "cart";

    // Update form
    [BindProperty] public Dictionary<int, int> TicketQty { get; set; } = new();
    [BindProperty] public Dictionary<int, int> ProductQty { get; set; } = new();
    [BindProperty] public string? Remove { get; set; }

    // Checkout form
    [BindProperty] public string? FirstName { get; set; }
    [BindProperty] public string? LastName { get; set; }
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public DateTime VisitDate { get; set; } = DateTime.Today;
    [BindProperty] public DateTime MemberStart { get; set; } = DateTime.Today;
    [BindProperty] public string PaymentMethod { get; set; } = VisitData.OnlinePayments[0];

    private List<ShopProduct> _products = new();

    public async Task OnGetAsync(string? step)
    {
        var cart = Cart.Load(HttpContext.Session);
        await using var conn = await VisitData.OpenAsync(_config);
        await BuildLinesAsync(conn, null, cart);
        Step = step == "checkout" && Lines.Count > 0 ? "checkout" : "cart";
        if (DateTime.TryParse(cart.VisitDate, out var visit) && visit.Date >= DateTime.Today) VisitDate = visit.Date;
    }

    public IActionResult OnPostUpdate()
    {
        var cart = Cart.Load(HttpContext.Session);
        foreach (var (id, qty) in TicketQty)
            if (cart.Tickets.ContainsKey(id)) cart.Tickets[id] = Math.Clamp(qty, 0, Cart.MaxPerLine);
        foreach (var (id, qty) in ProductQty)
            if (cart.Products.ContainsKey(id)) cart.Products[id] = Math.Clamp(qty, 0, Cart.MaxPerLine);

        // "Remove" buttons send T:5, P:12 or M
        if (Remove == "M") cart.MembershipTypeId = null;
        else if (Remove != null && Remove.Length > 2 && int.TryParse(Remove[2..], out var removeId))
        {
            if (Remove.StartsWith("T:")) cart.Tickets.Remove(removeId);
            if (Remove.StartsWith("P:")) cart.Products.Remove(removeId);
        }

        cart.Save(HttpContext.Session);
        if (cart.Tickets.Count == 0) { cart.VisitDate = null; cart.Save(HttpContext.Session); }
        return Redirect("/visit/cart");
    }

    public async Task<IActionResult> OnPostCheckoutAsync()
    {
        var cart = Cart.Load(HttpContext.Session);
        await using var conn = await VisitData.OpenAsync(_config);
        await BuildLinesAsync(conn, null, cart);
        if (Lines.Count == 0) return Redirect("/visit/cart");
        Step = "checkout";

        FirstName = (FirstName ?? "").Trim();
        LastName = (LastName ?? "").Trim();
        Email = (Email ?? "").Trim().ToLowerInvariant();
        Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim();

        if (FirstName.Length == 0 || LastName.Length == 0) ErrorMessage = "Please enter your first and last name.";
        else if (!Regex.IsMatch(Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) ErrorMessage = "Please enter a valid email address.";
        else if (!VisitData.OnlinePayments.Contains(PaymentMethod)) ErrorMessage = "Please choose a payment method.";
        else if (HasTickets && (VisitDate.Date < DateTime.Today || VisitDate.Date > DateTime.Today.AddYears(1)))
            ErrorMessage = "Please choose a visit date between today and one year from now.";
        else if (HasMembership && (MemberStart.Date < DateTime.Today || MemberStart.Date > DateTime.Today.AddMonths(3)))
            ErrorMessage = "Memberships can start today or within the next 3 months.";
        else
        {
            var shortItem = Lines.Where(l => l.Kind == "P")
                .Select(l => (Line: l, Stock: _products.First(p => p.Id == l.Id).Stock))
                .FirstOrDefault(x => x.Line.Qty > x.Stock);
            if (shortItem.Line != null) ErrorMessage = $"Sorry, only {shortItem.Stock} {shortItem.Line.Name} left. Please lower the quantity.";
        }
        if (ErrorMessage != null) return Page();

        var summary = new OrderSummary { Name = $"{FirstName} {LastName}", Email = Email, Payment = PaymentMethod, Total = Total };
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            int customerId = await FindOrCreateCustomerAsync(conn, tx);

            // Membership
            var memberLine = Lines.FirstOrDefault(l => l.Kind == "M");
            if (memberLine != null)
            {
                await using (var check = new MySqlCommand(
                    "SELECT MAX(Expiration_Date) FROM Membership WHERE Customer_ID = @c AND Expiration_Date >= @start", conn, tx))
                {
                    check.Parameters.AddWithValue("@c", customerId);
                    check.Parameters.AddWithValue("@start", MemberStart.Date);
                    var existing = await check.ExecuteScalarAsync();
                    if (existing != null && existing != DBNull.Value)
                    {
                        await tx.RollbackAsync();
                        Step = "checkout";
                        ErrorMessage = $"{Email} already has a membership through {Convert.ToDateTime(existing):MMMM d, yyyy}. Remove the membership from your cart or pick a later start date.";
                        return Page();
                    }
                }
                var end = MemberStart.Date.AddYears(1).AddDays(-1);
                await using var cmd = new MySqlCommand(@"
                    INSERT INTO Membership (Customer_ID, MembershipType_ID, Start_Date, Expiration_Date, Status)
                    VALUES (@c, @t, @start, @end, 'Active')", conn, tx);
                cmd.Parameters.AddWithValue("@c", customerId);
                cmd.Parameters.AddWithValue("@t", memberLine.Id);
                cmd.Parameters.AddWithValue("@start", MemberStart.Date);
                cmd.Parameters.AddWithValue("@end", end);
                await cmd.ExecuteNonQueryAsync();
                summary.MembershipId = (int)cmd.LastInsertedId;
                summary.MembershipText = $"{memberLine.Name}, {MemberStart:MMM d, yyyy} to {end:MMM d, yyyy}";
            }

            // Tickets
            var ticketLines = Lines.Where(l => l.Kind == "T").ToList();
            if (ticketLines.Count > 0)
            {
                int saleId;
                await using (var cmd = new MySqlCommand(@"
                    INSERT INTO Ticket_Sale (Customer_ID, Purchase_Date, Total_Amount, Payment_Method, Visit_Date)
                    VALUES (@c, NOW(), 0, @pay, @visit)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@c", customerId);
                    cmd.Parameters.AddWithValue("@pay", PaymentMethod);
                    cmd.Parameters.AddWithValue("@visit", VisitDate.Date);
                    await cmd.ExecuteNonQueryAsync();
                    saleId = (int)cmd.LastInsertedId;
                }
                foreach (var l in ticketLines)
                {
                    await using var cmd = new MySqlCommand(@"
                        INSERT INTO Ticket_Purchase_Item (TicketPurchase_ID, TicketType_ID, Quantity, Unit_Price)
                        VALUES (@sale, @type, @qty, @price)", conn, tx);
                    cmd.Parameters.AddWithValue("@sale", saleId);
                    cmd.Parameters.AddWithValue("@type", l.Id);
                    cmd.Parameters.AddWithValue("@qty", l.Qty);
                    cmd.Parameters.AddWithValue("@price", l.UnitPrice);
                    await cmd.ExecuteNonQueryAsync();
                }
                summary.TicketOrderId = saleId;
                summary.VisitDate = VisitDate.ToString("dddd, MMMM d, yyyy");
            }

            // Gift shop items: one sale per gift shop, no cashier (Employee_ID stays empty for online orders)
            foreach (var shop in Lines.Where(l => l.Kind == "P").GroupBy(l => _products.First(p => p.Id == l.Id).GiftShopId))
            {
                int saleId;
                await using (var cmd = new MySqlCommand(@"
                    INSERT INTO Sale (Sale_Date, Employee_ID, Customer_ID, GiftShop_ID, Sale_Type, Total_Amount, Payment_Method)
                    VALUES (NOW(), NULL, @c, @shop, 'GiftShop', 0, @pay)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@c", customerId);
                    cmd.Parameters.AddWithValue("@shop", shop.Key);
                    cmd.Parameters.AddWithValue("@pay", PaymentMethod);
                    await cmd.ExecuteNonQueryAsync();
                    saleId = (int)cmd.LastInsertedId;
                }
                foreach (var l in shop)
                {
                    await using var cmd = new MySqlCommand(@"
                        INSERT INTO Sale_Item (Sale_ID, Product_ID, Quantity, Unit_Price) VALUES (@sale, @item, @qty, @price)", conn, tx);
                    cmd.Parameters.AddWithValue("@sale", saleId);
                    cmd.Parameters.AddWithValue("@item", l.Id);
                    cmd.Parameters.AddWithValue("@qty", l.Qty);
                    cmd.Parameters.AddWithValue("@price", l.UnitPrice);
                    await cmd.ExecuteNonQueryAsync();
                }
                summary.ShopOrderIds.Add(saleId);
                summary.PickupShops.Add(_products.First(p => p.GiftShopId == shop.Key).GiftShop);
            }

            await tx.CommitAsync();
        }
        catch (MySqlException ex)
        {
            await tx.RollbackAsync();
            // A trigger message (like "not enough stock") is written for people, so show it as is
            ErrorMessage = ex.SqlState == "45000" ? ex.Message : "Sorry, we couldn't place your order. Please try again.";
            return Page();
        }

        summary.Lines = Lines.Select(l => new OrderSummary.Item(l.Name, l.Detail, l.Qty, l.Total)).ToList();
        Cart.Clear(HttpContext.Session);
        TempData["Order"] = JsonSerializer.Serialize(summary);
        return Redirect("/visit/order-complete");
    }

    private async Task<int> FindOrCreateCustomerAsync(MySqlConnection conn, MySqlTransaction tx)
    {
        await using (var find = new MySqlCommand(
            "SELECT Customer_ID FROM Customer WHERE LOWER(Email) = @e ORDER BY Customer_ID LIMIT 1", conn, tx))
        {
            find.Parameters.AddWithValue("@e", Email);
            var found = await find.ExecuteScalarAsync();
            if (found != null && found != DBNull.Value) return Convert.ToInt32(found);
        }

        await using var cmd = new MySqlCommand(
            "INSERT INTO Customer (First_Name, Last_Name, Email, Phone_Number) VALUES (@f, @l, @e, @p)", conn, tx);
        cmd.Parameters.AddWithValue("@f", FirstName);
        cmd.Parameters.AddWithValue("@l", LastName);
        cmd.Parameters.AddWithValue("@e", Email);
        cmd.Parameters.AddWithValue("@p", (object?)Phone ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    // Turns the cart (IDs and quantities) into lines with names and current prices
    private async Task BuildLinesAsync(MySqlConnection conn, MySqlTransaction? tx, Cart cart)
    {
        Lines.Clear();
        if (cart.Tickets.Count > 0)
        {
            foreach (var t in await VisitData.TicketTypesAsync(conn, tx))
                if (cart.Tickets.TryGetValue(t.Id, out var q) && q > 0)
                    Lines.Add(new Line("T", t.Id, t.Name + " ticket", t.Note, q, t.Price));
        }
        if (cart.MembershipTypeId.HasValue)
        {
            var m = (await VisitData.MembershipTypesAsync(conn, tx)).FirstOrDefault(x => x.Id == cart.MembershipTypeId.Value);
            if (m != null) Lines.Add(new Line("M", m.Id, m.Name + " membership", "One year of unlimited visits", 1, m.Price));
        }
        if (cart.Products.Count > 0)
        {
            _products = await VisitData.ProductsAsync(conn, _env, cart.Products.Keys, tx: tx);
            foreach (var p in _products)
                Lines.Add(new Line("P", p.Id, p.Name, "Pick up at " + p.GiftShop, cart.Products[p.Id], p.Price));
        }
    }
}

// What the confirmation page shows after a successful order
public class OrderSummary
{
    public record Item(string Name, string? Detail, int Qty, decimal Total);

    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Payment { get; set; } = "";
    public decimal Total { get; set; }
    public List<Item> Lines { get; set; } = new();
    public int? TicketOrderId { get; set; }
    public string? VisitDate { get; set; }
    public int? MembershipId { get; set; }
    public string? MembershipText { get; set; }
    public List<int> ShopOrderIds { get; set; } = new();
    public List<string> PickupShops { get; set; } = new();
}
