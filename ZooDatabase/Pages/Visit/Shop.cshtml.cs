using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Visit;

// Public "Shop Online" page: a handful of best-selling gift shop items that are in stock.
// The full selection is only in the gift shops at the park.
public class ShopModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public ShopModel(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    public List<ShopProduct> Products { get; set; } = new();
    [BindProperty] public Dictionary<int, int> Quantities { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await using var conn = await VisitData.OpenAsync(_config);
        Products = await VisitData.ProductsAsync(conn, _env, null);
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        await using var conn = await VisitData.OpenAsync(_config);
        Products = await VisitData.ProductsAsync(conn, _env, null);

        var cart = Cart.Load(HttpContext.Session);
        int added = 0;
        foreach (var p in Products)
        {
            if (!Quantities.TryGetValue(p.Id, out var qty) || qty <= 0) continue;
            var already = cart.Products.GetValueOrDefault(p.Id);
            if (already + qty > p.Stock)
            {
                ErrorMessage = $"Sorry, only {p.Stock} {p.Name} left.";
                return Page();
            }
            Cart.Add(cart.Products, p.Id, Math.Min(qty, Cart.MaxPerLine));
            added += qty;
        }

        if (added == 0)
        {
            ErrorMessage = "Choose how many of an item you'd like first.";
            return Page();
        }

        cart.Save(HttpContext.Session);
        return Redirect("/visit/cart");
    }
}
