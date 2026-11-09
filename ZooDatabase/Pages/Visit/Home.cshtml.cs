using Microsoft.AspNetCore.Mvc.RazorPages;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Visit;

// The public homepage at "/": rotating photo banner, featured animals, ticket prices and memberships.
public class HomeModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public HomeModel(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    public int AnimalCount { get; set; }
    public int FoodStands { get; set; }
    public int GiftShops { get; set; }
    public List<AnimalCard> Featured { get; set; } = new();
    public List<PriceRow> Tickets { get; set; } = new();
    public List<PriceRow> Memberships { get; set; } = new();
    public List<Slide> Slides { get; set; } = new();
    public string? MemberPhoto { get; set; }

    public async Task OnGetAsync()
    {
        await using var conn = await VisitData.OpenAsync(_config);

        AnimalCount = await VisitData.CountAsync(conn, "Animal");
        FoodStands = await VisitData.CountAsync(conn, "Food_Stand");
        GiftShops = await VisitData.CountAsync(conn, "Gift_Shop");
        Tickets = await VisitData.TicketTypesAsync(conn);
        Memberships = await VisitData.MembershipTypesAsync(conn);

        // Our four "stars"; if any are missing, just show the first four animals A to Z
        Featured = await VisitData.AnimalsAsync(conn, _env,
            "a.Animal_Name IN ('Pebble','Ziggy','Twig','Coco')", null,
            "FIELD(a.Animal_Name,'Pebble','Ziggy','Twig','Coco')", 4);
        if (Featured.Count < 4)
        {
            Featured = await VisitData.AnimalsAsync(conn, _env, "1=1", null, "a.Animal_Name", 4);
        }

        MemberPhoto = PublicSite.Photo(_env, "membership");

        // Banner slides. Add photos named hero-1.jpg ... hero-4.jpg in wwwroot/images/public to replace the colors.
        Slides = new List<Slide>
        {
            new($"Meet {AnimalCount} animals in one wild day.",
                "Penguins, zebras, giraffes and more. Plan your visit and see them up close.",
                "Buy Tickets", "/visit/tickets", PublicSite.Photo(_env, "hero-1"), "#2B3A31"),
            new("Visit all year as a member.",
                "Unlimited visits for a full year, plus member perks around the park.",
                "Become a Member", "/visit/membership", PublicSite.Photo(_env, "hero-2"), "#3A2F26"),
            new("Say hi to Pebble and the penguins.",
                "Watch our penguins dive, swim and waddle every day of the week.",
                "Meet the Animals", "/visit/animals", PublicSite.Photo(_env, "hero-3"), "#1F3440"),
            new("Grab a bite. Take home a souvenir.",
                "Food stands and gift shops all over the park, plus a few favorites you can order online.",
                "Shop Gifts Online", "/visit/shop", PublicSite.Photo(_env, "hero-4"), "#3B2A2A")
        };
    }
}
