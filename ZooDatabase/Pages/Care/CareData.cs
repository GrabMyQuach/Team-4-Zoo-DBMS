using System.Security.Claims;
using MySqlConnector;
using ZooDatabase.Data;
using ZooDatabase.Pages.Shop;

namespace ZooDatabase.Pages.Care;

public static class CareData
{
    public static async Task<MySqlConnection> OpenAsync(IConfiguration config)
    {
        var conn = new MySqlConnection(config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }

    // The logged-in person's Employee_ID (through User_Account), or null if the login isn't linked to an employee
    public static Task<int?> MeAsync(MySqlConnection conn, ClaimsPrincipal user) =>
        ShopText.CurrentEmployeeIdAsync(conn, user.Identity?.Name);

    // "My animals" for a keeper: animals they're assigned to, or whose feedings they're scheduled for.
    // Office roles see every animal. Uses the @me parameter.
    public static string MyAnimalsSql(ClaimsPrincipal user) => CareOptions.IsOffice(user)
        ? "1=1"
        : @"(a.Animal_ID IN (SELECT ae.Animal_ID FROM Animal_Employee ae WHERE ae.Employee_ID = @me)
             OR a.Animal_ID IN (SELECT x.Animal_ID FROM Feeding_Schedule x WHERE x.Keeper_Employee_ID = @me AND x.Is_Active = 1))";

    public static string HealthBadge(string? status) => status switch
    {
        "Healthy" or "Recovered" => "bg-success",
        "Under Observation" or "Recovering" => "bg-warning text-dark",
        "Quarantined" => "bg-danger",
        _ => "bg-secondary"
    };
}
