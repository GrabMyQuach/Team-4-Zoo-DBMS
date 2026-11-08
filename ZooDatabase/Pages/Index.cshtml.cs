
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ZooDatabase.Pages;

[Authorize]
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
