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

    private readonly ILocationRepository _locationRepository;

    public LocationService(ILocationRepository locationRepository)
    {
        _locationRepository = locationRepository;
    }

    public async Task<GetLocationDetailsResponseDto> GetLocationDetailsAsync(int locationId, int? userId)
    {
        var location = await _locationRepository.GetWithDetailsAsync(locationId)
            ?? throw new KeyNotFoundException("Locations not found");

        var reviews = location.LocationReviews.ToList();
        var averageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;
        var reviewsCount = reviews.Count;

        var isSaved = false;
        var savedCollectionIds = new List<int>();

        if (userId.HasValue)
        {
            isSaved = await _locationRepository.IsLocationSavedByUserAsync(locationId, userId.Value);
            savedCollectionIds = await _locationRepository.GetSavedCollectionIdsByUserAsync(locationId, userId.Value);
        }

        return new GetLocationDetailsResponseDto
        {
            Id = location.Id,
            Name = location.Name,
            DestinationId = location.DestinationId,
            LocationTypeId = location.LocationTypeId,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            CreatedAt = location.CreatedAt,
            UpdatedAt = location.UpdatedAt,
            AverageRating = averageRating,
            ReviewsCount = reviewsCount,
            IsSaved = isSaved,
            SavedCollectionIds = savedCollectionIds,
            Sections = BuildLocationPreviewSections(location)
        };
    }

    public async Task<GetLocationSectionResponseDto> GetLocationSectionAsync(int locationId, string sectionKey)
    {
        var location = await _locationRepository.GetWithDetailsAsync(locationId)
            ?? throw new KeyNotFoundException("Locations not found");

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
                .Where(p => p.PlaceTags.Any(pt => pt.TagId == tag.Id))
                .OrderBy(p => p.Name)
                .ToList();

            events = location.Events
                .Where(e => e.EventTags.Any(et => et.TagId == tag.Id))
                .OrderBy(e => e.StartDate)
                .ToList();

            title = tag.Name;
        }
        else if (normalizedKey == "places")
        {
            places = location.Places.OrderBy(p => p.Name).ToList();
            events = new List<Event>();
            title = "Places";
        }
        else if (normalizedKey == "events")
        {
            places = new List<Place>();
            events = location.Events.OrderBy(e => e.StartDate).ToList();
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

    public async Task<GetLocationReviewsResponseDto> GetLocationReviewsAsync(int locationId)
    {
        var location = await _locationRepository.GetByIDAsync(locationId);
        if (location == null)
            throw new KeyNotFoundException("Locations not found");

        var reviews = await _locationRepository.GetReviewsByLocationIdAsync(locationId);
        var averageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;

        return new GetLocationReviewsResponseDto
        {
            LocationId = locationId,
            AverageRating = averageRating,
            ReviewsCount = reviews.Count,
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

        var location = await _locationRepository.GetByIDAsync(locationId);
        if (location == null)
            throw new KeyNotFoundException("Locations not found");

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
                .Where(p => p.PlaceTags.Any(pt => pt.TagId == tag.Id))
                .OrderBy(p => p.Name)
                .Take(SectionPreviewLimit)
                .Select(MapPlaceItem)
                .ToList();

            var events = location.Events
                .Where(e => e.EventTags.Any(et => et.TagId == tag.Id))
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
            LocationId = place.LocationId
        };
    }

    private static LocationSectionEventItemDto MapEventItem(Event @event)
    {
        return new LocationSectionEventItemDto
        {
            Id = @event.Id,
            Name = @event.Name,
            LocationId = @event.LocationId,
            PlaceId = @event.PlaceId,
            StartDate = @event.StartDate,
            EndDate = @event.EndDate
        };
    }
}