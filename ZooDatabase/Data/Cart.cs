using System.Text.Json;

namespace ZooDatabase.Data;

// The visitor's shopping cart on the public website.
// It only stores IDs and quantities; prices are always looked up fresh from the database at checkout.
public class Cart
{
    public Dictionary<int, int> Tickets { get; set; } = new();   // TicketType_ID -> how many
    public Dictionary<int, int> Products { get; set; } = new();  // Product_ID -> how many
    public int? MembershipTypeId { get; set; }                   // at most one membership per order
    public string? VisitDate { get; set; }                       // yyyy-MM-dd, picked on the Tickets page

    public const int MaxPerLine = 20;

    public int ItemCount => Tickets.Values.Sum() + Products.Values.Sum() + (MembershipTypeId.HasValue ? 1 : 0);
    public bool IsEmpty => ItemCount == 0;

    private const string SessionKey = "Cart";

    public static Cart Load(ISession session)
    {
        var json = session.GetString(SessionKey);
        if (string.IsNullOrEmpty(json)) return new Cart();
        try
        {
            return JsonSerializer.Deserialize<Cart>(json) ?? new Cart();
        }
        catch (JsonException)
        {
            return new Cart();
        }
    }

    public void Save(ISession session)
    {
        // drop anything set to zero
        foreach (var k in Tickets.Where(x => x.Value <= 0).Select(x => x.Key).ToList()) Tickets.Remove(k);
        foreach (var k in Products.Where(x => x.Value <= 0).Select(x => x.Key).ToList()) Products.Remove(k);
        session.SetString(SessionKey, JsonSerializer.Serialize(this));
    }

    public static void Clear(ISession session) => session.Remove(SessionKey);

    // Adds to a line without going over the per-line limit
    public static void Add(Dictionary<int, int> lines, int id, int qty)
    {
        if (qty <= 0) return;
        lines[id] = Math.Min(MaxPerLine, lines.GetValueOrDefault(id) + qty);
    }
}
