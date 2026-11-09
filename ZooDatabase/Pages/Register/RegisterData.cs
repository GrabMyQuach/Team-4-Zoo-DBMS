using MySqlConnector;

namespace ZooDatabase.Pages.Register;

// One button on a register: what it sells, its price, and the most that can be sold (stock)
public record PosItem(int Id, string Name, string? Detail, decimal Price, int Max);

public static class RegisterData
{
    public static async Task<MySqlConnection> OpenAsync(IConfiguration config)
    {
        var conn = new MySqlConnection(config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }

    // Finds the customer by email or phone. If nobody matches and a first and last name were given,
    // adds them as a new customer. Returns null with a message the cashier can act on otherwise.
    public static async Task<(int? Id, string? Error)> CustomerForSaleAsync(
        MySqlConnection conn, MySqlTransaction tx, string? lookup, string? firstName, string? lastName)
    {
        lookup = lookup?.Trim() ?? "";
        if (lookup.Length == 0) return (null, "Enter the customer's email or phone number.");

        bool isEmail = lookup.Contains('@');
        var digits = new string(lookup.Where(char.IsDigit).ToArray());
        if (isEmail && !System.Text.RegularExpressions.Regex.IsMatch(lookup, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return (null, "That email doesn't look right. Check it and try again.");
        if (!isEmail && digits.Length < 10)
            return (null, "Enter a full 10-digit phone number, or an email address.");

        await using (var find = new MySqlCommand(@"
            SELECT Customer_ID FROM Customer
            WHERE (@isEmail = 1 AND LOWER(Email) = LOWER(@v))
               OR (@isEmail = 0 AND RIGHT(REGEXP_REPLACE(COALESCE(Phone_Number, ''), '[^0-9]', ''), 10) = RIGHT(@digits, 10))
            ORDER BY Customer_ID LIMIT 1", conn, tx))
        {
            find.Parameters.AddWithValue("@isEmail", isEmail ? 1 : 0);
            find.Parameters.AddWithValue("@v", lookup);
            find.Parameters.AddWithValue("@digits", digits);
            var found = await find.ExecuteScalarAsync();
            if (found is not null and not DBNull) return (Convert.ToInt32(found), null);
        }

        firstName = firstName?.Trim();
        lastName = lastName?.Trim();
        if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName))
            return (null, $"No customer found for {lookup}. Enter their first and last name to add them.");

        // Phone saved as (713) 555-0100 so it matches the rest of the data
        string? phone = null;
        if (!isEmail)
        {
            var d = digits[^10..];
            phone = $"({d[..3]}) {d[3..6]}-{d[6..]}";
        }
        await using var add = new MySqlCommand(
            "INSERT INTO Customer (First_Name, Last_Name, Email, Phone_Number) VALUES (@f, @l, @e, @p)", conn, tx);
        add.Parameters.AddWithValue("@f", firstName);
        add.Parameters.AddWithValue("@l", lastName);
        add.Parameters.AddWithValue("@e", isEmail ? lookup.ToLowerInvariant() : DBNull.Value);
        add.Parameters.AddWithValue("@p", (object?)phone ?? DBNull.Value);
        await add.ExecuteNonQueryAsync();
        return ((int)add.LastInsertedId, null);
    }
}
