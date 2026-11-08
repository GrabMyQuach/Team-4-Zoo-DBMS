using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace ZooDatabase.Pages.Reports;

// Shared base for every report page: the date range filter, the database connection,
// and small helpers, so each report only contains its own queries.
public abstract class ReportPageModel : PageModel
{
    private readonly IConfiguration _config;

    protected ReportPageModel(IConfiguration config)
    {
        _config = config;
    }

    // Date range filter (defaults to the last 90 days)
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    public DateTime EndExclusive => EndDate.AddDays(1);
    public int DayCount => (EndDate - StartDate).Days + 1;
    public string DateRangeText => $"{StartDate:MMM d, yyyy} to {EndDate:MMM d, yyyy}";

    // Link to the Excel download that keeps the current filters
    public string ExcelUrl
    {
        get
        {
            var qs = Request.QueryString.Value;
            return Request.Path.Value + (string.IsNullOrEmpty(qs) ? "?handler=Excel" : qs + "&handler=Excel");
        }
    }

    protected void ResolveDates()
    {
        EndDate = (To ?? DateTime.Today).Date;
        StartDate = (From ?? EndDate.AddDays(-89)).Date;
        if (StartDate > EndDate) (StartDate, EndDate) = (EndDate, StartDate);
    }

    protected async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }

    protected void AddDateParams(MySqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("@from", StartDate);
        cmd.Parameters.AddWithValue("@to", EndExclusive);
    }

    // Loads a dropdown list from a query that returns (id, name)
    protected static async Task<List<Option>> LoadOptionsAsync(MySqlConnection conn, string sql)
    {
        var list = new List<Option>();
        await using var cmd = new MySqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new Option(r.GetInt32(0), r.GetString(1)));
        return list;
    }

    // Read helpers: database NULL becomes null / 0
    protected static string? Str(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
    protected static int Int(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
    protected static decimal Dec(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToDecimal(r.GetValue(i));
    protected static decimal? DecOrNull(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
    protected static DateTime? DateOrNull(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);

    public static decimal Pct(int part, int whole) => whole == 0 ? 0 : Math.Round(100m * part / whole, 1);

    public static string MonthName(string yearMonth) =>
        DateTime.TryParse(yearMonth + "-01", out var d) ? d.ToString("MMM yyyy") : yearMonth;

    public record Option(int Id, string Name);
}
