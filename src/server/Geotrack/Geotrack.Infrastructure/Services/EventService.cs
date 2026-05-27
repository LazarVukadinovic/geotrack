using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Events;
using Geotrack.Application.DTOs.Images;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Services;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Infrastructure.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public class EventService : IEventService
{
    private const int ListingMaxTake = 50;
    private const string PublishedStatus = "Published";

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
        var destination = await _db.Destinations
            .AsNoTracking()
            .Where(d => d.Id == destinationId.Value && d.Status == PublishedStatus)
            .Select(d => new { d.Id, d.Latitude, d.Longitude })
            .SingleOrDefaultAsync();

        if (destination is null)
        {
            return new DestinationScopedResult<EventsListingResponseDto> { Status = DestinationScopedStatus.DestinationNotFound };
        }

        var userPoint = BuildUserPoint(latitude, longitude)
            ?? BuildUserPoint(destination.Latitude, destination.Longitude);
        var query = _db.Events
            .AsNoTracking()
            .Where(e =>
                e.Status == PublishedStatus &&
                e.Destination.Status == PublishedStatus &&
                (e.Location == null || e.Location.Status == PublishedStatus) &&
                (e.Place == null || e.Place.Status == PublishedStatus));

        if (upcoming == true)
            query = query.Where(e => e.EndDate >= DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(tags) && !IsAllFilter(tags))
        {
            var tagTerms = BuildFilterTerms(tags);
            query = query.Where(e =>
                e.EventTags.Any(et =>
                    tagTerms.Contains(et.Tag.Slug.ToLower()) ||
                    tagTerms.Contains(et.Tag.Name.ToLower())) ||
                tagTerms.Contains(e.EventType.Name.ToLower()));
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
                    Location = e.VenueName ?? (e.Place != null
                        ? e.Place.Name
                        : e.Location != null
                            ? e.Location.Name
                            : e.Destination.Name),
                    Image = e.Images
                        .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.CreatedAt)
                        .ThenBy(image => image.Id)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    DestinationPriority = e.DestinationId == destination.Id ? 0 : 1,
                    Tags = e.EventTags.OrderBy(et => et.Tag.Name).Select(et => et.Tag.Name).ToList()
                })
                .OrderBy(e => e.DestinationPriority)
                .ThenBy(e => e.StartDate)
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
                    Location = e.VenueName ?? (e.Place != null
                        ? e.Place.Name
                        : e.Location != null
                            ? e.Location.Name
                            : e.Destination.Name),
                    Image = e.Images
                        .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                        .ThenBy(image => image.SortOrder)
                        .ThenBy(image => image.CreatedAt)
                        .ThenBy(image => image.Id)
                        .Select(image => image.Url)
                        .FirstOrDefault(),
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    Tags = e.EventTags.OrderBy(et => et.Tag.Name).Select(et => et.Tag.Name).ToList(),
                    DestinationPriority = e.DestinationId == destination.Id ? 0 : 1,
                    DistanceMeters = EF.Property<Point>(e, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint)
                })
                .OrderBy(e => e.DestinationPriority)
                .ThenBy(e => e.DistanceMeters)
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
            .AsNoTracking()
            .Include(e => e.EventType)
            .Include(e => e.Images)
            .Include(e => e.Destination)
            .Include(e => e.Location)
            .Include(e => e.Place)
            .Include(e => e.EventTags).ThenInclude(et => et.Tag)
                .ThenInclude(tag => tag.TagType)
                    .ThenInclude(tagType => tagType!.PlaceTypes)
            .FirstOrDefaultAsync(e =>
                e.Id == id &&
                e.Status == PublishedStatus &&
                e.Destination.Status == PublishedStatus &&
                (e.Location == null || e.Location.Status == PublishedStatus) &&
                (e.Place == null || e.Place.Status == PublishedStatus));

        if (evt == null) return null;

        var isSaved = userId.HasValue && await _favoriteService.IsFavoriteAsync(userId.Value, "event", id);

        var similar = await _db.Events
            .AsNoTracking()
            .Include(e => e.EventType)
            .Include(e => e.Images)
            .Where(e =>
                e.Id != id &&
                e.DestinationId == evt.DestinationId &&
                e.Status == PublishedStatus &&
                e.Destination.Status == PublishedStatus &&
                (e.Location == null || e.Location.Status == PublishedStatus) &&
                (e.Place == null || e.Place.Status == PublishedStatus))
            .Take(5)
            .ToListAsync();

        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "event")
            : new HashSet<int>();
        var (eventLatitude, eventLongitude) = ResolveCoordinates(evt);
        var images = ToImageDtos(evt.Images);

        return new EventDetailDto
        {
            Id = evt.Id,
            Title = evt.Name,
            Location = evt.VenueName ?? evt.Place?.Name ?? evt.Location?.Name ?? evt.Destination.Name,
            LocationId = evt.LocationId,
            LocationName = evt.Location?.Name,
            PlaceId = evt.PlaceId,
            PlaceName = evt.Place?.Name,
            Latitude = eventLatitude,
            Longitude = eventLongitude,
            HeroImage = GetCoverImageUrl(evt.Images) ?? images.FirstOrDefault()?.Url,
            Images = images,
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
                TimeRange = evt.IsAllDay ? "All day" : $"{evt.StartDate:HH:mm} - {evt.EndDate:HH:mm}",
                Venue = evt.VenueName ?? evt.Place?.Name ?? evt.Location?.Name ?? evt.Destination.Name,
                Ticket = evt.IsFree ? "Free" : evt.TicketPrice ?? "Ticket info TBA",
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
                Image = GetMainImageUrl(s.Images),
                IsSaved = favoriteIds.Contains(s.Id)
            }).ToList(),
            IsSaved = isSaved
        };
    }

    // --- Master detail/section/review methods ---

    public async Task<GetEventDetailsResponseDto> GetEventDetailsAsync(int eventId, int? userId)
    {
        var evt = await _db.Events
            .Include(e => e.Images)
            .Include(e => e.EventReviews)
            .Include(e => e.EventTags).ThenInclude(et => et.Tag)
                .ThenInclude(tag => tag.TagType)
                    .ThenInclude(tagType => tagType!.PlaceTypes)
            .FirstOrDefaultAsync(e =>
                e.Id == eventId &&
                e.Status == PublishedStatus &&
                e.Destination.Status == PublishedStatus &&
                (e.Location == null || e.Location.Status == PublishedStatus) &&
                (e.Place == null || e.Place.Status == PublishedStatus))
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

    public async Task<GetEventSectionResponseDto> GetEventSectionAsync(
        int eventId,
        string sectionKey,
        int skip = 0,
        int take = ReviewPaging.DefaultTake)
    {
        var evt = await _db.Events
            .Include(e => e.Images)
            .Include(e => e.EventTags).ThenInclude(et => et.Tag)
                .ThenInclude(tag => tag.TagType)
                    .ThenInclude(tagType => tagType!.PlaceTypes)
            .FirstOrDefaultAsync(e =>
                e.Id == eventId &&
                e.Status == PublishedStatus &&
                e.Destination.Status == PublishedStatus &&
                (e.Location == null || e.Location.Status == PublishedStatus) &&
                (e.Place == null || e.Place.Status == PublishedStatus))
            ?? throw new KeyNotFoundException("Event not found");

        var normalizedKey = sectionKey.Trim().ToLowerInvariant();

        if (normalizedKey == "reviews")
        {
            var normalizedSkip = ReviewPaging.NormalizeSkip(skip);
            var normalizedTake = ReviewPaging.NormalizeTake(take);
            var totalCount = await _db.EventReviews
                .AsNoTracking()
                .CountAsync(r => r.EventId == eventId);
            var reviews = await _db.EventReviews
                .AsNoTracking()
                .Where(r => r.EventId == eventId)
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.Id)
                .Skip(normalizedSkip)
                .Take(normalizedTake)
                .ToListAsync();

            return new GetEventSectionResponseDto
            {
                EventId = eventId,
                SectionKey = "reviews",
                SectionTitle = "Reviews",
                Reviews = reviews
                    .Select(r => new EventReviewItemDto
                    {
                        Id = r.Id,
                        UserId = r.UserId,
                        Rating = r.Rating,
                        Comment = r.Comment,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt
                    }).ToList(),
                TotalCount = totalCount,
                Skip = normalizedSkip,
                Take = normalizedTake,
                NextSkip = normalizedSkip + reviews.Count,
                HasMore = totalCount > normalizedSkip + reviews.Count
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

    public async Task<GetEventReviewsResponseDto> GetEventReviewsAsync(
        int eventId,
        int skip = 0,
        int take = ReviewPaging.DefaultTake)
    {
        var evt = await _db.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e =>
                e.Id == eventId &&
                e.Status == PublishedStatus &&
                e.Destination.Status == PublishedStatus &&
                (e.Location == null || e.Location.Status == PublishedStatus) &&
                (e.Place == null || e.Place.Status == PublishedStatus));
        if (evt == null)
            throw new KeyNotFoundException("Event not found");

        var normalizedSkip = ReviewPaging.NormalizeSkip(skip);
        var normalizedTake = ReviewPaging.NormalizeTake(take);
        var totalCount = await _db.EventReviews
            .AsNoTracking()
            .CountAsync(r => r.EventId == eventId);
        var averageRating = totalCount > 0
            ? Math.Round(await _db.EventReviews
                .AsNoTracking()
                .Where(r => r.EventId == eventId)
                .AverageAsync(r => r.Rating), 2)
            : 0;

        var reviews = await _db.EventReviews
            .AsNoTracking()
            .Where(r => r.EventId == eventId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .ToListAsync();

        return new GetEventReviewsResponseDto
        {
            EventId = eventId,
            AverageRating = averageRating,
            ReviewsCount = totalCount,
            TotalCount = totalCount,
            Skip = normalizedSkip,
            Take = normalizedTake,
            NextSkip = normalizedSkip + reviews.Count,
            HasMore = totalCount > normalizedSkip + reviews.Count,
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

        var evt = await _db.Events.FirstOrDefaultAsync(e =>
            e.Id == eventId &&
            e.Status == PublishedStatus &&
            e.Destination.Status == PublishedStatus &&
            (e.Location == null || e.Location.Status == PublishedStatus) &&
            (e.Place == null || e.Place.Status == PublishedStatus));
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

        if (HasUsableCoordinates(evt.Destination?.Latitude, evt.Destination?.Longitude))
        {
            return (evt.Destination!.Latitude, evt.Destination.Longitude);
        }

        return (null, null);
    }

    private static string? GetMainImageUrl(IEnumerable<EntityImage> images)
    {
        return OrderImages(images)
            .Select(image => image.Url)
            .FirstOrDefault();
    }

    private static string? GetCoverImageUrl(IEnumerable<EntityImage> images)
    {
        return images
            .Where(image => image.ImageRole == EntityImageRole.Cover)
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAt)
            .ThenBy(image => image.Id)
            .Select(image => image.Url)
            .FirstOrDefault();
    }

    private static List<EntityImageDto> ToImageDtos(IEnumerable<EntityImage> images)
    {
        return OrderImages(images)
            .Select(image => new EntityImageDto
            {
                Id = image.Id,
                Url = image.Url,
                AltText = DisplayTextSafety.CleanOptional(image.AltText),
                Caption = DisplayTextSafety.CleanOptional(image.Caption),
                IsMain = image.IsMain,
                ImageRole = EntityImageRole.Normalize(image.ImageRole),
                SortOrder = image.SortOrder
            })
            .ToList();
    }

    private static IOrderedEnumerable<EntityImage> OrderImages(IEnumerable<EntityImage> images)
    {
        return images
            .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAt)
            .ThenBy(image => image.Id);
    }

    private static bool HasUsableCoordinates(double? latitude, double? longitude)
    {
        return latitude.HasValue &&
               longitude.HasValue &&
               !double.IsNaN(latitude.Value) &&
               !double.IsNaN(longitude.Value) &&
               !double.IsInfinity(latitude.Value) &&
               !double.IsInfinity(longitude.Value) &&
               latitude.Value >= -90 &&
               latitude.Value <= 90 &&
               longitude.Value >= -180 &&
               longitude.Value <= 180 &&
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

    private static bool IsAllFilter(string value) =>
        string.Equals(value.Trim(), "all", StringComparison.OrdinalIgnoreCase);

    private static List<string> BuildFilterTerms(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue) ||
            string.Equals(rawValue.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var terms = new HashSet<string>();

        foreach (var rawTerm in rawValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = rawTerm.ToLowerInvariant();
            AddTermVariants(terms, normalized);
            AddTermVariants(terms, normalized.Replace(" ", "-"));
            AddTermVariants(terms, normalized.Replace("-", " "));
        }

        return terms.ToList();
    }

    private static void AddTermVariants(HashSet<string> terms, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim();
        terms.Add(normalized);

        if (normalized.EndsWith("ies", StringComparison.Ordinal) && normalized.Length > 3)
        {
            terms.Add(normalized[..^3] + "y");
            return;
        }

        if (normalized.EndsWith("s", StringComparison.Ordinal) && normalized.Length > 1)
        {
            terms.Add(normalized[..^1]);
            return;
        }

        terms.Add(normalized + "s");
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
        public int DestinationPriority { get; set; }
        public double? DistanceMeters { get; set; }
        public List<string> Tags { get; set; } = new();
    }
}
