using Geotrack.Domain.Entities;
using Geotrack.Domain.Entities.Analytics;
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
    Task<List<string>> GetCollectionCoverImagesAsync(int collectionId, int maxCount);

    Task<List<Tag>> GetTagsByIdsAsync(IEnumerable<int> tagIds);

    Task<bool> CollectionItemExistsAsync(int collectionId, string entityType, int entityId);
    Task<CollectionItem?> GetCollectionItemByIdAsync(int collectionId, int itemId);

    Task AddCollectionAsync(Collection collection);
    Task DeleteCollectionAsync(Collection collection);
    Task DeleteCollectionItemAsync(CollectionItem item);
    Task AddCollectionItemAsync(CollectionItem item);
    Task<List<SupportRequest>> GetSupportRequestsByUserIdAsync(int userId, int take);
    Task AddSupportRequestAsync(SupportRequest supportRequest);

    Task AddUserTagAsync(UserTag userTag);
    Task DeleteUserTagsAsync(IEnumerable<UserTag> userTags);
    Task<List<UserInterestSignal>> GetVisibleInferredInterestSignalsAsync(int userId, double minimumScore);
    Task<UserInterestSignal?> GetUserInterestSignalAsync(int userId, int tagId);

    Task<User?> GetByIdWithExploreDataAsync(int userId);
    Task<Dictionary<int, double>> GetLocationRatingsByUserIdAsync(int userId);
    Task<Dictionary<int, double>> GetPlaceRatingsByUserIdAsync(int userId);
    Task<Dictionary<int, double>> GetEventRatingsByUserIdAsync(int userId);

    Task<User?> GetByIdWithRolesAsync(int userId);
    Task<Role?> GetRoleByNameAsync(string roleName);
    Task RemoveUserRoleAsync(UserRole userRole);
    Task<User?> GetByIdWithRegionsAsync(int userId);
    Task<User?> GetByIdWithRolesAndRegionsAsync(int userId);
    Task AddRegionalOrganizationAsync(RegionalOrganization regionalOrganization);
    Task<bool> RegionExistsAsync(int regionId);
    Task<bool> UserRegionExistsAsync(int userId, int regionId);
    Task AddUserRegionAsync(RegionalOrganizationRegion region);
    Task<RegionalOrganizationRegion?> GetUserRegionAsync(int userId, int regionId);
    Task DeleteUserRegionAsync(RegionalOrganizationRegion region);
}
