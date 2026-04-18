using Geotrack.Application.DTOs.Favorites;

namespace Geotrack.Application.Interfaces.Service;

public interface IFavoriteService
{
    Task<FavoriteToggleResult> ToggleAsync(int userId, FavoriteToggleDto dto);
    Task<HashSet<int>> GetFavoriteIdsAsync(int userId, string entityType);
    Task<bool> IsFavoriteAsync(int userId, string entityType, int entityId);
}
