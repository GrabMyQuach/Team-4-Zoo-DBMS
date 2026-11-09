using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Care;

// An animal's health history for vets: weight over time (chart + first vs. latest),
// every exam newest first, and past quarantines.
[Authorize(Roles = CareOptions.KeeperRoles)]
public class HistoryModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    public HistoryModel(IConfiguration config, IWebHostEnvironment env) { _config = config; _env = env; }

    public record Exam(DateTime Date, string? Type, string? Diagnosis, string? Severity, bool Contagious,
                       string? Treatment, decimal? Weight, string? Notes, string? Vet);
    public record WeighIn(DateTime Date, decimal Kg);
    public record QuarantineRow(DateTime? Start, DateTime? ExpectedEnd, DateTime? Ended, string? Reason, string? Status);

    public int AnimalId { get; set; }
    public string Name { get; set; } = "";
    public string Species { get; set; } = "";
    public string Enclosure { get; set; } = "";
    public string? Gender { get; set; }
    public DateTime? Born { get; set; }
    public string? Health { get; set; }
    public string? Photo { get; set; }
    public bool CanRecord => AnimalOptions.CanRecordHealth(User);

    public List<Exam> Exams { get; set; } = new();
    public List<WeighIn> Weights { get; set; } = new();
    public List<QuarantineRow> Quarantines { get; set; } = new();

    public decimal? WeightChange => Weights.Count >= 2 && Weights[0].Kg != 0
        ? Math.Round(100 * (Weights[^1].Kg - Weights[0].Kg) / Weights[0].Kg, 1)
        : null;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        AnimalId = id;
        await using var conn = await CareData.OpenAsync(_config);

        await using (var cmd = new MySqlCommand(@"
            SELECT a.Animal_Name, s.Common_Name, e.Enclosure_Name, a.Gender, a.Date_of_Birth, a.Health_Status
            FROM Animal a JOIN Species s ON s.Species_ID = a.Species_ID JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
            WHERE a.Animal_ID = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            Name = r.GetString(0);
            Species = r.GetString(1);
            Enclosure = r.GetString(2);
            Gender = r.IsDBNull(3) ? null : r.GetString(3);
            Born = r.IsDBNull(4) ? null : r.GetDateTime(4);
            Health = r.IsDBNull(5) ? null : r.GetString(5);
        }
        Photo = PublicSite.AnimalPhoto(_env, id, Species);

        await using (var cmd = new MySqlCommand(@"
            SELECT h.Exam_Date, h.Record_Type, h.Diagnosis, h.Severity, h.Is_Contagious, h.Treatment, h.Weight_kg, h.Notes,
                   CONCAT('Dr. ', e.First_Name, ' ', e.Last_Name)
            FROM Health_Record h LEFT JOIN Employee e ON e.Employee_ID = h.Vet_Employee_ID
            WHERE h.Animal_ID = @id
            ORDER BY h.Exam_Date DESC, h.Health_Record_ID DESC", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Exams.Add(new Exam(r.GetDateTime(0), Str(r, 1), Str(r, 2), Str(r, 3), !r.IsDBNull(4) && Convert.ToBoolean(r.GetValue(4)),
                                   Str(r, 5), r.IsDBNull(6) ? null : r.GetDecimal(6), Str(r, 7), Str(r, 8)));
        }
        Weights = Exams.Where(e => e.Weight != null).OrderBy(e => e.Date).Select(e => new WeighIn(e.Date, e.Weight!.Value)).ToList();

        await using (var cmd = new MySqlCommand(@"
            SELECT Start_Date, Expected_End_Date, Actual_End_Date, Reason, Status
            FROM Quarantine WHERE Animal_ID = @id ORDER BY Start_Date DESC", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Quarantines.Add(new QuarantineRow(r.IsDBNull(0) ? null : r.GetDateTime(0), r.IsDBNull(1) ? null : r.GetDateTime(1),
                                                  r.IsDBNull(2) ? null : r.GetDateTime(2), Str(r, 3), Str(r, 4)));
        }
        return Page();
    }

    private static string? Str(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));

    // ---- Weight chart: an SVG line drawn from the weigh-ins ----
    public const int ChartW = 720, ChartH = 220, PadL = 56, PadR = 16, PadT = 16, PadB = 36;

    public (decimal Min, decimal Max) WeightRange
    {
        get
        {
            decimal min = Weights.Min(w => w.Kg), max = Weights.Max(w => w.Kg);
            var pad = Math.Max((max - min) * 0.15m, max * 0.02m + 0.1m);
            return (Math.Max(0, min - pad), max + pad);
        }
    }

    public List<(double X, double Y, WeighIn W)> ChartPoints()
    {
        var (min, max) = WeightRange;
        var first = Weights[0].Date;
        var spanDays = Math.Max(1, (Weights[^1].Date - first).TotalDays);
        return Weights.Select(w => (
            X: PadL + (ChartW - PadL - PadR) * ((w.Date - first).TotalDays / spanDays),
            Y: PadT + (ChartH - PadT - PadB) * (1 - (double)((w.Kg - min) / (max - min))),
            W: w)).ToList();
    }

    public double YFor(decimal kg)
    {
        var (min, max) = WeightRange;
        return PadT + (ChartH - PadT - PadB) * (1 - (double)((kg - min) / (max - min)));
    }
}
