using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Reports;

// ANIMAL HEALTH & VETERINARY REPORT
// Question: "Is animal health improving or getting worse, and which animals need a vet?"
// Uses Health_Record, Quarantine, Care_Task, Animal, Species, Veterinarian.
[Authorize(Roles = ReportAccess.AnimalHealth)]
public class AnimalHealthModel : ReportPageModel
{
    public AnimalHealthModel(IConfiguration config) : base(config) { }

    // ---- Filters (plus From/To from the base class) ----
    [BindProperty(SupportsGet = true)] public int? SpeciesId { get; set; }
    [BindProperty(SupportsGet = true)] public int? VetId { get; set; }
    [BindProperty(SupportsGet = true)] public string? RecordType { get; set; }

    public static readonly string[] RecordTypes =
        { "Routine Checkup", "Illness", "Injury", "Vaccination", "Follow-up", "Neonatal Exam" };

    public List<Option> SpeciesOptions { get; set; } = new();
    public List<Option> VetOptions { get; set; } = new();

    // ---- Results ----
    public int TotalRecords { get; set; }
    public int IllnessInjuryCases { get; set; }
    public int ContagiousCases { get; set; }
    public int AnimalsExamined { get; set; }
    public int SeriousCases { get; set; }
    public int QuarantinesStarted { get; set; }
    public decimal? AvgQuarantineDays { get; set; }
    public int StillQuarantined { get; set; }

    public List<MonthRow> Months { get; set; } = new();
    public List<SpeciesRow> BySpecies { get; set; } = new();
    public List<VetRow> Vets { get; set; } = new();
    public List<OverdueRow> Overdue { get; set; } = new();
    public List<WeightRow> Weights { get; set; } = new();

