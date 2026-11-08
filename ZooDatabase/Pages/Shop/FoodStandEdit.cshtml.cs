namespace ZooDatabase.Pages.Shop;

// Shared code lives in Outlets.cs; this page just says which kind of outlet it shows.
public class FoodStandEditModel : OutletEditModel
{
    public FoodStandEditModel(IConfiguration config) : base(config) { }
    public override OutletKind Kind => OutletKind.Food;
}
