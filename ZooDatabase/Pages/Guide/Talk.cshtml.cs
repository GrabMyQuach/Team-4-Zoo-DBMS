using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;
using ZooDatabase.Pages.Care;

namespace ZooDatabase.Pages.Guide;

// Talking Points: one card per species at the zoo with its facts (diet, wild habitat, status in the wild),
// the animals guests can meet here, and simple sentences a guide can say out loud.
// Everything comes from Species, Animal and Conservation_Program, so it stays up to date.
[Authorize(Roles = GuideOptions.GuideRoles)]
public class TalkModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    public TalkModel(IConfiguration config, IWebHostEnvironment env) { _config = config; _env = env; }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public record Resident(string Name, string? Age, bool BornHere);
    public class SpeciesCard
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string? Scientific { get; init; }
        public string? Diet { get; init; }
        public string? Habitat { get; init; }
        public string? Status { get; init; }
        public string? Photo { get; set; }
        public List<Resident> Animals { get; } = new();
        public List<string> Programs { get; } = new();
        public List<string> Points { get; } = new();
    }

    public List<SpeciesCard> Species { get; set; } = new();

    public async Task OnGetAsync()
    {
        await using var conn = await CareData.OpenAsync(_config);
        var byId = new Dictionary<int, SpeciesCard>();

        await using (var cmd = new MySqlCommand(@"
            SELECT s.Species_ID, s.Common_Name, s.Scientific_Name, s.Diet_Type, s.Habitat_Type, s.Conservation_Status
            FROM Species s
            WHERE EXISTS (SELECT 1 FROM Animal a WHERE a.Species_ID = s.Species_ID AND a.Is_Active = 1)
            ORDER BY s.Common_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var card = new SpeciesCard
                {
                    Id = r.GetInt32(0), Name = r.GetString(1),
                    Scientific = r.IsDBNull(2) ? null : r.GetString(2), Diet = r.IsDBNull(3) ? null : r.GetString(3),
                    Habitat = r.IsDBNull(4) ? null : r.GetString(4), Status = r.IsDBNull(5) ? null : r.GetString(5)
                };
                byId[card.Id] = card;
                Species.Add(card);
            }
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT a.Species_ID, a.Animal_ID, a.Animal_Name, a.Date_of_Birth,
                   (a.Breeding_ID IS NOT NULL OR COALESCE(a.Acquisition_Type, '') LIKE '%Born%')
            FROM Animal a WHERE a.Is_Active = 1 ORDER BY a.Animal_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                if (!byId.TryGetValue(r.GetInt32(0), out var card)) continue;
                card.Animals.Add(new Resident(r.GetString(2), GuideData.Age(r.IsDBNull(3) ? null : r.GetDateTime(3)),
                                              Convert.ToInt32(r.GetValue(4)) == 1));
                card.Photo ??= PublicSite.AnimalPhoto(_env, r.GetInt32(1), card.Name);
            }
        }

        await using (var cmd = new MySqlCommand(@"
            SELECT Species_ID, Program_Name, Program_Type, Partner_Organization
            FROM Conservation_Program WHERE Status = 'Active' ORDER BY Program_Name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                if (!byId.TryGetValue(r.GetInt32(0), out var card)) continue;
                var partner = r.IsDBNull(3) ? "" : " with " + r.GetString(3);
                card.Programs.Add($"{r.GetString(1)} ({r.GetString(2)}){partner}");
            }
        }

        foreach (var card in Species) card.Points.AddRange(PointsFor(card));

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            Species = Species.Where(s => s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                         || (s.Scientific?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                                         || s.Animals.Any(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
                             .ToList();
        }
    }

    // Simple sentences built from the facts, for a guide to say out loud
    private static IEnumerable<string> PointsFor(SpeciesCard s)
    {
        var names = s.Animals.Select(a => a.Age == null ? a.Name : $"{a.Name} ({a.Age})").ToList();
        if (names.Count == 1) yield return $"Meet {names[0]}, our only {s.Name.ToLowerInvariant()}.";
        else if (names.Count > 1) yield return $"We have {names.Count} here: {JoinNames(names)}.";

        var born = s.Animals.Where(a => a.BornHere).Select(a => a.Name).ToList();
        if (born.Count > 0) yield return $"{JoinNames(born)} {(born.Count == 1 ? "was" : "were")} born right here at the zoo.";

        if (s.Habitat != null) yield return $"In the wild they live in {s.Habitat.ToLowerInvariant()} habitats.";
        if (GuideData.AtRisk(s.Status)) yield return $"They're {s.Status!.ToLowerInvariant()} in the wild, so every visit helps protect them.";
        foreach (var p in s.Programs) yield return $"The zoo helps through the {p}.";
    }

    private static string JoinNames(List<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1]
    };
}
