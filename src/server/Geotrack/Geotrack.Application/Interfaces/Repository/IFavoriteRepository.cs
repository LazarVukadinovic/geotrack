using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Interfaces.Repository;

public interface IFavoriteRepository
{
    Task<bool> EntityExistsAsync(string entityType, int entityId);

    Task<List<CollectionItem>> GetSavedItemsForEntityAsync(
        int userId,
        string entityType,
        int entityId);

    Task<Collection?> GetLegacyFavoritesCollectionAsync(int userId, string collectionName);

    void AddCollection(Collection collection);
    void AddCollectionItem(CollectionItem item);
    void RemoveCollectionItems(IEnumerable<CollectionItem> items);

    Task<bool> FavoriteRowExistsAsync(int userId, string entityType, int entityId);
    void AddFavorite(Favorite favorite);
    Task RemoveFavoriteRowAsync(int userId, string entityType, int entityId);

    Task<HashSet<int>> GetSavedEntityIdsAsync(int userId, string entityType);
    Task<bool> IsSavedAsync(int userId, string entityType, int entityId);

    Task DeleteEmptyLegacyFavoritesCollectionAsync(
        int userId,
        string collectionName,
        string collectionDescription);

    Task SaveChangesAsync();
}
