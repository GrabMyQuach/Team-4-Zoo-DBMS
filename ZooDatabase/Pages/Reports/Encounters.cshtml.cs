using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Reports;

// ANIMAL ENCOUNTERS REPORT
// Question: "Are encounters filling up, which animals draw the crowds, and how many are lost to cancellations?"
// Uses Animal_Encounter, Animal, Species, Attraction, Employee.
// Encounters count in the period they were scheduled for. Attendance = guests who came / places offered,
// for completed encounters only. Cancellations with a reason starting "Quarantine:" were made automatically
// by the trg_contagious_diagnosis trigger.
[Authorize(Roles = ReportAccess.Encounters)]
public class EncountersModel : RunnableReport
{
    public EncountersModel(IConfiguration config) : base(config) { }

    public override string ReportTitle => "Animal Encounters";

    public override IReadOnlyList<ReportView> Views { get; } = new[]
    {
        new ReportView("monthly", "Encounters by month", "held, cancelled, guests and attendance each month."),
        new ReportView("animals", "By animal", "which animals draw the most guests and fill their places."),
        new ReportView("attractions", "By attraction", "guests and attendance at each attraction."),
        new ReportView("cancellations", "Cancellations", "why encounters were cancelled and how many guest places that cost."),
        new ReportView("list", "Encounters", "", Hidden: true)
    };

    [BindProperty(SupportsGet = true)] public int? AnimalId { get; set; }
    [BindProperty(SupportsGet = true)] public int? AttractionId { get; set; }
    private List<Option> _animals = new();
    private List<Option> _attractions = new();

    private const string EncFrom = @"
        FROM Animal_Encounter ae
        JOIN Animal a ON a.Animal_ID = ae.Animal_ID
        JOIN Species s ON s.Species_ID = a.Species_ID
        LEFT JOIN Attraction at ON at.Attraction_ID = ae.Attraction_ID";

    // Quarantine cancellations are written by the trigger as "Quarantine: <disease>"
    private const string ReasonGroup = @"
        CASE WHEN ae.Cancel_Reason LIKE 'Quarantine:%' THEN 'Animal quarantined (automatic)'
             WHEN NULLIF(TRIM(ae.Cancel_Reason), '') IS NULL THEN 'No reason given'
             ELSE TRIM(ae.Cancel_Reason) END";

    private const string Counts = @"
        COUNT(*), SUM(ae.Status = 'Completed'), SUM(ae.Status = 'Cancelled'),
        COALESCE(SUM(CASE WHEN ae.Status = 'Completed' THEN ae.Guests_Attended END), 0),
        COALESCE(SUM(CASE WHEN ae.Status = 'Completed' AND ae.Guests_Attended IS NOT NULL THEN ae.Max_Guests END), 0),
        COALESCE(SUM(CASE WHEN ae.Status = 'Completed' AND ae.Guests_Attended IS NOT NULL THEN ae.Guests_Attended END), 0)";

    protected override async Task LoadFiltersAsync(MySqlConnection conn)
    {
        _animals = await LoadAnimalOptionsAsync(conn);
        _attractions = await LoadOptionsAsync(conn, "SELECT Attraction_ID, Attraction_Name FROM Attraction ORDER BY Attraction_Name");
        Filters.Add(new FilterField(nameof(AnimalId), "Animal", _animals, AnimalId?.ToString()));
        Filters.Add(new FilterField(nameof(AttractionId), "Attraction", _attractions, AttractionId?.ToString()));
    }

    private string Where => "ae.Scheduled_Start >= @from AND ae.Scheduled_Start < @to"
                            + (AnimalId != null ? " AND ae.Animal_ID = @animal" : "")
                            + (AttractionId != null ? " AND ae.Attraction_ID = @attraction" : "");

    private MySqlCommand Cmd(MySqlConnection conn, string sql)
    {
        var cmd = new MySqlCommand(sql, conn);
        AddDateParams(cmd);
        if (AnimalId != null) cmd.Parameters.AddWithValue("@animal", AnimalId);
        if (AttractionId != null) cmd.Parameters.AddWithValue("@attraction", AttractionId);
        return cmd;
    }

    protected override Task<ReportTable> BuildAsync(MySqlConnection conn) => CurrentView.Key switch
    {
        "animals" => GroupedAsync(conn, "a", "a.Animal_ID", "a.Animal_Name", "Animal", withSpecies: true),
        "attractions" => GroupedAsync(conn, "t", "COALESCE(ae.Attraction_ID, 0)", "COALESCE(at.Attraction_Name, 'No attraction')", "Attraction"),
        "cancellations" => CancellationsAsync(conn),
        "list" => ListAsync(conn),
        _ => MonthlyAsync(conn)
    };

