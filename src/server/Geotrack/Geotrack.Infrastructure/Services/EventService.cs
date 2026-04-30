using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Events;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Infrastructure.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public class EventService : IEventService
{
    private const int ListingMaxTake = 50;

    private readonly AppDbContext _db;
    private readonly IFavoriteService _favoriteService;
    private readonly IReviewContentModerationService _contentModeration;

    public EventService(
        AppDbContext db,
        IFavoriteService favoriteService,
        IReviewContentModerationService contentModeration)
    {
        _db = db;
        _favoriteService = favoriteService;
        _contentModeration = contentModeration;
    }

    // --- Frontend-facing methods ---

    public async Task<DestinationScopedResult<EventsListingResponseDto>> GetEventsAsync(
        int? destinationId,
        string? tags = null,
        bool? upcoming = null,
        int? userId = null,
        double? latitude = null,
        double? longitude = null,
        int skip = 0,
        int? take = null)
    {
        if (!destinationId.HasValue || destinationId.Value <= 0)
        {
            return new DestinationScopedResult<EventsListingResponseDto> { Status = DestinationScopedStatus.InvalidDestination };
        }
        if (!await _db.Destinations.AsNoTracking().AnyAsync(d => d.Id == destinationId.Value))
        {
            return new DestinationScopedResult<EventsListingResponseDto> { Status = DestinationScopedStatus.DestinationNotFound };
        }

        var userPoint = BuildUserPoint(latitude, longitude);
        var query = _db.Events.AsNoTracking();

        query = query.Where(e => e.Location.DestinationId == destinationId.Value);

        if (upcoming == true)
            query = query.Where(e => e.EndDate >= DateTime.UtcNow);

        if (!string.IsNullOrEmpty(tags) && tags.ToLower() != "all")
        {
            var tagList = tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToLowerInvariant())
                .ToList();
            query = query.Where(e => e.EventTags.Any(et => tagList.Contains(et.Tag.Slug.ToLower()) || tagList.Contains(et.Tag.Name.ToLower())));
        }

        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "event")
            : new HashSet<int>();

        var normalizedSkip = Math.Max(0, skip);
        var normalizedTake = take.HasValue
            ? Math.Clamp(take.Value, 1, ListingMaxTake)
            : (int?)null;
        IQueryable<EventListProjection> orderedQuery;

        if (userPoint is null)
        {
            orderedQuery = query
                .Select(e => new EventListProjection
                {
                    Id = e.Id,
                    Title = e.Name,
                    Subtitle = e.EventType.Name,
                    Description = e.Description,
                    Location = e.Location.Name,
                    Image = e.ImageUrl,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    Tags = e.EventTags.OrderBy(et => et.Tag.Name).Select(et => et.Tag.Name).ToList()
                })
                .OrderBy(e => e.StartDate)
                .ThenBy(e => e.Title)
                .ThenBy(e => e.Id);
        }
        else
        {
            orderedQuery = query
                .Where(e => EF.Property<Point?>(e, SpatialPointFactory.CoordinatesPropertyName) != null)
                .Select(e => new EventListProjection
                {
                    Id = e.Id,
                    Title = e.Name,
                    Subtitle = e.EventType.Name,
                    Description = e.Description,
                    Location = e.Location.Name,
                    Image = e.ImageUrl,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    Tags = e.EventTags.OrderBy(et => et.Tag.Name).Select(et => et.Tag.Name).ToList(),
                    DistanceMeters = EF.Property<Point>(e, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint)
                })
                .OrderBy(e => e.DistanceMeters)
                .ThenBy(e => e.StartDate)
                .ThenBy(e => e.Title)
                .ThenBy(e => e.Id);
        }

        if (normalizedSkip > 0)
        {
            orderedQuery = orderedQuery.Skip(normalizedSkip);
        }

        if (normalizedTake.HasValue)
        {
            orderedQuery = orderedQuery.Take(normalizedTake.Value + 1);
        }

        var events = await orderedQuery.ToListAsync();
        var pageSize = normalizedTake ?? events.Count;
        var hasMore = normalizedTake.HasValue && events.Count > pageSize;
        var pageEvents = hasMore
            ? events.Take(pageSize).ToList()
            : events;

        var items = pageEvents.Select((e, index) => new EventListItemDto
        {
            Id = e.Id,
            Title = e.Title,
            Subtitle = e.Subtitle,
            Description = e.Description.Length > 150 ? e.Description[..150] + "..." : e.Description,
            Location = e.Location,
            Image = e.Image,
            DateLabel = FormatDateLabel(e.StartDate, e.EndDate),
            Tags = e.Tags,
            Variant = normalizedSkip + index == 0 ? "large" : "compact",
            IsSaved = favoriteIds.Contains(e.Id)
        }).ToList();

        EventsFeaturedDto? featured = null;
        if (pageEvents.Count > 0)
        {
            var first = pageEvents.First();
            featured = new EventsFeaturedDto
            {
                Title = first.Title,
                Description = first.Description.Length > 200 ? first.Description[..200] + "..." : first.Description,
                DateRange = FormatDateLabel(first.StartDate, first.EndDate),
                LocationLine = first.Location
            };
        }

        return new DestinationScopedResult<EventsListingResponseDto>
        {
            Status = DestinationScopedStatus.Success,
            Data = new EventsListingResponseDto
            {
                Items = items,
                Featured = featured,
                Skip = normalizedSkip,
                Take = normalizedTake ?? items.Count,
                NextSkip = normalizedSkip + items.Count,
                HasMore = hasMore
            }
        };
    }

    public async Task<EventDetailDto?> GetEventDetailAsync(
        int id,
        int? userId = null,
        double? latitude = null,
        double? longitude = null)
    {
        var evt = await _db.Events
            .Include(e => e.EventType)
            .Include(e => e.Location)
            .Include(e => e.Place)
            .Include(e => e.EventTags).ThenInclude(et => et.Tag)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evt == null) return null;

        var isSaved = userId.HasValue && await _favoriteService.IsFavoriteAsync(userId.Value, "event", id);

        var similar = await _db.Events
            .Include(e => e.EventType)
            .Where(e => e.Id != id && e.Location.DestinationId == evt.Location.DestinationId)
            .Take(5)
            .ToListAsync();

        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "event")
            : new HashSet<int>();
        var (eventLatitude, eventLongitude) = ResolveCoordinates(evt);

        return new EventDetailDto
        {
            Id = evt.Id,
            Title = evt.Name,
            Location = evt.Location?.Name,
            Latitude = eventLatitude,
            Longitude = eventLongitude,
            HeroImage = evt.ImageUrl,
            Tags = evt.EventTags.Select(et => new TagLabelDto { Label = et.Tag.Name }).ToList(),
            Distance = SpatialDistanceFormatter.FormatBetween(
                latitude,
                longitude,
                eventLatitude,
                eventLongitude),
            About = evt.Description,
            Info = new EventDetailInfoDto
            {
                Date = evt.StartDate.ToString("d MMMM yyyy"),
                TimeRange = $"{evt.StartDate:HH:mm} - {evt.EndDate:HH:mm}",
                Venue = evt.Place?.Name ?? evt.Location?.Name ?? "TBA",
                Ticket = evt.TicketPrice ?? "Free",
                Duration = evt.Duration ?? FormatDuration(evt.StartDate, evt.EndDate),
                HostedBy = evt.HostedBy ?? "Local Organizers"
            },
            VenueInfo = new EventVenueInfoDto
            {
                Description = evt.VenueDescription,
                ArrivalTips = evt.ArrivalTips,
                Accessibility = evt.Accessibility
            },
            Similar = similar.Select(s => new EventSimilarDto
            {
                Id = s.Id,
                Title = s.Name,
                Subtitle = s.EventType?.Name,
                Image = s.ImageUrl,
                IsSaved = favoriteIds.Contains(s.Id)
            }).ToList(),
            IsSaved = isSaved
        };
    }

    // --- Master detail/section/review methods ---

    public async Task<GetEventDetailsResponseDto> GetEventDetailsAsync(int eventId, int? userId)
    {
        var evt = await _db.Events
            .Include(e => e.EventReviews)
            .Include(e => e.EventTags).ThenInclude(et => et.Tag)
            .FirstOrDefaultAsync(e => e.Id == eventId)
            ?? throw new KeyNotFoundException("Event not found");

        var reviews = evt.EventReviews.ToList();
        var averageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;

        var isSaved = false;
        var savedCollectionIds = new List<int>();

        if (userId.HasValue)
        {
            isSaved = await _favoriteService.IsFavoriteAsync(userId.Value, "event", eventId);
            savedCollectionIds = await _db.CollectionItems
                .Where(ci => ci.EventId == eventId && ci.Collection.UserId == userId.Value)
                .Select(ci => ci.CollectionId)
                .ToListAsync();
        }

        var sections = new List<EventSectionPreviewDto>();
        if (evt.EventReviews.Any())
            sections.Add(new EventSectionPreviewDto { Key = "reviews", Title = "Reviews" });
        if (evt.EventTags.Any())
            sections.Add(new EventSectionPreviewDto { Key = "tags", Title = "Tags" });

        return new GetEventDetailsResponseDto
        {
            Id = evt.Id,
            Name = evt.Name,
            EventTypeId = evt.EventTypeId,
            LocationId = evt.LocationId,
            PlaceId = evt.PlaceId,
            Description = evt.Description,
            StartDate = evt.StartDate,
            EndDate = evt.EndDate,
            CreatedAt = evt.CreatedAt,
            UpdatedAt = evt.UpdatedAt,
            AverageRating = averageRating,
            ReviewsCount = reviews.Count,
            IsSaved = isSaved,
            SavedCollectionIds = savedCollectionIds,
            Sections = sections
        };
    }

    public async Task<GetEventSectionResponseDto> GetEventSectionAsync(int eventId, string sectionKey)
    {
        var evt = await _db.Events
            .Include(e => e.EventReviews)
            .Include(e => e.EventTags).ThenInclude(et => et.Tag)
            .FirstOrDefaultAsync(e => e.Id == eventId)
            ?? throw new KeyNotFoundException("Event not found");

        var normalizedKey = sectionKey.Trim().ToLowerInvariant();

        if (normalizedKey == "reviews")
        {
            return new GetEventSectionResponseDto
            {
                EventId = eventId,
                SectionKey = "reviews",
                SectionTitle = "Reviews",
                Reviews = evt.EventReviews
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => new EventReviewItemDto
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

        if (normalizedKey == "tags")
        {
            return new GetEventSectionResponseDto
            {
                EventId = eventId,
                SectionKey = "tags",
                SectionTitle = "Tags",
                Tags = evt.EventTags
                    .Where(t => t.Tag != null)
                    .Select(t => new EventTagItemDto
                    {
                        Id = t.TagId,
                        Name = t.Tag.Name
                    }).ToList()
            };
        }

        throw new KeyNotFoundException("Section not found");
    }

    public async Task<GetEventReviewsResponseDto> GetEventReviewsAsync(int eventId)
    {
        var evt = await _db.Events.FindAsync(eventId);
        if (evt == null)
            throw new KeyNotFoundException("Event not found");

        var reviews = await _db.EventReviews
            .Where(r => r.EventId == eventId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var averageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;

        return new GetEventReviewsResponseDto
        {
            EventId = eventId,
            AverageRating = averageRating,
            ReviewsCount = reviews.Count,
            Reviews = reviews.Select(r => new EventReviewItemDto
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

    public async Task<CreateEventReviewResponseDto> CreateEventReviewAsync(int eventId, int userId, CreateEventReviewRequestDto request)
    {
        if (request.Rating < 1 || request.Rating > 5)
            throw new InvalidOperationException("Rating must be between 1 and 5");

        if ((request.Comment ?? string.Empty).Length > 1000)
            throw new InvalidOperationException("Comment is too long");

        await _contentModeration.EnsureAllowedReviewTextAsync(request.Comment);

        var evt = await _db.Events.FindAsync(eventId);
        if (evt == null)
            throw new KeyNotFoundException("Event not found");

        var alreadyReviewed = await _db.EventReviews.AnyAsync(r => r.EventId == eventId && r.UserId == userId);
        if (alreadyReviewed)
            throw new InvalidOperationException("User has already reviewed this event");

        var review = new EventReview
        {
            UserId = userId,
            EventId = eventId,
            Rating = request.Rating,
            Comment = request.Comment?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _db.EventReviews.AddAsync(review);
        await _db.SaveChangesAsync();

        return new CreateEventReviewResponseDto
        {
            Id = review.Id,
            UserId = review.UserId,
            EventId = review.EventId,
            Rating = review.Rating,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }

    private static string FormatDateLabel(DateTime start, DateTime end)
    {
        if (start.Date == end.Date) return start.ToString("d MMM yyyy");
        return $"{start:d MMM} - {end:d MMM yyyy}";
    }

    private static string FormatDuration(DateTime start, DateTime end)
    {
        var diff = end - start;
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} hours";
        return $"{(int)diff.TotalDays} days";
    }

    private static (double? Latitude, double? Longitude) ResolveCoordinates(Domain.Entities.Core.Event evt)
    {
        if (HasUsableCoordinates(evt.Place?.Latitude, evt.Place?.Longitude))
        {
            return (evt.Place!.Latitude, evt.Place.Longitude);
        }

        if (HasUsableCoordinates(evt.Location?.Latitude, evt.Location?.Longitude))
        {
            return (evt.Location!.Latitude, evt.Location.Longitude);
        }

        return (null, null);
    }

    private static bool HasUsableCoordinates(double? latitude, double? longitude)
    {
        return latitude.HasValue &&
               longitude.HasValue &&
               !double.IsNaN(latitude.Value) &&
               !double.IsNaN(longitude.Value) &&
               !double.IsInfinity(latitude.Value) &&
               !double.IsInfinity(longitude.Value) &&
               (latitude.Value != 0d || longitude.Value != 0d);
    }

    private static Point? BuildUserPoint(double? latitude, double? longitude)
    {
        if (!HasUsableCoordinates(latitude, longitude))
        {
            return null;
        }

        return SpatialPointFactory.Create(latitude!.Value, longitude!.Value);
    }

    private sealed class EventListProjection
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? Location { get; set; }
        public string? Image { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public double? DistanceMeters { get; set; }
        public List<string> Tags { get; set; } = new();
    }
}
