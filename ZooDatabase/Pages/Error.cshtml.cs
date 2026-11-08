using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ZooDatabase.Pages;

// Shown instead of a blank screen when something goes wrong:
//   - a page or record that doesn't exist (404)
//   - a form that expired or was sent twice (400)
//   - an unexpected crash (500)
// Program.cs sends every error here with ?code=404 etc.
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public int? Code { get; set; }
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId) && (Code ?? 500) >= 500;

    public string Heading => Code switch
    {
        404 => "Page not found",
        400 => "That didn't go through",
        403 => "Access denied",
        405 => "That didn't go through",
        _ => "Something went wrong"
    };

    public string Message => Code switch
    {
        404 => "The page or record you were looking for doesn't exist. It may have been removed, or the link may be wrong.",
        400 or 405 => "The form may have expired. Go back, refresh the page, and try again.",
        403 => "Your account doesn't have permission to view this page.",
        _ => "An unexpected error happened on our side. Please try again in a moment."
    };

    // GET and POST both land here (a failed form post is re-run as a POST)
    public void OnGet(int? code) => Load(code);
    public void OnPost(int? code) => Load(code);

    private void Load(int? code)
    {
        Code = code;
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}
