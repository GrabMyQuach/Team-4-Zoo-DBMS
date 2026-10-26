namespace ZooDatabase.Data;

// One item in a section's sidebar (like "Species")
public record ModuleSection(string Title, string Slug);

// One big section of the zoo (like "Animals")
public record ZooModule(string Key, string Title, string Icon, string Image, string Description, ModuleSection[] Sections);
public static class ZooModules
{
    public static readonly ZooModule[] All =
    {
        new("Animals", "Animals", "🦁","card-animals.png", "Animals, species, enclosures, and feeding", new ModuleSection[]
        {
            new("Animals", "Animals"),
            new("Manage Animals", "Manage"),
            new("Species", "Species"),
            new("Enclosures", "Enclosures"),
            new("Feed", "Feed"),
            new("Reports", "animalreports")
        }),

        new("Staff", "Staff & Operations", "👷","card-staff.png", "Employees, departments, and maintenance", new ModuleSection[]
        {
            new("Employees", "Employees"),
            new("Manage Employees", "EmployeeManage"),
            new("Departments", "Departments"),
            new("Keepers", "Keepers"),
            new("Veterinarians", "Veterinarians"),
            new("Volunteers", "Volunteers"),
            new("Maintenance", "Maintenance"),
            new("Reports", "employeereports")
        }),

        new("Tickets", "Tickets & Memberships", "🎟️","card-tickets.webp", "Ticket sales, customers, and attractions", new ModuleSection[]
        {
            new("Ticket Sales", "TicketSales"),
            new("Ticket Types", "TicketTypes"),
            new("Customers", "Customers"),
            new("Memberships", "Memberships"),
            new("Attractions", "Attractions"),
            new("Reports", "Reports")
        }),

        new("Shop", "Food & Gift Shop", "🛍️","card-shop.png", "Food stands, gift shops, inventory, and sales", new ModuleSection[]
        {
            new("Food Stands", "FoodStands"),
            new("Menu Items", "MenuItems"),
            new("Gift Shops", "GiftShops"),
            new("Products", "Products"),
            new("Inventory", "Inventory"),
            new("Suppliers", "Suppliers"),
            new("Sales", "Sales"),
            new("Reports", "Reports")
        })
    };

    // Find a section by its key, like "Animals"
    public static ZooModule? Find(string? key) =>
        All.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));
}