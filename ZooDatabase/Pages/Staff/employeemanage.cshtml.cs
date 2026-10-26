
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using System.Data;

namespace ZooDatabase.Pages.Staff;

[Authorize(Roles = "DBA,SuperUser,Admin")]
public class EmployeeManageModel : PageModel
{
    private readonly IConfiguration _config;

    public EmployeeManageModel(IConfiguration config)
    {
        _config = config;
    }

    [TempData]
    public string? SuccessMessage { get; set; }

    
public class EmployeeRecord
{
    public int EmployeeId { get; set; }
    public string? SSN { get; set;}
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? JobPosition { get; set; }
    public int? DepartmentId { get; set; }
}

public List<EmployeeRecord> Employees { get; set; } = new();

public async Task OnGetAsync()
{
    await LoadEmployeesAsync();
}

private async Task LoadEmployeesAsync()
{
    Employees.Clear();

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
            Last_Name,
            Job_Position,
            Department_ID
        FROM Employee
        ORDER BY Employee_ID DESC;";

    await using var command =
        new MySqlCommand(sql, connection);

    await using var reader =
        await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        
    Employees.Add(new EmployeeRecord
    {
      EmployeeId = reader.GetInt32(0),
      SSN = reader.IsDBNull(1) ? null : reader.GetString(1),
      FirstName = reader.GetString(2),
      LastName = reader.GetString(3),
      JobPosition = reader.IsDBNull(4)
        ? null : reader.GetString(4),
      DepartmentId = reader.IsDBNull(5)
        ? null : reader.GetInt32(5)
    });

    }
}


    public async Task<IActionResult> OnPostAsync(
    
        string? SSN,
        string FirstName,
        string? MiddleName,
        string LastName,
        string? Address,
        string? Gender,
        string? PhoneNumber,
        string? JobPosition,
        DateTime? HireDate,
        int? DepartmentId,
        string? EmploymentType,
        string? PayType,
        decimal? PayRate)
    {
        if (string.IsNullOrWhiteSpace(FirstName) ||
            string.IsNullOrWhiteSpace(LastName))
        {
            ModelState.AddModelError(
                "",
                "First Name and Last Name are required."
            );

            return Page();
        }

        if (DepartmentId.HasValue && DepartmentId.Value <= 0)
        {
            ModelState.AddModelError(
                "",
                "Department ID must be greater than zero."
            );

            return Page();
        }

        var connectionString =
            _config.GetConnectionString("ZooDb");

        await using var connection =
            new MySqlConnection(connectionString);

        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO Employee
            (
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
            )
            VALUES
            (
                @SSN,
                @FirstName,
                @MiddleName,
                @LastName,
                @Address,
                @Gender,
                @PhoneNumber,
                @JobPosition,
                @HireDate,
                @DepartmentId,
                @EmploymentType,
                @PayType,
                @PayRate
            );";

        await using var command =
            new MySqlCommand(sql, connection);
        
        
        command.Parameters.AddWithValue(
           "@SSN",
           string.IsNullOrWhiteSpace(SSN)
           ? DBNull.Value
           : SSN.Trim());


        command.Parameters.AddWithValue(
            "@FirstName", FirstName.Trim());

        command.Parameters.AddWithValue(
            "@MiddleName",
            (object?)MiddleName ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@LastName", LastName.Trim());

        command.Parameters.AddWithValue(
            "@Address",
            (object?)Address ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@Gender",
            (object?)Gender ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@PhoneNumber",
            (object?)PhoneNumber ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@JobPosition",
            (object?)JobPosition ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@HireDate",
            (object?)HireDate ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@DepartmentId",
            (object?)DepartmentId ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@EmploymentType",
            (object?)EmploymentType ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@PayType",
            (object?)PayType ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "@PayRate",
            (object?)PayRate ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();

        SuccessMessage = "Employee added successfully!";

        return RedirectToPage();
    }
    
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
       if (id <= 0)
    {
        return BadRequest();
    }

        var connectionString =
           _config.GetConnectionString("ZooDb");
  
        await using var connection =
        new MySqlConnection(connectionString);

        await connection.OpenAsync();

        const string sql = @"
           DELETE FROM Employee
           WHERE Employee_ID = @EmployeeId;";

        await using var command =
          new MySqlCommand(sql, connection);

        command.Parameters.AddWithValue(
           "@EmployeeId", id);

        try
        {
            int rowsAffected =
             await command.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                TempData["ErrorMessage"] =
                   "Employee not found.";
            }
            else
            {
                 TempData["SuccessMessage"] =
                    "Employee deleted successfully!";
            }
        }
            catch (MySqlException ex) when (ex.Number == 1451)
            {
               TempData["ErrorMessage"] =
                   "Cannot delete this employee because other database records reference them.";
            }

        return RedirectToPage();
   }

}
