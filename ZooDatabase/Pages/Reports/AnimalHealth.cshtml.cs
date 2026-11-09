using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Reports;

// ANIMAL HEALTH & VETERINARY REPORT
// Question: "Is animal health improving or getting worse, and which animals need a vet?"
// Uses Health_Record, Quarantine, Care_Task, Animal, Species, Veterinarian.
// Each view is its own small report; every count links to the Health_Record rows behind it.
[Authorize(Roles = ReportAccess.AnimalHealth)]
public class AnimalHealthModel : RunnableReport
{
    public AnimalHealthModel(IConfiguration config) : base(config) { }

    public override string ReportTitle => "Animal Health";

    public override IReadOnlyList<ReportView> Views { get; } = new[]
    {
        new ReportView("monthly", "Exams by month", "how many vet records each month, by type."),
        new ReportView("species", "Health by species", "which species get sick most, compared to how many we have."),
        new ReportView("vets", "Vet caseload", "how much each vet handled, and their open tasks."),
        new ReportView("overdue", "Overdue checkups", "animals with no routine checkup in the last year (as of today).", UsesDates: false),
        new ReportView("weight", "Weight changes", "first vs. latest weigh-in for each animal, biggest loss first."),
        new ReportView("quarantine", "Quarantines", "every quarantine that started in the date range."),
        new ReportView("records", "Health records", "", Hidden: true),
        new ReportView("tasks", "Open care tasks", "", UsesDates: false, Hidden: true)
    };

    // ---- Filters (plus From/To) ----
    [BindProperty(SupportsGet = true)] public int? SpeciesId { get; set; }
    [BindProperty(SupportsGet = true)] public int? VetId { get; set; }
    [BindProperty(SupportsGet = true)] public int? TypeId { get; set; }

    public static readonly string[] RecordTypes =
        { "Routine Checkup", "Illness", "Injury", "Vaccination", "Follow-up", "Neonatal Exam" };
    private string? RecordType => TypeId is >= 1 and <= 6 ? RecordTypes[TypeId.Value - 1] : null;

    private List<Option> _species = new();
    private List<Option> _vets = new();

    // What each kind of count means, so a number and its source records always use the same rule
    private static readonly Dictionary<string, (string Label, string Sql)> Kinds = new()
    {
        ["all"] = ("All records", "1=1"),
        ["routine"] = ("Routine checkups", "h.Record_Type = 'Routine Checkup'"),
        ["cases"] = ("Illness and injury", "h.Record_Type IN ('Illness', 'Injury')"),
        ["vacc"] = ("Vaccinations", "h.Record_Type = 'Vaccination'"),
        ["contagious"] = ("Contagious", "h.Is_Contagious = 1"),
        ["serious"] = ("Serious (high or critical)", "h.Severity IN ('High', 'Critical')"),
        ["weighed"] = ("Weigh-ins", "h.Weight_kg IS NOT NULL")
    };

