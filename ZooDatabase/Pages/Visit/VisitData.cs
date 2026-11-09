using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Visit;

// Shared pieces for the public pages: the small records they show and the queries they share.
public record AnimalCard(string Name, string Species, string Enclosure, string? Photo, string Tint);
public record PriceRow(int Id, string Name, decimal Price, string? Note);
public record Slide(string Title, string Body, string Cta, string Href, string? Photo, string Tint);
public record ShopProduct(int Id, string Name, string? Category, decimal Price, int Stock, int GiftShopId, string GiftShop, string? Photo, string Tint);

public static class VisitData
{
    // Payment choices offered online (no cash online). These match the database's allowed values.
    public static readonly string[] OnlinePayments = { "Credit Card", "Debit Card", "PayPal", "Mobile Pay" };

    public static async Task<MySqlConnection> OpenAsync(IConfiguration config)
    {
        var conn = new MySqlConnection(config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }

    // Animal cards. Photo: the animal's own (12.webp), then its species (penguin.webp), then its name (pebble.webp).
    public static async Task<List<AnimalCard>> AnimalsAsync(MySqlConnection conn, IWebHostEnvironment env, string whereSql, Action<MySqlCommand>? addParams, string orderSql, int? limit = null)
    {
        var list = new List<AnimalCard>();
        var sql = @"
            SELECT a.Animal_Name, s.Common_Name, e.Enclosure_Name, a.Animal_ID
            FROM Animal a
            JOIN Species s ON a.Species_ID = s.Species_ID
            JOIN Enclosure e ON a.Enclosure_ID = e.Enclosure_ID
            WHERE " + whereSql + " ORDER BY " + orderSql + (limit.HasValue ? " LIMIT " + limit.Value : "");
        await using var cmd = new MySqlCommand(sql, conn);
        addParams?.Invoke(cmd);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var name = r.GetString(0);
            var species = r.GetString(1);
            var photo = PublicSite.AnimalPhoto(env, r.GetInt32(3), species)
                        ?? PublicSite.Photo(env, "animals/" + PublicSite.Slug(name));
            var tint = PublicSite.Tints[list.Count % PublicSite.Tints.Length];
            list.Add(new AnimalCard(name, species, r.GetString(2), photo, tint));
        }
        return list;
    }

    public static async Task<List<PriceRow>> TicketTypesAsync(MySqlConnection conn, MySqlTransaction? tx = null)
    {
        var list = new List<PriceRow>();
        await using var cmd = new MySqlCommand(
            "SELECT TicketType_ID, TicketType_Name, Price, Eligibility_Requirements FROM Ticket_Type WHERE TicketType_Name NOT LIKE '%Member%' ORDER BY Price DESC, TicketType_Name", conn, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new PriceRow(r.GetInt32(0), r.GetString(1), r.GetDecimal(2), r.IsDBNull(3) ? null : r.GetString(3)));
        }
        return list;
    }

    public static async Task<List<PriceRow>> MembershipTypesAsync(MySqlConnection conn, MySqlTransaction? tx = null)
    {
        var list = new List<PriceRow>();
        await using var cmd = new MySqlCommand(
            "SELECT MembershipType_ID, Membership_Name, Price, Benefits FROM Membership_Type ORDER BY Price", conn, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new PriceRow(r.GetInt32(0), r.GetString(1), r.GetDecimal(2), r.IsDBNull(3) ? null : r.GetString(3)));
        }
        return list;
    }

    // Gift shop products. With ids: just those products. Without: the best sellers that are in stock.
    public static async Task<List<ShopProduct>> ProductsAsync(MySqlConnection conn, IWebHostEnvironment env, IEnumerable<int>? ids, int limit = 12, MySqlTransaction? tx = null)
    {
        var list = new List<ShopProduct>();
        var idList = ids?.ToList();
        if (idList != null && idList.Count == 0) return list;

        var where = idList != null
            ? "p.Product_ID IN (" + string.Join(",", idList.Select((_, i) => "@p" + i)) + ")"
            : "COALESCE(i.Quantity_In_Stock, 0) > 0";
        var sql = @"
            SELECT p.Product_ID, p.Product_Name, p.Category, p.Price, COALESCE(i.Quantity_In_Stock, 0),
                   g.GiftShop_ID, g.GiftShop_Name,
                   COALESCE((SELECT SUM(si.Quantity) FROM Sale_Item si WHERE si.Product_ID = p.Product_ID), 0) AS Sold
            FROM Product p
            JOIN Gift_Shop g ON g.GiftShop_ID = p.GiftShop_ID
            LEFT JOIN Inventory i ON i.Product_ID = p.Product_ID
            WHERE " + where + @"
            ORDER BY Sold DESC, p.Product_Name" + (idList == null ? " LIMIT " + limit : "");

        await using var cmd = new MySqlCommand(sql, conn, tx);
        if (idList != null)
        {
            for (int i = 0; i < idList.Count; i++) cmd.Parameters.AddWithValue("@p" + i, idList[i]);
        }
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var name = r.GetString(1);
            list.Add(new ShopProduct(
                r.GetInt32(0), name, r.IsDBNull(2) ? null : r.GetString(2), r.GetDecimal(3),
                Convert.ToInt32(r.GetValue(4)), r.GetInt32(5), r.GetString(6),
                PublicSite.Photo(env, "shop/" + PublicSite.Slug(name)),
                PublicSite.Tints[list.Count % PublicSite.Tints.Length]));
        }
        return list;
    }

    public static async Task<int> CountAsync(MySqlConnection conn, string table)
    {
        // table names here are fixed strings from our own code, never user input
        await using var cmd = new MySqlCommand($"SELECT COUNT(*) FROM {table}", conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
