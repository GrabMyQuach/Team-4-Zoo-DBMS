using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Reports;

// ===== Building blocks for "run on demand" reports =====
// A report has several views (smaller pages). The user picks a view, dates and filters, then clicks Run Report.
// Every number can link to a hidden "source records" view that lists the exact rows behind it.

public record ReportView(string Key, string Title, string Description, bool UsesDates = true, bool Hidden = false);

// A dropdown filter. Name must match the page's [BindProperty] name.
public record FilterField(string Name, string Label, List<ReportPageModel.Option> Options, string? Selected);

public record Col(string Title, bool Num = false);

// A headline number for the "Key findings" box, compared with the same-length period just before.
// Format: "n" (count), "money", "pct" (compared in percentage points), "dec".
// HigherIsBetter: true / false colours the change green or red; null keeps it neutral (e.g. costs that just "are").
public record Kpi(string Label, decimal Now, decimal? Prev, string Format = "n", bool? HigherIsBetter = true)
{
    public string Show(decimal v) => Format switch
    {
        "money" => v.ToString("C0"),
        "pct" => v.ToString("0.#") + "%",
        "dec" => v.ToString("0.#"),
        _ => v.ToString("N0")
    };

    public string? ChangeText
    {
        get
        {
            if (Prev == null) return null;
            var diff = Now - Prev.Value;
            if (Format == "pct") return diff == 0 ? "no change" : $"{(diff > 0 ? "+" : "")}{diff:0.#} pts";
            if (Prev == 0) return Now == 0 ? "no change" : "new";
            var pct = Math.Round(100 * diff / Prev.Value, 0);
            return pct == 0 ? "no change" : $"{(pct > 0 ? "+" : "")}{pct}%";
        }
    }

    private bool Unchanged => Prev == null || Now == Prev || ChangeText == "no change";

    public string Arrow => Unchanged ? "" : Now > Prev ? "▲" : "▼";

    public string Tone => Unchanged || HigherIsBetter == null ? "flat"
                        : (Now > Prev) == HigherIsBetter ? "good" : "bad";
}

// One sentence in the "Key findings" box. Tone: "good" (green), "bad" (red) or "info".
public record Finding(string Text, string Tone = "info");

public class Cell
{
    public string Text { get; init; } = "";
    public object? Value { get; init; }     // what goes into Excel
    public string? Link { get; init; }      // where clicking the value goes
}

public class Row
{
    public List<Cell> Cells { get; } = new();
    public string? Tone { get; init; }      // null, "warn" (yellow) or "bad" (red)
    public string? Link { get; init; }      // the record's own page (put on the first cell)
}

public class ReportTable
{
    public List<Col> Cols { get; } = new();
    public List<Row> Rows { get; } = new();
    public Row? Total { get; set; }
    public string? Note { get; set; }
    public string EmptyText { get; set; } = "Nothing matches these filters.";
}

public abstract class RunnableReport : ReportPageModel
{
    protected RunnableReport(IConfiguration config) : base(config) { }

    [BindProperty(SupportsGet = true)] public string? View { get; set; }
    [BindProperty(SupportsGet = true)] public bool Run { get; set; }
    [BindProperty(SupportsGet = true)] public string? Key { get; set; }
    [BindProperty(SupportsGet = true)] public string? Key2 { get; set; }
    [BindProperty(SupportsGet = true)] public string? Back { get; set; }

    public abstract string ReportTitle { get; }
    public abstract IReadOnlyList<ReportView> Views { get; }

    public List<FilterField> Filters { get; } = new();

    // "Key findings": headline numbers vs. the previous period, and sentences that answer the report's question
    public List<Kpi> Kpis { get; } = new();
    public List<Finding> Findings { get; } = new();
    public DateTime PrevStart => StartDate.AddDays(-DayCount);
    public string PrevRangeText => $"{PrevStart:MMM d} to {StartDate.AddDays(-1):MMM d, yyyy}";

    // Each report fills Kpis and Findings here. It runs once per report run, whatever view is picked.
    protected virtual Task AnalyzeAsync(MySqlConnection conn) => Task.CompletedTask;
    public ReportTable? Result { get; set; }
    public string ResultTitle { get; set; } = "";

