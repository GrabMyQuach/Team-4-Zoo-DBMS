
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ZooDatabase.Data;

namespace ZooDatabase.Pages;

public class ModuleModel : PageModel
{
    public string SectionTitle { get; set; } = "";

    public IActionResult OnGet(string module, string? section)
    {
        var m = ZooModules.Find(module);
        if (m == null) return NotFound();

        string role = User.FindFirst(
            System.Security.Claims.ClaimTypes.Role)?.Value ?? "";

        // ReadOnly cannot access internal staff or finance modules.
        if (role == "Customer" &&
            (m.Key == "Staff" || m.Key == "Finance"))
        {
            return Forbid();
        }

        // Zookeeper and Veterinarian only access Animals.
        if ((role == "Zookeeper" || role == "Veterinarian") &&
            m.Key != "Animals")
        {
            return Forbid();
        }

        // Cashier only accesses Tickets.
        if (role == "Cashier" && m.Key != "Tickets")
        {
            return Forbid();
        }

        if (section == null)
        {
            return Redirect($"/{m.Key}/{m.Sections[0].Slug}");
        }

        var s = m.Sections.FirstOrDefault(x =>
            string.Equals(x.Slug, section,
                StringComparison.OrdinalIgnoreCase));

        if (s == null) return NotFound();

        // Cashier can only access approved ticket sections.
        if (role == "Cashier" &&
            s.Slug != "TicketSales" &&
            s.Slug != "TicketTypes" &&
            s.Slug != "Memberships")
        {
            return Forbid();
        }

        SectionTitle = s.Title;
        return Page();
    }
}
