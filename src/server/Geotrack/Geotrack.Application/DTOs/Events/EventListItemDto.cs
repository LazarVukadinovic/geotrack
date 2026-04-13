namespace Geotrack.Application.DTOs.Events;

public class EventListItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? Image { get; set; }
    public string? DateLabel { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Variant { get; set; } = "large";
    public bool IsSaved { get; set; }
}

public class EventsFeaturedDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DateRange { get; set; }
    public string? LocationLine { get; set; }
}

public class EventsListingResponseDto
{
    public List<EventListItemDto> Items { get; set; } = new();
    public EventsFeaturedDto? Featured { get; set; }
}
