using System.Security.Claims;

namespace ZooDatabase.Data;

// Who uses the animal care workspace (/Care): zookeepers get feedings, tasks, their animals and problem reports;
// vets get the animals needing attention, exams and history, and quarantines. Office roles see both.
public static class CareOptions
{
    public const string KeeperRoles = AnimalOptions.FeedingEditorRoles;   // Zookeeper, Veterinarian, Admin, SuperUser, DBA
    public const string VetRoles = AnimalOptions.HealthEditorRoles;       // Veterinarian, Admin, SuperUser, DBA

    public static bool IsOffice(ClaimsPrincipal user) =>
        user.IsInRole("DBA") || user.IsInRole("SuperUser") || user.IsInRole("Admin");

    public static bool ShowKeeperTools(ClaimsPrincipal user) => user.IsInRole("Zookeeper") || IsOffice(user);
    public static bool ShowVetTools(ClaimsPrincipal user) => user.IsInRole("Veterinarian") || IsOffice(user);

    // Keepers and vets (without an office role) skip the dashboard and land on their workspace
    public static bool LandsOnCare(ClaimsPrincipal user) =>
        !IsOffice(user) && (user.IsInRole("Zookeeper") || user.IsInRole("Veterinarian"));
}
