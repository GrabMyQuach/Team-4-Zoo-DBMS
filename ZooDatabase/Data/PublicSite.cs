namespace ZooDatabase.Data;

// Settings for the public website (the part visitors see before logging in).
// Change the zoo name, hours or city here and every public page updates.
public static class PublicSite
{
    public const string ZooName = "Coog Zoo";
    public const string Hours = "9:00 AM – 5:00 PM";
    public const string LastEntry = "4:00 PM";
    public const string City = "Houston, TX";

    // Background colors used when a photo hasn't been added yet
    public static readonly string[] Tints = { "#BFD3DD", "#D6D1C4", "#E3D2B0", "#CDBDA6", "#C9D8C5", "#D8C7C2" };

    // Looks for a photo in wwwroot/images/public (any of .jpg/.jpeg/.png/.webp).
    // Returns its web address, or null so the page can show a colored box instead.
    public static string? Photo(IWebHostEnvironment env, string fileStem)
    {
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp" })
        {
            var relative = $"images/public/{fileStem}{ext}";
            if (File.Exists(Path.Combine(env.WebRootPath, relative)))
            {
                return "/" + relative;
            }
        }
        return null;
    }

    // An animal's photo: its own (animals/12.webp, by Animal_ID) first, then its species (animals/red-panda.webp)
    public static string? AnimalPhoto(IWebHostEnvironment env, int animalId, string? species)
        => Photo(env, "animals/" + animalId)
           ?? (string.IsNullOrWhiteSpace(species) ? null : Photo(env, "animals/" + Slug(species)));

    // "Red Panda" -> "red-panda" (used for photo file names)
    public static string Slug(string text)
    {
        var chars = text.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
