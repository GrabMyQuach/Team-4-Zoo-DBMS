using System.Security.Claims;

namespace ZooDatabase.Data;

// Shared rules and dropdown choices for the Staff pages
public static class StaffOptions
{
    // Only the DBA can add or change employees and departments
    public const string EditorRoles = "DBA";

    public static bool CanEdit(ClaimsPrincipal user) => user.IsInRole("DBA");

    // Roles allowed to create and update maintenance requests (Staff > Maintenance).
    public const string MaintenanceEditorRoles = "DBA,SuperUser,Admin";

    public static bool CanEditMaintenance(ClaimsPrincipal user) =>
        MaintenanceEditorRoles.Split(',').Any(user.IsInRole);

    // Roles allowed to mark someone absent for a shift (Employee page > Shifts)
    public const string AttendanceRoles = "DBA,SuperUser,Admin";

    public static bool CanMarkAbsent(ClaimsPrincipal user) =>
        AttendanceRoles.Split(',').Any(user.IsInRole);

    // These must match the database: the Priority ENUM and the chk_maintenance_status rule
    public static readonly string[] MaintenancePriorities = { "Low", "Normal", "High", "Emergency" };
    public static readonly string[] MaintenanceStatuses = { "Open", "Pending", "In Progress", "Completed", "Cancelled" };

    // Private details (SSN, pay, address, login) are only shown to these roles
    public static bool CanSeePrivate(ClaimsPrincipal user) => user.IsInRole("DBA") || user.IsInRole("SuperUser");

    public static readonly string[] Genders = { "Female", "Male", "Other" };
    public static readonly string[] EmploymentTypes = { "Full-Time", "Part-Time" };
    public static readonly string[] PayTypes = { "Salary", "Hourly" };
    public static readonly string[] ExperienceLevels = { "Junior", "Intermediate", "Senior" };

    // Specialty is stored by having a row in Keeper or Veterinarian
    public static readonly string[] Specialties = { "None", "Keeper", "Veterinarian" };

    public static string MaskSsn(string? ssn) =>
        string.IsNullOrEmpty(ssn) || ssn.Length < 4 ? "—" : "***-**-" + ssn[^4..];

    public static string YearsText(DateTime? hired)
    {
        if (hired == null) return "—";
        var months = (DateTime.Today.Year - hired.Value.Year) * 12 + DateTime.Today.Month - hired.Value.Month;
        if (months < 12) return months <= 1 ? "New" : $"{months} months";
        return months / 12 == 1 ? "1 year" : $"{months / 12} years";
    }
}
