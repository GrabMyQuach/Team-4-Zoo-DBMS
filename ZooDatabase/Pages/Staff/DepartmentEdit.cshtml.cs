using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using ZooDatabase.Data;

namespace ZooDatabase.Pages.Staff;

// Add or edit a department (DBA only):
//   /Staff/DepartmentEdit      -> add
//   /Staff/DepartmentEdit/3    -> edit department #3
[Authorize(Roles = StaffOptions.EditorRoles)]
public class DepartmentEditModel : PageModel
{
    private readonly IConfiguration _config;

    public DepartmentEditModel(IConfiguration config)
    {
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public DepartmentInput Input { get; set; } = new();

    public List<(int Id, string Label)> ManagerOptions { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public bool IsNew => Id == null;

    public async Task<IActionResult> OnGetAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        if (!IsNew)
        {
            await using var cmd = new MySqlCommand(
                "SELECT Department_Name, Location, Phone_Number, Manager_Employee_ID FROM Department WHERE Department_ID = @id", conn);
            cmd.Parameters.AddWithValue("@id", Id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            Input = new DepartmentInput
            {
                Name = r.GetString(0),
                Location = r.IsDBNull(1) ? null : r.GetString(1),
                Phone = r.IsDBNull(2) ? null : r.GetString(2),
                ManagerId = r.IsDBNull(3) ? 0 : r.GetInt32(3)
            };
        }

        await LoadManagersAsync(conn);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await using var conn = new MySqlConnection(_config.GetConnectionString("ZooDb"));
        await conn.OpenAsync();

        Input.Name = (Input.Name ?? "").Trim();
        if (Input.Name.Length == 0)
        {
            ErrorMessage = "Please enter a department name.";
        }
        else
        {
            // No two departments with the same name
            await using var check = new MySqlCommand(
                "SELECT COUNT(*) FROM Department WHERE Department_Name = @name AND Department_ID <> @id", conn);
            check.Parameters.AddWithValue("@name", Input.Name);
            check.Parameters.AddWithValue("@id", Id ?? 0);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0)
                ErrorMessage = "A department with that name already exists.";
        }

        if (ErrorMessage != null)
        {
            await LoadManagersAsync(conn);
            return Page();
        }

        var sql = IsNew
            ? @"INSERT INTO Department (Department_Name, Location, Phone_Number, Manager_Employee_ID)
                VALUES (@name, @location, @phone, @manager)"
            : @"UPDATE Department
                SET Department_Name = @name, Location = @location, Phone_Number = @phone, Manager_Employee_ID = @manager
                WHERE Department_ID = @id";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@name", Input.Name);
        cmd.Parameters.AddWithValue("@location", string.IsNullOrWhiteSpace(Input.Location) ? DBNull.Value : (object)Input.Location.Trim());
        cmd.Parameters.AddWithValue("@phone", string.IsNullOrWhiteSpace(Input.Phone) ? DBNull.Value : (object)Input.Phone.Trim());
        cmd.Parameters.AddWithValue("@manager", Input.ManagerId > 0 ? (object)Input.ManagerId : DBNull.Value);
        cmd.Parameters.AddWithValue("@id", Id ?? 0);

        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex)
        {
            ErrorMessage = "Could not save: " + ex.Message;
            await LoadManagersAsync(conn);
            return Page();
        }

        var newId = IsNew ? (int)cmd.LastInsertedId : Id!.Value;
        TempData["Success"] = $"{Input.Name} was {(IsNew ? "added" : "updated")}.";
        return Redirect($"/Staff/DepartmentDetails/{newId}");
    }

    // Any employee can be picked as manager; people already in this department are listed first
    private async Task LoadManagersAsync(MySqlConnection conn)
    {
        ManagerOptions.Clear();
        await using var cmd = new MySqlCommand(@"
            SELECT e.Employee_ID, CONCAT(e.First_Name, ' ', e.Last_Name, COALESCE(CONCAT(' (', d.Department_Name, ')'), ''))
            FROM Employee e
            LEFT JOIN Department d ON d.Department_ID = e.Department_ID
            ORDER BY (e.Department_ID <=> @id) DESC, e.Last_Name, e.First_Name", conn);
        cmd.Parameters.AddWithValue("@id", Id ?? 0);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) ManagerOptions.Add((r.GetInt32(0), r.GetString(1)));
    }

    public class DepartmentInput
    {
        public string Name { get; set; } = "";
        public string? Location { get; set; }
        public string? Phone { get; set; }
        public int ManagerId { get; set; }
    }
}
