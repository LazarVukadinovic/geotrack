using Geotrack.Domain.Entities.Core;
using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure;
using Geotrack.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class ExploreSearchService : IExploreSearchService
{
    private const int MinQueryLength = 2;
    private const int MaxQueryLength = 100;
    private const int DefaultTake = 20;
    private const int MaxTake = 50;
    private const int ExtraCandidateBuffer = 25;
    private const string PublishedStatus = "Published";

    private readonly AppDbContext _db;

    public ExploreSearchService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ExploreSearchResponseDto> SearchAsync(
        int destinationId,
        string? query,
        int skip = 0,
        int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        if (destinationId <= 0)
        {
            throw new InvalidOperationException("destinationId is required.");
        }

        var normalizedQuery = NormalizeQuery(query);
        var normalizedSkip = Math.Max(0, skip);
        var normalizedTake = Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);

        if (normalizedQuery.Length < MinQueryLength)
        {
            return Empty(destinationId, normalizedQuery, normalizedSkip, normalizedTake);
        }

        if (normalizedQuery.Length > MaxQueryLength)
        {
            throw new InvalidOperationException($"Search query must be {MaxQueryLength} characters or fewer.");
        }

        var destinationExists = await _db.Destinations
            .AsNoTracking()
            .AnyAsync(destination => destination.Id == destinationId, cancellationToken);

        if (!destinationExists)
        {
            throw new KeyNotFoundException("Destination not found.");
        }

        var term = SearchTextNormalizer.Normalize(normalizedQuery);
        if (term.Length < MinQueryLength)
        {
            return Empty(destinationId, normalizedQuery, normalizedSkip, normalizedTake);
        }

        var candidateTake = normalizedSkip + normalizedTake + ExtraCandidateBuffer;

        var placeQuery = BuildPlaceQuery(destinationId, term);
        var areaQuery = BuildAreaQuery(destinationId, term);

        var placeCount = await placeQuery.CountAsync(cancellationToken);
        var areaCount = await areaQuery.CountAsync(cancellationToken);

        var places = await placeQuery
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Rating ?? 0)
            .ThenBy(item => item.Title)
            .Take(candidateTake)
            .ToListAsync(cancellationToken);

        var areas = await areaQuery
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Title)
            .Take(candidateTake)
            .ToListAsync(cancellationToken);

        var totalCount = placeCount + areaCount;

        var pageItems = places
            .Concat(areas)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => EntityPriority(item))
            .ThenBy(item => item.Title)
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .ToList();

        return new ExploreSearchResponseDto
        {
            DestinationId = destinationId,
            Query = normalizedQuery,
            Items = pageItems,
            TotalCount = totalCount,
            Skip = normalizedSkip,
            Take = normalizedTake,
            NextSkip = normalizedSkip + pageItems.Count,
            HasMore = normalizedSkip + pageItems.Count < totalCount
        };
    }

    private IQueryable<ExploreSearchResultItemDto> BuildPlaceQuery(int destinationId, string term)
    {
        return _db.Places
            .AsNoTracking()
            .Where(place =>
                place.Status == PublishedStatus &&
                place.Location.DestinationId == destinationId)
            .Where(place =>
                place.SearchText.Contains(term) ||
                place.Location.SearchText.Contains(term) ||
                place.PlaceType.Name.ToLower().Contains(term) ||
                (place.Stay != null &&
                    (place.Stay.StayType.Name.ToLower().Contains(term) ||
                     place.Stay.StayType.Slug.ToLower().Contains(term))) ||
                place.PlaceTags.Any(placeTag =>
                    placeTag.Tag.ForPlace &&
                    (placeTag.Tag.Name.ToLower().Contains(term) ||
                     placeTag.Tag.Slug.ToLower().Contains(term))))
            .Select(place => new ExploreSearchResultItemDto
            {
                Id = place.Id,
                EntityType = "place",
                PlaceKind = place.Stay != null ? "stay" : "place",
                DisplayType = "Place",
                Title = place.Name,
                Subtitle = place.Subtitle ?? place.Location.Name,
                Description = TrimDescription(place.Description, 150),
                CategoryLabel = place.Stay != null
                    ? place.Stay.StayType.Name
                    : place.PlaceType.Name.ToLower() == "activity"
                        ? (place.PlaceTags
                            .Where(placeTag =>
                                placeTag.Tag.ForPlace &&
                                (placeTag.Tag.Name.ToLower().Contains(term) ||
                                 placeTag.Tag.Slug.ToLower().Contains(term)))
                            .OrderBy(placeTag => placeTag.Tag.Name)
                            .Select(placeTag => placeTag.Tag.Name)
                            .FirstOrDefault() ?? "Place")
                        : place.PlaceType.Name,
                Image = place.Images
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                DetailRoute = place.Stay != null ? $"/stays/{place.Id}" : $"/places/{place.Id}",
                DestinationId = place.Location.DestinationId,
                LocationName = place.Location.Name,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                Rating = place.PlaceReviews.Select(review => (double?)review.Rating).Average(),
                ReviewCount = FormatReviewCountLabel(place.PlaceReviews.Count()),
                Tags = place.PlaceTags
                    .Where(placeTag => placeTag.Tag.ForPlace)
                    .OrderBy(placeTag => placeTag.Tag.Name)
                    .Select(placeTag => placeTag.Tag.Name)
                    .ToList(),
                Score =
                    place.SearchText.Contains(term) ? 100 :
                    place.PlaceTags.Any(placeTag =>
                        placeTag.Tag.ForPlace &&
                        (placeTag.Tag.Name.ToLower().Contains(term) ||
                         placeTag.Tag.Slug.ToLower().Contains(term))) ? 85 :
                    place.PlaceType.Name.ToLower().Contains(term) ? 75 :
                    (place.Stay != null &&
                        (place.Stay.StayType.Name.ToLower().Contains(term) ||
                         place.Stay.StayType.Slug.ToLower().Contains(term))) ? 75 :
                    place.Location.SearchText.Contains(term) ? 65 :
                    30
            });
    }

    private IQueryable<ExploreSearchResultItemDto> BuildAreaQuery(int destinationId, string term)
    {
        return _db.Locations
            .AsNoTracking()
            .Where(location =>
                location.Status == PublishedStatus &&
                location.DestinationId == destinationId)
            .Where(location =>
                location.SearchText.Contains(term) ||
                location.LocationType.Name.ToLower().Contains(term) ||
                location.LocationTags.Any(locationTag =>
                    locationTag.Tag.ForLocation &&
                    (locationTag.Tag.Name.ToLower().Contains(term) ||
                     locationTag.Tag.Slug.ToLower().Contains(term))))
            .Select(location => new ExploreSearchResultItemDto
            {
                Id = location.Id,
                EntityType = "location",
                PlaceKind = "area",
                DisplayType = "Area",
                Title = location.Name,
                Subtitle = location.LocationType.Name,
                Description = TrimDescription(location.Description, 150),
                CategoryLabel = "Area",
                Image = location.Images
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                DetailRoute = "/map",
                DestinationId = location.DestinationId,
                LocationName = location.Name,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                Tags = location.LocationTags
                    .Where(locationTag => locationTag.Tag.ForLocation)
                    .OrderBy(locationTag => locationTag.Tag.Name)
                    .Select(locationTag => locationTag.Tag.Name)
                    .ToList(),
                Score =
                    location.SearchText.Contains(term) ? 95 :
                    location.LocationTags.Any(locationTag =>
                        locationTag.Tag.ForLocation &&
                        (locationTag.Tag.Name.ToLower().Contains(term) ||
                         locationTag.Tag.Slug.ToLower().Contains(term))) ? 80 :
                    location.LocationType.Name.ToLower().Contains(term) ? 70 :
                    30
            });
    }

    private static ExploreSearchResponseDto Empty(int destinationId, string query, int skip, int take) =>
        new()
        {
            DestinationId = destinationId,
            Query = query,
            Skip = skip,
            Take = take,
            NextSkip = skip,
            HasMore = false
        };

    private static string NormalizeQuery(string? query)
    {
        return (query ?? string.Empty)
            .Trim()
            .Replace("%", string.Empty)
            .Replace("_", string.Empty);
    }

    private static int EntityPriority(ExploreSearchResultItemDto item)
    {
        return item.PlaceKind switch
        {
            "place" => 0,
            "stay" => 1,
            "area" => 2,
            _ => 10
        };
    }

    private static string? TrimDescription(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength].TrimEnd() + "...";
    }

    private static string? FormatReviewCountLabel(int count)
    {
        if (count <= 0)
        {
            return null;
        }

        return count == 1 ? "1 review" : $"{count} reviews";
    }
}
