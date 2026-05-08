using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class FavoriteRepository : IFavoriteRepository
{
    private readonly AppDbContext _db;

    public FavoriteRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> EntityExistsAsync(string entityType, int entityId)
    {
        return entityType == "place"
            ? await _db.Places.AnyAsync(place => place.Id == entityId)
            : await _db.Events.AnyAsync(evt => evt.Id == entityId);
    }

    public async Task<List<CollectionItem>> GetSavedItemsForEntityAsync(
        int userId,
        string entityType,
        int entityId)
    {
        return entityType == "place"
            ? await _db.CollectionItems
                .Where(item => item.Collection.UserId == userId && item.PlaceId == entityId)
                .ToListAsync()
            : await _db.CollectionItems
                .Where(item => item.Collection.UserId == userId && item.EventId == entityId)
                .ToListAsync();
    }

    public async Task<Collection?> GetLegacyFavoritesCollectionAsync(int userId, string collectionName)
    {
        return await _db.Collections
            .FirstOrDefaultAsync(collection => collection.UserId == userId && collection.Name == collectionName);
    }

    public void AddCollection(Collection collection)
    {
        _db.Collections.Add(collection);
    }

    public void AddCollectionItem(CollectionItem item)
    {
        _db.CollectionItems.Add(item);
    }

    public void RemoveCollectionItems(IEnumerable<CollectionItem> items)
    {
        _db.CollectionItems.RemoveRange(items);
    }

    public async Task<bool> FavoriteRowExistsAsync(int userId, string entityType, int entityId)
    {
        return await _db.Favorites.AnyAsync(
            favorite => favorite.UserId == userId &&
                        favorite.EntityType == entityType &&
                        favorite.EntityId == entityId);
    }

    public void AddFavorite(Favorite favorite)
    {
        _db.Favorites.Add(favorite);
    }

    public async Task RemoveFavoriteRowAsync(int userId, string entityType, int entityId)
    {
        var favorite = await _db.Favorites.FirstOrDefaultAsync(
            item => item.UserId == userId &&
                    item.EntityType == entityType &&
                    item.EntityId == entityId);

        if (favorite is not null)
        {
            _db.Favorites.Remove(favorite);
        }
    }

    public async Task<HashSet<int>> GetSavedEntityIdsAsync(int userId, string entityType)
    {
        var query = SavedCollectionItems(userId, entityType);

        var ids = entityType == "place"
            ? await query
                .Where(item => item.PlaceId.HasValue)
                .Select(item => item.PlaceId!.Value)
                .Distinct()
                .ToListAsync()
            : await query
                .Where(item => item.EventId.HasValue)
                .Select(item => item.EventId!.Value)
                .Distinct()
                .ToListAsync();

        return ids.ToHashSet();
    }

    public async Task<bool> IsSavedAsync(int userId, string entityType, int entityId)
    {
        var query = SavedCollectionItems(userId, entityType);

        return entityType == "place"
            ? await query.AnyAsync(item => item.PlaceId == entityId)
            : await query.AnyAsync(item => item.EventId == entityId);
    }

    public async Task DeleteEmptyLegacyFavoritesCollectionAsync(
        int userId,
        string collectionName,
        string collectionDescription)
    {
        var collection = await _db.Collections
            .Include(item => item.CollectionItems)
            .FirstOrDefaultAsync(collection =>
                collection.UserId == userId &&
                collection.Name == collectionName &&
                collection.Description == collectionDescription);

        if (collection is not null && collection.CollectionItems.Count == 0)
        {
            _db.Collections.Remove(collection);
        }
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
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
}
