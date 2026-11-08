using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Animals;

// One animal's full profile. Everyone logged in can view it;
// only editor roles see (and can use) Edit and Remove.
public class DetailsModel : PageModel
{
    private readonly IConfiguration _config;

    public DetailsModel(IConfiguration config)
    {
        _config = config;
    }

    public AnimalProfile Animal { get; set; } = new();
    public QuarantineInfo? ActiveQuarantine { get; set; }
    public ConservationInfo? Conservation { get; set; }
    public List<HealthEntry> HealthHistory { get; set; } = new();
    public List<TaskEntry> OpenTasks { get; set; } = new();
    public List<FeedingEntry> FeedingSchedule { get; set; } = new();
    public List<DietItem> SpeciesDiet { get; set; } = new();
    public List<CareTeamMember> CareTeam { get; set; } = new();
    public int Enclosemates { get; set; }

    public bool CanEdit => AnimalOptions.CanEdit(User);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // 1. The animal, its species, its enclosure, and its parents (Animal joined to itself!)
        await using (var cmd = new MySqlCommand(@"
            SELECT a.Animal_ID, a.Animal_Name, a.Gender, a.Date_of_Birth, a.Health_Status,
                   a.Date_Acquired, a.Acquisition_Type, a.Public_Encounter_Eligible, a.Needs_Relocation,
                   s.Common_Name, s.Scientific_Name, s.Diet_Type, s.Conservation_Status, s.Habitat_Type,
                   e.Enclosure_ID, e.Enclosure_Name, e.Enclosure_Type, e.Location, e.Capacity, e.Status,
                   mom.Animal_ID, mom.Animal_Name, dad.Animal_ID, dad.Animal_Name
            FROM Animal a
            JOIN Species s ON a.Species_ID = s.Species_ID
            JOIN Enclosure e ON a.Enclosure_ID = e.Enclosure_ID
            LEFT JOIN Animal mom ON mom.Animal_ID = a.Mother_ID
            LEFT JOIN Animal dad ON dad.Animal_ID = a.Father_ID
            WHERE a.Animal_ID = @id AND a.Is_Active = 1", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();

            Animal = new AnimalProfile
            {
                AnimalId = r.GetInt32(0),
                Name = r.GetString(1),
                Gender = Str(r, 2),
                DateOfBirth = Date(r, 3),
                HealthStatus = Str(r, 4),
                DateAcquired = Date(r, 5),
                AcquisitionType = Str(r, 6),
                EncounterEligible = r.GetBoolean(7),
                NeedsRelocation = r.GetBoolean(8),
                Species = r.GetString(9),
                ScientificName = Str(r, 10),
                DietType = Str(r, 11),
                ConservationStatus = Str(r, 12),
                HabitatType = Str(r, 13),
                EnclosureId = r.GetInt32(14),
                Enclosure = r.GetString(15),
                EnclosureType = Str(r, 16),
                EnclosureLocation = Str(r, 17),
                EnclosureCapacity = r.GetInt32(18),
                EnclosureStatus = Str(r, 19) ?? "Open",
                MotherId = r.IsDBNull(20) ? null : r.GetInt32(20),
                MotherName = Str(r, 21),
                FatherId = r.IsDBNull(22) ? null : r.GetInt32(22),
                FatherName = Str(r, 23)
            };
        }

        // 2. Is it in quarantine right now?
        await using (var cmd = new MySqlCommand(@"
            SELECT Start_Date, Expected_End_Date, Reason, Status
            FROM Quarantine
            WHERE Animal_ID = @id AND Status IN ('Active', 'Extended')
            ORDER BY Start_Date DESC LIMIT 1", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                ActiveQuarantine = new QuarantineInfo
                {
                    StartDate = r.GetDateTime(0),
                    ExpectedEnd = Date(r, 1),
                    Reason = Str(r, 2),
                    Status = r.GetString(3)
                };
            }
        }

        // 3. Conservation program enrollment (if any)
        await using (var cmd = new MySqlCommand(@"
            SELECT cp.Program_Name, cr.Studbook_Number, cr.Status
            FROM Conservation_Record cr
            LEFT JOIN Conservation_Program cp ON cp.Program_ID = cr.Program_ID
            WHERE cr.Animal_ID = @id
            ORDER BY cr.Enrollment_Date DESC LIMIT 1", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                Conservation = new ConservationInfo
                {
                    ProgramName = Str(r, 0),
                    StudbookNumber = Str(r, 1),
                    Status = r.GetString(2)
                };
            }
        }

        // 4. Health history: the 10 most recent vet records
        await using (var cmd = new MySqlCommand(@"
            SELECT h.Exam_Date, h.Record_Type, h.Diagnosis, h.Severity, h.Is_Contagious,
                   h.Treatment, h.Weight_kg, CONCAT(e.First_Name, ' ', e.Last_Name)
            FROM Health_Record h
            LEFT JOIN Employee e ON e.Employee_ID = h.Vet_Employee_ID
            WHERE h.Animal_ID = @id
            ORDER BY h.Exam_Date DESC
            LIMIT 10", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                HealthHistory.Add(new HealthEntry
                {
                    ExamDate = r.GetDateTime(0),
                    RecordType = r.GetString(1),
                    Diagnosis = Str(r, 2),
                    Severity = r.GetString(3),
                    IsContagious = r.GetBoolean(4),
                    Treatment = Str(r, 5),
                    WeightKg = r.IsDBNull(6) ? null : r.GetDecimal(6),
                    Vet = Str(r, 7)
                });
            }
        }

