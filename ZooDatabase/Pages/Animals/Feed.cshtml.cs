using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Animals;

[Authorize(Roles = "Zookeeper,Veterinarian,Admin,SuperUser,DBA")]
public class FeedModel : PageModel
{
    private readonly IConfiguration _config;

    public FeedModel(IConfiguration config)
    {
        _config = config;
    }

    public List<FeedRow> Rows { get; set; } = new();
    public List<OrderRow> Orders { get; set; } = new();
    public bool CanManageOrders => AnimalOptions.CanManageFeedOrders(User);

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // GROUP_CONCAT joins all the species that eat this feed into one line, like "Lion, Tiger"
        const string sql = @"
            SELECT f.Feed_ID, f.Feed_Name, f.Feed_Type, sup.Supplier_Name,
                   f.Quantity_In_Stock, f.Storage_Location, f.Expiration_Date, f.Reorder_Level, f.Unit,
                   GROUP_CONCAT(s.Common_Name ORDER BY s.Common_Name SEPARATOR ', ') AS Eaten_By
            FROM Feed f
            LEFT JOIN Supplier sup ON f.Supplier_ID = sup.Supplier_ID
            LEFT JOIN Species_Feed sf ON sf.Feed_ID = f.Feed_ID
            LEFT JOIN Species s ON s.Species_ID = sf.Species_ID
            GROUP BY f.Feed_ID, f.Feed_Name, f.Feed_Type, sup.Supplier_Name,
                     f.Quantity_In_Stock, f.Storage_Location, f.Expiration_Date, f.Reorder_Level, f.Unit
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
                ReorderLevel = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                Unit = reader.IsDBNull(8) ? null : reader.GetString(8),
                EatenBy = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }
        await reader.CloseAsync();

        // Feed restock orders still waiting (drafted automatically by sp_check_feed_stock when stock runs low)
        await using var orders = new MySqlCommand(@"
            SELECT po.PO_ID, f.Feed_Name, sup.Supplier_Name, po.Quantity, f.Unit, po.Total_Cost,
                   po.Order_Date, po.Expected_Delivery, po.Status, po.Auto_Generated
            FROM Purchase_Order po
            JOIN Feed f ON f.Feed_ID = po.Feed_ID
            LEFT JOIN Supplier sup ON sup.Supplier_ID = po.Supplier_ID
            WHERE po.Status IN ('Draft', 'Ordered')
            ORDER BY FIELD(po.Status, 'Draft', 'Ordered'), po.Order_Date", conn);
        await using var o = await orders.ExecuteReaderAsync();
        while (await o.ReadAsync())
        {
            Orders.Add(new OrderRow
            {
                Id = o.GetInt32(0),
                Feed = o.GetString(1),
                Supplier = o.IsDBNull(2) ? null : o.GetString(2),
                Quantity = o.GetDecimal(3),
                Unit = o.IsDBNull(4) ? "" : o.GetString(4),
                Total = o.IsDBNull(5) ? null : o.GetDecimal(5),
                OrderDate = o.IsDBNull(6) ? null : o.GetDateTime(6),
                ExpectedDelivery = o.IsDBNull(7) ? null : o.GetDateTime(7),
                Status = o.GetString(8),
                Auto = !o.IsDBNull(9) && o.GetBoolean(9)
            });
        }
    }

    // Approve / Receive / Cancel a feed restock order.
    // "Received" fires trg_purchase_received: the amount is added to feed stock,
    // the received date is filled in, and the order's approval task is closed.
    public async Task<IActionResult> OnPostOrderAsync(int poId, string status)
    {
        if (!CanManageOrders) return Forbid();

        string[] allowedFrom = status switch
        {
            "Ordered" => new[] { "Draft" },
            "Received" => new[] { "Draft", "Ordered" },
            "Cancelled" => new[] { "Draft", "Ordered" },
            _ => Array.Empty<string>()
        };
        if (allowedFrom.Length == 0) return BadRequest();

        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using (var cmd = new MySqlCommand($@"
            UPDATE Purchase_Order SET Status = @status
            WHERE PO_ID = @id AND Feed_ID IS NOT NULL AND Status IN ({string.Join(",", allowedFrom.Select(s => $"'{s}'"))})", conn))
        {
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@id", poId);
            if (await cmd.ExecuteNonQueryAsync() == 0)
            {
                TempData["Success"] = "That order can't be changed anymore.";
                return RedirectToPage();
            }
        }

        if (status == "Received")
        {
            await using var cmd = new MySqlCommand(@"
                SELECT f.Feed_Name, f.Quantity_In_Stock, f.Unit
                FROM Purchase_Order po JOIN Feed f ON f.Feed_ID = po.Feed_ID WHERE po.PO_ID = @id", conn);
            cmd.Parameters.AddWithValue("@id", poId);
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            TempData["Success"] = $"Order #{poId} received. {r.GetString(0)} stock is now {r.GetDecimal(1):0.##} {r.GetString(2)}.";
        }
        else
        {
            TempData["Success"] = status == "Ordered"
                ? $"Order #{poId} approved and placed with the supplier."
                : $"Order #{poId} cancelled.";
        }
        return RedirectToPage();
    }

    public class OrderRow
    {
        public int Id { get; set; }
        public string Feed { get; set; } = "";
        public string? Supplier { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = "";
        public decimal? Total { get; set; }
        public DateTime? OrderDate { get; set; }
        public DateTime? ExpectedDelivery { get; set; }
        public string Status { get; set; } = "";
        public bool Auto { get; set; }
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
        public decimal? ReorderLevel { get; set; }
        public string? Unit { get; set; }
        public bool IsLow => QuantityInStock != null && ReorderLevel != null && QuantityInStock <= ReorderLevel;
    }
}