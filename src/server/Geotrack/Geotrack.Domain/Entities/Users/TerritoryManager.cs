namespace Geotrack.Domain.Entities.Users;

public class TerritoryManager
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public ICollection<TerritoryManagerTerritory> TerritoryManagerTerritories { get; set; } = new List<TerritoryManagerTerritory>();
}