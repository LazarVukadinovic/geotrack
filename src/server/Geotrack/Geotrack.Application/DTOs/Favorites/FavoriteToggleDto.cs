using System.ComponentModel.DataAnnotations;

namespace Geotrack.Application.DTOs.Favorites;

public class FavoriteToggleDto
{
    [Required]
    [RegularExpression("^(place|event)$", ErrorMessage = "EntityType must be 'place' or 'event'.")]
    public string EntityType { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int EntityId { get; set; }
}

public class FavoriteToggleResponseDto
{
    public bool IsSaved { get; set; }
}

public enum FavoriteToggleStatus
{
    Success,
    InvalidEntityType,
    EntityNotFound
}

public class FavoriteToggleResult
{
    public FavoriteToggleStatus Status { get; set; }
    public bool IsSaved { get; set; }
}
