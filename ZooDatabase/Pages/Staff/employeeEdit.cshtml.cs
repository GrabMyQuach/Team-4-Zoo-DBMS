using MySqlConnector;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ZooDatabase.Pages.Staff;

[Authorize(Roles = "DBA,SuperUser,Admin")]
public class EmployeeEditModel : PageModel
{
    private readonly IConfiguration _config;

        public EmployeeEditModel(IConfiguration config)
    {
           _config = config;
    }
    [BindProperty]
    public EmployeeInput Employee { get; set; } = new();

    public class EmployeeInput
    {
        public int EmployeeId { get; set; }
        public string? SSN { get; set; }
        public string FirstName { get; set; } = "";
        public string? MiddleName { get; set; }
        public string LastName { get; set; } = "";
        public string? Address { get; set; }
        public string? Gender { get; set; }
        public string? PhoneNumber { get; set; }
        public string? JobPosition { get; set; }
        public DateTime? HireDate { get; set; }
        public int? DepartmentId { get; set; }
        public string? EmploymentType { get; set; }
        public string? PayType { get; set; }
        public decimal? PayRate { get; set; }
    }
    
public async Task<IActionResult> OnGetAsync(int id)
{
    var connectionString =
        _config.GetConnectionString("ZooDb");

    await using var connection =
        new MySqlConnection(connectionString);

    await connection.OpenAsync();

    const string sql = @"
        SELECT
            Employee_ID,
            SSN,
            First_Name,
            Middle_Name,
            Last_Name,
            Address,
            Gender,
            Phone_Number,
            Job_Position,
            Hire_Date,
            Department_ID,
            Employment_Type,
            Pay_Type,
            Pay_Rate
        FROM Employee
        WHERE Employee_ID = @id;";

    await using var command =
        new MySqlCommand(sql, connection);

    command.Parameters.AddWithValue("@id", id);

    await using var reader =
        await command.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
    {
        return NotFound();
    }

    Employee = new EmployeeInput
    {
        EmployeeId = reader.GetInt32(0),
        SSN = reader.IsDBNull(1) ? null : reader.GetString(1),
        FirstName = reader.GetString(2),
        MiddleName = reader.IsDBNull(3) ? null : reader.GetString(3),
        LastName = reader.GetString(4),
        Address = reader.IsDBNull(5) ? null : reader.GetString(5),
        Gender = reader.IsDBNull(6) ? null : reader.GetString(6),
        PhoneNumber = reader.IsDBNull(7) ? null : reader.GetString(7),
        JobPosition = reader.IsDBNull(8) ? null : reader.GetString(8),
        HireDate = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
        DepartmentId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
        EmploymentType = reader.IsDBNull(11) ? null : reader.GetString(11),
        PayType = reader.IsDBNull(12) ? null : reader.GetString(12),
        PayRate = reader.IsDBNull(13) ? null : reader.GetDecimal(13)
    };

    return Page();
}
    
public async Task<IActionResult> OnPostAsync(int id)
{
    if (id != Employee.EmployeeId)
    {
        return BadRequest();
    }

    if (string.IsNullOrWhiteSpace(Employee.FirstName) ||
        string.IsNullOrWhiteSpace(Employee.LastName))
    {
        ModelState.AddModelError(
            "",
            "First Name and Last Name are required."
        );

        return Page();
    }

    if (!string.IsNullOrWhiteSpace(Employee.SSN) &&
        (Employee.SSN.Length != 9 ||
         !Employee.SSN.All(char.IsDigit)))
    {
        ModelState.AddModelError(
            "",
            "SSN must contain exactly 9 digits."
        );

        return Page();
    }

    var connectionString =
        _config.GetConnectionString("ZooDb");

    await using var connection =
        new MySqlConnection(connectionString);

    await connection.OpenAsync();

    const string sql = @"
        UPDATE Employee
        SET
            SSN = @SSN,
            First_Name = @FirstName,
            Middle_Name = @MiddleName,
            Last_Name = @LastName,
            Address = @Address,
            Gender = @Gender,
            Phone_Number = @PhoneNumber,
            Job_Position = @JobPosition,
            Hire_Date = @HireDate,
            Department_ID = @DepartmentId,
            Employment_Type = @EmploymentType,
            Pay_Type = @PayType,
            Pay_Rate = @PayRate
        WHERE Employee_ID = @EmployeeId;";

    await using var command =
        new MySqlCommand(sql, connection);

    command.Parameters.AddWithValue(
        "@EmployeeId", id);

    command.Parameters.AddWithValue(
        "@SSN",
        string.IsNullOrWhiteSpace(Employee.SSN)
            ? DBNull.Value
            : Employee.SSN.Trim());

    command.Parameters.AddWithValue(
        "@FirstName", Employee.FirstName.Trim());

    command.Parameters.AddWithValue(
        "@MiddleName",
        (object?)Employee.MiddleName ?? DBNull.Value);

    command.Parameters.AddWithValue(
        "@LastName", Employee.LastName.Trim());

    command.Parameters.AddWithValue(
        "@Address",
        (object?)Employee.Address ?? DBNull.Value);

    command.Parameters.AddWithValue(
        "@Gender",
        (object?)Employee.Gender ?? DBNull.Value);

    command.Parameters.AddWithValue(
        "@PhoneNumber",
        (object?)Employee.PhoneNumber ?? DBNull.Value);

    command.Parameters.AddWithValue(
        "@JobPosition",
        (object?)Employee.JobPosition ?? DBNull.Value);

    command.Parameters.AddWithValue(
        "@HireDate",
        (object?)Employee.HireDate ?? DBNull.Value);

    command.Parameters.AddWithValue(
        "@DepartmentId",
        (object?)Employee.DepartmentId ?? DBNull.Value);

    command.Parameters.AddWithValue(
        "@EmploymentType",
        (object?)Employee.EmploymentType ?? DBNull.Value);

    command.Parameters.AddWithValue(
        "@PayType",
        (object?)Employee.PayType ?? DBNull.Value);

    command.Parameters.AddWithValue(
        "@PayRate",
        (object?)Employee.PayRate ?? DBNull.Value);

    int rowsAffected = await command.ExecuteNonQueryAsync();

    if (rowsAffected == 0)
    {
        return NotFound();
    }

    TempData["SuccessMessage"] =
        "Employee updated successfully!";

    return RedirectToPage("./EmployeeManage");
}



    
}
