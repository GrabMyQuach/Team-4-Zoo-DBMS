using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Animals;

public class FeedModel : PageModel
{
    private readonly IConfiguration _config;

    public FeedModel(IConfiguration config)
    {
        _config = config;
    }

    public List<FeedRow> Rows { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // GROUP_CONCAT joins all the species that eat this feed into one line, like "Lion, Tiger"
        const string sql = @"
            SELECT f.Feed_ID, f.Feed_Name, f.Feed_Type, sup.Supplier_Name,
                   f.Quantity_In_Stock, f.Storage_Location, f.Expiration_Date,
                   GROUP_CONCAT(s.Common_Name ORDER BY s.Common_Name SEPARATOR ', ') AS Eaten_By
            FROM Feed f
            LEFT JOIN Supplier sup ON f.Supplier_ID = sup.Supplier_ID
            LEFT JOIN Species_Feed sf ON sf.Feed_ID = f.Feed_ID
            LEFT JOIN Species s ON s.Species_ID = sf.Species_ID
            GROUP BY f.Feed_ID, f.Feed_Name, f.Feed_Type, sup.Supplier_Name,
                     f.Quantity_In_Stock, f.Storage_Location, f.Expiration_Date
            ORDER BY f.Feed_Name";

        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Rows.Add(new FeedRow
            {
                Name = reader.GetString(1),
                Type = reader.IsDBNull(2) ? null : reader.GetString(2),
                Supplier = reader.IsDBNull(3) ? null : reader.GetString(3),
                QuantityInStock = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                StorageLocation = reader.IsDBNull(5) ? null : reader.GetString(5),
                ExpirationDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                EatenBy = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }
    }

    public class FeedRow
    {
        public string Name { get; set; } = "";
        public string? Type { get; set; }
        public string? Supplier { get; set; }
        public decimal? QuantityInStock { get; set; }
        public string? StorageLocation { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public string? EatenBy { get; set; }
    }
}