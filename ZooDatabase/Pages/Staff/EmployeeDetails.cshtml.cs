using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Staff;

// /Staff/EmployeeDetails/5 -> everything about one employee:
// job info, workload (shifts, open tasks), and what they look after
// (enclosures for keepers, animals for vets). Private info is DBA/SuperUser only.
public class EmployeeDetailsModel : PageModel
{
    private readonly IConfiguration _config;

    public EmployeeDetailsModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public EmployeeInfo Emp { get; set; } = new();
    public bool CanEdit => StaffOptions.CanEdit(User);
    public bool CanSeePrivate => StaffOptions.CanSeePrivate(User);

    public decimal HoursLast30 { get; set; }
    public int ShiftsLast30 { get; set; }
    public int AbsencesLast30 { get; set; }
    public List<ShiftRow> Shifts { get; set; } = new();
    public List<TaskRow> OpenTasks { get; set; } = new();
    public List<LinkRow> Enclosures { get; set; } = new();
    public List<LinkRow> Animals { get; set; } = new();
    public int ExamsLast90 { get; set; }
    public int FeedingsLoggedLast30 { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        // 1. The employee, with department, specialty and login account
        await using (var cmd = new MySqlCommand(@"
            SELECT e.First_Name, e.Middle_Name, e.Last_Name, e.Job_Position, e.Department_ID, d.Department_Name,
                   e.Employment_Type, e.Hire_Date, e.Phone_Number, e.Gender,
                   e.SSN, e.Address, e.Pay_Type, e.Pay_Rate,
                   k.Experience_Level, (k.Employee_ID IS NOT NULL) AS Is_Keeper,
                   v.Specialization, (v.Employee_ID IS NOT NULL) AS Is_Vet,
                   (d.Manager_Employee_ID = e.Employee_ID) AS Is_Manager,
                   (SELECT CONCAT(u.Username, ' (', u.Role, ')') FROM User_Account u
                     WHERE u.Employee_ID = e.Employee_ID ORDER BY u.User_ID LIMIT 1) AS Login
            FROM Employee e
            LEFT JOIN Department d ON d.Department_ID = e.Department_ID
            LEFT JOIN Keeper k ON k.Employee_ID = e.Employee_ID
            LEFT JOIN Veterinarian v ON v.Employee_ID = e.Employee_ID
            WHERE e.Employee_ID = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();

            string? S(int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
            bool B(int i) => !r.IsDBNull(i) && Convert.ToInt32(r.GetValue(i)) == 1;

            Emp = new EmployeeInfo
            {
                Name = string.Join(" ", new[] { S(0), S(1), S(2) }.Where(x => !string.IsNullOrWhiteSpace(x))),
                JobPosition = S(3),
                DepartmentId = r.IsDBNull(4) ? null : r.GetInt32(4),
                Department = S(5),
                EmploymentType = S(6),
                HireDate = r.IsDBNull(7) ? null : r.GetDateTime(7),
                Phone = S(8),
                Gender = S(9),
                Ssn = S(10),
                Address = S(11),
                PayType = S(12),
                PayRate = r.IsDBNull(13) ? null : r.GetDecimal(13),
                ExperienceLevel = S(14),
                IsKeeper = B(15),
                Specialization = S(16),
                IsVet = B(17),
                IsManager = B(18),
                Login = S(19)
            };
        }

        // 2. Workload: shifts in the last 30 days
        await using (var cmd = new MySqlCommand(@"
            SELECT COALESCE(SUM(CASE WHEN Status = 'Worked' THEN Hours END), 0),
                   SUM(Status = 'Worked'), SUM(Status = 'Absent')
            FROM Employee_Shift
            WHERE Employee_ID = @id AND Shift_Date BETWEEN CURDATE() - INTERVAL 30 DAY AND CURDATE()", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                HoursLast30 = r.IsDBNull(0) ? 0 : Math.Round(Convert.ToDecimal(r.GetValue(0)), 1);
                ShiftsLast30 = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1));
                AbsencesLast30 = r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2));
            }
        }

        // Recent and upcoming shifts (last 14 days through next 7)
        await using (var cmd = new MySqlCommand(@"
            SELECT s.Shift_Date, s.Start_Time, s.End_Time, s.Hours, s.Status, s.Absence_Reason, d.Department_Name
            FROM Employee_Shift s
            LEFT JOIN Department d ON d.Department_ID = s.Department_ID
            WHERE s.Employee_ID = @id
              AND s.Shift_Date BETWEEN CURDATE() - INTERVAL 14 DAY AND CURDATE() + INTERVAL 7 DAY
            ORDER BY s.Shift_Date DESC, s.Start_Time", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                Shifts.Add(new ShiftRow
                {
                    Date = r.GetDateTime(0),
                    Start = r.GetTimeSpan(1),
                    End = r.GetTimeSpan(2),
                    Hours = r.IsDBNull(3) ? 0 : r.GetDecimal(3),
                    Status = r.GetString(4),
                    Reason = r.IsDBNull(5) ? null : r.GetString(5),
                    Department = r.IsDBNull(6) ? null : r.GetString(6)
                });
            }
        }

        // 3. Open tasks assigned to them
        await using (var cmd = new MySqlCommand(@"
            SELECT t.Title, t.Task_Category, t.Priority, t.Due_At, t.Animal_ID, a.Animal_Name
            FROM Care_Task t
            LEFT JOIN Animal a ON a.Animal_ID = t.Animal_ID
            WHERE t.Assigned_Employee_ID = @id AND t.Status IN ('Open', 'In Progress')
            ORDER BY FIELD(t.Priority, 'Urgent', 'High', 'Normal', 'Low'), t.Due_At", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                OpenTasks.Add(new TaskRow
                {
                    Title = r.GetString(0),
                    Category = r.GetString(1),
                    Priority = r.GetString(2),
                    Due = r.IsDBNull(3) ? null : r.GetDateTime(3),
                    AnimalId = r.IsDBNull(4) ? null : r.GetInt32(4),
                    AnimalName = r.IsDBNull(5) ? null : r.GetString(5)
                });
            }
        }

        // 4a. Keepers: enclosures they are assigned to, and feedings they logged
        if (Emp.IsKeeper)
        {
            await using (var cmd = new MySqlCommand(@"
                SELECT e.Enclosure_ID, e.Enclosure_Name,
                       CONCAT(e.Animal_Count, ' animals · ', e.Status,
                              IF(ke.Assignment_Start_Date IS NULL, '', CONCAT(' · since ', DATE_FORMAT(ke.Assignment_Start_Date, '%b %Y'))))
                FROM Keeper_Enclosure ke
                JOIN Enclosure e ON e.Enclosure_ID = ke.Enclosure_ID
                WHERE ke.Employee_ID = @id
                  AND (ke.Assignment_End_Date IS NULL OR ke.Assignment_End_Date >= CURDATE())
                ORDER BY e.Enclosure_Name", conn))
            {
                cmd.Parameters.AddWithValue("@id", Id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    Enclosures.Add(new LinkRow(r.GetInt32(0), r.GetString(1), r.GetString(2)));
            }

            await using (var cmd = new MySqlCommand(@"
                SELECT COUNT(*) FROM Feeding_Log
                WHERE Recorded_By_Employee_ID = @id AND Feeding_Date >= CURDATE() - INTERVAL 30 DAY", conn))
            {
                cmd.Parameters.AddWithValue("@id", Id);
                FeedingsLoggedLast30 = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
        }

        // 4b. Vets: animals under their care, and exams in the last 90 days
        if (Emp.IsVet)
        {
            await using (var cmd = new MySqlCommand(@"
                SELECT a.Animal_ID, a.Animal_Name, CONCAT(s.Common_Name, ' · ', COALESCE(a.Health_Status, 'Unknown'))
                FROM Animal a
                JOIN Species s ON s.Species_ID = a.Species_ID
                WHERE a.Is_Active = 1
                  AND (a.Animal_ID IN (SELECT ae.Animal_ID FROM Animal_Employee ae WHERE ae.Employee_ID = @id)
                       OR a.Animal_ID IN (SELECT h.Animal_ID FROM Health_Record h
                                          WHERE h.Vet_Employee_ID = @id AND h.Exam_Date >= CURDATE() - INTERVAL 90 DAY))
                ORDER BY a.Animal_Name", conn))
            {
                cmd.Parameters.AddWithValue("@id", Id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    Animals.Add(new LinkRow(r.GetInt32(0), r.GetString(1), r.GetString(2)));
            }

            await using (var cmd = new MySqlCommand(@"
                SELECT COUNT(*) FROM Health_Record
                WHERE Vet_Employee_ID = @id AND Exam_Date >= CURDATE() - INTERVAL 90 DAY", conn))
            {
                cmd.Parameters.AddWithValue("@id", Id);
                ExamsLast90 = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
        }

        return Page();
    }

    public class EmployeeInfo
    {
        public string Name { get; set; } = "";
        public string? JobPosition { get; set; }
        public int? DepartmentId { get; set; }
        public string? Department { get; set; }
        public string? EmploymentType { get; set; }
        public DateTime? HireDate { get; set; }
        public string? Phone { get; set; }
        public string? Gender { get; set; }
        public string? Ssn { get; set; }
        public string? Address { get; set; }
        public string? PayType { get; set; }
        public decimal? PayRate { get; set; }
        public bool IsKeeper { get; set; }
        public string? ExperienceLevel { get; set; }
        public bool IsVet { get; set; }
        public string? Specialization { get; set; }
        public bool IsManager { get; set; }
        public string? Login { get; set; }
    }

    public class ShiftRow
    {
        public DateTime Date { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public decimal Hours { get; set; }
        public string Status { get; set; } = "";
        public string? Reason { get; set; }
        public string? Department { get; set; }
    }

    public class TaskRow
    {
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public string Priority { get; set; } = "";
        public DateTime? Due { get; set; }
        public int? AnimalId { get; set; }
        public string? AnimalName { get; set; }
    }

    public record LinkRow(int Id, string Name, string Info);
}
