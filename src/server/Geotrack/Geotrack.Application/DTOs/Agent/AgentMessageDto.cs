namespace Geotrack.Application.DTOs.Agent;

public class AgentSendMessageDto
{
    public string Text { get; set; } = string.Empty;
}

public class AgentResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = "assistant";
    public string Type { get; set; } = "text";
    public string? Text { get; set; }
    public string? CardsTitle { get; set; }
    public List<AgentRecommendationCardDto>? Cards { get; set; }
    public List<SuggestionPillDto>? SuggestionPills { get; set; }
    public DateTime Timestamp { get; set; }
}

public class AgentRecommendationCardDto
{
    public int Id { get; set; }
    public string EntityType { get; set; } = "place";
    public string? DetailUrl { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Image { get; set; }
    public double? Rating { get; set; }
    public double? DurationHours { get; set; }
    public double? DistanceKm { get; set; }
    public bool IsFavorite { get; set; }
}

public class SuggestionPillDto
{
    public string Label { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}
