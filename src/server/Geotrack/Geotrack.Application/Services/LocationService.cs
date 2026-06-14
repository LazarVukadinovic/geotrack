using Geotrack.Application.DTOs.Common;
using Geotrack.Application.DTOs.Locations;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;

namespace Geotrack.Application.Services;

public class LocationService : ILocationService
{
    private const int SectionPreviewLimit = 5;
    private const string PublishedStatus = "Published";

    private readonly ILocationRepository _locationRepository;

    public LocationService(ILocationRepository locationRepository)
    {
        _locationRepository = locationRepository;
    }

    public async Task<GetLocationDetailsResponseDto> GetLocationDetailsAsync(int locationId, int? userId)
    {
        var location = await _locationRepository.GetWithDetailsAsync(locationId)
            ?? throw new KeyNotFoundException("Locations not found");

        EnsurePublished(location);

        var reviewsCount = await _locationRepository.CountReviewsByLocationIdAsync(locationId);
        var averageRating = reviewsCount > 0
            ? Math.Round((await _locationRepository.GetAverageRatingByLocationIdAsync(locationId)) ?? 0, 2)
            : 0;

        var isSaved = false;
        var savedCollectionIds = new List<int>();

        if (userId.HasValue)
        {
            isSaved = await _locationRepository.IsLocationSavedByUserAsync(locationId, userId.Value);
            savedCollectionIds = await _locationRepository.GetSavedCollectionIdsByUserAsync(locationId, userId.Value);
        }

        var similarLocations = await _locationRepository.GetSimilarPublishedLocationsInTerritoryAsync(locationId, 6);
        var relatedEvents = await _locationRepository.GetPublishedEventsForLocationAsync(locationId, 6);

        return new GetLocationDetailsResponseDto
        {
            Id = location.Id,
            Name = location.Name,
            Description = string.IsNullOrWhiteSpace(location.Description) ? null : location.Description,
            DestinationId = location.DestinationId,
            DestinationName = location.Destination?.Name,
            LocationTypeId = location.LocationTypeId,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            CreatedAt = location.CreatedAt,
            UpdatedAt = location.UpdatedAt,
            AverageRating = averageRating,
            ReviewsCount = reviewsCount,
            IsSaved = isSaved,
            SavedCollectionIds = savedCollectionIds,
            Images = location.Images.ToPublicEntityImageDtos(),
            Sections = BuildLocationPreviewSections(location),
            SimilarLocations = similarLocations,
            RelatedEvents = relatedEvents.Select(MapEventItem).ToList()
        };
    }

    public async Task<GetLocationSectionResponseDto> GetLocationSectionAsync(int locationId, string sectionKey)
    {
        var location = await _locationRepository.GetWithDetailsAsync(locationId)
            ?? throw new KeyNotFoundException("Locations not found");

        EnsurePublished(location);

        var normalizedKey = sectionKey.Trim().ToLowerInvariant();

        var tag = location.LocationTags
            .Where(x => x.Tag != null)
            .Select(x => x.Tag)
            .FirstOrDefault(t => t.Slug.ToLower() == normalizedKey);

        List<Place> places;
        List<Event> events;
        string title;

        if (tag != null)
        {
            places = location.Places
                .Where(p => p.Status == PublishedStatus && p.PlaceTags.Any(pt => pt.TagId == tag.Id))
                .OrderBy(p => p.Name)
                .ToList();

            events = location.Events
                .Where(e => e.Status == PublishedStatus && e.EventTags.Any(et => et.TagId == tag.Id))
                .OrderBy(e => e.StartDate)
                .ToList();

            title = tag.Name;
        }
        else if (normalizedKey == "places")
        {
            places = location.Places
                .Where(p => p.Status == PublishedStatus)
                .OrderBy(p => p.Name)
                .ToList();
            events = new List<Event>();
            title = "Places";
        }
        else if (normalizedKey == "events")
        {
            places = new List<Place>();
            events = location.Events
                .Where(e => e.Status == PublishedStatus)
                .OrderBy(e => e.StartDate)
                .ToList();
            title = "Events";
        }
        else
        {
            throw new KeyNotFoundException("Section not found");
        }

        return new GetLocationSectionResponseDto
        {
            LocationId = locationId,
            SectionKey = normalizedKey,
            SectionTitle = title,
            Places = places.Select(MapPlaceItem).ToList(),
            Events = events.Select(MapEventItem).ToList()
        };
    }

