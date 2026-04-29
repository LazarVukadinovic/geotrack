using Geotrack.Application.DTOs.Favorites;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class FavoriteService : IFavoriteService
{
    private const string LegacyFavoritesCollectionName = "Favorites";
    private const string LegacyFavoritesCollectionDescription = "Items saved from the favorite button.";
    private readonly AppDbContext _db;

    public FavoriteService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<FavoriteToggleResult> ToggleAsync(int userId, FavoriteToggleDto dto)
    {
        var type = NormalizeEntityType(dto.EntityType);

        if (!IsSupportedEntityType(type))
        {
            return new FavoriteToggleResult { Status = FavoriteToggleStatus.InvalidEntityType };
        }

        var exists = await EntityExistsAsync(type, dto.EntityId);

        if (!exists)
        {
            return new FavoriteToggleResult { Status = FavoriteToggleStatus.EntityNotFound };
        }

        var savedItems = type == "place"
            ? await _db.CollectionItems
                .Where(item => item.Collection.UserId == userId && item.PlaceId == dto.EntityId)
                .ToListAsync()
            : await _db.CollectionItems
                .Where(item => item.Collection.UserId == userId && item.EventId == dto.EntityId)
                .ToListAsync();

        if (savedItems.Count > 0)
        {
            _db.CollectionItems.RemoveRange(savedItems);
            await RemoveFavoriteRowAsync(userId, type, dto.EntityId);
            await _db.SaveChangesAsync();
            await DeleteEmptyLegacyFavoritesCollectionAsync(userId);
            await _db.SaveChangesAsync();
            return new FavoriteToggleResult { Status = FavoriteToggleStatus.Success, IsSaved = false };
        }

        var collection = await _db.Collections
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Name == LegacyFavoritesCollectionName);

        if (collection is null)
        {
            collection = new Collection
            {
                UserId = userId,
                Name = LegacyFavoritesCollectionName,
                Description = LegacyFavoritesCollectionDescription,
                CreatedAt = DateTime.UtcNow
            };
            _db.Collections.Add(collection);
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

        _db.CollectionItems.Add(item);
        await EnsureSavedAsync(userId, type, dto.EntityId);
        await _db.SaveChangesAsync();
        return new FavoriteToggleResult { Status = FavoriteToggleStatus.Success, IsSaved = true };
    }

    public async Task EnsureSavedAsync(int userId, string entityType, int entityId)
    {
        var type = NormalizeEntityType(entityType);
        if (!IsSupportedEntityType(type))
        {
            return;
        }

        var exists = await _db.Favorites.AnyAsync(
            f => f.UserId == userId && f.EntityType == type && f.EntityId == entityId);

        if (exists)
        {
            return;
        }

        _db.Favorites.Add(new Favorite
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

        await RemoveFavoriteRowAsync(userId, type, entityId);
    }

    public async Task<HashSet<int>> GetFavoriteIdsAsync(int userId, string entityType)
    {
        var type = NormalizeEntityType(entityType);
        if (!IsSupportedEntityType(type))
        {
            return new HashSet<int>();
        }

        var ids = type == "place"
            ? await SavedCollectionItems(userId, type)
                .Where(item => item.PlaceId.HasValue)
                .Select(item => item.PlaceId!.Value)
                .Distinct()
                .ToListAsync()
            : await SavedCollectionItems(userId, type)
                .Where(item => item.EventId.HasValue)
                .Select(item => item.EventId!.Value)
                .Distinct()
                .ToListAsync();
        return ids.ToHashSet();
    }

    public async Task<bool> IsFavoriteAsync(int userId, string entityType, int entityId)
    {
        var type = NormalizeEntityType(entityType);
        if (!IsSupportedEntityType(type))
        {
            return false;
        }

        return type == "place"
            ? await SavedCollectionItems(userId, type).AnyAsync(item => item.PlaceId == entityId)
            : await SavedCollectionItems(userId, type).AnyAsync(item => item.EventId == entityId);
    }

    private IQueryable<CollectionItem> SavedCollectionItems(int userId, string entityType)
    {
        var query = _db.CollectionItems
            .AsNoTracking()
            .Where(item => item.Collection.UserId == userId);

        return entityType == "place"
            ? query.Where(item => item.PlaceId.HasValue)
            : query.Where(item => item.EventId.HasValue);
    }

    private async Task<bool> EntityExistsAsync(string entityType, int entityId) =>
        entityType == "place"
            ? await _db.Places.AnyAsync(p => p.Id == entityId)
            : await _db.Events.AnyAsync(e => e.Id == entityId);

    private async Task RemoveFavoriteRowAsync(int userId, string entityType, int entityId)
    {
        var favorite = await _db.Favorites.FirstOrDefaultAsync(
            f => f.UserId == userId && f.EntityType == entityType && f.EntityId == entityId);

        if (favorite is not null)
        {
            _db.Favorites.Remove(favorite);
        }
    }

    private async Task DeleteEmptyLegacyFavoritesCollectionAsync(int userId)
    {
        var collection = await _db.Collections
            .Include(c => c.CollectionItems)
            .FirstOrDefaultAsync(c =>
                c.UserId == userId &&
                c.Name == LegacyFavoritesCollectionName &&
                c.Description == LegacyFavoritesCollectionDescription);

        if (collection is not null && collection.CollectionItems.Count == 0)
        {
            _db.Collections.Remove(collection);
        }
    }

    private static string NormalizeEntityType(string? entityType) =>
        entityType?.Trim().ToLowerInvariant() ?? string.Empty;

    private static bool IsSupportedEntityType(string entityType) =>
        entityType is "place" or "event";
}
