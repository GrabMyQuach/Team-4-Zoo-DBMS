namespace ZooDatabase.Pages.Register;

// What the shared register partial (_Pos.cshtml) needs to draw itself
public class PosView
{
    public List<PosItem> Items { get; set; } = new();
    public Dictionary<string, string> Hidden { get; set; } = new();   // extra form fields, e.g. the shop location
    public bool ShowCustomer { get; set; }                            // ticket sales are recorded under a customer
    public string? Lookup { get; set; }                               // keep what the cashier typed if the sale needs fixing
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public Dictionary<int, int> Start { get; set; } = new();          // quantities to put back on the receipt after an error
}
