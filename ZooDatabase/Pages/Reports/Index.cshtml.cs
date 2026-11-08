using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Reports;

// The Reports landing page: one card per report the user is allowed to open
[Authorize(Roles = ReportAccess.AnyReport)]
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
