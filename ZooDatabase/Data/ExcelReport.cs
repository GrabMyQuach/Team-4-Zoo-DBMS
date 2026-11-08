using ClosedXML.Excel;

namespace ZooDatabase.Data;

// Builds a real Excel (.xlsx) file for the "Download Excel" buttons.
// Each table in a report becomes its own tab, with a title, a styled header row,
// filters on every column, and highlighted rows that need attention.
public class ExcelReport
{
    private readonly XLWorkbook _workbook = new();
    private readonly string _title;
    private readonly string _subtitle;

    public ExcelReport(string title, string subtitle)
    {
        _title = title;
        _subtitle = subtitle;
    }

    // rowHighlight: return a color (like "#FDE2E1") for rows to highlight, or null for none
    public ExcelReport AddSheet(string name, string[] headers, IEnumerable<object?[]> rows,
                                Func<object?[], string?>? rowHighlight = null)
    {
        var ws = _workbook.Worksheets.Add(name.Length > 31 ? name[..31] : name);

        ws.Cell(1, 1).Value = _title + " - " + name;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#1F5F3F");
        ws.Cell(2, 1).Value = _subtitle;
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;

        const int headerRow = 4;
        var widths = new double[headers.Length];
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cell(headerRow, c + 1).Value = headers[c];
            widths[c] = headers[c].Length;
        }
        var header = ws.Range(headerRow, 1, headerRow, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F5F3F");

        int r = headerRow;
        foreach (var row in rows)
        {
            r++;
            for (int c = 0; c < headers.Length && c < row.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                SetCell(cell, row[c]);
                widths[c] = Math.Max(widths[c], DisplayLength(row[c]));
            }

            var color = rowHighlight?.Invoke(row);
            if (color != null)
                ws.Range(r, 1, r, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml(color);
        }

        if (r > headerRow) ws.Range(headerRow, 1, r, headers.Length).SetAutoFilter();
        else ws.Cell(headerRow + 1, 1).Value = "No data for the selected filters.";

        ws.SheetView.FreezeRows(headerRow);
        for (int c = 0; c < headers.Length; c++)
            ws.Column(c + 1).Width = Math.Min(60, widths[c] + 3);

        return this;
    }

    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        _workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SetCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case DateTime d:
                cell.Value = d;
                cell.Style.DateFormat.Format = "mmm d, yyyy";
                break;
            case bool b:
                cell.Value = b ? "Yes" : "No";
                break;
            case int i:
                cell.Value = (double)i;
                break;
            case decimal m:
                cell.Value = (double)m;
                cell.Style.NumberFormat.Format = "#,##0.##";
                break;
            case double x:
                cell.Value = x;
                cell.Style.NumberFormat.Format = "#,##0.##";
                break;
            default:
                cell.Value = value.ToString() ?? "";
                break;
        }
    }

    private static double DisplayLength(object? value) => value switch
    {
        null => 0,
        DateTime => 12,
        _ => value.ToString()?.Length ?? 0
    };

    // Shared highlight colors
    public const string Red = "#FDE2E1";
    public const string Yellow = "#FFF4CE";
}