    public ReportView CurrentView => Views.FirstOrDefault(v => v.Key == View) ?? Views[0];
    public bool IsDrill => CurrentView.Hidden;
    public ReportView? BackView => Views.FirstOrDefault(v => v.Key == Back && !v.Hidden);

    // Filled in by each report: the dropdown filters, and the table for the chosen view
    protected abstract Task LoadFiltersAsync(MySqlConnection conn);
    protected abstract Task<ReportTable> BuildAsync(MySqlConnection conn);

    public async Task<IActionResult> OnGetAsync()
    {
        ResolveDates();
        await using var conn = await OpenAsync();
        await LoadFiltersAsync(conn);
        if (Run)
        {
            ResultTitle = CurrentView.Title;
            Result = await BuildAsync(conn);
            if (!IsDrill) await AnalyzeAsync(conn);
        }
        return Page();
    }

    public async Task<IActionResult> OnGetExcelAsync()
    {
        ResolveDates();
        await using var conn = await OpenAsync();
        await LoadFiltersAsync(conn);
        ResultTitle = CurrentView.Title;
        Result = await BuildAsync(conn);

        var subtitle = $"{(CurrentView.UsesDates ? DateRangeText : "As of " + DateTime.Today.ToString("MMM d, yyyy"))}"
                       + (FilterText == "" ? "" : " · " + FilterText)
                       + $" · Generated {DateTime.Now:MMM d, yyyy h:mm tt}";
        var rows = Result.Rows.Select(r => (Values: r.Cells.Select(c => (object?)(c.Value ?? c.Text)).ToArray(), r.Tone)).ToList();
        if (Result.Total != null) rows.Add((Result.Total.Cells.Select(c => (object?)(c.Value ?? c.Text)).ToArray(), null));
        var tones = rows.ToDictionary(r => r.Values, r => r.Tone);

        // Excel sheet names can't contain : \ / ? * [ ] and max out at 31 characters
        var sheet = new string(ResultTitle.Select(ch => ":\\/?*[]".Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (sheet.Length > 31) sheet = sheet[..31];
        if (sheet.Length == 0) sheet = "Report";

        var xl = new ExcelReport(ReportTitle, subtitle).AddSheet(
            sheet,
            Result.Cols.Select(c => c.Title).ToArray(),
            rows.Select(r => r.Values),
            values => tones.GetValueOrDefault(values) switch { "bad" => ExcelReport.Red, "warn" => ExcelReport.Yellow, _ => null });

        var file = $"{ReportTitle}-{CurrentView.Key}-{DateTime.Today:yyyy-MM-dd}.xlsx".ToLowerInvariant().Replace(' ', '-').Replace("&", "and");
        return File(xl.ToBytes(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file);
    }

    // "Species: Lion · Vet: Dr. Smith" for the print header and Excel
    public string FilterText => string.Join(" · ", Filters
        .Where(f => !string.IsNullOrEmpty(f.Selected))
        .Select(f => f.Label + ": " + (f.Options.FirstOrDefault(o => o.Id.ToString() == f.Selected)?.Name ?? f.Selected)));

    // ---- Links that keep the current dates and filters ----
    public string UrlWith(params (string Name, string? Value)[] changes)
    {
        var q = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in Request.Query) q[kv.Key] = kv.Value.ToString();
        q.Remove("handler");
        foreach (var (name, value) in changes) q[name] = value;
        var pairs = q.Where(kv => !string.IsNullOrEmpty(kv.Value)).Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value));
        return Request.Path + QueryString.Create(pairs).ToString();
    }

    protected string Drill(string view, string key, string? key2 = null) =>
        UrlWith(("View", view), ("Key", key), ("Key2", key2), ("Back", CurrentView.Key), ("Run", "true"));

    public string BackUrl => UrlWith(("View", Back), ("Key", null), ("Key2", null), ("Back", null), ("Run", "true"));

    // ---- Analysis helpers ----

    // Runs a one-row query for this period and again for the previous period (same filters, @from/@to moved back)
    protected static async Task<(decimal[] Now, decimal[] Prev)> CompareAsync(Func<MySqlCommand> make, DateTime prevFrom, DateTime prevTo)
    {
        await using var a = make();
        var now = await FirstRowAsync(a);
        await using var b = make();
        b.Parameters["@from"].Value = prevFrom;
        b.Parameters["@to"].Value = prevTo;
        return (now, await FirstRowAsync(b));
    }

