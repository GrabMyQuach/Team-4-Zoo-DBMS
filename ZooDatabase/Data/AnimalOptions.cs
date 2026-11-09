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
    public const string HealthEditorRoles = "DBA,SuperUser,Admin,Veterinarian";

    public static bool CanRecordHealth(ClaimsPrincipal user) =>
    HealthEditorRoles.Split(',').Any(user.IsInRole);

    public static readonly string[] RecordTypes =
    { "Routine Checkup", "Illness", "Injury", "Vaccination", "Follow-up", "Neonatal Exam" };

    public static readonly string[] Severities = { "Low", "Moderate", "High", "Critical" };

    // Roles allowed to log feedings (Animals > Details > Log Feeding). Same roles as the Feed page.
    public const string FeedingEditorRoles = "Zookeeper,Veterinarian,Admin,SuperUser,DBA";

    public static bool CanLogFeeding(ClaimsPrincipal user) =>
        FeedingEditorRoles.Split(',').Any(user.IsInRole);

    // Must match the Status ENUM on Feeding_Log in the database
    public static readonly string[] FeedingStatuses = { "Completed", "Partial", "Refused", "Missed" };

    // Roles allowed to record habitat inspections (Enclosure page > Record Inspection)
    public const string InspectionRoles = "Zookeeper,Admin,SuperUser,DBA";

    public static bool CanInspect(ClaimsPrincipal user) =>
        InspectionRoles.Split(',').Any(user.IsInRole);

    // Must match the Result and Issue_Category ENUMs on Habitat_Inspection
    public static readonly string[] InspectionResults = { "Pass", "Minor Issues", "Critical" };
    public static readonly string[] IssueCategories =
        { "None", "Structural", "Barrier/Fencing", "Water Quality", "Temperature/Climate",
          "Electrical", "Sanitation", "Vegetation/Hazard", "Other" };

    // Roles allowed to approve and receive feed restock orders (Animals > Feed)
    public const string FeedOrderRoles = "Admin,SuperUser,DBA";

    public static bool CanManageFeedOrders(ClaimsPrincipal user) =>
        FeedOrderRoles.Split(',').Any(user.IsInRole);

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
