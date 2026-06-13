using Geotrack.Application.DTOs.Common;
using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;

namespace Geotrack.Application.Services;

public class DestinationService : IDestinationService
{
    private const int SectionPreviewLimit = 5;
    private const string PublishedStatus = "Published";

    private readonly IDestinationRepository _destinationRepository;
    private static readonly HashSet<string> AllTypes = new() { "location", "place", "event" };

    public DestinationService(IDestinationRepository destinationRepository)
    {
        _destinationRepository = destinationRepository;
    }

    public async Task<GetDestinationDetailsResponseDto> GetDestinationDetailsAsync(int destinationId, int? userId)
    {
        var destination = await _destinationRepository.GetWithDetailsAsync(destinationId)
            ?? throw new KeyNotFoundException("Destination not found");

        EnsurePublished(destination);

        var reviewsCount = await _destinationRepository.CountReviewsByDestinationIdAsync(destinationId);
        var averageRating = reviewsCount > 0
            ? Math.Round((await _destinationRepository.GetAverageRatingByDestinationIdAsync(destinationId)) ?? 0, 2)
            : 0;

        var isSaved = false;
        var savedCollectionIds = new List<int>();

        if (userId.HasValue)
        {
            isSaved = await _destinationRepository.IsDestinationSavedByUserAsync(destinationId, userId.Value);
            savedCollectionIds = await _destinationRepository.GetSavedCollectionIdsByUserAsync(destinationId, userId.Value);
        }

        var similarDestinations = await _destinationRepository.GetSimilarPublishedDestinationsInTerritoryAsync(destinationId, 6);

        return new GetDestinationDetailsResponseDto
        {
            Id = destination.Id,
            Name = destination.Name,
            Description = destination.Description,
            Latitude = destination.Latitude,
            Longitude = destination.Longitude,
            DestinationTypeId = destination.DestinationTypeId,
            RegionId = destination.RegionId,
            RegionName = destination.Region?.Name,
            CreatedAt = destination.CreatedAt,
            UpdatedAt = destination.UpdatedAt,
            AverageRating = averageRating,
            ReviewsCount = reviewsCount,
            IsSaved = isSaved,
            SavedCollectionIds = savedCollectionIds,
            Images = MapImages(destination.Images),
            Sections = BuildDestinationPreviewSections(destination),
            SimilarDestinations = similarDestinations.Select(MapRelatedDestinationItem).ToList()
        };
    }

    public async Task<GetDestinationSectionResponseDto> GetDestinationSectionAsync(int destinationId, string sectionKey)
    {
        var destination = await _destinationRepository.GetWithDetailsAsync(destinationId)
            ?? throw new KeyNotFoundException("Destination not found");

        EnsurePublished(destination);

        var normalizedKey = sectionKey.Trim().ToLowerInvariant();

        var tag = destination.DestinationTags
            .Where(x => x.Tag != null)
            .Select(x => x.Tag)
            .FirstOrDefault(t => t.Slug.ToLower() == normalizedKey);

        List<Location> locations;
        List<Place> places;
        string title;

        if (tag != null)
        {
            locations = destination.Locations
                .Where(l => l.Status == PublishedStatus && l.LocationTags.Any(lt => lt.TagId == tag.Id))
                .OrderBy(l => l.Name)
                .ToList();

            places = destination.Locations
                .Where(l => l.Status == PublishedStatus)
                .SelectMany(l => l.Places)
                .Where(p => p.Status == PublishedStatus && p.PlaceTags.Any(pt => pt.TagId == tag.Id))
                .OrderBy(p => p.Name)
                .ToList();

            title = tag.Name;
        }
        else if (normalizedKey == "locations")
        {
            locations = destination.Locations
                .Where(l => l.Status == PublishedStatus)
                .OrderBy(l => l.Name)
                .ToList();

            places = destination.Locations
                .Where(l => l.Status == PublishedStatus)
                .SelectMany(l => l.Places)
                .Where(p => p.Status == PublishedStatus)
                .OrderBy(p => p.Name)
                .ToList();

            title = "Locations";
        }
        else
        {
            throw new KeyNotFoundException("Section not found");
        }

        return new GetDestinationSectionResponseDto
        {
            DestinationId = destinationId,
            SectionKey = normalizedKey,
            SectionTitle = title,
            Locations = locations.Select(MapDestinationLocationItem).ToList(),
            Places = places.Select(MapDestinationPlaceItem).ToList()
        };
    }

