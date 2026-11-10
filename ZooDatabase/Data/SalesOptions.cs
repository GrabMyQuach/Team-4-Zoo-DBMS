using System.Security.Claims;

namespace ZooDatabase.Data;

// Shared rules for the Tickets and Shop pages
public static class SalesOptions
{
    // Who can ring up sales and sign people up (front-desk work)
    public const string SalesRoles = "DBA,SuperUser,Admin,Cashier";

    // Who can change prices, ticket types, attractions, shops, products, suppliers
    public const string ManagerRoles = "DBA";

    // Who can book animal encounters (Tickets > Attractions > Book Encounter)
    public const string EncounterRoles = "DBA,SuperUser,Admin";

    public static bool CanBookEncounter(ClaimsPrincipal user) => EncounterRoles.Split(',').Any(user.IsInRole);

    // Who can void (undo) a food, gift shop or ticket sale that was rung up by mistake
    public const string VoidRoles = "DBA,SuperUser,Admin";
    public static bool CanVoid(ClaimsPrincipal user) => VoidRoles.Split(',').Any(user.IsInRole);

    public static bool CanSell(ClaimsPrincipal user) => SalesRoles.Split(',').Any(user.IsInRole);
    public static bool CanManage(ClaimsPrincipal user) => user.IsInRole("DBA");

    // A cashier with no office role: they get the simple register instead of the full staff screens
    public static bool IsCashierOnly(ClaimsPrincipal user) =>
        user.IsInRole("Cashier") && !user.IsInRole("DBA") && !user.IsInRole("SuperUser") && !user.IsInRole("Admin");

    // Payment methods offered at a counter
    public static readonly string[] CounterPayments = { "Cash", "Credit Card", "Debit Card", "Mobile Pay" };

    // Read-only visitors never see customer emails or phone numbers
    public static bool CanSeeContact(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && !user.IsInRole("Customer");

    public static readonly string[] PaymentMethods = { "Credit Card", "Debit Card", "Cash", "Mobile Pay", "PayPal" };
    public static readonly string[] AttractionStatuses = { "Open", "Closed", "Maintenance", "Closed for Season" };

    // Membership status from its dates, so it is always current
    public static string MembershipStatus(DateTime start, DateTime end)
    {
        var today = DateTime.Today;
        if (start > today) return "Upcoming";
        if (end < today) return "Expired";
        return end <= today.AddDays(30) ? "Expiring Soon" : "Active";
    }

    public static string MembershipBadge(string status) => status switch
    {
        "Active" => "bg-success",
        "Expiring Soon" => "bg-warning text-dark",
        "Expired" => "bg-secondary",
        _ => "bg-light text-dark border"
    };

    public static string StatusBadge(string? status) => status switch
    {
        "Open" => "bg-success",
        "Closed" => "bg-danger",
        null or "" => "bg-secondary",
        _ => "bg-warning text-dark"
    };

    // Payment methods for a dropdown: our standard list plus anything already in the data
    public static string[] WithExisting(IEnumerable<string> fromDb) =>
        PaymentMethods.Concat(fromDb.Where(x => !string.IsNullOrWhiteSpace(x))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
