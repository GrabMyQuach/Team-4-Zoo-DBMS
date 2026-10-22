using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages;

public class IndexModel : PageModel
{
    private readonly IConfiguration _config;
    public string Message { get; set; } = "";

    public IndexModel(IConfiguration config)
    {
        _config = config;
    }

    public async Task OnGetAsync()
    {
        try
        {
            using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
            await conn.OpenAsync();

            using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE()", conn);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            Message = $"Connected to the zoo database! Found {count} tables.";
        }
        catch (Exception ex)
        {
            Message = "Connection failed: " + ex.Message;
        }
    }
}
