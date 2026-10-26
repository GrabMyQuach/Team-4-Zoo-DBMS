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
        // "/Animals" with no section → jump to the first sidebar item, like "/Animals/Animals"
if (section == null) return Redirect($"/{m.Key}/{m.Sections[0].Slug}");

        var s = section == null
            ? m.Sections[0]
            : m.Sections.FirstOrDefault(x => string.Equals(x.Slug, section, StringComparison.OrdinalIgnoreCase));
        if (s == null) return NotFound();

        SectionTitle = s.Title;
        return Page();
    }
}