using MySqlConnector;

namespace ZooDatabase.Data;

public static class UserSeeder
{
    public static async Task SeedAsync(IConfiguration config)
    {
        // Get the test password from user secrets. If there isn't one, skip.
        var seedPassword = config["SeedPassword"];
        if (string.IsNullOrWhiteSpace(seedPassword)) return;

        var users = new (string Username, string Role)[]
        {
            ("dba", "DBA"),
            ("superuser", "SuperUser"),
            ("admin", "Admin"),
            ("readonly", "ReadOnly")
        };

        using var conn = new MySqlConnection(config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        foreach (var (username, role) in users)
        {
            // INSERT IGNORE = skip if this username already exists
            using var cmd = new MySqlCommand(
                "INSERT IGNORE INTO User_Account (Username, Password_Hash, Role) " +
                "VALUES (@username, @hash, @role)", conn);
            cmd.Parameters.AddWithValue("@username", username);
            cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(seedPassword));
            cmd.Parameters.AddWithValue("@role", role);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}