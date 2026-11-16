using MySqlConnector;

namespace ZooDatabase.Data;

// Unified users: every login lives in User_Account, and each login can be linked to the
// person who uses it. Staff link to Employee, the Volunteer role (saved as 'ReadOnly') links to
// Volunteer, and the Customer role links to Customer. A login belongs to at most one person.
// The keeper, vet, cashier and tour guide workspaces use the employee link to show "my animals",
// "my tasks" and to record who made a sale, so those accounts need it set.
// The database checks the same rules (triggers in unified_users.sql).
public static class UserEmployees
{
    // Roles whose workspace only works when the login is linked to an employee
    public static readonly string[] NeedsLink = { "Zookeeper", "Veterinarian", "Cashier", "TourGuide" };

    public const string CustomerRole = "Customer";
    public const string VolunteerRole = "ReadOnly";

    public record Choice(int Id, string Name, string? Job, string? LinkedTo);

    // The person a login is linked to (at most one of the three is set)
    public record Links(int? EmployeeId, int? VolunteerId, int? CustomerId);

    // What the Add User and Edit User forms need to draw the "Linked to" dropdowns
    public record LinkFields(List<Choice> Employees, List<Choice> Volunteers, List<Choice> Customers,
                             int? EmployeeId, int? VolunteerId, int? CustomerId);

    // Every employee, with the username already linked to them (ignoring the account being edited)
    public static Task<List<Choice>> LoadAsync(MySqlConnection conn, int? exceptUserId = null) =>
        LoadPeopleAsync(conn, exceptUserId, @"
            SELECT e.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name), e.Job_Position,
                   (SELECT u.Username FROM User_Account u
                    WHERE u.Employee_ID = e.Employee_ID AND (@except IS NULL OR u.User_ID <> @except) LIMIT 1)
            FROM Employee e
            ORDER BY e.Last_Name, e.First_Name");

    // Every volunteer, with the username already linked to them
    public static Task<List<Choice>> LoadVolunteersAsync(MySqlConnection conn, int? exceptUserId = null) =>
        LoadPeopleAsync(conn, exceptUserId, @"
            SELECT v.Volunteer_ID, CONCAT(v.First_Name, ' ', v.Last_Name), v.Volunteer_Status,
                   (SELECT u.Username FROM User_Account u
                    WHERE u.Volunteer_ID = v.Volunteer_ID AND (@except IS NULL OR u.User_ID <> @except) LIMIT 1)
            FROM Volunteer v
            ORDER BY v.Last_Name, v.First_Name");

    // Every customer, with the username already linked to them
    public static Task<List<Choice>> LoadCustomersAsync(MySqlConnection conn, int? exceptUserId = null) =>
        LoadPeopleAsync(conn, exceptUserId, @"
            SELECT c.Customer_ID, CONCAT(c.First_Name, ' ', c.Last_Name), c.Email,
                   (SELECT u.Username FROM User_Account u
                    WHERE u.Customer_ID = c.Customer_ID AND (@except IS NULL OR u.User_ID <> @except) LIMIT 1)
            FROM Customer c
            ORDER BY c.Last_Name, c.First_Name");

    private static async Task<List<Choice>> LoadPeopleAsync(MySqlConnection conn, int? exceptUserId, string sql)
    {
        var list = new List<Choice>();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@except", (object?)exceptUserId ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new Choice(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                                r.IsDBNull(3) ? null : r.GetString(3)));
        return list;
    }

    // Keeps only the link that fits the role, so a hidden dropdown on the form can't sneak one in
    public static Links ForRole(string role, int? employeeId, int? volunteerId, int? customerId) => role switch
    {
        CustomerRole => new Links(null, null, customerId),
        // A Volunteer login links to a volunteer, or (for read-only staff) to an employee
        VolunteerRole => volunteerId != null ? new Links(null, volunteerId, null) : new Links(employeeId, null, null),
        _ => new Links(employeeId, null, null)
    };

    // Returns an error message, or null if the links are fine for this role
    public static string? Check(string role, Links links,
                                List<Choice> employees, List<Choice> volunteers, List<Choice> customers)
    {
        if (role == CustomerRole)
        {
            if (links.CustomerId == null) return "A Customer account needs to be linked to the customer who uses it.";
            return CheckChoice(customers, links.CustomerId, "customer");
        }
        if (links.VolunteerId != null) return CheckChoice(volunteers, links.VolunteerId, "volunteer");
        if (links.EmployeeId == null)
            return NeedsLink.Contains(role)
                ? $"A {RoleNames.Label(role)} account needs to be linked to the employee who uses it."
                : null;
        return CheckChoice(employees, links.EmployeeId, "employee");
    }

    private static string? CheckChoice(List<Choice> choices, int? id, string kind)
    {
        var p = choices.FirstOrDefault(c => c.Id == id);
        if (p == null) return $"Please choose a {kind} from the list.";
        if (p.LinkedTo != null) return $"{p.Name} is already linked to the account '{p.LinkedTo}'.";
        return null;
    }
}
