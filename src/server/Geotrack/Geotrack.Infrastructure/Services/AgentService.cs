using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class AgentService : IAgentService
{
    private readonly AppDbContext _db;

    public AgentService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AgentResponseDto> ProcessMessageAsync(string text, int? userId = null)
    {
        var lower = text.ToLower();

        // Keyword-based stub responses matching frontend mock patterns
        if (ContainsAny(lower, "beach", "swim", "coast", "sea"))
            return BuildCardsResponse("Beach Recommendations", await GetPlaceCards("beach", 3));

        if (ContainsAny(lower, "restaurant", "food", "eat", "dinner", "lunch", "cuisine"))
            return BuildCardsResponse("Restaurant Picks", await GetPlaceCards("restaurant", 3));

        if (ContainsAny(lower, "hotel", "stay", "accommodation", "sleep"))
            return BuildCardsResponse("Top Stays", await GetHotelCards(3));

        if (ContainsAny(lower, "event", "festival", "concert", "show"))
            return BuildCardsResponse("Upcoming Events", await GetEventCards(3));

        if (ContainsAny(lower, "nature", "hike", "park", "island", "outdoor"))
            return BuildCardsResponse("Nature Escapes", await GetPlaceCards("nature", 3));

        if (ContainsAny(lower, "culture", "museum", "history", "landmark"))
            return BuildCardsResponse("Cultural Highlights", await GetPlaceCards("museum", 3));

        if (ContainsAny(lower, "fun", "weekend", "things to do", "activities", "recommend"))
            return BuildCardsResponse("Suggested Weekend Plans", await GetMixedCards(3));

        // Default text response
        return new AgentResponseDto
        {
            Id = Guid.NewGuid().ToString(),
            Role = "assistant",
            Type = "text",
            Text = "Great question! I'd love to help you explore. Could you tell me more about what you're interested in? For example, are you looking for beaches, restaurants, cultural sites, events, or nature spots?",
            SuggestionPills = GetDefaultPills(),
            Timestamp = DateTime.UtcNow
        };
    }

    private AgentResponseDto BuildCardsResponse(string title, List<AgentRecommendationCardDto> cards)
    {
        if (!cards.Any())
        {
            return new AgentResponseDto
            {
                Id = Guid.NewGuid().ToString(),
                Role = "assistant",
                Type = "text",
                Text = $"I don't have specific recommendations for that yet, but I'm sure you'll find great options to explore!",
                SuggestionPills = GetDefaultPills(),
                Timestamp = DateTime.UtcNow
            };
        }

        return new AgentResponseDto
        {
            Id = Guid.NewGuid().ToString(),
            Role = "assistant",
            Type = "suggestion_cards",
            Text = $"Here are some great options for you:",
            CardsTitle = title,
            Cards = cards,
            SuggestionPills = GetDefaultPills(),
            Timestamp = DateTime.UtcNow
        };
    }

    private async Task<List<AgentRecommendationCardDto>> GetPlaceCards(string category, int count)
    {
        var places = await _db.Places
            .Include(p => p.PlaceTags).ThenInclude(pt => pt.Tag)
            .Where(p => p.PlaceTags.Any(pt => pt.Tag.Slug.ToLower().Contains(category) || pt.Tag.Name.ToLower().Contains(category))
                        || p.PlaceType.Name.ToLower().Contains(category))
            .Take(count)
            .ToListAsync();

        return places.Select(p => new AgentRecommendationCardDto
        {
            Id = p.Id,
            Title = p.Name,
            Description = p.Description.Length > 100 ? p.Description[..100] + "..." : p.Description,
            Image = p.ImageUrl,
            Rating = p.AverageRating ?? 4.0,
            DurationHours = 2.0,
            DistanceKm = 3.5,
            IsFavorite = false
        }).ToList();
    }

    private async Task<List<AgentRecommendationCardDto>> GetHotelCards(int count)
    {
        var hotels = await _db.Places
            .Where(p => p.PricePerNight != null)
            .Take(count)
            .ToListAsync();

        return hotels.Select(p => new AgentRecommendationCardDto
        {
            Id = p.Id,
            Title = p.Name,
            Description = p.Description.Length > 100 ? p.Description[..100] + "..." : p.Description,
            Image = p.ImageUrl,
            Rating = p.AverageRating ?? 4.0,
            DurationHours = 0,
            DistanceKm = 2.0,
            IsFavorite = false
        }).ToList();
    }

    private async Task<List<AgentRecommendationCardDto>> GetEventCards(int count)
    {
        var events = await _db.Events
            .Where(e => e.EndDate >= DateTime.UtcNow)
            .Take(count)
            .ToListAsync();

        return events.Select(e => new AgentRecommendationCardDto
        {
            Id = e.Id,
            Title = e.Name,
            Description = e.Description.Length > 100 ? e.Description[..100] + "..." : e.Description,
            Image = e.ImageUrl,
            Rating = 4.5,
            DurationHours = (e.EndDate - e.StartDate).TotalHours,
            DistanceKm = 1.5,
            IsFavorite = false
        }).ToList();
    }

    private async Task<List<AgentRecommendationCardDto>> GetMixedCards(int count)
    {
        var places = await _db.Places
            .OrderByDescending(p => p.AverageRating)
            .Take(count)
            .ToListAsync();

        return places.Select(p => new AgentRecommendationCardDto
        {
            Id = p.Id,
            Title = p.Name,
            Description = p.Description.Length > 100 ? p.Description[..100] + "..." : p.Description,
            Image = p.ImageUrl,
            Rating = p.AverageRating ?? 4.0,
            DurationHours = 2.5,
            DistanceKm = 3.0,
            IsFavorite = false
        }).ToList();
    }

    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(k => text.Contains(k));

    private static List<SuggestionPillDto> GetDefaultPills() => new()
    {
        new() { Label = "Top Attractions", Prompt = "What are the top attractions?" },
        new() { Label = "Best Restaurants", Prompt = "Recommend the best restaurants" },
        new() { Label = "Beaches", Prompt = "Show me the best beaches" },
        new() { Label = "Events", Prompt = "What events are happening?" }
    };
}
