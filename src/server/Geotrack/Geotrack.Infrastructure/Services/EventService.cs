using Geotrack.Application.DTOs.Events;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class EventService : IEventService
{
    private readonly AppDbContext _db;
    private readonly IFavoriteService _favoriteService;

    public EventService(AppDbContext db, IFavoriteService favoriteService)
    {
        _db = db;
        _favoriteService = favoriteService;
    }

    public async Task<EventsListingResponseDto> GetEventsAsync(int destinationId, string? tags = null, bool? upcoming = null, int? userId = null)
    {
        var query = _db.Events
            .Include(e => e.EventType)
            .Include(e => e.Location)
            .Include(e => e.EventTags).ThenInclude(et => et.Tag)
            .Where(e => e.Location.DestinationId == destinationId);

        if (upcoming == true)
            query = query.Where(e => e.EndDate >= DateTime.UtcNow);

        if (!string.IsNullOrEmpty(tags) && tags.ToLower() != "all")
        {
            var tagList = tags.Split(',').Select(t => t.Trim().ToLower()).ToList();
            query = query.Where(e => e.EventTags.Any(et => tagList.Contains(et.Tag.Slug.ToLower()) || tagList.Contains(et.Tag.Name.ToLower())));
        }

        var events = await query.OrderBy(e => e.StartDate).ToListAsync();

        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "event")
            : new HashSet<int>();

        var items = new List<EventListItemDto>();
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            items.Add(new EventListItemDto
            {
                Id = e.Id,
                Title = e.Name,
                Subtitle = e.EventType?.Name,
                Description = e.Description.Length > 150 ? e.Description[..150] + "..." : e.Description,
                Location = e.Location?.Name,
                Image = e.ImageUrl,
                DateLabel = FormatDateLabel(e.StartDate, e.EndDate),
                Tags = e.EventTags.Select(et => et.Tag.Name).ToList(),
                Variant = i == 0 ? "large" : "compact",
                IsSaved = favoriteIds.Contains(e.Id)
            });
        }

        EventsFeaturedDto? featured = null;
        if (events.Any())
        {
            var first = events.First();
            featured = new EventsFeaturedDto
            {
                Title = first.Name,
                Description = first.Description.Length > 200 ? first.Description[..200] + "..." : first.Description,
                DateRange = FormatDateLabel(first.StartDate, first.EndDate),
                LocationLine = first.Location?.Name
            };
        }

        return new EventsListingResponseDto { Items = items, Featured = featured };
    }

    public async Task<EventDetailDto?> GetEventDetailAsync(int id, int? userId = null)
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

        return new EventDetailDto
        {
            Id = evt.Id,
            Title = evt.Name,
            Location = evt.Location?.Name,
            HeroImage = evt.ImageUrl,
            Tags = evt.EventTags.Select(et => new TagLabelDto { Label = et.Tag.Name }).ToList(),
            Distance = null,
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
}
