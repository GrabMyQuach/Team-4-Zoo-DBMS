using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Visit;

// Public "Buy Tickets" page: -/+ buttons for each ticket type, pick a day on the calendar, Continue.
// Prices come straight from the Ticket_Type table.
public class TicketsModel : PageModel
{
    private readonly IConfiguration _config;
    public TicketsModel(IConfiguration config) { _config = config; }

    public List<PriceRow> Tickets { get; set; } = new();
    [BindProperty] public Dictionary<int, int> Quantities { get; set; } = new();
    [BindProperty] public string? VisitDate { get; set; }
    public string? ErrorMessage { get; set; }

    // Lowest paid ticket, shown under each calendar day as "from $X"
    public decimal FromPrice => Tickets.Where(t => t.Price > 0).Select(t => t.Price).DefaultIfEmpty(0).Min();

    public async Task OnGetAsync()
    {
        await using var conn = await VisitData.OpenAsync(_config);
        Tickets = await VisitData.TicketTypesAsync(conn);

        // Show what's already in the cart so people can change it here
        var cart = Cart.Load(HttpContext.Session);
        Quantities = new Dictionary<int, int>(cart.Tickets);
        VisitDate = cart.VisitDate;
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        await using var conn = await VisitData.OpenAsync(_config);
        Tickets = await VisitData.TicketTypesAsync(conn);

        var chosen = Tickets
            .Select(t => (t.Id, Qty: Math.Clamp(Quantities.GetValueOrDefault(t.Id), 0, Cart.MaxPerLine)))
            .Where(x => x.Qty > 0)
            .ToList();

        bool dateOk = DateTime.TryParse(VisitDate, out var date)
                      && date.Date >= DateTime.Today && date.Date <= DateTime.Today.AddYears(1);

        if (chosen.Count == 0) ErrorMessage = "Choose how many tickets you'd like first.";
        else if (!dateOk) ErrorMessage = "Please pick the day you'd like to visit.";
        if (ErrorMessage != null) return Page();

        // The ticket picks on this page replace the tickets in the cart
        var cart = Cart.Load(HttpContext.Session);
        cart.Tickets = chosen.ToDictionary(x => x.Id, x => x.Qty);
        cart.VisitDate = date.ToString("yyyy-MM-dd");
        cart.Save(HttpContext.Session);
        return Redirect("/visit/cart");
    }
}