        // 5. Open care tasks for this animal (many are created automatically by triggers)
        await using (var cmd = new MySqlCommand(@"
            SELECT t.Title, t.Task_Category, t.Priority, t.Status, t.Due_At,
                   CONCAT(e.First_Name, ' ', e.Last_Name)
            FROM Care_Task t
            LEFT JOIN Employee e ON e.Employee_ID = t.Assigned_Employee_ID
            WHERE t.Animal_ID = @id AND t.Status IN ('Open', 'In Progress')
            ORDER BY FIELD(t.Priority, 'Urgent', 'High', 'Normal', 'Low'), t.Due_At", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                OpenTasks.Add(new TaskEntry
                {
                    Title = r.GetString(0),
                    Category = r.GetString(1),
                    Priority = r.GetString(2),
                    Status = r.GetString(3),
                    DueAt = Date(r, 4),
                    AssignedTo = Str(r, 5)
                });
            }
        }

        // 6. Feeding schedule, plus whether each feeding happened today (Feeding_Log)
        await using (var cmd = new MySqlCommand(@"
            SELECT fs.Feeding_Time, f.Feed_Name, fs.Quantity, fs.Unit, fs.Special_Instructions,
                   CONCAT(e.First_Name, ' ', e.Last_Name), fl.Status
            FROM Feeding_Schedule fs
            JOIN Feed f ON f.Feed_ID = fs.Feed_ID
            LEFT JOIN Employee e ON e.Employee_ID = fs.Keeper_Employee_ID
            LEFT JOIN Feeding_Log fl ON fl.Schedule_ID = fs.Schedule_ID AND fl.Feeding_Date = CURDATE()
            WHERE fs.Animal_ID = @id AND fs.Is_Active = 1
            ORDER BY fs.Feeding_Time", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                FeedingSchedule.Add(new FeedingEntry
                {
                    Time = r.GetTimeSpan(0),
                    FeedName = r.GetString(1),
                    Quantity = r.GetDecimal(2),
                    Unit = r.GetString(3),
                    Instructions = Str(r, 4),
                    Keeper = Str(r, 5),
                    TodayStatus = Str(r, 6)
                });
            }
        }

        // 7. If there's no schedule yet, fall back to the species' general diet (Species_Feed)
        if (FeedingSchedule.Count == 0)
        {
            await using var cmd = new MySqlCommand(@"
                SELECT f.Feed_Name, f.Feed_Type, sf.Daily_Quantity, f.Unit
                FROM Animal a
                JOIN Species_Feed sf ON sf.Species_ID = a.Species_ID
                JOIN Feed f ON f.Feed_ID = sf.Feed_ID
                WHERE a.Animal_ID = @id
                ORDER BY f.Feed_Name", conn);
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                SpeciesDiet.Add(new DietItem
                {
                    FeedName = r.GetString(0),
                    FeedType = Str(r, 1),
                    DailyQuantity = r.IsDBNull(2) ? null : r.GetDecimal(2),
                    Unit = r.GetString(3)
                });
            }
        }

        // 8. Care team (keepers and vets assigned through Animal_Employee)
        await using (var cmd = new MySqlCommand(@"
            SELECT emp.First_Name, emp.Last_Name, emp.Job_Position, ae.Assigned_Role, emp.Phone_Number
            FROM Animal_Employee ae
            JOIN Employee emp ON emp.Employee_ID = ae.Employee_ID
            WHERE ae.Animal_ID = @id
            ORDER BY emp.Last_Name, emp.First_Name", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                CareTeam.Add(new CareTeamMember
                {
                    Name = r.GetString(0) + " " + r.GetString(1),
                    JobPosition = Str(r, 2),
                    AssignedRole = Str(r, 3),
                    Phone = Str(r, 4)
                });
            }
        }

        // 9. How many animals share the enclosure (for "3 of 5 spots")
        await using (var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM Animal WHERE Enclosure_ID = @enc AND Is_Active = 1", conn))
        {
            cmd.Parameters.AddWithValue("@enc", Animal.EnclosureId);
            Enclosemates = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        return Page();
    }

    // "Remove" button: soft delete (hide the animal, keep its history)
    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        if (!CanEdit) return Forbid();

        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "UPDATE Animal SET Is_Active = 0 WHERE Animal_ID = @id AND Is_Active = 1", conn);
        cmd.Parameters.AddWithValue("@id", id);
        int changed = await cmd.ExecuteNonQueryAsync();

        TempData["Success"] = changed > 0 ? "The animal was removed." : "That animal was already removed.";
        return RedirectToPage("/Animals/Animals");
    }

    // Small helpers: return null when the database cell is empty
    private static string? Str(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateTime? Date(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);

    // Badge colors used by the page
    public static string SeverityBadge(string severity) => severity switch
    {
        "Critical" => "bg-danger",
        "High" => "bg-danger",
        "Moderate" => "bg-warning text-dark",
        _ => "bg-light text-dark border"
    };

    public static string PriorityBadge(string priority) => priority switch
    {
        "Urgent" => "bg-danger",
        "High" => "bg-warning text-dark",
        _ => "bg-light text-dark border"
    };

    public class AnimalProfile
    {
        public int AnimalId { get; set; }
        public string Name { get; set; } = "";
        public string? Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? HealthStatus { get; set; }
        public DateTime? DateAcquired { get; set; }
        public string? AcquisitionType { get; set; }
        public bool EncounterEligible { get; set; }
        public bool NeedsRelocation { get; set; }
        public string Species { get; set; } = "";
        public string? ScientificName { get; set; }
        public string? DietType { get; set; }
        public string? ConservationStatus { get; set; }
        public string? HabitatType { get; set; }
        public int EnclosureId { get; set; }
        public string Enclosure { get; set; } = "";
        public string? EnclosureType { get; set; }
        public string? EnclosureLocation { get; set; }
        public int EnclosureCapacity { get; set; }
        public string EnclosureStatus { get; set; } = "Open";
        public int? MotherId { get; set; }
        public string? MotherName { get; set; }
        public int? FatherId { get; set; }
        public string? FatherName { get; set; }
    }

    public class QuarantineInfo
    {
        public DateTime StartDate { get; set; }
        public DateTime? ExpectedEnd { get; set; }
        public string? Reason { get; set; }
        public string Status { get; set; } = "";
    }

    public class ConservationInfo
    {
        public string? ProgramName { get; set; }
        public string? StudbookNumber { get; set; }
        public string Status { get; set; } = "";
    }

    public class HealthEntry
    {
        public DateTime ExamDate { get; set; }
        public string RecordType { get; set; } = "";
        public string? Diagnosis { get; set; }
        public string Severity { get; set; } = "";
        public bool IsContagious { get; set; }
        public string? Treatment { get; set; }
        public decimal? WeightKg { get; set; }
        public string? Vet { get; set; }
    }

    public class TaskEntry
    {
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public string Priority { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime? DueAt { get; set; }
        public string? AssignedTo { get; set; }
    }

    public class FeedingEntry
    {
        public TimeSpan Time { get; set; }
        public string FeedName { get; set; } = "";
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = "";
        public string? Instructions { get; set; }
        public string? Keeper { get; set; }
        public string? TodayStatus { get; set; }
    }

    public class DietItem
    {
        public string FeedName { get; set; } = "";
        public string? FeedType { get; set; }
        public decimal? DailyQuantity { get; set; }
        public string Unit { get; set; } = "";
    }

    public class CareTeamMember
    {
        public string Name { get; set; } = "";
        public string? JobPosition { get; set; }
        public string? AssignedRole { get; set; }
        public string? Phone { get; set; }
    }
}