    protected Task<(decimal[] Now, decimal[] Prev)> CompareAsync(Func<MySqlCommand> make) => CompareAsync(make, PrevStart, StartDate);

    protected static async Task<decimal[]> FirstRowAsync(MySqlCommand cmd)
    {
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return Array.Empty<decimal>();
        var row = new decimal[r.FieldCount];
        for (int i = 0; i < r.FieldCount; i++) row[i] = r.IsDBNull(i) ? 0 : Convert.ToDecimal(r.GetValue(i));
        return row;
    }

    // Rows as plain values (null stays null) for small "top 1" lookups
    protected static async Task<List<object?[]>> RowsAsync(MySqlCommand cmd)
    {
        var list = new List<object?[]>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var row = new object?[r.FieldCount];
            for (int i = 0; i < r.FieldCount; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            list.Add(row);
        }
        return list;
    }

    protected static decimal Rate(decimal part, decimal whole) => whole == 0 ? 0 : Math.Round(100 * part / whole, 1);

    // ", up from 12 in the previous period" / ", down from ..." / ", the same as the previous period"
    protected static string Versus(decimal now, decimal prev, Func<decimal, string> show) =>
        now == prev ? ", the same as the previous period"
        : $", {(now > prev ? "up" : "down")} from {show(prev)} in the previous period";

    protected static string Plural(decimal n, string one, string many) => $"{n:N0} {(n == 1 ? one : many)}";

    // ---- Cell helpers ----
    protected static Cell N(int n, string? link = null) => new() { Text = n.ToString("N0"), Value = n, Link = n == 0 ? null : link };
    protected static Cell Money(decimal d, string? link = null) => new() { Text = d.ToString("C"), Value = d, Link = d == 0 ? null : link };
    protected static Cell PctCell(decimal p) => new() { Text = p.ToString("0.#") + "%", Value = p };
    protected static Cell Dec1(decimal? d) => new() { Text = d?.ToString("0.##") ?? "—", Value = d };
    protected static Cell T(string? text, string? link = null) => new() { Text = text ?? "—", Value = text, Link = link };
    protected static Cell D(DateTime? d, string? link = null) => new() { Text = d?.ToString("MMM d, yyyy") ?? "Never", Value = d, Link = link };

    protected static Row R(IEnumerable<Cell> cells, string? tone = null, string? link = null)
    {
        var row = new Row { Tone = tone, Link = link };
        row.Cells.AddRange(cells);
        return row;
    }

    protected static ReportTable Table(params Col[] cols)
    {
        var t = new ReportTable();
        t.Cols.AddRange(cols);
        return t;
    }

    protected string? OptionName(List<Option> options, int? id) =>
        id == null ? null : options.FirstOrDefault(o => o.Id == id)?.Name;

    // Drill keys look like "m:2026-09" or "e:12": the part before the first colon says what kind of key it is
    protected static (string Type, string Text) SplitKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return ("", "");
        var i = key.IndexOf(':');
        return i <= 0 ? ("", "") : (key[..i], key[(i + 1)..]);
    }

    protected static int? IntOrNull(string text) => int.TryParse(text, out var n) ? n : null;

    // Week number like 202644 (ISO-style, weeks start Monday) -> "Week of Oct 26, 2026" uses the first date we saw
    protected static string WeekLabel(DateTime firstDay) => "Week of " + firstDay.AddDays(-(((int)firstDay.DayOfWeek + 6) % 7)).ToString("MMM d, yyyy");
}

// Labor cost rules shared by the Staffing and Revenue reports
public static class LaborMath
{
    // Salaried pay is stored per year; turn it into an hourly rate (52 weeks x 40 hours)
    public const string HourlyRate =
        "CASE WHEN e.Pay_Type = 'Salary' AND e.Pay_Rate > 500 THEN e.Pay_Rate / 2080 ELSE COALESCE(e.Pay_Rate, 0) END";

    // Shifts of 6+ hours include a 30-minute unpaid break
    public const string PaidHours = "(s.Hours - IF(s.Hours >= 6, 0.5, 0))";
}
