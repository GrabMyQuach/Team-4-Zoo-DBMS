using System.Security.Claims;

namespace ZooDatabase.Data;

// Who can open each report (least privilege: each report goes only to the roles that need it).
// Change a line here and the report's lock, tab, and card all follow.
public static class ReportAccess
{
    // Vets need health data; zookeepers don't.
    public const string AnimalHealth = "DBA,SuperUser,Veterinarian";

    // Zookeepers run feeding; vets and cashiers don't need it.
    public const string Feeding = "DBA,SuperUser,Zookeeper";

    // Facility safety and repair costs are a management concern.
    public const string HabitatSafety = "DBA,SuperUser,Zookeeper";

    // Labor cost and staff attendance are a management concern.
    public const string Staffing = "DBA,SuperUser";

    // Revenue, expenses and profit are a management concern.
    public const string Revenue = "DBA,SuperUser";

    // Anyone who can see at least one report (used for the Reports link and overview page)
    public const string AnyReport = "DBA,SuperUser,Veterinarian,Zookeeper";

    public static bool CanView(ClaimsPrincipal user, string roles = AnyReport) =>
        roles.Split(',').Any(user.IsInRole);
}
