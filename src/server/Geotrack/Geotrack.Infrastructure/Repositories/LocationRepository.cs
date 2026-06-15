using Geotrack.Application.DTOs.Locations;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class LocationRepository : Repository<Location>, ILocationRepository
{
    private readonly AppDbContext _context;

    public LocationRepository(AppDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<Location?> GetWithDetailsAsync(int locationId)
    {
        return await _context.Locations
            .AsNoTracking()
            .Include(l => l.Destination)
            .Include(l => l.Images)
            .Include(l => l.Places)
                .ThenInclude(p => p.Images)
            .Include(l => l.Places)
                .ThenInclude(p => p.PlaceType)
            .Include(l => l.Places)
                .ThenInclude(p => p.PlaceReviews)
            .Include(l => l.Places)
                .ThenInclude(p => p.PlaceTags)
                    .ThenInclude(pt => pt.Tag)
                        .ThenInclude(tag => tag.TagType)
                            .ThenInclude(tagType => tagType!.PlaceTypes)
            .Include(l => l.Events)
                .ThenInclude(e => e.Images)
            .Include(l => l.Events)
                .ThenInclude(e => e.EventTags)
                    .ThenInclude(et => et.Tag)
                        .ThenInclude(tag => tag.TagType)
                            .ThenInclude(tagType => tagType!.PlaceTypes)
            .Include(l => l.LocationTags)
                .ThenInclude(lt => lt.Tag)
                    .ThenInclude(tag => tag.TagType)
                        .ThenInclude(tagType => tagType!.PlaceTypes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(l => l.Id == locationId);
    }

    public async Task<List<LocationRelatedItemDto>> GetSimilarPublishedLocationsInTerritoryAsync(int locationId, int take)
    {
        var source = await _context.Locations
            .AsNoTracking()
            .Where(l => l.Id == locationId)
            .Select(l => new
            {
                l.LocationTypeId,
                TerritoryId = l.Destination.Region.TerritoryId
            })
            .FirstOrDefaultAsync();

        if (source == null)
        {
            return new List<LocationRelatedItemDto>();
        }

        var tagIds = await _context.LocationTags
            .AsNoTracking()
            .Where(lt => lt.LocationId == locationId)
            .Select(lt => lt.TagId)
            .Distinct()
            .ToListAsync();

        return await _context.Locations
            .AsNoTracking()
            .Where(l => l.Id != locationId &&
                        l.Status == "Published" &&
                        l.Destination.Status == "Published" &&
                        l.Destination.Region.TerritoryId == source.TerritoryId &&
                        l.LocationTypeId == source.LocationTypeId)
            .OrderByDescending(l => l.LocationTags.Count(lt => tagIds.Contains(lt.TagId)))
            .ThenByDescending(l => l.LocationReviews.Any())
            .ThenBy(l => l.Name)
            .Take(take)
            .Select(l => new LocationRelatedItemDto
            {
                Id = l.Id,
                Name = l.Name,
                Description = string.IsNullOrWhiteSpace(l.Summary) ? l.Description : l.Summary,
                Image = l.Images
                    .OrderBy(image => image.ImageRole == "card" ? 0 : image.ImageRole == "cover" ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                AverageRating = l.LocationReviews
                    .Select(review => (double?)review.Rating)
                    .Average() ?? 0
            })
            .ToListAsync();
    }

    public async Task<List<Event>> GetPublishedEventsForLocationAsync(int locationId, int take)
    {
        return await _context.Events
            .AsNoTracking()
            .Include(e => e.Images)
            .Where(e => e.LocationId == locationId && e.Status == "Published")
            .OrderBy(e => e.StartDate)
            .Take(take)
            .ToListAsync();
    }

    public async Task<bool> IsLocationSavedByUserAsync(int locationId, int userId)
    {
        return await _context.CollectionItems
            .AnyAsync(ci => ci.LocationId == locationId && ci.Collection.UserId == userId);
    }

    public async Task<List<int>> GetSavedCollectionIdsByUserAsync(int locationId, int userId)
    {
        return await _context.CollectionItems
            .Where(ci => ci.LocationId == locationId && ci.Collection.UserId == userId)
            .Select(ci => ci.CollectionId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<LocationReview>> GetReviewsByLocationIdAsync(int locationId, int skip, int take)
    {
        return await _context.LocationReviews
            .AsNoTracking()
            .Where(r => r.LocationId == locationId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> CountReviewsByLocationIdAsync(int locationId)
    {
        return await _context.LocationReviews
            .AsNoTracking()
            .CountAsync(r => r.LocationId == locationId);
    }

    public async Task<double?> GetAverageRatingByLocationIdAsync(int locationId)
    {
        return await _context.LocationReviews
            .AsNoTracking()
            .Where(r => r.LocationId == locationId)
            .Select(r => (double?)r.Rating)
            .AverageAsync();
    }

    public async Task<bool> HasUserReviewedLocationAsync(int locationId, int userId)
    {
        return await _context.LocationReviews
            .AnyAsync(r => r.LocationId == locationId && r.UserId == userId);
    }

    public async Task AddReviewAsync(LocationReview review)
    {
        await _context.LocationReviews.AddAsync(review);
    }

    public async Task<List<Location>> SearchByNameAsync(string query)
    {
        return await _context.Locations.Include(l => l.Destination).Where(l => l.Name.ToLower().Contains(query.ToLower())).ToListAsync();
    }
}
