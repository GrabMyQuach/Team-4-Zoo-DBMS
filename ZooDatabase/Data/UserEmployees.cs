using MySqlConnector;

namespace ZooDatabase.Data;

// Links a login (User_Account) to the staff member who uses it (Employee).
// The keeper, vet, cashier and tour guide workspaces use this link to show "my animals", "my tasks"
// and to record who made a sale, so those accounts need it set.
public static class UserEmployees
{
    // Roles whose workspace only works when the login is linked to an employee
    public static readonly string[] NeedsLink = { "Zookeeper", "Veterinarian", "Cashier", "TourGuide" };

    public record Choice(int Id, string Name, string? Job, string? LinkedTo);

    // Every employee, with the username already linked to them (ignoring the account being edited)
    public static async Task<List<Choice>> LoadAsync(MySqlConnection conn, int? exceptUserId = null)
    {
        var list = new List<Choice>();
        await using var cmd = new MySqlCommand(@"
            SELECT e.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name), e.Job_Position,
                   (SELECT u.Username FROM User_Account u
                    WHERE u.Employee_ID = e.Employee_ID AND (@except IS NULL OR u.User_ID <> @except) LIMIT 1)
            FROM Employee e
            ORDER BY e.Last_Name, e.First_Name", conn);
        cmd.Parameters.AddWithValue("@except", (object?)exceptUserId ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new Choice(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                                r.IsDBNull(3) ? null : r.GetString(3)));
        return list;
    }

    // Returns an error message, or null if the choice is fine
    public static string? Check(List<Choice> choices, int? employeeId, string role)
    {
        if (employeeId == null)
            return NeedsLink.Contains(role)
                ? $"A {RoleNames.Label(role)} account needs to be linked to the employee who uses it."
                : null;
        var e = choices.FirstOrDefault(c => c.Id == employeeId);
        if (e == null) return "Please choose an employee from the list.";
        if (e.LinkedTo != null) return $"{e.Name} is already linked to the account '{e.LinkedTo}'.";
        return null;
    }
}
