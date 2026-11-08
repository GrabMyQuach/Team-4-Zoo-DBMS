using System.Security.Claims;

namespace ZooDatabase.Data;

// Shared settings for every animal page, so they all stay consistent.
public static class AnimalOptions
{
    // Roles allowed to add, edit, and remove animals.
    // Change this one line and every animal page follows (the lock AND the buttons).
    public const string EditorRoles = "DBA,SuperUser,Admin";

    public static bool CanEdit(ClaimsPrincipal user) =>
        EditorRoles.Split(',').Any(user.IsInRole);

    // Dropdown choices, so staff pick from a list instead of typing
    public static readonly string[] Genders = { "Male", "Female", "Unknown" };

    public static readonly string[] HealthStatuses =
        { "Healthy", "Under Observation", "Recovering", "Recovered", "Quarantined" };

    public static readonly string[] AcquisitionTypes =
        { "Born at Zoo", "Transfer In", "Rescue", "Purchase" };

    // Badge color for a health status: green = fine, yellow = watch, red = urgent
    public static string HealthBadgeClass(string? status) => status switch
    {
        "Healthy" or "Recovered" => "bg-success",
        "Quarantined" => "bg-danger",
        null or "" => "bg-secondary",
        _ => "bg-warning text-dark"
    };

    // Age in whole years from a birth date, like "7 years"
    public static string AgeText(DateTime? birth)
    {
        if (birth == null) return "Unknown";
        var today = DateTime.Today;
        int years = today.Year - birth.Value.Year;
        if (birth.Value.Date > today.AddYears(-years)) years--;
        if (years < 1)
        {
            int months = Math.Max(0, (today.Year - birth.Value.Year) * 12 + today.Month - birth.Value.Month);
            return months == 1 ? "1 month" : $"{months} months";
        }
        return years == 1 ? "1 year" : $"{years} years";
    }
}