    private static Col[] CountCols(string first, bool withSpecies = false)
    {
        var cols = new List<Col> { new(first) };
        if (withSpecies) cols.Add(new Col("Species"));
        cols.AddRange(new[]
        {
            new Col("Scheduled", true), new Col("Held", true), new Col("Cancelled", true),
            new Col("Guests", true), new Col("Avg guests", true), new Col("Attendance", true)
        });
        return cols.ToArray();
    }

    // Scheduled, held, cancelled, guests, average guests per held encounter, attendance
    private Cell[] CountCells(System.Data.Common.DbDataReader r, int start, string key)
    {
        int all = Int(r, start), held = Int(r, start + 1), cancelled = Int(r, start + 2), guests = Int(r, start + 3);
        int places = Int(r, start + 4), counted = Int(r, start + 5);
        return new[]
        {
            N(all, Drill("list", key, "all")), N(held, Drill("list", key, "held")), N(cancelled, Drill("list", key, "cancelled")),
            N(guests), Dec1(held == 0 ? null : Math.Round((decimal)guests / held, 1)),
            new Cell { Text = places == 0 ? "—" : Pct(counted, places).ToString("0.#") + "%", Value = places == 0 ? null : Pct(counted, places) }
        };
    }

    private static string? Tone(System.Data.Common.DbDataReader r, int start)
    {
        int all = Int(r, start), cancelled = Int(r, start + 2), places = Int(r, start + 4), counted = Int(r, start + 5);
        if (all > 0 && Pct(cancelled, all) >= 25) return "bad";
        if (places > 0 && Pct(counted, places) < 50) return "warn";
        return null;
    }

