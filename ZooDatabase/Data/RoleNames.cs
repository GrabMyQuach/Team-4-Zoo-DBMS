namespace ZooDatabase.Data;

// The roles saved in the database (User_Account.Role) never change, because the code and the database
// check them by these exact names. This file only decides the friendlier name people SEE on screen.
public static class RoleNames
{
    // Every role, in the order it shows in the Role dropdown: (saved name, name shown)
    public static readonly (string Role, string Label)[] All =
    {
        ("DBA", "Director"),
        ("SuperUser", "Manager"),
        ("Admin", "Supervisor"),
        ("Zookeeper", "Zookeeper"),
        ("Veterinarian", "Veterinarian"),
        ("Cashier", "Cashier"),
        ("TourGuide", "Tour Guide"),
        ("ReadOnly", "Volunteer")
    };

    // The saved names only, used to check a chosen role is valid
    public static readonly string[] Stored = All.Select(r => r.Role).ToArray();

    // "DBA" -> "Director". Unknown roles are shown as they are.
    public static string Label(string? role)
    {
        foreach (var r in All)
        {
            if (r.Role == role) return r.Label;
        }
        return role ?? "";
    }

    // Saved names whose shown name matches a search, so searching "manager" finds SuperUser accounts
    public static string[] Matching(string search) => All
        .Where(r => r.Label.Contains(search, StringComparison.OrdinalIgnoreCase))
        .Select(r => r.Role)
        .ToArray();
}
