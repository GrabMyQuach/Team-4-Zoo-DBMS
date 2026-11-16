using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ZooDatabase.Data;

// Customer logins share User_Account with staff (unified users), but customers only get the
// public website plus their own account page. Anything else sends them back to the homepage.
public static class CustomerOptions
{
    public static bool IsCustomer(ClaimsPrincipal user) => user.IsInRole(UserEmployees.CustomerRole);

    public static bool CustomerCanOpen(PathString path) =>
        path == "/"
        || path.StartsWithSegments("/visit")
        || path.StartsWithSegments("/Login")
        || path.StartsWithSegments("/Logout")
        || path.StartsWithSegments("/MyAccount")
        || path.StartsWithSegments("/AccessDenied")
        || path.StartsWithSegments("/Error")
        || Path.HasExtension(path.Value);   // pictures, styles, scripts
}
