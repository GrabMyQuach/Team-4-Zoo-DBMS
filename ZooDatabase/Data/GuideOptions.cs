using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ZooDatabase.Data;

// Tour guides use their own small workspace (/Guide): today's tours, which animals are on show,
// talking points for each species, and Report a Problem. Office roles can open it too.
public static class GuideOptions
{
    public const string GuideRole = "TourGuide";
    public const string GuideRoles = "TourGuide,DBA,SuperUser,Admin";

    public static bool IsGuideOnly(ClaimsPrincipal user) => user.IsInRole(GuideRole);

    // The only places a tour guide can go. Anything else sends them back to /Guide,
    // so they never see staff, sales, or medical pages.
    public static bool GuideCanOpen(PathString path) =>
        path == "/"
        || path.StartsWithSegments("/Guide")
        || path.StartsWithSegments("/Care/Problem")
        || path.StartsWithSegments("/visit")
        || path.StartsWithSegments("/Login")
        || path.StartsWithSegments("/Logout")
        || path.StartsWithSegments("/MyAccount")
        || path.StartsWithSegments("/AccessDenied")
        || path.StartsWithSegments("/Error")
        || Path.HasExtension(path.Value);   // pictures, styles, scripts
}
