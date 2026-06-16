using Geotrack.Application.DTOs.Images;
using Geotrack.Application.DTOs.Places;

namespace Geotrack.Application.DTOs.Events;

public class EventDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public int DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public string ParentEntityType { get; set; } = "Location";
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public int? PlaceId { get; set; }
    public string? PlaceName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? HeroImage { get; set; }
    public List<EntityImageDto> Images { get; set; } = new();
    public List<TagLabelDto> Tags { get; set; } = new();
    public string? Distance { get; set; }
    public string? About { get; set; }
    public EventDetailInfoDto Info { get; set; } = new();
    public EventVenueInfoDto VenueInfo { get; set; } = new();
    public List<EventSimilarDto> Similar { get; set; } = new();
    public bool IsSaved { get; set; }
    public string? ExternalTicketLink { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsAllDay { get; set; }
    public bool IsRecurring { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public int? AttendanceLimit { get; set; }
    public string? AgeRestriction { get; set; }
}

public class EventDetailInfoDto
{
    public string? Date { get; set; }
    public string? TimeRange { get; set; }
    public string? Venue { get; set; }
    public string? Ticket { get; set; }
    public string? Duration { get; set; }
    public string? HostedBy { get; set; }
}

public class EventVenueInfoDto
{
    public string? Description { get; set; }
    public string? ArrivalTips { get; set; }
    public string? Accessibility { get; set; }
}

public class EventSimilarDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Image { get; set; }
    public bool IsSaved { get; set; }
}