    public string FilterText
    {
        get
        {
            var parts = new List<string>();
            if (SpeciesId != null) parts.Add("Species: " + SpeciesOptions.FirstOrDefault(o => o.Id == SpeciesId)?.Name);
            if (VetId != null) parts.Add("Vet: " + VetOptions.FirstOrDefault(o => o.Id == VetId)?.Name);
            if (!string.IsNullOrWhiteSpace(RecordType)) parts.Add("Type: " + RecordType);
            return string.Join(" · ", parts);
        }
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnGetExcelAsync()
    {
        await LoadAsync();
        var subtitle = $"{DateRangeText}{(FilterText == "" ? "" : " · " + FilterText)} · Generated {DateTime.Now:MMM d, yyyy h:mm tt}";
        var xl = new ExcelReport("Animal Health Report", subtitle)
            .AddSheet("Summary", new[] { "Measure", "Value" }, new[]
            {
                new object?[] { "Vet records", TotalRecords },
                new object?[] { "Animals examined", AnimalsExamined },
                new object?[] { "Illness and injury cases", IllnessInjuryCases },
                new object?[] { "Serious cases (high or critical)", SeriousCases },
                new object?[] { "Contagious cases", ContagiousCases },
                new object?[] { "Quarantines started", QuarantinesStarted },
                new object?[] { "Average quarantine length (days)", AvgQuarantineDays },
                new object?[] { "Still in quarantine", StillQuarantined },
                new object?[] { "Animals overdue for a checkup", Overdue.Count }
            })
            .AddSheet("Monthly Trend", new[] { "Month", "Total Records", "Routine Checkups", "Illness/Injury", "Vaccinations", "Contagious" },
                Months.Select(m => new object?[] { MonthName(m.YearMonth), m.Total, m.Routine, m.Cases, m.Vaccinations, m.Contagious }))
            .AddSheet("By Species", new[] { "Species", "Animals", "Records", "Illness/Injury", "Cases per Animal", "Contagious", "Serious" },
                BySpecies.Select(s => new object?[] { s.Species, s.Animals, s.Records, s.Cases, s.CasesPerAnimal, s.Contagious, s.Serious }),
                row => (decimal)(row[4] ?? 0m) >= 1 ? ExcelReport.Yellow : null)
            .AddSheet("Vet Caseload", new[] { "Veterinarian", "Specialization", "Exams", "Illness/Injury", "Animals Seen", "Open Tasks" },
                Vets.Select(v => new object?[] { v.Name, v.Specialization, v.Exams, v.Cases, v.AnimalsSeen, v.OpenTasks }))
            .AddSheet("Overdue Checkups", new[] { "Animal", "Species", "Enclosure", "Last Routine Checkup", "Days Since" },
                Overdue.Select(o => new object?[] { o.Animal, o.Species, o.Enclosure, o.LastCheckup, o.DaysSince }),
                row => row[3] == null ? ExcelReport.Red : ExcelReport.Yellow)
            .AddSheet("Weight Changes", new[] { "Animal", "Species", "First Weight (kg)", "First Date", "Latest Weight (kg)", "Latest Date", "Change (%)" },
                Weights.Select(w => new object?[] { w.Animal, w.Species, w.FirstWeight, w.FirstDate, w.LatestWeight, w.LatestDate, w.ChangePercent }),
                row => (decimal)(row[6] ?? 0m) <= -10 ? ExcelReport.Red : (decimal)(row[6] ?? 0m) <= -5 ? ExcelReport.Yellow : null);

        return File(xl.ToBytes(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"animal-health-report-{DateTime.Today:yyyy-MM-dd}.xlsx");
    }

    private async Task LoadAsync()
    {
        ResolveDates();
        await using var conn = await OpenAsync();

        SpeciesOptions = await LoadOptionsAsync(conn, "SELECT Species_ID, Common_Name FROM Species ORDER BY Common_Name");
        VetOptions = await LoadOptionsAsync(conn, @"
            SELECT v.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name)
            FROM Veterinarian v JOIN Employee e ON e.Employee_ID = v.Employee_ID ORDER BY e.Last_Name");

        // The filter conditions every health query shares
        var where = "h.Exam_Date >= @from AND h.Exam_Date < @to";
        if (SpeciesId != null) where += " AND a.Species_ID = @species";
        if (VetId != null) where += " AND h.Vet_Employee_ID = @vet";
        if (!string.IsNullOrWhiteSpace(RecordType)) where += " AND h.Record_Type = @rtype";

        MySqlCommand Cmd(string sql)
        {
            var cmd = new MySqlCommand(sql, conn);
            AddDateParams(cmd);
            if (SpeciesId != null) cmd.Parameters.AddWithValue("@species", SpeciesId);
            if (VetId != null) cmd.Parameters.AddWithValue("@vet", VetId);
            if (!string.IsNullOrWhiteSpace(RecordType)) cmd.Parameters.AddWithValue("@rtype", RecordType);
            return cmd;
        }

        // 1. Headline numbers
        await using (var cmd = Cmd($@"
            SELECT COUNT(*),
                   SUM(h.Record_Type IN ('Illness', 'Injury')),
                   SUM(h.Is_Contagious),
                   COUNT(DISTINCT h.Animal_ID),
                   SUM(h.Severity IN ('High', 'Critical'))
            FROM Health_Record h
            JOIN Animal a ON a.Animal_ID = h.Animal_ID
            WHERE {where}"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                TotalRecords = Int(r, 0);
                IllnessInjuryCases = Int(r, 1);
                ContagiousCases = Int(r, 2);
                AnimalsExamined = Int(r, 3);
                SeriousCases = Int(r, 4);
            }
        }

        // 2. Quarantines that started in the date range (DATEDIFF = number of days between two dates)
        await using (var cmd = Cmd($@"
            SELECT COUNT(*),
                   AVG(DATEDIFF(COALESCE(q.Actual_End_Date, CURDATE()), q.Start_Date)),
                   SUM(q.Status IN ('Active', 'Extended'))
            FROM Quarantine q
            JOIN Animal a ON a.Animal_ID = q.Animal_ID
            WHERE q.Start_Date >= @from AND q.Start_Date < @to
                  {(SpeciesId != null ? "AND a.Species_ID = @species" : "")}"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                QuarantinesStarted = Int(r, 0);
                AvgQuarantineDays = r.IsDBNull(1) ? null : Math.Round(Dec(r, 1), 1);
                StillQuarantined = Int(r, 2);
            }
        }

        // 3. Month-by-month trend (GROUP BY month)
        await using (var cmd = Cmd($@"
            SELECT DATE_FORMAT(h.Exam_Date, '%Y-%m') AS ym,
                   COUNT(*),
                   SUM(h.Record_Type = 'Routine Checkup'),
                   SUM(h.Record_Type IN ('Illness', 'Injury')),
                   SUM(h.Record_Type = 'Vaccination'),
                   SUM(h.Is_Contagious)
            FROM Health_Record h
            JOIN Animal a ON a.Animal_ID = h.Animal_ID
            WHERE {where}
            GROUP BY ym
            ORDER BY ym"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                Months.Add(new MonthRow
                {
                    YearMonth = r.GetString(0),
                    Total = Int(r, 1),
                    Routine = Int(r, 2),
                    Cases = Int(r, 3),
                    Vaccinations = Int(r, 4),
                    Contagious = Int(r, 5)
                });
            }
        }

        // 4. Which species get sick most, compared to how many we have
        await using (var cmd = Cmd($@"
            SELECT s.Common_Name,
                   (SELECT COUNT(*) FROM Animal x WHERE x.Species_ID = s.Species_ID AND x.Is_Active = 1) AS Animals,
                   COUNT(*) AS Records,
                   SUM(h.Record_Type IN ('Illness', 'Injury')) AS Cases,
                   SUM(h.Is_Contagious),
                   SUM(h.Severity IN ('High', 'Critical'))
            FROM Health_Record h
            JOIN Animal a ON a.Animal_ID = h.Animal_ID
            JOIN Species s ON s.Species_ID = a.Species_ID
            WHERE {where}
            GROUP BY s.Species_ID, s.Common_Name
            ORDER BY Cases DESC, Records DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var row = new SpeciesRow
                {
                    Species = r.GetString(0),
                    Animals = Int(r, 1),
                    Records = Int(r, 2),
                    Cases = Int(r, 3),
                    Contagious = Int(r, 4),
                    Serious = Int(r, 5)
                };
                row.CasesPerAnimal = row.Animals == 0 ? row.Cases : Math.Round((decimal)row.Cases / row.Animals, 2);
                BySpecies.Add(row);
            }
        }

        // 5. Vet caseload: each subquery counts that vet's work in the date range
        await using (var cmd = Cmd($@"
            SELECT CONCAT(e.First_Name, ' ', e.Last_Name), v.Specialization,
                   (SELECT COUNT(*) FROM Health_Record h JOIN Animal a ON a.Animal_ID = h.Animal_ID
                     WHERE h.Vet_Employee_ID = v.Employee_ID AND {where}) AS Exams,
                   (SELECT COUNT(*) FROM Health_Record h JOIN Animal a ON a.Animal_ID = h.Animal_ID
                     WHERE h.Vet_Employee_ID = v.Employee_ID AND h.Record_Type IN ('Illness', 'Injury') AND {where}) AS Cases,
                   (SELECT COUNT(DISTINCT h.Animal_ID) FROM Health_Record h JOIN Animal a ON a.Animal_ID = h.Animal_ID
                     WHERE h.Vet_Employee_ID = v.Employee_ID AND {where}) AS Animals_Seen,
                   (SELECT COUNT(*) FROM Care_Task t
                     WHERE t.Assigned_Employee_ID = v.Employee_ID AND t.Status IN ('Open', 'In Progress')) AS Open_Tasks
            FROM Veterinarian v
            JOIN Employee e ON e.Employee_ID = v.Employee_ID
            {(VetId != null ? "WHERE v.Employee_ID = @vet" : "")}
            ORDER BY Exams DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                Vets.Add(new VetRow
                {
                    Name = "Dr. " + r.GetString(0),
                    Specialization = Str(r, 1),
                    Exams = Int(r, 2),
                    Cases = Int(r, 3),
                    AnimalsSeen = Int(r, 4),
                    OpenTasks = Int(r, 5)
                });
            }
        }

        // 6. Animals overdue for a yearly checkup (as of today, not limited to the date range)
        await using (var cmd = Cmd($@"
            SELECT a.Animal_Name, s.Common_Name, e.Enclosure_Name,
                   (SELECT MAX(h.Exam_Date) FROM Health_Record h
                     WHERE h.Animal_ID = a.Animal_ID AND h.Record_Type = 'Routine Checkup') AS Last_Checkup
            FROM Animal a
            JOIN Species s ON s.Species_ID = a.Species_ID
            JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
            WHERE a.Is_Active = 1 {(SpeciesId != null ? "AND a.Species_ID = @species" : "")}
            HAVING Last_Checkup IS NULL OR Last_Checkup < CURDATE() - INTERVAL 365 DAY
            ORDER BY Last_Checkup"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var last = DateOrNull(r, 3);
                Overdue.Add(new OverdueRow
                {
                    Animal = r.GetString(0),
                    Species = r.GetString(1),
                    Enclosure = r.GetString(2),
                    LastCheckup = last,
                    DaysSince = last == null ? null : (int)(DateTime.Today - last.Value.Date).TotalDays
                });
            }
        }

        // 7. Weight change: first vs latest weight in the range.
        //    ROW_NUMBER() is a window function: it numbers each animal's exams oldest-to-newest
        //    and newest-to-oldest, so we can pick out the first and last weigh-in.
        await using (var cmd = Cmd($@"
            WITH w AS (
                SELECT h.Animal_ID, h.Weight_kg, h.Exam_Date,
                       ROW_NUMBER() OVER (PARTITION BY h.Animal_ID ORDER BY h.Exam_Date) AS first_rank,
                       ROW_NUMBER() OVER (PARTITION BY h.Animal_ID ORDER BY h.Exam_Date DESC) AS last_rank,
                       COUNT(*) OVER (PARTITION BY h.Animal_ID) AS weigh_ins
                FROM Health_Record h
                JOIN Animal a ON a.Animal_ID = h.Animal_ID
                WHERE h.Weight_kg IS NOT NULL AND {where}
            )
            SELECT a.Animal_Name, s.Common_Name, f.Weight_kg, f.Exam_Date, l.Weight_kg, l.Exam_Date
            FROM w f
            JOIN w l ON l.Animal_ID = f.Animal_ID AND l.last_rank = 1
            JOIN Animal a ON a.Animal_ID = f.Animal_ID
            JOIN Species s ON s.Species_ID = a.Species_ID
            WHERE f.first_rank = 1 AND f.weigh_ins >= 2"))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var first = Dec(r, 2);
                var latest = Dec(r, 4);
                Weights.Add(new WeightRow
                {
                    Animal = r.GetString(0),
                    Species = r.GetString(1),
                    FirstWeight = first,
                    FirstDate = r.GetDateTime(3),
                    LatestWeight = latest,
                    LatestDate = r.GetDateTime(5),
                    ChangePercent = first == 0 ? 0 : Math.Round(100 * (latest - first) / first, 1)
                });
            }
        }
        Weights = Weights.OrderBy(w => w.ChangePercent).ToList();   // biggest loss first
    }

    public class MonthRow
    {
        public string YearMonth { get; set; } = "";
        public int Total { get; set; }
        public int Routine { get; set; }
        public int Cases { get; set; }
        public int Vaccinations { get; set; }
        public int Contagious { get; set; }
    }

    public class SpeciesRow
    {
        public string Species { get; set; } = "";
        public int Animals { get; set; }
        public int Records { get; set; }
        public int Cases { get; set; }
        public decimal CasesPerAnimal { get; set; }
        public int Contagious { get; set; }
        public int Serious { get; set; }
    }

    public class VetRow
    {
        public string Name { get; set; } = "";
        public string? Specialization { get; set; }
        public int Exams { get; set; }
        public int Cases { get; set; }
        public int AnimalsSeen { get; set; }
        public int OpenTasks { get; set; }
    }

    public class OverdueRow
    {
        public string Animal { get; set; } = "";
        public string Species { get; set; } = "";
        public string Enclosure { get; set; } = "";
        public DateTime? LastCheckup { get; set; }
        public int? DaysSince { get; set; }
    }

    public class WeightRow
    {
        public string Animal { get; set; } = "";
        public string Species { get; set; } = "";
        public decimal FirstWeight { get; set; }
        public DateTime FirstDate { get; set; }
        public decimal LatestWeight { get; set; }
        public DateTime LatestDate { get; set; }
        public decimal ChangePercent { get; set; }
    }
}
