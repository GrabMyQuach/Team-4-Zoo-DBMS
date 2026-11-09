using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ZooDatabase.Pages.Visit;

// Public "Our Animals" page: every animal, with a search box.
public class AnimalsModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public AnimalsModel(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public List<AnimalCard> Animals { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = await VisitData.OpenAsync(_config);
        if (string.IsNullOrWhiteSpace(Q))
        {
            Animals = await VisitData.AnimalsAsync(conn, _env, "1=1", null, "s.Common_Name, a.Animal_Name");
        }
        else
        {
            var term = "%" + Q.Trim() + "%";
            Animals = await VisitData.AnimalsAsync(conn, _env,
                "(a.Animal_Name LIKE @q OR s.Common_Name LIKE @q OR e.Enclosure_Name LIKE @q)",
                cmd => cmd.Parameters.AddWithValue("@q", term),
                "s.Common_Name, a.Animal_Name");
        }
    }
}
