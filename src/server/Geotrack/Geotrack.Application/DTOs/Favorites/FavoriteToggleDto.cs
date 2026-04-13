namespace Geotrack.Application.DTOs.Favorites;

public class FavoriteToggleDto
{
    public string EntityType { get; set; } = string.Empty; // "place", "event"
    public int EntityId { get; set; }
}

public class FavoriteToggleResponseDto
{
    public bool IsSaved { get; set; }
}
