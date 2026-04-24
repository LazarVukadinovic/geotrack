using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities;
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
            return await _context.Users
                .AnyAsync(u => u.Email == email);
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email);
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
                .Include(c => c.CollectionItems)
                    .ThenInclude(ci => ci.Location)
                .Include(c => c.CollectionItems)
                    .ThenInclude(ci => ci.Place)
                .Include(c => c.CollectionItems)
                    .ThenInclude(ci => ci.Event)
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
                    ci.Place != null ? ci.Place.ImageUrl :
                    ci.Event != null ? ci.Event.ImageUrl :
                    ci.Destination != null ? ci.Destination.ImageUrl :
                    ci.Location != null ? ci.Location.ImageUrl : null)
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
            return await _context.Users.Include(u => u.RegionalOrganization).ThenInclude(ro => ro.RegionalOrganizationRegions).ThenInclude(ror => ror.Region).FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<User?> GetByIdWithRolesAndRegionsAsync(int userId)
        {
            return await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Include(u => u.RegionalOrganization)
                    .ThenInclude(ro => ro.RegionalOrganizationRegions)
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task AddRegionalOrganizationAsync(RegionalOrganization regionalOrganization)
        {
            await _context.RegionalOrganizations.AddAsync(regionalOrganization);
        }

        public async Task<bool> RegionExistsAsync(int regionId)
        {
            return await _context.Regions.AnyAsync(r => r.Id == regionId);
        }

        public async Task<bool> UserRegionExistsAsync(int userId, int regionId)
        {
            return await _context.RegionalOrganizationRegions.AnyAsync(ror => ror.UserId == userId && ror.RegionId == regionId);
        }

        public async Task AddUserRegionAsync(RegionalOrganizationRegion region)
        {
            await _context.RegionalOrganizationRegions.AddAsync(region);
        }

        public async Task<RegionalOrganizationRegion?> GetUserRegionAsync(int userId, int regionId)
        {
            return await _context.RegionalOrganizationRegions
                .FirstOrDefaultAsync(ror => ror.UserId == userId && ror.RegionId == regionId);
        }

        public Task DeleteUserRegionAsync(RegionalOrganizationRegion region)
        {
            _context.RegionalOrganizationRegions.Remove(region);
            return Task.CompletedTask;
        }
    }
}