    // ===== Encounters by month =====
    private async Task<ReportTable> MonthlyAsync(MySqlConnection conn)
    {
        var t = Table(CountCols("Month"));
        await using var cmd = Cmd(conn, $"SELECT DATE_FORMAT(ae.Scheduled_Start, '%Y-%m'), {Counts} {EncFrom} WHERE {Where} GROUP BY 1 ORDER BY 1");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var ym = r.GetString(0);
            var cells = new List<Cell> { T(MonthName(ym)) };
            cells.AddRange(CountCells(r, 1, "m:" + ym));
            t.Rows.Add(R(cells, Tone(r, 1)));
        }
        t.Note = "Red: a quarter or more were cancelled. Yellow: under half of the places were filled. "
               + "Attendance counts held encounters that have a guest count.";
        return t;
    }

    // ===== By animal / by attraction =====
    private async Task<ReportTable> GroupedAsync(MySqlConnection conn, string prefix, string idSql, string nameSql, string label, bool withSpecies = false)
    {
        var t = Table(CountCols(label, withSpecies));
        await using var cmd = Cmd(conn, $@"
            SELECT {idSql}, {nameSql}, {(withSpecies ? "s.Common_Name" : "NULL")}, {Counts} {EncFrom} WHERE {Where}
            GROUP BY 1, 2{(withSpecies ? ", 3" : "")}
            ORDER BY 7 DESC, 2");
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var id = Convert.ToInt32(r.GetValue(0));
            var cells = new List<Cell> { T(r.GetString(1)) };
            if (withSpecies) cells.Add(T(Str(r, 2)));
            cells.AddRange(CountCells(r, 3, $"{prefix}:{id}"));
            t.Rows.Add(R(cells, Tone(r, 3), prefix == "a" ? $"/Animals/Details/{id}" : null));
        }
        t.Note = "Sorted by guests, most first. Red: a quarter or more were cancelled. Yellow: under half of the places were filled.";
        return t;
    }

    // ===== Cancellations =====
    private async Task<ReportTable> CancellationsAsync(MySqlConnection conn)
    {
        var t = Table(new Col("Reason"), new Col("Cancelled", true), new Col("Animals", true), new Col("Guest places lost", true), new Col("Share", true));
        var rows = new List<(string Reason, int Count, int Animals, int Places)>();
        await using (var cmd = Cmd(conn, $@"
            SELECT {ReasonGroup}, COUNT(*), COUNT(DISTINCT ae.Animal_ID), COALESCE(SUM(ae.Max_Guests), 0)
            {EncFrom} WHERE {Where} AND ae.Status = 'Cancelled'
            GROUP BY 1 ORDER BY 2 DESC"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) rows.Add((r.GetString(0), Int(r, 1), Int(r, 2), Int(r, 3)));

        var total = rows.Sum(x => x.Count);
        foreach (var x in rows)
            t.Rows.Add(R(new[]
            {
                T(x.Reason), N(x.Count, Drill("list", "c:" + x.Reason, "cancelled")), N(x.Animals), N(x.Places), PctCell(Pct(x.Count, total))
            }, x.Reason.StartsWith("Animal quarantined") ? "warn" : null));
        if (rows.Count > 0)
            t.Total = R(new[] { T("Total"), N(total, Drill("list", "all", "cancelled")), T(""), N(rows.Sum(x => x.Places)), PctCell(100) });
        t.Note = "Yellow: cancelled automatically by the contagious-diagnosis trigger when the animal went into quarantine.";
        return t;
    }

    // ===== Source records =====
    private async Task<ReportTable> ListAsync(MySqlConnection conn)
    {
        var where = Where;
        var title = new List<string>();
        switch (Key2)
        {
            case "held": where += " AND ae.Status = 'Completed'"; title.Add("Held"); break;
            case "cancelled": where += " AND ae.Status = 'Cancelled'"; title.Add("Cancelled"); break;
            default: title.Add("All scheduled"); break;
        }
        var (type, text) = SplitKey(Key);
        object? k = null;
        switch (type)
        {
            case "m": where += " AND DATE_FORMAT(ae.Scheduled_Start, '%Y-%m') = @k"; k = text; title.Add(MonthName(text)); break;
            case "a": where += " AND ae.Animal_ID = @k"; k = text; title.Add(OptionName(_animals, IntOrNull(text)) ?? "animal"); break;
            case "t":
                where += text == "0" ? " AND ae.Attraction_ID IS NULL" : " AND ae.Attraction_ID = @k";
                k = text; title.Add(text == "0" ? "No attraction" : OptionName(_attractions, IntOrNull(text)) ?? "attraction"); break;
            case "c": where += $" AND ({ReasonGroup}) = @k"; k = text; title.Add(text); break;
        }

        var t = Table(new Col("When"), new Col("Animal"), new Col("Attraction"), new Col("Handler"),
                      new Col("Places", true), new Col("Guests", true), new Col("Status"), new Col("Reason"));
        await using var cmd = Cmd(conn, $@"
            SELECT ae.Scheduled_Start, a.Animal_ID, a.Animal_Name, at.Attraction_Name, CONCAT(h.First_Name, ' ', h.Last_Name),
                   ae.Max_Guests, ae.Guests_Attended, ae.Status, ae.Cancel_Reason
            {EncFrom}
            LEFT JOIN Employee h ON h.Employee_ID = ae.Handler_Employee_ID
            WHERE {where}
            ORDER BY ae.Scheduled_Start DESC LIMIT 500");
        if (k != null) cmd.Parameters.AddWithValue("@k", k);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var status = r.GetString(7);
            t.Rows.Add(R(new[]
            {
                new Cell { Text = r.GetDateTime(0).ToString("MMM d, yyyy h:mm tt"), Value = r.GetDateTime(0) },
                T(r.GetString(2), $"/Animals/Details/{r.GetInt32(1)}"), T(Str(r, 3)), T(Str(r, 4)),
                N(Int(r, 5)), T(Str(r, 6) ?? "—"), T(status), T(Str(r, 8))
            }, status == "Cancelled" ? "warn" : null));
        }
        ResultTitle = "Encounters: " + string.Join(" · ", title);
        t.Note = "Yellow: cancelled. Shows up to 500 encounters.";
        return t;
    }

    // ===== Key findings =====
    // Target: fill at least 70% of the guest places offered.
    protected override async Task AnalyzeAsync(MySqlConnection conn)
    {
        var (n, p) = await CompareAsync(() => Cmd(conn, $@"
            SELECT {Counts}, COALESCE(SUM(ae.Status = 'Cancelled' AND ae.Cancel_Reason LIKE 'Quarantine:%'), 0),
                   COALESCE(SUM(CASE WHEN ae.Status = 'Cancelled' THEN ae.Max_Guests END), 0)
            {EncFrom} WHERE {Where}"));
        // n: 0 all, 1 held, 2 cancelled, 3 guests, 4 places (counted), 5 guests (counted), 6 quarantine cancels, 7 places lost
        decimal attNow = Rate(n[5], n[4]), attPrev = Rate(p[5], p[4]);
        Kpis.Add(new Kpi("Encounters held", n[1], p[1]));
        Kpis.Add(new Kpi("Guests", n[3], p[3]));
        Kpis.Add(new Kpi("Attendance", attNow, p[4] == 0 ? null : attPrev, "pct"));
        Kpis.Add(new Kpi("Cancelled", n[2], p[2], HigherIsBetter: false));

        if (n[0] == 0)
        {
            Findings.Add(new Finding("No encounters were scheduled in this period."));
            return;
        }

        if (n[4] > 0)
            Findings.Add(new Finding(
                $"Held encounters filled {attNow:0.#}% of their guest places" + (p[4] > 0 ? Versus(attNow, attPrev, v => v.ToString("0.#") + "%") : "")
                + (attNow >= 70 ? ". That meets the 70% target." : ". That's below the 70% target, so there's room to sell more places."),
                attNow >= 70 ? "good" : "bad"));

        if (n[2] > 0)
        {
            var cancelRate = Rate(n[2], n[0]);
            Findings.Add(new Finding($"{cancelRate:0.#}% of scheduled encounters were cancelled ({n[2]:N0} of {n[0]:N0}), losing {Plural(n[7], "guest place", "guest places")}.",
                cancelRate >= 15 ? "bad" : "info"));
            if (n[6] > 0)
                Findings.Add(new Finding($"{n[6]:N0} of those ({Rate(n[6], n[2]):0.#}%) were cancelled automatically by the contagious-diagnosis trigger when the animal went into quarantine. "
                    + "Without it, sick animals would still have been booked to meet the public.", "info"));
        }

        if (AnimalId == null)
        {
            await using var cmd = Cmd(conn, $@"
                SELECT a.Animal_Name, s.Common_Name, SUM(ae.Guests_Attended), SUM(ae.Status = 'Completed')
                {EncFrom} WHERE {Where} AND ae.Status = 'Completed'
                GROUP BY a.Animal_ID, a.Animal_Name, s.Common_Name ORDER BY 3 DESC LIMIT 1");
            foreach (var r in await RowsAsync(cmd))
                Findings.Add(new Finding($"Most popular animal: {r[0]} the {r[1]}, with {Convert.ToInt32(r[2] ?? 0):N0} guests over {Plural(Convert.ToInt32(r[3]), "encounter", "encounters")}.", "good"));
        }

        if (AttractionId == null)
        {
            await using var cmd = Cmd(conn, $@"
                SELECT at.Attraction_Name, SUM(ae.Guests_Attended), SUM(ae.Max_Guests)
                {EncFrom} WHERE {Where} AND ae.Status = 'Completed' AND ae.Guests_Attended IS NOT NULL AND ae.Attraction_ID IS NOT NULL
                GROUP BY at.Attraction_ID, at.Attraction_Name HAVING COUNT(*) >= 3 AND SUM(ae.Max_Guests) > 0
                ORDER BY SUM(ae.Guests_Attended) / SUM(ae.Max_Guests) LIMIT 1");
            foreach (var r in await RowsAsync(cmd))
            {
                var rate = Rate(Convert.ToDecimal(r[1] ?? 0), Convert.ToDecimal(r[2]));
                if (rate < 70)
                    Findings.Add(new Finding($"{r[0]} had the lowest attendance at {rate:0.#}% of places filled. Consider fewer sessions or smaller groups there.", "bad"));
            }
        }

        await using (var cmd = Cmd(conn, @"
            SELECT COUNT(*),
                   SUM(a.Public_Encounter_Eligible = 0 OR a.Needs_Relocation = 1 OR a.Is_Active = 0
                       OR EXISTS (SELECT 1 FROM Quarantine q WHERE q.Animal_ID = a.Animal_ID AND q.Status IN ('Active', 'Extended')))
            FROM Animal_Encounter ae JOIN Animal a ON a.Animal_ID = ae.Animal_ID
            WHERE ae.Status = 'Scheduled' AND ae.Scheduled_Start >= @now AND ae.Scheduled_Start < @now + INTERVAL 7 DAY"))
        {
            var r = await FirstRowAsync(cmd);
            Findings.Add(new Finding($"{Plural(r[0], "encounter is", "encounters are")} booked for the next 7 days"
                + (r[1] > 0 ? $"; {r[1]:N0} of them are with an animal that isn't cleared any more and should be cancelled (Tickets > Encounters > Needs attention)." : "."),
                r[1] > 0 ? "bad" : "info"));
        }
    }
}