    public async Task<GetDestinationReviewsResponseDto> GetDestinationReviewsAsync(
        int destinationId,
        int skip = 0,
        int take = ReviewPaging.DefaultTake)
    {
        var destination = await _destinationRepository.GetByIdAsync(destinationId);
        if (destination == null)
            throw new KeyNotFoundException("Destination not found");

        EnsurePublished(destination);

        var normalizedSkip = ReviewPaging.NormalizeSkip(skip);
        var normalizedTake = ReviewPaging.NormalizeTake(take);
        var reviewsCount = await _destinationRepository.CountReviewsByDestinationIdAsync(destinationId);
        var averageRating = reviewsCount > 0
            ? Math.Round((await _destinationRepository.GetAverageRatingByDestinationIdAsync(destinationId)) ?? 0, 2)
            : 0;
        var reviews = await _destinationRepository.GetReviewsByDestinationIdAsync(
            destinationId,
            normalizedSkip,
            normalizedTake);

        return new GetDestinationReviewsResponseDto
        {
            DestinationId = destinationId,
            AverageRating = averageRating,
            ReviewsCount = reviewsCount,
            TotalCount = reviewsCount,
            Skip = normalizedSkip,
            Take = normalizedTake,
            NextSkip = normalizedSkip + reviews.Count,
            HasMore = reviewsCount > normalizedSkip + reviews.Count,
            Reviews = reviews.Select(r => new DestinationReviewItemDto
            {
                Id = r.Id,
                UserId = r.UserId,
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            }).ToList()
        };
    }

