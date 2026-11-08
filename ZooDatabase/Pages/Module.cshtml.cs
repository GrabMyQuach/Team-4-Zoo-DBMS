
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
        
        // Zookeepers can access only the Animals module
        if (User.IsInRole("Zookeeper") && m.Key != "Animals")
        {
            return Forbid();
        }
        // Veterinarians can access only the Animals module for now
        if (User.IsInRole("Veterinarian") && m.Key != "Animals")
        {
            return Forbid();
        }
        // Cashiers can access Tickets, but not Customers or Attractions
        if (User.IsInRole("Cashier"))
        {
           if (m.Key != "Tickets")
           {
               return Forbid();
           }

           string[] allowedSections =
           {
              "TicketSales",
              "TicketTypes",
              "Memberships",
              
           };

           string requestedSection = section ?? m.Sections[0].Slug;

           if (!allowedSections.Contains(
              requestedSection,
              StringComparer.OrdinalIgnoreCase))
           {
              return Forbid();
           }
        }
     


        // Block ReadOnly users from Staff & Operations
        if (User.IsInRole("ReadOnly") && m.Key == "Staff")
        {
            return Forbid();
        }

        // Block ReadOnly users from internal sections
        if (User.IsInRole("ReadOnly"))
        {
            bool restricted =
                (m.Key == "Animals" &&
                    (section == "Manage" ||
                     section == "Enclosures" ||
                     section == "Feed" ||
                     section == "animalreports")) ||

                (m.Key == "Tickets" &&
                    (section == "TicketSales" ||
                     section == "Customers" ||
                     section == "Reports")) ||

                (m.Key == "Shop" &&
                    (section == "Inventory" ||
                     section == "Suppliers" ||
                     section == "Sales" ||
                     section == "Reports"));

            if (restricted)
            {
                return Forbid();
            }
        }

        var s = section == null
            ? m.Sections[0]
            : m.Sections.FirstOrDefault(x =>
                string.Equals(x.Slug, section,
                    StringComparison.OrdinalIgnoreCase));

        if (s == null) return NotFound();

        SectionTitle = s.Title;
        return Page();
    }
}
