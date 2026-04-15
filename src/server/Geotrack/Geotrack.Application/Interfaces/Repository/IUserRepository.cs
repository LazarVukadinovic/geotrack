using Geotrack.Domain.Entities;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Interfaces.Repository;

public interface IUserRepository : IRepository<User>
{
    Task<bool> ExistsByEmailAsync(string email);
    Task<User?> GetByEmailAsync(string email);

    Task<User?> GetByIdWithCollectionsAsync(int userId);
    Task<User?> GetByIdWithUserTagsAsync(int userId);

    Task<List<Collection>> GetCollectionsByUserIdAsync(int userId);
    Task<Collection?> GetCollectionByIdForUserAsync(int userId, int collectionId);
    Task<Collection?> GetCollectionByIdWithItemsAsync(int userId, int collectionId);

    Task<int> GetCollectionItemsCountAsync(int collectionId);

    Task<List<Tag>> GetTagsByIdsAsync(IEnumerable<int> tagIds);

    Task<bool> CollectionItemExistsAsync(int collectionId, string entityType, int entityId);
    Task<CollectionItem?> GetCollectionItemByIdAsync(int collectionId, int itemId);

    Task AddCollectionAsync(Collection collection);
    Task DeleteCollectionAsync(Collection collection);
    Task DeleteCollectionItemAsync(CollectionItem item);
    Task AddCollectionItemAsync(CollectionItem item);

    Task AddUserTagAsync(UserTag userTag);
    Task DeleteUserTagsAsync(IEnumerable<UserTag> userTags);

    Task<User?> GetByIdWithExploreDataAsync(int userId);
    Task<Dictionary<int, double>> GetLocationRatingsByUserIdAsync(int userId);
    Task<Dictionary<int, double>> GetPlaceRatingsByUserIdAsync(int userId);
    Task<Dictionary<int, double>> GetEventRatingsByUserIdAsync(int userId);
}