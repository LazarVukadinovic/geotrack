using Geotrack.Domain.Entities.Core;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities;
using Geotrack.Domain.Entities.Analytics;
using Geotrack.Domain.Entities.Interests;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories
{
    public class UserRepository : Repository<User>, IUserRepository
    {
        private readonly AppDbContext _context;

        public UserRepository(AppDbContext context) : base(context)
        {
            _context = context;
        }

        public async Task<bool> ExistsByEmailAsync(string email)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            return await _context.Users
                .AnyAsync(u => u.Email.ToLower() == normalizedEmail);
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
        }

        public async Task<User?> GetByIdWithCollectionsAsync(int userId)
        {
            return await _context.Users
                .Include(u => u.Collections)
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<User?> GetByIdWithUserTagsAsync(int userId)
        {
            return await _context.Users
                .Include(u => u.UserTags)
                    .ThenInclude(ut => ut.Tag)
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<User?> GetByIdWithUserInterestTopicsAsync(int userId)
        {
            return await _context.Users
                .Include(u => u.UserInterestTopics)
                    .ThenInclude(uit => uit.InterestTopic)
                        .ThenInclude(topic => topic.TopicTags)
                            .ThenInclude(link => link.Tag)
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<User?> GetByIdWithExploreDataAsync(int userId)
        {
            return await _context.Users
                .Include(u => u.UserTags)
                    .ThenInclude(ut => ut.Tag)
                .Include(u => u.Collections)
                    .ThenInclude(c => c.CollectionItems)
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<List<Collection>> GetCollectionsByUserIdAsync(int userId)
        {
            return await _context.Collections
                .Where(c => c.UserId == userId)
                .ToListAsync();
        }

        public async Task<Collection?> GetCollectionByIdForUserAsync(int userId, int collectionId)
        {
            return await _context.Collections
                .FirstOrDefaultAsync(c => c.Id == collectionId && c.UserId == userId);
        }

        public async Task<Collection?> GetCollectionByIdWithItemsAsync(int userId, int collectionId)
        {
            return await _context.Collections
                .Include(c => c.CollectionItems)
                    .ThenInclude(ci => ci.Destination)
                    .ThenInclude(d => d!.Images)
                .Include(c => c.CollectionItems)
                    .ThenInclude(ci => ci.Location)
                    .ThenInclude(l => l!.Images)
                .Include(c => c.CollectionItems)
                    .ThenInclude(ci => ci.Place)
                    .ThenInclude(p => p!.Images)
                .Include(c => c.CollectionItems)
                    .ThenInclude(ci => ci.Place)
                    .ThenInclude(p => p!.Stay)
                .Include(c => c.CollectionItems)
                    .ThenInclude(ci => ci.Event)
                    .ThenInclude(e => e!.Images)
                .FirstOrDefaultAsync(c => c.Id == collectionId && c.UserId == userId);
        }

        public async Task<int> GetCollectionItemsCountAsync(int collectionId)
        {
            return await _context.CollectionItems
                .CountAsync(ci => ci.CollectionId == collectionId);
        }

        public async Task<List<string>> GetCollectionCoverImagesAsync(int collectionId, int maxCount)
        {
            if (maxCount <= 0) return new List<string>();

            var urls = await _context.CollectionItems
                .Where(ci => ci.CollectionId == collectionId)
                .OrderByDescending(ci => ci.CreatedAt)
                .Select(ci =>
                    ci.Place != null ? ci.Place.Images.OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2).ThenBy(image => image.SortOrder).ThenBy(image => image.CreatedAt).ThenBy(image => image.Id).Select(image => image.Url).FirstOrDefault() :
                    ci.Event != null ? ci.Event.Images.OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2).ThenBy(image => image.SortOrder).ThenBy(image => image.CreatedAt).ThenBy(image => image.Id).Select(image => image.Url).FirstOrDefault() :
                    ci.Destination != null ? ci.Destination.Images.OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2).ThenBy(image => image.SortOrder).ThenBy(image => image.CreatedAt).ThenBy(image => image.Id).Select(image => image.Url).FirstOrDefault() :
                    ci.Location != null ? ci.Location.Images.OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2).ThenBy(image => image.SortOrder).ThenBy(image => image.CreatedAt).ThenBy(image => image.Id).Select(image => image.Url).FirstOrDefault() : null)
                .ToListAsync();

            return urls
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u!)
                .Take(maxCount)
                .ToList();
        }

        public async Task<List<Tag>> GetTagsByIdsAsync(IEnumerable<int> tagIds)
        {
            var ids = tagIds.Distinct().ToList();

            return await _context.Tags
                .Where(t => ids.Contains(t.Id))
                .ToListAsync();
        }

        public async Task<List<InterestTopic>> GetInterestTopicsByIdsAsync(IEnumerable<int> interestTopicIds)
        {
            var ids = interestTopicIds.Distinct().ToList();

            return await _context.InterestTopics
                .Include(topic => topic.TopicTags)
                    .ThenInclude(link => link.Tag)
                .Where(topic => ids.Contains(topic.Id) && topic.IsActive)
                .ToListAsync();
        }

        public async Task<bool> CollectionItemExistsAsync(int collectionId, string entityType, int entityId)
        {
            entityType = entityType.Trim().ToLowerInvariant();

            return entityType switch
            {
                "destination" => await _context.CollectionItems.AnyAsync(x => x.CollectionId == collectionId && x.DestinationId == entityId),
                "location" => await _context.CollectionItems.AnyAsync(x => x.CollectionId == collectionId && x.LocationId == entityId),
                "place" => await _context.CollectionItems.AnyAsync(x => x.CollectionId == collectionId && x.PlaceId == entityId),
                "event" => await _context.CollectionItems.AnyAsync(x => x.CollectionId == collectionId && x.EventId == entityId),
                _ => false
            };
        }

        public async Task<CollectionItem?> GetCollectionItemByIdAsync(int collectionId, int itemId)
        {
            return await _context.CollectionItems
                .FirstOrDefaultAsync(x => x.CollectionId == collectionId && x.Id == itemId);
        }

        public async Task<Dictionary<int, double>> GetLocationRatingsByUserIdAsync(int userId)
        {
            return await _context.LocationReviews
                .Where(x => x.UserId == userId)
                .ToDictionaryAsync(x => x.LocationId, x => x.Rating);
        }

        public async Task<Dictionary<int, double>> GetPlaceRatingsByUserIdAsync(int userId)
        {
            return await _context.PlaceReviews
                .Where(x => x.UserId == userId)
                .ToDictionaryAsync(x => x.PlaceId, x => x.Rating);
        }

        public async Task<Dictionary<int, double>> GetEventRatingsByUserIdAsync(int userId)
        {
            return await _context.EventReviews
                .Where(x => x.UserId == userId)
                .ToDictionaryAsync(x => x.EventId, x => x.Rating);
        }

        public async Task AddCollectionAsync(Collection collection)
        {
            await _context.Collections.AddAsync(collection);
        }

        public Task DeleteCollectionAsync(Collection collection)
        {
            _context.Collections.Remove(collection);
            return Task.CompletedTask;
        }

        public Task DeleteCollectionItemAsync(CollectionItem item)
        {
            _context.CollectionItems.Remove(item);
            return Task.CompletedTask;
        }

        public async Task AddCollectionItemAsync(CollectionItem item)
        {
            await _context.CollectionItems.AddAsync(item);
        }

        public async Task<List<SupportRequest>> GetSupportRequestsByUserIdAsync(int userId, int take)
        {
            return await _context.SupportRequests
                .Where(request => request.UserId == userId)
                .OrderByDescending(request => request.CreatedAt)
                .Take(take)
                .ToListAsync();
        }

        public async Task AddSupportRequestAsync(SupportRequest supportRequest)
        {
            await _context.SupportRequests.AddAsync(supportRequest);
        }

        public async Task AddUserTagAsync(UserTag userTag)
        {
            await _context.UserTags.AddAsync(userTag);
        }

        public Task DeleteUserTagsAsync(IEnumerable<UserTag> userTags)
        {
            _context.UserTags.RemoveRange(userTags);
            return Task.CompletedTask;
        }

        public async Task AddUserInterestTopicAsync(UserInterestTopic userInterestTopic)
        {
            await _context.UserInterestTopics.AddAsync(userInterestTopic);
        }

        public Task DeleteUserInterestTopicsAsync(IEnumerable<UserInterestTopic> userInterestTopics)
        {
            _context.UserInterestTopics.RemoveRange(userInterestTopics);
            return Task.CompletedTask;
        }

        public async Task<List<UserInterestSignal>> GetVisibleInferredInterestSignalsAsync(int userId, double minimumScore)
        {
            var explicitTagIds = _context.UserTags
                .Where(ut => ut.UserId == userId)
                .Select(ut => ut.TagId);

            return await _context.UserInterestSignals
                .Include(signal => signal.Tag)
                .Where(signal =>
                    signal.UserId == userId &&
                    signal.Source == "inferred" &&
                    !signal.IsDismissed &&
                    signal.Score >= minimumScore &&
                    !explicitTagIds.Contains(signal.TagId))
                .OrderByDescending(signal => signal.Score)
                .ThenBy(signal => signal.Tag.Name)
                .ToListAsync();
        }

        public async Task<UserInterestSignal?> GetUserInterestSignalAsync(int userId, int tagId)
        {
            return await _context.UserInterestSignals
                .Include(signal => signal.Tag)
                .FirstOrDefaultAsync(signal =>
                    signal.UserId == userId &&
                    signal.TagId == tagId &&
                    signal.Source == "inferred");
        }

        public async Task<List<UserInterestTopicSignal>> GetVisibleInferredInterestTopicSignalsAsync(int userId, double minimumScore)
        {
            var explicitTopicIds = _context.UserInterestTopics
                .Where(ut => ut.UserId == userId)
                .Select(ut => ut.InterestTopicId);

            return await _context.UserInterestTopicSignals
                .Include(signal => signal.InterestTopic)
                    .ThenInclude(topic => topic.TopicTags)
                        .ThenInclude(link => link.Tag)
                .Where(signal =>
                    signal.UserId == userId &&
                    signal.Source == "inferred" &&
                    !signal.IsDismissed &&
                    signal.Score >= minimumScore &&
                    signal.InterestTopic.IsActive &&
                    !explicitTopicIds.Contains(signal.InterestTopicId))
                .OrderByDescending(signal => signal.Score)
                .ThenBy(signal => signal.InterestTopic.Name)
                .ToListAsync();
        }

        public async Task<UserInterestTopicSignal?> GetUserInterestTopicSignalAsync(int userId, int interestTopicId)
        {
            return await _context.UserInterestTopicSignals
                .Include(signal => signal.InterestTopic)
                .FirstOrDefaultAsync(signal =>
                    signal.UserId == userId &&
                    signal.InterestTopicId == interestTopicId &&
                    signal.Source == "inferred");
        }

        public async Task<User?> GetByIdWithRolesAsync(int userId)
        {
            return await _context.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<Role?> GetRoleByNameAsync(string roleName)
        {
            return await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
        }

        public Task RemoveUserRoleAsync(UserRole userRole)
        {
            _context.UserRoles.Remove(userRole);
            return Task.CompletedTask;
        }

        public async Task<User?> GetByIdWithRegionsAsync(int userId)
        {
            return await _context.Users
                .Include(u => u.RegionalManagerRegions)
                    .ThenInclude(ror => ror.Region)
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<User?> GetByIdWithRolesAndRegionsAsync(int userId)
        {
            return await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Include(u => u.RegionalManagerRegions)
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<bool> RegionExistsAsync(int regionId)
        {
            return await _context.Regions.AnyAsync(r => r.Id == regionId);
        }

        public async Task<bool> UserRegionExistsAsync(int userId, int regionId)
        {
            return await _context.RegionalManagerRegions.AnyAsync(ror => ror.UserId == userId && ror.RegionId == regionId);
        }

        public async Task AddUserRegionAsync(RegionalManagerRegion region)
        {
            await _context.RegionalManagerRegions.AddAsync(region);
        }

        public async Task<RegionalManagerRegion?> GetUserRegionAsync(int userId, int regionId)
        {
            return await _context.RegionalManagerRegions
                .FirstOrDefaultAsync(ror => ror.UserId == userId && ror.RegionId == regionId);
        }

        public Task DeleteUserRegionAsync(RegionalManagerRegion region)
        {
            _context.RegionalManagerRegions.Remove(region);
            return Task.CompletedTask;
        }
    }
}