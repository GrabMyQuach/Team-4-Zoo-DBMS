using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Shop;

// Suppliers with what they supply, what we spend with them, and how reliable their deliveries are
public class SuppliersModel : PageModel
{
    private readonly IConfiguration _config;
    public SuppliersModel(IConfiguration config) { _config = config; }

    public List<SupplierRow> Rows { get; set; } = new();
    public bool CanManage => SalesOptions.CanManage(User);

    public async Task OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            SELECT s.Supplier_ID, s.Supplier_Name, s.Phone, s.Email, s.Delivery_Schedule,
                   (SELECT COUNT(*) FROM Supplier_Product sp WHERE sp.Supplier_ID = s.Supplier_ID),
                   (SELECT COUNT(*) FROM Feed f WHERE f.Supplier_ID = s.Supplier_ID),
                   (SELECT COUNT(*) FROM Purchase_Order po WHERE po.Supplier_ID = s.Supplier_ID AND po.Status IN ('Draft', 'Ordered')),
                   (SELECT COALESCE(SUM(po.Total_Cost), 0) FROM Purchase_Order po WHERE po.Supplier_ID = s.Supplier_ID
                      AND po.Status = 'Received' AND po.Received_Date >= CURDATE() - INTERVAL 90 DAY),
                   (SELECT COUNT(*) FROM Purchase_Order po WHERE po.Supplier_ID = s.Supplier_ID AND po.Status = 'Received'
                      AND po.Expected_Delivery IS NOT NULL),
                   (SELECT COUNT(*) FROM Purchase_Order po WHERE po.Supplier_ID = s.Supplier_ID AND po.Status = 'Received'
                      AND po.Received_Date > po.Expected_Delivery)
            FROM Supplier s
            ORDER BY s.Supplier_Name", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            Rows.Add(new SupplierRow
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                Phone = r.IsDBNull(2) ? null : r.GetString(2),
                Email = r.IsDBNull(3) ? null : r.GetString(3),
                Delivery = r.IsDBNull(4) ? null : r.GetString(4),
                Products = Convert.ToInt32(r.GetValue(5)),
                Feeds = Convert.ToInt32(r.GetValue(6)),
                OpenOrders = Convert.ToInt32(r.GetValue(7)),
                Spend90 = Convert.ToDecimal(r.GetValue(8)),
                Delivered = Convert.ToInt32(r.GetValue(9)),
                Late = Convert.ToInt32(r.GetValue(10))
            });
        }
    }

    public class SupplierRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Delivery { get; set; }
        public int Products { get; set; }
        public int Feeds { get; set; }
        public int OpenOrders { get; set; }
        public decimal Spend90 { get; set; }
        public int Delivered { get; set; }
        public int Late { get; set; }
        public decimal? OnTimePct => Delivered == 0 ? null : Math.Round(100m * (Delivered - Late) / Delivered, 1);
    }
}
