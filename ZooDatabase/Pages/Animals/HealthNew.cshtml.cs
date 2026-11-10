using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Animals;

// Record a vet exam for one animal:
//   /Animals/HealthNew/5  -> new health record for animal #5
//
// Saving inserts a row into Health_Record. If the exam is marked contagious,
// the trg_contagious_diagnosis trigger runs automatically: it opens a quarantine,
// cancels the animal's upcoming encounters, creates follow-up and exposure tasks,
// and alerts the keepers. This page just reports what the trigger did.
[Authorize(Roles = AnimalOptions.HealthEditorRoles)]
public class HealthNewModel : PageModel
{
    private readonly IConfiguration _config;

    public HealthNewModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty] public HealthInput Input { get; set; } = new();

    // Where to go after saving (the vet's animal history page sends ?Back=/Care/History/5)
    [BindProperty(SupportsGet = true)] public string? Back { get; set; }

    public string AnimalName { get; set; } = "";
    public List<VetOption> VetOptions { get; set; } = new();
    public string? ErrorMessage { get; set; }

    // The animal's last few exams, shown next to the form so the vet can compare
    public List<PastExam> Recent { get; set; } = new();
    public PastExam? LastWeighed { get; set; }
    public record PastExam(DateTime Date, string Type, string? Diagnosis, string Severity, bool Contagious,
                           decimal? WeightKg, string? Vet, string? Treatment);

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        if (!await LoadAnimalAsync(conn)) return NotFound();

        // Start the form with sensible defaults: now, a routine checkup,
        // and the animal's Primary Vet if it has one.
        Input.ExamDate = DateTime.Now.AddSeconds(-DateTime.Now.Second);
        Input.RecordType = "Routine Checkup";
        Input.Severity = "Low";
        await using (var cmd = new MySqlCommand("SELECT fn_primary_vet(@id)", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            var vet = await cmd.ExecuteScalarAsync();
            Input.VetId = vet is int v ? v : vet is long l ? (int)l : 0;
        }

        await LoadVetsAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        if (!await LoadAnimalAsync(conn)) return NotFound();

        // 1. Check the form makes sense before touching the database
        Input.Diagnosis = Input.Diagnosis?.Trim();
        Input.Treatment = Input.Treatment?.Trim();
        Input.Notes = Input.Notes?.Trim();

        if (!AnimalOptions.RecordTypes.Contains(Input.RecordType))
            ErrorMessage = "Please choose a record type.";
        else if (!AnimalOptions.Severities.Contains(Input.Severity))
            ErrorMessage = "Please choose a severity.";
        else if (Input.ExamDate > DateTime.Now.AddMinutes(5))
            ErrorMessage = "Exam date can't be in the future.";
        else if (Input.WeightKg < 0)
            ErrorMessage = "Weight can't be negative.";
        else if (Input.IsContagious && string.IsNullOrEmpty(Input.Diagnosis))
            ErrorMessage = "Please enter the diagnosis for a contagious case. It is used in the quarantine and staff alerts.";

        if (ErrorMessage != null)
        {
            await LoadVetsAsync(conn);
            return Page();
        }

        // 2. Save it. If it's contagious, the trigger does the rest.
        try
        {
            long recordId;
            await using (var cmd = new MySqlCommand(@"
                INSERT INTO Health_Record (Animal_ID, Vet_Employee_ID, Exam_Date, Record_Type, Diagnosis,
                                           Is_Contagious, Severity, Treatment, Weight_kg, Notes)
                VALUES (@animal, @vet, @date, @type, @diag, @contagious, @severity, @treat, @weight, @notes)", conn))
            {
                cmd.Parameters.AddWithValue("@animal", Id);
                cmd.Parameters.AddWithValue("@vet", Input.VetId > 0 ? Input.VetId : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@date", Input.ExamDate);
                cmd.Parameters.AddWithValue("@type", Input.RecordType);
                cmd.Parameters.AddWithValue("@diag", string.IsNullOrEmpty(Input.Diagnosis) ? DBNull.Value : Input.Diagnosis);
                cmd.Parameters.AddWithValue("@contagious", Input.IsContagious ? 1 : 0);
                cmd.Parameters.AddWithValue("@severity", Input.Severity);
                cmd.Parameters.AddWithValue("@treat", string.IsNullOrEmpty(Input.Treatment) ? DBNull.Value : Input.Treatment);
                cmd.Parameters.AddWithValue("@weight", (object?)Input.WeightKg ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@notes", string.IsNullOrEmpty(Input.Notes) ? DBNull.Value : Input.Notes);
                await cmd.ExecuteNonQueryAsync();
                recordId = cmd.LastInsertedId;
            }

            TempData["Success"] = Input.IsContagious
                ? await DescribeQuarantineAsync(conn, recordId)
                : $"Exam recorded for {AnimalName}.";
            return Redirect(!string.IsNullOrEmpty(Back) && Url.IsLocalUrl(Back) ? Back : $"/Animals/Details/{Id}");
        }
        catch (MySqlException ex) when (ex.SqlState == "45000")
        {
            // 45000 = a custom error thrown by a trigger with SIGNAL
            ErrorMessage = ex.Message;
            await LoadVetsAsync(conn);
            return Page();
        }
    }

    // After a contagious exam, read back what the trigger created so staff can see it happened
    private async Task<string> DescribeQuarantineAsync(MySqlConnection conn, long recordId)
    {
        await using var cmd = new MySqlCommand(@"
            SELECT (SELECT Expected_End_Date FROM Quarantine
                    WHERE Animal_ID = @animal AND Status IN ('Active', 'Extended')
                    ORDER BY Quarantine_ID DESC LIMIT 1),
                   (SELECT COUNT(*) FROM Care_Task
                    WHERE Source_Type = 'Health_Record' AND Source_ID = @record),
                   (SELECT COUNT(*) FROM Animal_Encounter
                    WHERE Animal_ID = @animal AND Status = 'Cancelled'
                      AND Cancel_Reason LIKE 'Quarantine:%' AND Scheduled_Start >= NOW())", conn);
        cmd.Parameters.AddWithValue("@animal", Id);
        cmd.Parameters.AddWithValue("@record", recordId);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();

        string until = r.IsDBNull(0) ? "" : $" until {r.GetDateTime(0):MMM d}";
        long tasks = Convert.ToInt64(r.GetValue(1));
        long cancelled = Convert.ToInt64(r.GetValue(2));
        return $"Exam recorded. {AnimalName} is now in quarantine{until}: " +
               $"{cancelled} upcoming encounter(s) cancelled, {tasks} follow-up task(s) created, and the keepers were alerted.";
    }

    private async Task<bool> LoadAnimalAsync(MySqlConnection conn)
    {
        await using var cmd = new MySqlCommand(
            "SELECT Animal_Name FROM Animal WHERE Animal_ID = @id AND Is_Active = 1", conn);
        cmd.Parameters.AddWithValue("@id", Id);
        var name = await cmd.ExecuteScalarAsync();
        if (name is not string s) return false;
        AnimalName = s;

        Recent.Clear();
        await using (var hist = new MySqlCommand(@"
            SELECT h.Exam_Date, h.Record_Type, h.Diagnosis, h.Severity, h.Is_Contagious, h.Weight_kg,
                   CONCAT(e.First_Name, ' ', e.Last_Name), h.Treatment
            FROM Health_Record h
            LEFT JOIN Employee e ON e.Employee_ID = h.Vet_Employee_ID
            WHERE h.Animal_ID = @id
            ORDER BY h.Exam_Date DESC, h.Health_Record_ID DESC
            LIMIT 5", conn))
        {
            hist.Parameters.AddWithValue("@id", Id);
            await using var r = await hist.ExecuteReaderAsync();
            while (await r.ReadAsync())
                Recent.Add(ReadExam(r));
        }

        // Latest weight, even if it's older than the last 5 exams
        LastWeighed = Recent.FirstOrDefault(x => x.WeightKg != null);
        if (LastWeighed == null)
        {
            await using var w = new MySqlCommand(@"
                SELECT h.Exam_Date, h.Record_Type, h.Diagnosis, h.Severity, h.Is_Contagious, h.Weight_kg,
                       CONCAT(e.First_Name, ' ', e.Last_Name), h.Treatment
                FROM Health_Record h
                LEFT JOIN Employee e ON e.Employee_ID = h.Vet_Employee_ID
                WHERE h.Animal_ID = @id AND h.Weight_kg IS NOT NULL
                ORDER BY h.Exam_Date DESC LIMIT 1", conn);
            w.Parameters.AddWithValue("@id", Id);
            await using var r = await w.ExecuteReaderAsync();
            if (await r.ReadAsync()) LastWeighed = ReadExam(r);
        }
        return true;
    }

    private static PastExam ReadExam(MySqlDataReader r) => new(
        r.GetDateTime(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4),
        r.IsDBNull(5) ? null : r.GetDecimal(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7));

    // Vets come from the Veterinarian table, since Health_Record only accepts those
    private async Task LoadVetsAsync(MySqlConnection conn)
    {
        VetOptions.Clear();
        await using var cmd = new MySqlCommand(@"
            SELECT v.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name), v.Specialization
            FROM Veterinarian v
            JOIN Employee e ON e.Employee_ID = v.Employee_ID
            ORDER BY e.Last_Name, e.First_Name", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            string label = r.IsDBNull(2) ? r.GetString(1) : $"{r.GetString(1)} ({r.GetString(2)})";
            VetOptions.Add(new VetOption(r.GetInt32(0), label));
        }
    }

    public record VetOption(int Id, string Label);

    public class HealthInput
    {
        public DateTime ExamDate { get; set; }
        public int VetId { get; set; }
        public string RecordType { get; set; } = "";
        public string? Diagnosis { get; set; }
        public bool IsContagious { get; set; }
        public string Severity { get; set; } = "";
        public string? Treatment { get; set; }
        public decimal? WeightKg { get; set; }
        public string? Notes { get; set; }
    }
}
