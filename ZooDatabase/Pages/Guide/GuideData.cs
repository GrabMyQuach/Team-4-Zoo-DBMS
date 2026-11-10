namespace ZooDatabase.Pages.Guide;

// Shared rules for the tour guide pages
public static class GuideData
{
    // Why an animal can't be visited today, or null when it's on show.
    // Off exhibit = its enclosure isn't open, it's in quarantine or under vet observation, or it's being moved.
    public static string? OffExhibitReason(string? enclosureStatus, string? closedReason, string? health, bool needsRelocation)
    {
        if (enclosureStatus != null && enclosureStatus != "Open")
            return "Enclosure " + enclosureStatus.ToLowerInvariant() + (string.IsNullOrWhiteSpace(closedReason) ? "" : ": " + closedReason);
        if (health == "Quarantined") return "In quarantine";
        if (health == "Under Observation") return "Under vet observation";
        if (needsRelocation) return "Being moved to a new enclosure";
        return null;
    }

    // Same rule in SQL, for counting. Needs Animal a and Enclosure e.
    public const string OnShowSql =
        "e.Status = 'Open' AND COALESCE(a.Health_Status, '') NOT IN ('Quarantined', 'Under Observation') AND a.Needs_Relocation = 0";

    // "3 years old", "8 months old"
    public static string? Age(DateTime? born)
    {
        if (born == null) return null;
        var today = DateTime.Today;
        int years = today.Year - born.Value.Year - (today < born.Value.AddYears(today.Year - born.Value.Year) ? 1 : 0);
        if (years >= 1) return years == 1 ? "1 year old" : $"{years} years old";
        int months = (today.Year - born.Value.Year) * 12 + today.Month - born.Value.Month - (today.Day < born.Value.Day ? 1 : 0);
        return months <= 1 ? "a baby" : $"{months} months old";
    }

    public static bool AtRisk(string? status) =>
        status != null && (status.Contains("Endangered", StringComparison.OrdinalIgnoreCase)
                           || status.Contains("Vulnerable", StringComparison.OrdinalIgnoreCase));
}