    public async Task<GetLocationReviewsResponseDto> GetLocationReviewsAsync(
        int locationId,
        int skip = 0,
        int take = ReviewPaging.DefaultTake)
    {
        var location = await _locationRepository.GetByIdAsync(locationId);
        if (location == null)
            throw new KeyNotFoundException("Locations not found");

        EnsurePublished(location);

        var normalizedSkip = ReviewPaging.NormalizeSkip(skip);
        var normalizedTake = ReviewPaging.NormalizeTake(take);
        var reviewsCount = await _locationRepository.CountReviewsByLocationIdAsync(locationId);
        var averageRating = reviewsCount > 0
            ? Math.Round((await _locationRepository.GetAverageRatingByLocationIdAsync(locationId)) ?? 0, 2)
            : 0;
        var reviews = await _locationRepository.GetReviewsByLocationIdAsync(
            locationId,
            normalizedSkip,
            normalizedTake);

        return new GetLocationReviewsResponseDto
        {
            LocationId = locationId,
            AverageRating = averageRating,
            ReviewsCount = reviewsCount,
            TotalCount = reviewsCount,
            Skip = normalizedSkip,
            Take = normalizedTake,
            NextSkip = normalizedSkip + reviews.Count,
            HasMore = reviewsCount > normalizedSkip + reviews.Count,
            Reviews = reviews.Select(r => new LocationReviewItemDto
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

    public async Task<CreateLocationReviewResponseDto> CreateLocationReviewAsync(int locationId, int userId, CreateLocationReviewRequestDto request)
    {
        if (request.Rating < 1 || request.Rating > 5)
            throw new InvalidOperationException("Rating must be between 1 and 5");

        if ((request.Comment ?? string.Empty).Length > 1000)
            throw new InvalidOperationException("Comment is too long");

        var location = await _locationRepository.GetByIdAsync(locationId);
        if (location == null)
            throw new KeyNotFoundException("Locations not found");

        EnsurePublished(location);

        var alreadyReviewed = await _locationRepository.HasUserReviewedLocationAsync(locationId, userId);
        if (alreadyReviewed)
            throw new InvalidOperationException("User has already reviewed this location");

        var review = new LocationReview
        {
            UserId = userId,
            LocationId = locationId,
            Rating = request.Rating,
            Comment = request.Comment?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _locationRepository.AddReviewAsync(review);
        await _locationRepository.SaveChangesAsync();

        return new CreateLocationReviewResponseDto
        {
            Id = review.Id,
            UserId = review.UserId,
            LocationId = review.LocationId,
            Rating = review.Rating,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }

    private List<LocationSectionPreviewDto> BuildLocationPreviewSections(Location location)
    {
        var sections = new List<LocationSectionPreviewDto>();

        var tags = location.LocationTags
            .Where(x => x.Tag != null)
            .Select(x => x.Tag)
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .OrderBy(t => t.Name)
            .ToList();

        foreach (var tag in tags)
        {
            var places = location.Places
                .Where(p => p.Status == PublishedStatus && p.PlaceTags.Any(pt => pt.TagId == tag.Id))
                .OrderBy(p => p.Name)
                .Take(SectionPreviewLimit)
                .Select(MapPlaceItem)
                .ToList();

            var events = location.Events
                .Where(e => e.Status == PublishedStatus && e.EventTags.Any(et => et.TagId == tag.Id))
                .OrderBy(e => e.StartDate)
                .Take(SectionPreviewLimit)
                .Select(MapEventItem)
                .ToList();

            if (places.Any() || events.Any())
            {
                sections.Add(new LocationSectionPreviewDto
                {
                    Key = tag.Slug,
                    Title = tag.Name,
                    Places = places,
                    Events = events
                });
            }
        }

        if (!sections.Any())
        {
            sections.Add(new LocationSectionPreviewDto
            {
                Key = "places",
                Title = "Places",
                Places = location.Places
                    .Where(p => p.Status == PublishedStatus)
                    .OrderBy(p => p.Name)
                    .Take(SectionPreviewLimit)
                    .Select(MapPlaceItem)
                    .ToList(),
                Events = new List<LocationSectionEventItemDto>()
            });

            sections.Add(new LocationSectionPreviewDto
            {
                Key = "events",
                Title = "Events",
                Places = new List<LocationSectionPlaceItemDto>(),
                Events = location.Events
                    .Where(e => e.Status == PublishedStatus)
                    .OrderBy(e => e.StartDate)
                    .Take(SectionPreviewLimit)
                    .Select(MapEventItem)
                    .ToList()
            });
        }

        return sections;
    }

    private static LocationSectionPlaceItemDto MapPlaceItem(Place place)
    {
        return new LocationSectionPlaceItemDto
        {
            Id = place.Id,
            Name = place.Name,
            Image = PickCardImage(place.Images),
            LocationId = place.LocationId
        };
    }

    private static LocationSectionEventItemDto MapEventItem(Event @event)
    {
        return new LocationSectionEventItemDto
        {
            Id = @event.Id,
            Name = @event.Name,
            Image = PickCardImage(@event.Images),
            LocationId = @event.LocationId,
            PlaceId = @event.PlaceId,
            StartDate = @event.StartDate,
            EndDate = @event.EndDate
        };
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

    private static void EnsurePublished(Location location)
    {
        if (location.Status != PublishedStatus ||
            (location.Destination is not null && location.Destination.Status != PublishedStatus))
            throw new KeyNotFoundException("Locations not found");
    }
}
