using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Staff;

// One page for BOTH adding and editing (DBA only):
//   /Staff/EmployeeEdit      -> add a new employee
//   /Staff/EmployeeEdit/5    -> edit employee #5
[Authorize(Roles = StaffOptions.EditorRoles)]
public class EmployeeEditModel : PageModel
{
    private readonly IConfiguration _config;

    public EmployeeEditModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public EmployeeInput Input { get; set; } = new();

    public List<(int Id, string Name)> DepartmentOptions { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? CurrentSsnMasked { get; set; }
    public bool IsNew => Id == null;

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = await OpenAsync();

        if (!IsNew)
        {
            await using var cmd = new MySqlCommand(@"
                SELECT e.First_Name, e.Middle_Name, e.Last_Name, e.Job_Position, e.Department_ID, e.Employment_Type,
                       e.Hire_Date, e.Pay_Type, e.Pay_Rate, e.Phone_Number, e.Gender, e.Address, e.SSN,
                       k.Employee_ID IS NOT NULL, k.Experience_Level, v.Employee_ID IS NOT NULL, v.Specialization
                FROM Employee e
                LEFT JOIN Keeper k ON k.Employee_ID = e.Employee_ID
                LEFT JOIN Veterinarian v ON v.Employee_ID = e.Employee_ID
                WHERE e.Employee_ID = @id", conn);
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();

            string? S(int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
            bool isKeeper = Convert.ToInt32(r.GetValue(13)) == 1;
            bool isVet = Convert.ToInt32(r.GetValue(15)) == 1;

            Input = new EmployeeInput
            {
                FirstName = S(0) ?? "",
                MiddleName = S(1),
                LastName = S(2) ?? "",
                JobPosition = S(3),
                DepartmentId = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                EmploymentType = S(5),
                HireDate = r.IsDBNull(6) ? null : r.GetDateTime(6),
                PayType = S(7),
                PayRate = r.IsDBNull(8) ? null : r.GetDecimal(8),
                Phone = S(9),
                Gender = S(10),
                Address = S(11),
                Specialty = isKeeper ? "Keeper" : isVet ? "Veterinarian" : "None",
                ExperienceLevel = S(14),
                Specialization = S(16)
            };
            CurrentSsnMasked = StaffOptions.MaskSsn(S(12));
        }
        else
        {
            Input.HireDate = DateTime.Today;
            Input.EmploymentType = "Full-Time";
            Input.PayType = "Hourly";
        }

        await LoadDepartmentsAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = await OpenAsync();

        // 1. Check the form
        Input.FirstName = (Input.FirstName ?? "").Trim();
        Input.LastName = (Input.LastName ?? "").Trim();
        var ssn = Regex.Replace(Input.Ssn ?? "", "[^0-9]", "");

        if (Input.FirstName.Length == 0 || Input.LastName.Length == 0)
            ErrorMessage = "Please enter a first and last name.";
        else if (Input.DepartmentId <= 0)
            ErrorMessage = "Please choose a department.";
        else if (Input.HireDate > DateTime.Today)
            ErrorMessage = "Hire date can't be in the future.";
        else if (Input.PayRate is < 0)
            ErrorMessage = "Pay rate can't be negative.";
        else if (ssn.Length > 0 && ssn.Length != 9)
            ErrorMessage = "SSN must be 9 digits.";
        else if (IsNew && ssn.Length == 0)
            ErrorMessage = "Please enter the SSN for a new employee.";

        if (ErrorMessage != null)
        {
            await LoadDepartmentsAsync(conn);
            return Page();
        }

        // 2. Save the employee, then their specialty, all-or-nothing in a transaction
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            int employeeId;
            if (IsNew)
            {
                await using var cmd = new MySqlCommand(@"
                    INSERT INTO Employee (SSN, First_Name, Middle_Name, Last_Name, Address, Gender, Phone_Number,
                                          Job_Position, Hire_Date, Department_ID, Employment_Type, Pay_Type, Pay_Rate)
                    VALUES (@ssn, @first, @middle, @last, @address, @gender, @phone,
                            @job, @hired, @dept, @empType, @payType, @payRate)", conn, tx);
                AddParameters(cmd, ssn);
                await cmd.ExecuteNonQueryAsync();
                employeeId = (int)cmd.LastInsertedId;
            }
            else
            {
                employeeId = Id!.Value;
                // Leaving SSN blank on edit keeps the current one
                await using var cmd = new MySqlCommand(@"
                    UPDATE Employee
                    SET SSN = COALESCE(@ssn, SSN), First_Name = @first, Middle_Name = @middle, Last_Name = @last,
                        Address = @address, Gender = @gender, Phone_Number = @phone, Job_Position = @job,
                        Hire_Date = @hired, Department_ID = @dept, Employment_Type = @empType,
                        Pay_Type = @payType, Pay_Rate = @payRate
                    WHERE Employee_ID = @id", conn, tx);
                AddParameters(cmd, ssn);
                cmd.Parameters.AddWithValue("@id", employeeId);
                if (await cmd.ExecuteNonQueryAsync() == 0) return NotFound();
            }

            await SaveSpecialtyAsync(conn, tx, employeeId);
            await tx.CommitAsync();

            TempData["Success"] = $"{Input.FirstName} {Input.LastName} was {(IsNew ? "added" : "updated")}.";
            return Redirect($"/Staff/EmployeeDetails/{employeeId}");
        }
        catch (MySqlException ex)
        {
            await tx.RollbackAsync();
            ErrorMessage = ex.ErrorCode switch
            {
                MySqlErrorCode.DuplicateKeyEntry => "Another employee already has that SSN.",
                MySqlErrorCode.RowIsReferenced2 => Input.Specialty == "None"
                    ? "This person still has keeper or vet records (enclosure assignments, schedules, or exams), so their specialty can't be removed. Reassign that work first."
                    : "This change conflicts with records that depend on this employee.",
                _ => ex.SqlState == "45000" ? ex.Message : "Could not save: " + ex.Message
            };
            await LoadDepartmentsAsync(conn);
            return Page();
        }
    }

    // Keeper and Veterinarian are subtype tables: being a keeper = having a row in Keeper
    private async Task SaveSpecialtyAsync(MySqlConnection conn, MySqlTransaction tx, int employeeId)
    {
        async Task Run(string sql, params (string, object?)[] ps)
        {
            await using var cmd = new MySqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@id", employeeId);
            foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        if (Input.Specialty == "Keeper")
        {
            await Run("DELETE FROM Veterinarian WHERE Employee_ID = @id");
            await Run(@"INSERT INTO Keeper (Employee_ID, Experience_Level) VALUES (@id, @lvl)
                        ON DUPLICATE KEY UPDATE Experience_Level = @lvl", ("@lvl", Input.ExperienceLevel));
        }
        else if (Input.Specialty == "Veterinarian")
        {
            await Run("DELETE FROM Keeper WHERE Employee_ID = @id");
            await Run(@"INSERT INTO Veterinarian (Employee_ID, Specialization) VALUES (@id, @spec)
                        ON DUPLICATE KEY UPDATE Specialization = @spec", ("@spec", Input.Specialization?.Trim()));
        }
        else
        {
            await Run("DELETE FROM Keeper WHERE Employee_ID = @id");
            await Run("DELETE FROM Veterinarian WHERE Employee_ID = @id");
        }
    }

    private void AddParameters(MySqlCommand cmd, string ssn)
    {
        object? Val(string? s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
        cmd.Parameters.AddWithValue("@ssn", ssn.Length == 0 ? DBNull.Value : (object)ssn);
        cmd.Parameters.AddWithValue("@first", Input.FirstName);
        cmd.Parameters.AddWithValue("@middle", Val(Input.MiddleName));
        cmd.Parameters.AddWithValue("@last", Input.LastName);
        cmd.Parameters.AddWithValue("@address", Val(Input.Address));
        cmd.Parameters.AddWithValue("@gender", Val(Input.Gender));
        cmd.Parameters.AddWithValue("@phone", Val(Input.Phone));
        cmd.Parameters.AddWithValue("@job", Val(Input.JobPosition));
        cmd.Parameters.AddWithValue("@hired", (object?)Input.HireDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dept", Input.DepartmentId);
        cmd.Parameters.AddWithValue("@empType", Val(Input.EmploymentType));
        cmd.Parameters.AddWithValue("@payType", Val(Input.PayType));
        cmd.Parameters.AddWithValue("@payRate", (object?)Input.PayRate ?? DBNull.Value);
    }

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();
        return conn;
    }

    private async Task LoadDepartmentsAsync(MySqlConnection conn)
    {
        DepartmentOptions.Clear();
        await using var cmd = new MySqlCommand("SELECT Department_ID, Department_Name FROM Department ORDER BY Department_Name", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) DepartmentOptions.Add((r.GetInt32(0), r.GetString(1)));
    }

    public class EmployeeInput
    {
        public string FirstName { get; set; } = "";
        public string? MiddleName { get; set; }
        public string LastName { get; set; } = "";
        public string? JobPosition { get; set; }
        public int DepartmentId { get; set; }
        public string? EmploymentType { get; set; }
        public DateTime? HireDate { get; set; }
        public string? PayType { get; set; }
        public decimal? PayRate { get; set; }
        public string? Phone { get; set; }
        public string? Gender { get; set; }
        public string? Address { get; set; }
        public string? Ssn { get; set; }
        public string Specialty { get; set; } = "None";
        public string? ExperienceLevel { get; set; }
        public string? Specialization { get; set; }
    }
}
