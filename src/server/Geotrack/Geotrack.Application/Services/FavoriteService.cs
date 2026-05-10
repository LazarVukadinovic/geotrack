using Geotrack.Application.DTOs.Favorites;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Services;

public class FavoriteService : IFavoriteService
{
    private const string LegacyFavoritesCollectionName = "Favorites";
    private const string LegacyFavoritesCollectionDescription = "Items saved from the favorite button.";

    private readonly IFavoriteRepository _favoriteRepository;

    public FavoriteService(IFavoriteRepository favoriteRepository)
    {
        _favoriteRepository = favoriteRepository;
    }

    public async Task<FavoriteToggleResult> ToggleAsync(int userId, FavoriteToggleDto dto)
    {
        var type = NormalizeEntityType(dto.EntityType);

        if (!IsSupportedEntityType(type))
        {
            return new FavoriteToggleResult { Status = FavoriteToggleStatus.InvalidEntityType };
        }

        var exists = await _favoriteRepository.EntityExistsAsync(type, dto.EntityId);
        if (!exists)
        {
            return new FavoriteToggleResult { Status = FavoriteToggleStatus.EntityNotFound };
        }

        var savedItems = await _favoriteRepository.GetSavedItemsForEntityAsync(userId, type, dto.EntityId);
        if (savedItems.Count > 0)
        {
            _favoriteRepository.RemoveCollectionItems(savedItems);
            await _favoriteRepository.RemoveFavoriteRowAsync(userId, type, dto.EntityId);
            await _favoriteRepository.SaveChangesAsync();

            await _favoriteRepository.DeleteEmptyLegacyFavoritesCollectionAsync(
                userId,
                LegacyFavoritesCollectionName,
                LegacyFavoritesCollectionDescription);
            await _favoriteRepository.SaveChangesAsync();

            return new FavoriteToggleResult { Status = FavoriteToggleStatus.Success, IsSaved = false };
        }

        var collection = await _favoriteRepository.GetLegacyFavoritesCollectionAsync(
            userId,
            LegacyFavoritesCollectionName);

        if (collection is null)
        {
            collection = new Collection
            {
                UserId = userId,
                Name = LegacyFavoritesCollectionName,
                Description = LegacyFavoritesCollectionDescription,
                CreatedAt = DateTime.UtcNow
            };

            _favoriteRepository.AddCollection(collection);
        }

        var item = new CollectionItem
        {
            Collection = collection,
            CreatedAt = DateTime.UtcNow
        };

        if (type == "place")
        {
            item.PlaceId = dto.EntityId;
        }
        else
        {
            item.EventId = dto.EntityId;
        }

        _favoriteRepository.AddCollectionItem(item);
        await EnsureSavedAsync(userId, type, dto.EntityId);
        await _favoriteRepository.SaveChangesAsync();

        return new FavoriteToggleResult { Status = FavoriteToggleStatus.Success, IsSaved = true };
    }

    public async Task EnsureSavedAsync(int userId, string entityType, int entityId)
    {
        var type = NormalizeEntityType(entityType);
        if (!IsSupportedEntityType(type))
        {
            return;
        }

        var exists = await _favoriteRepository.FavoriteRowExistsAsync(userId, type, entityId);
        if (exists)
        {
            return;
        }

        _favoriteRepository.AddFavorite(new Favorite
        {
            UserId = userId,
            EntityType = type,
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task RemoveIfUnsavedAsync(int userId, string entityType, int entityId)
    {
        var type = NormalizeEntityType(entityType);
        if (!IsSupportedEntityType(type))
        {
            return;
        }

        if (await IsFavoriteAsync(userId, type, entityId))
        {
            return;
        }

        await _favoriteRepository.RemoveFavoriteRowAsync(userId, type, entityId);
    }

    public async Task<HashSet<int>> GetFavoriteIdsAsync(int userId, string entityType)
    {
        var type = NormalizeEntityType(entityType);
        if (!IsSupportedEntityType(type))
        {
            return new HashSet<int>();
        }

        return await _favoriteRepository.GetSavedEntityIdsAsync(userId, type);
    }

    public async Task<bool> IsFavoriteAsync(int userId, string entityType, int entityId)
    {
        var type = NormalizeEntityType(entityType);
        if (!IsSupportedEntityType(type))
        {
            return false;
        }

        return await _favoriteRepository.IsSavedAsync(userId, type, entityId);
    }

    private static string NormalizeEntityType(string? entityType) =>
        entityType?.Trim().ToLowerInvariant() ?? string.Empty;

    private static bool IsSupportedEntityType(string entityType) =>
        entityType is "place" or "event";
}
