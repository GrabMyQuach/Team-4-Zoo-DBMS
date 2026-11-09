using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Visit;

// Public "Membership" page: levels, prices and benefits come from the Membership_Type table.
public class MembershipModel : PageModel
{
    private readonly IConfiguration _config;
    public MembershipModel(IConfiguration config) { _config = config; }

    public List<PriceRow> Levels { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = await VisitData.OpenAsync(_config);
        Levels = await VisitData.MembershipTypesAsync(conn);
    }

    // One membership per order: choosing a level replaces any level already in the cart
    public async Task<IActionResult> OnPostAddAsync(int typeId)
    {
        await using var conn = await VisitData.OpenAsync(_config);
        Levels = await VisitData.MembershipTypesAsync(conn);
        if (!Levels.Any(l => l.Id == typeId)) return Page();

        var cart = Cart.Load(HttpContext.Session);
        cart.MembershipTypeId = typeId;
        cart.Save(HttpContext.Session);
        return Redirect("/visit/cart");
    }
}
