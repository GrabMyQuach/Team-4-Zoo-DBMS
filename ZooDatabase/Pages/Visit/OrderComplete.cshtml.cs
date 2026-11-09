using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ZooDatabase.Pages.Visit;

// "Thank you" page shown once right after an order is placed
public class OrderCompleteModel : PageModel
{
    public OrderSummary Order { get; set; } = new();

    public IActionResult OnGet()
    {
        if (TempData["Order"] is not string json) return Redirect("/");
        Order = JsonSerializer.Deserialize<OrderSummary>(json) ?? new OrderSummary();
        return Page();
    }
}