    public async Task<CreateDestinationReviewResponseDto> CreateDestinationReviewAsync(int destinationId, int userId, CreateDestinationReviewRequestDto request)
    {
        if (request.Rating < 1 || request.Rating > 5)
            throw new InvalidOperationException("Rating must be between 1 and 5");

        if ((request.Comment ?? string.Empty).Length > 1000)
            throw new InvalidOperationException("Comment is too long");

        var destination = await _destinationRepository.GetByIdAsync(destinationId);
        if (destination == null)
            throw new KeyNotFoundException("Destination not found");

        EnsurePublished(destination);

        var alreadyReviewed = await _destinationRepository.HasUserReviewedDestinationAsync(destinationId, userId);
        if (alreadyReviewed)
            throw new InvalidOperationException("User has already reviewed this destination");

        var review = new DestinationReview
        {
            UserId = userId,
            DestinationId = destinationId,
            Rating = request.Rating,
            Comment = request.Comment?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _destinationRepository.AddReviewAsync(review);
        await _destinationRepository.SaveChangesAsync();

        return new CreateDestinationReviewResponseDto
        {
            Id = review.Id,
            UserId = review.UserId,
            DestinationId = review.DestinationId,
            Rating = review.Rating,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }

    private List<DestinationSectionPreviewDto> BuildDestinationPreviewSections(Destination destination)
    {
        var sections = new List<DestinationSectionPreviewDto>();

        var tags = destination.DestinationTags
            .Where(x => x.Tag != null)
            .Select(x => x.Tag)
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .OrderBy(t => t.Name)
            .ToList();

        foreach (var tag in tags)
        {
            var locations = destination.Locations
                .Where(l => l.LocationTags.Any(lt => lt.TagId == tag.Id))
                .Where(l => l.Status == PublishedStatus)
                .OrderBy(l => l.Name)
                .Take(SectionPreviewLimit)
                .Select(MapDestinationLocationItem)
                .ToList();

            var places = destination.Locations
                .Where(l => l.Status == PublishedStatus)
                .SelectMany(l => l.Places)
                .Where(p => p.Status == PublishedStatus && p.PlaceTags.Any(pt => pt.TagId == tag.Id))
                .OrderBy(p => p.Name)
                .Take(SectionPreviewLimit)
                .Select(MapDestinationPlaceItem)
                .ToList();

            if (locations.Any() || places.Any())
            {
                sections.Add(new DestinationSectionPreviewDto
                {
                    Key = tag.Slug,
                    Title = tag.Name,
                    Locations = locations,
                    Places = places
                });
            }
        }

        if (!sections.Any())
        {
            sections.Add(new DestinationSectionPreviewDto
            {
                Key = "locations",
                Title = "Locations",
                Locations = destination.Locations
                    .Where(l => l.Status == PublishedStatus)
                    .OrderBy(l => l.Name)
                    .Take(SectionPreviewLimit)
                    .Select(MapDestinationLocationItem)
                    .ToList(),
                Places = destination.Locations
                    .Where(l => l.Status == PublishedStatus)
                    .SelectMany(l => l.Places)
                    .Where(p => p.Status == PublishedStatus)
                    .OrderBy(p => p.Name)
                    .Take(SectionPreviewLimit)
                    .Select(MapDestinationPlaceItem)
                    .ToList()
            });
        }

        return sections;
    }

    private static DestinationSectionLocationItemDto MapDestinationLocationItem(Location location)
    {
        return new DestinationSectionLocationItemDto
        {
            Id = location.Id,
            Name = location.Name,
            Image = PickCardImage(location.Images),
            Description = string.IsNullOrWhiteSpace(location.Description) ? null : location.Description,
            Latitude = location.Latitude,
            Longitude = location.Longitude
        };
    }

    private static DestinationSectionPlaceItemDto MapDestinationPlaceItem(Place place)
    {
        return new DestinationSectionPlaceItemDto
        {
            Id = place.Id,
            Name = place.Name,
            Image = PickCardImage(place.Images),
            LocationId = place.LocationId
        };
    }

    private static DestinationRelatedItemDto MapRelatedDestinationItem(Destination destination)
    {
        return new DestinationRelatedItemDto
        {
            Id = destination.Id,
            Name = destination.Name,
            Description = string.IsNullOrWhiteSpace(destination.Summary) ? destination.Description : destination.Summary,
            Image = PickCardImage(destination.Images)
        };
    }

    private static List<PublicEntityImageDto> MapImages(IEnumerable<EntityImage> images)
    {
        return images
            .Where(image => !string.IsNullOrWhiteSpace(image.Url))
            .OrderBy(image => ImageRoleSort(image.ImageRole))
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .Select(image => new PublicEntityImageDto
            {
                Id = image.Id,
                Url = image.Url,
                AltText = image.AltText,
                Caption = image.Caption,
                IsMain = image.IsMain,
                ImageRole = image.ImageRole,
                SortOrder = image.SortOrder,
                ImagePosition = string.IsNullOrWhiteSpace(image.ImagePosition) ? "50% 50%" : image.ImagePosition,
                ImageSize = string.IsNullOrWhiteSpace(image.ImageSize) ? "cover" : image.ImageSize
            })
            .ToList();
    }

    private static string? PickCardImage(IEnumerable<EntityImage> images)
    {
        return images
            .Where(image => !string.IsNullOrWhiteSpace(image.Url))
            .OrderBy(image => image.ImageRole == "cover" ? 0 : image.ImageRole == "card" ? 1 : 2)
            .ThenBy(image => image.SortOrder)
            .Select(image => image.Url)
            .FirstOrDefault();
    }

    private static int ImageRoleSort(string? role)
    {
        return role?.Trim().ToLowerInvariant() switch
        {
            "cover" => 0,
            "card" => 1,
            _ => 2
        };
    }

    public async Task<List<DestinationSearchResultDto>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<DestinationSearchResultDto>();

        var destinations = await _destinationRepository.SearchByNameAsync(query);

        return destinations
            .Where(d => d.Status == PublishedStatus)
            .Select(d => new DestinationSearchResultDto
            {
                Id = d.Id,
                Name = d.Name,
                Description = d.Description,
                Latitude = d.Latitude,
                Longitude = d.Longitude,
            }).ToList();
    }

    public async Task<List<DestinationSearchItemDto>> SearchWithinDestinationAsync(int destinationId, string query, IEnumerable<string>? types)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<DestinationSearchItemDto>();

        var destination = await _destinationRepository.GetByIdAsync(destinationId);

        if (destination is null || destination.Status != PublishedStatus)
            throw new KeyNotFoundException($"Destinacija {destinationId} nije pronadjena.");

        var typesToSearch = types?.Select(t => t.ToLower().Trim()).Where(t => AllTypes.Contains(t)).ToHashSet();

        if (typesToSearch == null || typesToSearch.Count == 0)
            typesToSearch = AllTypes;

        var results = new List<DestinationSearchItemDto>();

        if (typesToSearch.Contains("location"))
        {
            var locations = await _destinationRepository.SearchLocationsAsync(destinationId, query);
            results.AddRange(locations
                .Where(l => l.Status == PublishedStatus)
                .Select(l => new DestinationSearchItemDto
                {
                    Id = l.Id,
                    Type = "location",
                    Name = l.Name,
                    Latitude = l.Latitude,
                    Longitude = l.Longitude,
                    ParentName = null
                }));
        }

        if (typesToSearch.Contains("place"))
        {
            var places = await _destinationRepository.SearchPlacesAsync(destinationId, query);
            results.AddRange(places
                .Where(p => p.Status == PublishedStatus && p.Location?.Status == PublishedStatus)
                .Select(p => new DestinationSearchItemDto
                {
                    Id = p.Id,
                    Type = "place",
                    Name = p.Name,
                    Description = p.Description,
                    Latitude = p.Latitude,
                    Longitude = p.Longitude,
                    ParentName = p.Location?.Name
                }));
        }

        if (typesToSearch.Contains("event"))
        {
            var events = await _destinationRepository.SearchEventsAsync(destinationId, query);
            results.AddRange(events
                .Where(e =>
                    e.Status == PublishedStatus &&
                    (e.Location == null || e.Location.Status == PublishedStatus) &&
                    (e.Place == null || e.Place.Status == PublishedStatus))
                .Select(e => new DestinationSearchItemDto
                {
                    Id = e.Id,
                    Type = "event",
                    Name = e.Name,
                    Description = e.Description,
                    Latitude = e.Place?.Latitude ?? e.Location?.Latitude ?? e.Destination.Latitude,
                    Longitude = e.Place?.Longitude ?? e.Location?.Longitude ?? e.Destination.Longitude,
                    ParentName = e.Place?.Name ?? e.Location?.Name ?? e.Destination.Name
                }));
        }

        return results;
    }

    private static void EnsurePublished(Destination destination)
    {
        if (destination.Status != PublishedStatus)
            throw new KeyNotFoundException("Destination not found");
    }

}