    protected override async Task LoadFiltersAsync(MySqlConnection conn)
    {
        _species = await LoadOptionsAsync(conn, "SELECT Species_ID, Common_Name FROM Species ORDER BY Common_Name");
        _vets = await LoadOptionsAsync(conn, @"
            SELECT v.Employee_ID, CONCAT('Dr. ', e.First_Name, ' ', e.Last_Name)
            FROM Veterinarian v JOIN Employee e ON e.Employee_ID = v.Employee_ID ORDER BY e.Last_Name");
        var types = RecordTypes.Select((t, i) => new Option(i + 1, t)).ToList();

        Filters.Add(new FilterField(nameof(SpeciesId), "Species", _species, SpeciesId?.ToString()));
        Filters.Add(new FilterField(nameof(VetId), "Vet", _vets, VetId?.ToString()));
        Filters.Add(new FilterField(nameof(TypeId), "Record type", types, TypeId?.ToString()));
    }

    // Conditions every health query shares: dates + the dropdown filters
    private string HealthWhere(bool useDates = true)
    {
        var w = useDates ? "h.Exam_Date >= @from AND h.Exam_Date < @to" : "1=1";
        if (SpeciesId != null) w += " AND a.Species_ID = @species";
        if (VetId != null) w += " AND h.Vet_Employee_ID = @vet";
        if (RecordType != null) w += " AND h.Record_Type = @rtype";
        return w;
    }

    private MySqlCommand Cmd(MySqlConnection conn, string sql)
    {
        var cmd = new MySqlCommand(sql, conn);
        AddDateParams(cmd);
        if (SpeciesId != null) cmd.Parameters.AddWithValue("@species", SpeciesId);
        if (VetId != null) cmd.Parameters.AddWithValue("@vet", VetId);
        if (RecordType != null) cmd.Parameters.AddWithValue("@rtype", RecordType);
        return cmd;
    }

    protected override Task<ReportTable> BuildAsync(MySqlConnection conn) => CurrentView.Key switch
    {
        "species" => BySpeciesAsync(conn),
        "vets" => VetsAsync(conn),
        "overdue" => OverdueAsync(conn),
        "weight" => WeightAsync(conn),
        "quarantine" => QuarantineAsync(conn),
        "records" => RecordsAsync(conn),
        "tasks" => TasksAsync(conn),
        _ => MonthlyAsync(conn)
    };

    // ===== Exams by month =====
    private async Task<ReportTable> MonthlyAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Month"), new Col("All records", true), new Col("Routine checkups", true),
                      new Col("Illness / injury", true), new Col("Vaccinations", true), new Col("Contagious", true));
        int[] sum = new int[5];
        await using var cmd = Cmd(conn, $@"
            SELECT DATE_FORMAT(h.Exam_Date, '%Y-%m') AS ym, COUNT(*),
                   SUM(h.Record_Type = 'Routine Checkup'), SUM(h.Record_Type IN ('Illness', 'Injury')),
                   SUM(h.Record_Type = 'Vaccination'), SUM(h.Is_Contagious)
            FROM Health_Record h JOIN Animal a ON a.Animal_ID = h.Animal_ID
            WHERE {HealthWhere()}
            GROUP BY ym ORDER BY ym");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var ym = r.GetString(0);
            var key = "m:" + ym;
            var v = new[] { Int(r, 1), Int(r, 2), Int(r, 3), Int(r, 4), Int(r, 5) };
            for (int i = 0; i < 5; i++) sum[i] += v[i];
            t.Rows.Add(R(new[]
            {
                T(MonthName(ym)),
                N(v[0], Drill("records", key, "all")), N(v[1], Drill("records", key, "routine")),
                N(v[2], Drill("records", key, "cases")), N(v[3], Drill("records", key, "vacc")),
                N(v[4], Drill("records", key, "contagious"))
            }));
        }
        t.Total = R(new[]
        {
            T("Total"),
            N(sum[0], Drill("records", "all", "all")), N(sum[1], Drill("records", "all", "routine")),
            N(sum[2], Drill("records", "all", "cases")), N(sum[3], Drill("records", "all", "vacc")),
            N(sum[4], Drill("records", "all", "contagious"))
        });
        t.Note = "Click any number to see the vet records it counts.";
        return t;
    }

    // ===== Health by species =====
    private async Task<ReportTable> BySpeciesAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Species"), new Col("Animals", true), new Col("Records", true), new Col("Illness / injury", true),
                      new Col("Cases per animal", true), new Col("Contagious", true), new Col("Serious", true));
        await using var cmd = Cmd(conn, $@"
            SELECT s.Species_ID, s.Common_Name,
                   (SELECT COUNT(*) FROM Animal x WHERE x.Species_ID = s.Species_ID AND x.Is_Active = 1) AS Animals,
                   COUNT(*) AS Records, SUM(h.Record_Type IN ('Illness', 'Injury')) AS Cases,
                   SUM(h.Is_Contagious), SUM(h.Severity IN ('High', 'Critical'))
            FROM Health_Record h
            JOIN Animal a ON a.Animal_ID = h.Animal_ID
            JOIN Species s ON s.Species_ID = a.Species_ID
            WHERE {HealthWhere()}
            GROUP BY s.Species_ID, s.Common_Name
            ORDER BY Cases DESC, Records DESC");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var key = "s:" + r.GetInt32(0);
            int animals = Int(r, 2), cases = Int(r, 4);
            decimal perAnimal = animals == 0 ? cases : Math.Round((decimal)cases / animals, 2);
            t.Rows.Add(R(new[]
            {
                T(r.GetString(1)), N(animals),
                N(Int(r, 3), Drill("records", key, "all")), N(cases, Drill("records", key, "cases")),
                Dec1(perAnimal),
                N(Int(r, 5), Drill("records", key, "contagious")), N(Int(r, 6), Drill("records", key, "serious"))
            }, perAnimal >= 1 ? "warn" : null));
        }
        t.Note = "Yellow: a species averaging one or more illness or injury cases per animal.";
        return t;
    }

    // ===== Vet caseload =====
    private async Task<ReportTable> VetsAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Veterinarian"), new Col("Specialization"), new Col("Exams", true),
                      new Col("Illness / injury", true), new Col("Animals seen", true), new Col("Open tasks", true));
        var w = HealthWhere();
        await using var cmd = Cmd(conn, $@"
            SELECT v.Employee_ID, CONCAT('Dr. ', e.First_Name, ' ', e.Last_Name), v.Specialization,
                   (SELECT COUNT(*) FROM Health_Record h JOIN Animal a ON a.Animal_ID = h.Animal_ID
                     WHERE h.Vet_Employee_ID = v.Employee_ID AND {w}) AS Exams,
                   (SELECT COUNT(*) FROM Health_Record h JOIN Animal a ON a.Animal_ID = h.Animal_ID
                     WHERE h.Vet_Employee_ID = v.Employee_ID AND h.Record_Type IN ('Illness', 'Injury') AND {w}),
                   (SELECT COUNT(DISTINCT h.Animal_ID) FROM Health_Record h JOIN Animal a ON a.Animal_ID = h.Animal_ID
                     WHERE h.Vet_Employee_ID = v.Employee_ID AND {w}),
                   (SELECT COUNT(*) FROM Care_Task ct
                     WHERE ct.Assigned_Employee_ID = v.Employee_ID AND ct.Status IN ('Open', 'In Progress'))
            FROM Veterinarian v
            JOIN Employee e ON e.Employee_ID = v.Employee_ID
            {(VetId != null ? "WHERE v.Employee_ID = @vet" : "")}
            ORDER BY Exams DESC");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var key = "v:" + r.GetInt32(0);
            t.Rows.Add(R(new[]
            {
                T(r.GetString(1)), T(Str(r, 2)),
                N(Int(r, 3), Drill("records", key, "all")), N(Int(r, 4), Drill("records", key, "cases")),
                N(Int(r, 5), Drill("records", key, "all")), N(Int(r, 6), Drill("tasks", key))
            }));
        }
        t.Note = "Open tasks are counted as of today.";
        return t;
    }

    // ===== Overdue checkups (as of today) =====
    private async Task<ReportTable> OverdueAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Animal"), new Col("Species"), new Col("Enclosure"), new Col("Last routine checkup"), new Col("Days since", true));
        await using var cmd = Cmd(conn, $@"
            SELECT a.Animal_ID, a.Animal_Name, s.Common_Name, e.Enclosure_Name,
                   (SELECT MAX(h.Exam_Date) FROM Health_Record h
                     WHERE h.Animal_ID = a.Animal_ID AND h.Record_Type = 'Routine Checkup') AS Last_Checkup
            FROM Animal a
            JOIN Species s ON s.Species_ID = a.Species_ID
            JOIN Enclosure e ON e.Enclosure_ID = a.Enclosure_ID
            WHERE a.Is_Active = 1 {(SpeciesId != null ? "AND a.Species_ID = @species" : "")}
            HAVING Last_Checkup IS NULL OR Last_Checkup < CURDATE() - INTERVAL 365 DAY
            ORDER BY Last_Checkup");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = r.GetInt32(0);
            var last = DateOrNull(r, 4);
            t.Rows.Add(R(new[]
            {
                T(r.GetString(1)), T(r.GetString(2)), T(r.GetString(3)),
                D(last, last == null ? null : Drill("records", "a:" + id, "routine-ever")),
                last == null ? T("—") : N((int)(DateTime.Today - last.Value.Date).TotalDays)
            }, last == null ? "bad" : "warn", $"/Animals/Details/{id}"));
        }
        t.EmptyText = "Every animal has had a routine checkup in the last year.";
        t.Note = "Red: never had a routine checkup. Yellow: last one was over a year ago. Click a name to open the animal.";
        return t;
    }

    // ===== Weight changes =====
    private async Task<ReportTable> WeightAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Animal"), new Col("Species"), new Col("Weigh-ins", true), new Col("First (kg)", true),
                      new Col("First date"), new Col("Latest (kg)", true), new Col("Latest date"), new Col("Change", true));
        // ROW_NUMBER() numbers each animal's weigh-ins oldest-to-newest and newest-to-oldest,
        // so the first and latest can be picked out and compared.
        await using var cmd = Cmd(conn, $@"
            WITH w AS (
                SELECT h.Animal_ID, h.Weight_kg, h.Exam_Date,
                       ROW_NUMBER() OVER (PARTITION BY h.Animal_ID ORDER BY h.Exam_Date) AS first_rank,
                       ROW_NUMBER() OVER (PARTITION BY h.Animal_ID ORDER BY h.Exam_Date DESC) AS last_rank,
                       COUNT(*) OVER (PARTITION BY h.Animal_ID) AS weigh_ins
                FROM Health_Record h
                JOIN Animal a ON a.Animal_ID = h.Animal_ID
                WHERE h.Weight_kg IS NOT NULL AND {HealthWhere()}
            )
            SELECT a.Animal_ID, a.Animal_Name, s.Common_Name, f.weigh_ins, f.Weight_kg, f.Exam_Date, l.Weight_kg, l.Exam_Date
            FROM w f
            JOIN w l ON l.Animal_ID = f.Animal_ID AND l.last_rank = 1
            JOIN Animal a ON a.Animal_ID = f.Animal_ID
            JOIN Species s ON s.Species_ID = a.Species_ID
            WHERE f.first_rank = 1 AND f.weigh_ins >= 2");
        var rows = new List<(decimal Change, Row Row)>();
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var id = r.GetInt32(0);
                decimal first = Dec(r, 4), latest = Dec(r, 6);
                var change = first == 0 ? 0 : Math.Round(100 * (latest - first) / first, 1);
                rows.Add((change, R(new[]
                {
                    T(r.GetString(1)), T(r.GetString(2)),
                    N(Int(r, 3), Drill("records", "a:" + id, "weighed")),
                    Dec1(first), D(r.GetDateTime(5)), Dec1(latest), D(r.GetDateTime(7)), PctCell(change)
                }, change <= -10 ? "bad" : change <= -5 ? "warn" : null, $"/Animals/Details/{id}")));
            }
        }
        t.Rows.AddRange(rows.OrderBy(x => x.Change).Select(x => x.Row));
        t.EmptyText = "No animal was weighed at least twice in this date range.";
        t.Note = "Red: lost 10% or more. Yellow: lost 5% or more. Click Weigh-ins to see every weigh-in.";
        return t;
    }

    // ===== Quarantines =====
    private async Task<ReportTable> QuarantineAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Animal"), new Col("Species"), new Col("Reason"), new Col("Started"),
                      new Col("Expected end"), new Col("Ended"), new Col("Days", true), new Col("Status"));
        await using var cmd = Cmd(conn, $@"
            SELECT a.Animal_ID, a.Animal_Name, s.Common_Name, q.Reason, q.Start_Date, q.Expected_End_Date,
                   q.Actual_End_Date, DATEDIFF(COALESCE(q.Actual_End_Date, CURDATE()), q.Start_Date), q.Status
            FROM Quarantine q
            JOIN Animal a ON a.Animal_ID = q.Animal_ID
            JOIN Species s ON s.Species_ID = a.Species_ID
            WHERE q.Start_Date >= @from AND q.Start_Date < @to
                  {(SpeciesId != null ? "AND a.Species_ID = @species" : "")}
            ORDER BY q.Start_Date DESC");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var status = Str(r, 8);
            bool active = status is "Active" or "Extended";
            t.Rows.Add(R(new[]
            {
                T(r.GetString(1)), T(r.GetString(2)), T(Str(r, 3)), D(DateOrNull(r, 4)),
                D(DateOrNull(r, 5)), active ? T("Still in quarantine") : D(DateOrNull(r, 6)), N(Int(r, 7)), T(status)
            }, active ? "warn" : null, $"/Animals/Details/{r.GetInt32(0)}"));
        }
        t.EmptyText = "No quarantines started in this date range.";
        t.Note = "Yellow: still in quarantine. Click a name to open the animal.";
        return t;
    }

    // ===== Source records: the vet records behind a number =====
    private async Task<ReportTable> RecordsAsync(MySqlConnection conn)
    {
        var kind = Key2 ?? "all";
        bool ever = kind.EndsWith("-ever");      // e.g. every routine checkup, ignoring the date range
        if (ever) kind = kind[..^5];
        if (!Kinds.ContainsKey(kind)) kind = "all";

        var where = HealthWhere(useDates: !ever) + " AND " + Kinds[kind].Sql;
        var titleParts = new List<string> { Kinds[kind].Label };
        string? keyValue = null;

        var (keyType, keyText) = SplitKey(Key);
        switch (keyType)
        {
            case "m":
                where += " AND DATE_FORMAT(h.Exam_Date, '%Y-%m') = @k";
                keyValue = keyText;
                titleParts.Add(MonthName(keyText));
                break;
            case "s":
                where += " AND a.Species_ID = @k";
                keyValue = keyText;
                titleParts.Add(OptionName(_species, int.TryParse(keyText, out var sid) ? sid : null) ?? "species");
                break;
            case "v":
                where += " AND h.Vet_Employee_ID = @k";
                keyValue = keyText;
                titleParts.Add(OptionName(_vets, int.TryParse(keyText, out var vid) ? vid : null) ?? "vet");
                break;
            case "a":
                where += " AND h.Animal_ID = @k";
                keyValue = keyText;
                break;
        }

        var t = Table(new Col("Date"), new Col("Animal"), new Col("Species"), new Col("Type"), new Col("Diagnosis"),
                      new Col("Severity"), new Col("Contagious"), new Col("Weight (kg)", true), new Col("Vet"));
        await using var cmd = Cmd(conn, $@"
            SELECT h.Exam_Date, a.Animal_ID, a.Animal_Name, s.Common_Name, h.Record_Type, h.Diagnosis,
                   h.Severity, h.Is_Contagious, h.Weight_kg, CONCAT('Dr. ', e.First_Name, ' ', e.Last_Name)
            FROM Health_Record h
            JOIN Animal a ON a.Animal_ID = h.Animal_ID
            JOIN Species s ON s.Species_ID = a.Species_ID
            LEFT JOIN Employee e ON e.Employee_ID = h.Vet_Employee_ID
            WHERE {where}
            ORDER BY h.Exam_Date DESC
            LIMIT 500");
        if (keyValue != null) cmd.Parameters.AddWithValue("@k", keyValue);
        await using var r = await cmd.ExecuteReaderAsync();
        string? animalName = null;
        while (await r.ReadAsync())
        {
            animalName ??= r.GetString(2);
            var severity = Str(r, 6);
            bool contagious = !r.IsDBNull(7) && Convert.ToBoolean(r.GetValue(7));
            t.Rows.Add(R(new[]
            {
                D(r.GetDateTime(0)), T(r.GetString(2), $"/Animals/Details/{r.GetInt32(1)}"), T(r.GetString(3)),
                T(Str(r, 4)), T(Str(r, 5)), T(severity), T(contagious ? "Yes" : "No"),
                Dec1(DecOrNull(r, 8)), T(Str(r, 9))
            }, severity is "High" or "Critical" ? "bad" : contagious ? "warn" : null));
        }
        if (keyType == "a" && animalName != null) titleParts.Add(animalName);
        ResultTitle = "Health records: " + string.Join(" · ", titleParts);
        if (ever) ResultTitle += " (all time)";
        t.Note = "Red: high or critical severity. Yellow: contagious. Click an animal's name to open its page. Shows up to 500 records.";
        return t;
    }

    // ===== Source records: a vet's open care tasks =====
    private async Task<ReportTable> TasksAsync(MySqlConnection conn)
    {
        var (_, vetText) = SplitKey(Key);
        int.TryParse(vetText, out var vetId);
        var t = Table(new Col("Task"), new Col("Animal"), new Col("Priority"), new Col("Due"), new Col("Status"));
        await using var cmd = new MySqlCommand(@"
            SELECT ct.Title, ct.Animal_ID, a.Animal_Name, ct.Priority, ct.Due_At, ct.Status
            FROM Care_Task ct
            LEFT JOIN Animal a ON a.Animal_ID = ct.Animal_ID
            WHERE ct.Assigned_Employee_ID = @vet AND ct.Status IN ('Open', 'In Progress')
            ORDER BY (ct.Due_At < NOW()) DESC, FIELD(ct.Priority, 'Urgent', 'High', 'Normal', 'Low'), ct.Due_At", conn);
        cmd.Parameters.AddWithValue("@vet", vetId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var due = DateOrNull(r, 4);
            bool overdue = due != null && due < DateTime.Now;
            t.Rows.Add(R(new[]
            {
                T(Str(r, 0)),
                r.IsDBNull(1) ? T("—") : T(Str(r, 2), $"/Animals/Details/{r.GetInt32(1)}"),
                T(Str(r, 3)), T(due?.ToString("MMM d, h:mm tt") ?? "—"), T(Str(r, 5))
            }, overdue ? "bad" : null));
        }
        ResultTitle = "Open care tasks: " + (OptionName(_vets, vetId) ?? "vet");
        t.EmptyText = "No open tasks.";
        t.Note = "Red: past due.";
        return t;
    }
}
